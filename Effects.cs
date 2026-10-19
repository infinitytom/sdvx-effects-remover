using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace SdvxSmooth;

record EffectSource(string Path, string Sha256, long Size);
record EffectItem(string Group, string Source, long Offset, int Size, string Output, string BeforeHash, string AfterHash);
sealed class EffectCatalog
{
    public int Version { get; set; }
    public string GameIdentifier { get; set; } = "";
    public List<EffectSource> Sources { get; set; } = new();
    public List<EffectItem> Items { get; set; } = new();
    public List<string> ProtectedOutputs { get; set; } = new();
    public static EffectCatalog Load(bool legacy = false)
    {
        string name = legacy ? "LegacyEffectCatalog.json.gz" : "EffectCatalog.json.gz";
        return ReadResource(name);
    }
    public static EffectCatalog LoadUnsafe() => ReadResource("UnsafeEffectCatalog.json.gz");
    static EffectCatalog ReadResource(string name)
    {
        using var compressed = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.EndsWith("." + name, StringComparison.Ordinal)).Select(n => Assembly.GetExecutingAssembly().GetManifestResourceStream(n)).Single()!;
        using var stream = new GZipStream(compressed, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<EffectCatalog>(stream)!;
    }
}
record EffectMode(bool Background, bool Hit, bool Lane, bool Extra = false, bool Judgment = false)
{
    public bool Enabled(string group) => group switch { "background" => Background, "hit" => Hit, "lane" => Lane, "extra" => Extra, "judgment" => Judgment, _ => throw new IOException("未知特效分组。") };
}
sealed class EffectTransaction
{
    public string Root { get; set; } = "";
    public string State { get; set; } = "pending";
    public EffectMode Before { get; set; } = new(false, false, false);
    public EffectMode After { get; set; } = new(false, false, false);
}

sealed class Effects
{
    public static readonly string[] Groups = { "background", "hit", "lane", "extra", "judgment" };
    public static string Title(string group) => group switch { "background" => "隐藏曲目背景绘制", "hit" => "减少旋钮尘粒（保留按键命中）", "lane" => "减少轨道反光和 HUD 泛光", "extra" => "隐藏旋钮周边光效、连击光束与 HUD 装饰", "judgment" => "隐藏判定文字及通关/失败演出", _ => group };
    readonly Game game;
    readonly EffectCatalog catalog;
    readonly Action guard;
    readonly string modRoot;
    readonly string store;
    const string Marker = ".sdvx-smooth-owned.json";
    public Effects(Game game, EffectCatalog? catalog = null, Action? processGuard = null)
    {
        this.game = game; this.catalog = catalog ?? EffectCatalog.Load(); guard = processGuard ?? Game.EnsureStopped;
        modRoot = Path.Combine(game.Root, "contents", "data_mods");
        store = Path.Combine(game.Root, ".sdvx-smooth", "effects-v" + this.catalog.Version);
    }
    public int Count(string group) => catalog.Items.Count(i => i.Group == group);
    string Active(string group) => Safe(modRoot, "zzzz_sdvx_smooth_" + group);
    string Parked(string group) => Safe(store, "disabled/" + group);
    public string History => Safe(store, "history");
    static string Safe(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("特效资源路径越界。");
        // Refuse junctions/symlinks in both existing parents and the destination.
        for (var current = new DirectoryInfo(Path.GetDirectoryName(path)!); current != null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("特效目录不支持符号链接或目录联接。");
        if ((Directory.Exists(path) || File.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("拒绝修改链接目标。");
        return path;
    }
    bool ActiveState(string group)
    {
        string active = Active(group), parked = Parked(group);
        if (Directory.Exists(active))
        {
            CheckMarker(active, group);
            if (Directory.Exists(parked)) throw new IOException("启用和停用目录同时存在，请检查特效备份目录：" + store);
            return true;
        }
        if (Directory.Exists(parked)) CheckMarker(parked, group);
        return false;
    }
    void CheckMarker(string folder, string group)
    {
        string marker = Safe(folder, Marker);
        if (!File.Exists(marker) || File.ReadAllText(marker) != $"sdvx-smooth-effects-v{catalog.Version}:{group}")
            throw new IOException("特效目录版本不匹配或不属于本工具。旧版请先点击停用旧版覆盖：" + folder);
    }
    public EffectMode Read() => new(ActiveState("background"), ActiveState("hit"), ActiveState("lane"), ActiveState("extra"), ActiveState("judgment"));
    public string Description() =>
        "游戏内特效精简（资源级实验功能）\r\n" +
        $"背景：{Count("background")} 个绘制资源；旋钮尘粒：{Count("hit")} 个；轨道装饰：{Count("lane")} 个。\r\n" +
        $"额外光效：{Count("extra")} 个；判定显示/演出：{Count("judgment")} 个，可分别恢复。\r\n" +
        "修正版保持原始绘制命令和计数，仅把选中几何的顶点重合为一个点。\r\n" +
        "旧版计数清零会触发原生渲染断言，已停用；本版采用独立的 v2 备份目录。\r\n" +
        "保留按键命中闪光、长按反馈、按键光柱、音符、旋钮定位、血条与分数。\r\n" +
        "勾选判定精简会隐藏 CRITICAL/ERROR、EARLY/LATE 等显示及通关/失败大字，不改变判定逻辑。\r\n" +
        "不处理镜头倾斜/旋转、谱面演出指令或 Live2D。动画计算和纹理加载仍可能存在。\r\n" +
        "已经过资源格式与覆盖路径校验；尚未进歌验证，无法承诺与 USC 完全相同或保证提升 FPS。";
    void ValidateHook()
    {
        var doc = XDocument.Load(game.PathOf(Game.XmlRel));
        var options = doc.Descendants("game").Single(e => (string?)e.Attribute("name") == "Sound Voltex").Element("options")!;
        bool injected = options.Elements("option").Any(e => new[] { "k", "z" }.Contains((string?)e.Attribute("name")) && ((string?)e.Attribute("value") ?? "").Contains("ifs_hook.dll", StringComparison.OrdinalIgnoreCase));
        if (!injected) throw new IOException("未配置 ifs_hook.dll，资源覆盖不会生效。请先恢复 LAZY 的 IFS Hook 注入设置。");
    }
    void CheckConflicts(IEnumerable<EffectItem> items)
    {
        if (!Directory.Exists(modRoot)) return;
        var containers = items.Select(i => i.Output[..i.Output.IndexOf("/geo/", StringComparison.Ordinal)]).Distinct().ToArray();
        foreach (string mod in Directory.GetDirectories(modRoot))
        {
            string name = Path.GetFileName(mod);
            if (Groups.Any(g => name == "zzzz_sdvx_smooth_" + g) || name == "_cache") continue;
            foreach (var container in containers)
            {
                string ifs = container[..^4] + ".ifs";
                if (File.Exists(Safe(mod, ifs)) || Directory.Exists(Safe(mod, container)))
                    throw new IOException("其他资源模组覆盖相同容器，为避免冲突已停止：" + name + "/" + container);
            }
        }
    }
    static string FileHash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    void Verify(string folder, string group, EffectCatalog? rules = null)
    {
        CheckMarker(folder, group);
        var items = (rules ?? catalog).Items.Where(i => i.Group == group).ToArray();
        if (Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length != items.Length + 1) throw new IOException("特效目录含新增或缺失的文件，已保留目录并停止操作。");
        foreach (var item in items)
        {
            string file = Safe(folder, item.Output);
            if (!File.Exists(file) || new FileInfo(file).Length != item.Size || FileHash(file) != item.AfterHash)
                throw new IOException("特效文件已被修改，已停止操作并保留文件：" + item.Output);
        }
    }
    IEnumerable<EffectItem> RetiredFeedback(EffectCatalog legacy)
    {
        var kept = catalog.Items.Select(i => i.Output).ToHashSet(StringComparer.Ordinal);
        return legacy.Items.Where(old => !kept.Contains(old.Output));
    }
    public bool NeedsFeedbackUpgrade()
    {
        if (catalog.Version >= 2) return false;
        var legacy = EffectCatalog.Load(true);
        return RetiredFeedback(legacy).Any(i => File.Exists(Safe(Active(i.Group), i.Output)) || File.Exists(Safe(Parked(i.Group), i.Output)));
    }
    public bool UpgradeFeedback(EffectCatalog? oldRules = null)
    {
        if (catalog.Version >= 2 && oldRules == null) return false;
        guard(); var legacy = oldRules ?? EffectCatalog.Load(true);
        var retired = RetiredFeedback(legacy).ToArray();
        var moves = new List<(string Source, string Destination)>();
        string archive = Safe(store, "feedback-backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        foreach (var group in new[] { "hit", "lane" })
        {
            foreach (var folder in new[] { Active(group), Parked(group) })
            {
                var entries = retired.Where(i => i.Group == group && File.Exists(Safe(folder, i.Output))).ToArray();
                if (entries.Length == 0) continue;
                CheckMarker(folder, group);
                var known = legacy.Items.Where(i => i.Group == group).ToDictionary(i => i.Output, StringComparer.Ordinal);
                // Allow already withdrawn files after an interrupted upgrade, but preserve all current-rule files.
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    if (relative == Marker) continue;
                    if (!known.TryGetValue(relative, out var expected) || FileHash(Safe(folder, relative)) != expected.AfterHash)
                        throw new IOException("旧版特效文件被改动，已保留现状并停止升级：" + relative);
                }
                foreach (var required in catalog.Items.Where(i => i.Group == group))
                    if (!File.Exists(Safe(folder, required.Output))) throw new IOException("旧版特效目录缺少文件，已停止升级。");
                foreach (var item in entries)
                    moves.Add((Safe(folder, item.Output), Safe(archive, group + "/" + item.Output)));
            }
        }
        if (moves.Count == 0) return false;
        guard(); var completed = new List<(string Source, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                File.Move(move.Source, move.Destination); completed.Add(move);
            }
        }
        catch
        {
            foreach (var move in completed.AsEnumerable().Reverse()) File.Move(move.Destination, move.Source);
            throw;
        }
        return true;
    }
    public static byte[] StripGeometry(byte[] original)
    {
        bool big = original.Length >= 52 && original.AsSpan(0, 4).SequenceEqual("GE2D"u8);
        bool little = original.Length >= 52 && original.AsSpan(0, 4).SequenceEqual("D2EG"u8);
        if (!big && !little) throw new IOException("特效几何格式不支持。");
        uint U32(int off) => big ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(off, 4)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(off, 4));
        ushort count = big ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(28, 2)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(28, 2));
        if (U32(12) != original.Length || (long)U32(48) + count * 16L > original.Length) throw new IOException("特效几何边界校验失败。");
        ushort vertices = big ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(20, 2)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(20, 2));
        uint vertexOffset = U32(32);
        if (count == 0 || vertices == 0 || vertexOffset < 52 || (long)vertexOffset + vertices * 8L > original.Length) throw new IOException("无效的几何顶点流或绘制计数。");
        int start = checked((int)vertexOffset);
        for (int coordinate = 0; coordinate < 2; coordinate++)
        {
            int bits = big ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(start + coordinate * 4, 4)) : System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(start + coordinate * 4, 4));
            if (!float.IsFinite(BitConverter.Int32BitsToSingle(bits))) throw new IOException("几何顶点包含无效数值。");
        }
        byte[] result = (byte[])original.Clone();
        for (int i = 1; i < vertices; i++) original.AsSpan(start, 8).CopyTo(result.AsSpan(start + i * 8, 8));
        return result;
    }
    void Prepare(string group, IProgress<string>? progress)
    {
        var items = catalog.Items.Where(i => i.Group == group).ToArray();
        var sources = catalog.Sources.Where(s => items.Any(i => i.Source == s.Path)).ToArray();
        foreach (var source in sources)
        {
            progress?.Report("核对原始资源：" + Path.GetFileName(source.Path));
            string path = Safe(Path.Combine(game.Root, "contents", "data"), source.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != source.Size || FileHash(path) != source.Sha256)
                throw new IOException("原资源与本版特效规则不一致，未启用：" + source.Path);
        }
        if (Directory.Exists(Parked(group))) { Verify(Parked(group), group); return; }
        string stage = Safe(store, "staging/" + group + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        int completed = 0;
        foreach (var source in sources)
        {
            using var stream = File.OpenRead(Safe(Path.Combine(game.Root, "contents", "data"), source.Path));
            foreach (var item in items.Where(i => i.Source == source.Path))
            {
                if (item.Size < 52 || item.Offset < 0 || item.Offset > stream.Length - item.Size) throw new IOException("特效目录条目无效。");
                stream.Position = item.Offset; byte[] original = new byte[item.Size]; stream.ReadExactly(original);
                if (Game.Hash(original) != item.BeforeHash) throw new IOException("特效资源在校验后发生变化。");
                byte[] patched = StripGeometry(original);
                if (Game.Hash(patched) != item.AfterHash) throw new IOException("特效补丁结果校验失败。");
                string target = Safe(stage, item.Output); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, patched);
                if (++completed % 250 == 0) progress?.Report($"生成 {Title(group)}：{completed}/{items.Length}");
            }
        }
        File.WriteAllText(Safe(stage, Marker), $"sdvx-smooth-effects-v{catalog.Version}:{group}");
        Directory.CreateDirectory(Path.GetDirectoryName(Parked(group))!);
        Directory.Move(stage, Parked(group));
    }
    public string Apply(EffectMode desired, IProgress<string>? progress = null)
    {
        guard(); bool upgraded = UpgradeFeedback(); var before = Read();
        var changed = Groups.Where(g => before.Enabled(g) != desired.Enabled(g)).ToArray();
        if (changed.Length == 0) return upgraded ? "已撤回旧版的按键命中、长按及按键光柱覆盖；原版命中反馈将在下次启动恢复。背景精简保持原设置。" : "特效模式没有变化。";
        if (changed.Any(desired.Enabled))
        {
            if (game.Identifier() != catalog.GameIdentifier) throw new IOException("特效目录仅支持已核对的游戏 DLL：" + catalog.GameIdentifier);
            ValidateHook(); CheckConflicts(catalog.Items.Where(i => desired.Enabled(i.Group)));
        }
        foreach (var group in changed)
        {
            if (desired.Enabled(group)) Prepare(group, progress);
            else Verify(Active(group), group);
        }
        guard();
        if (Read() != before) throw new IOException("生成期间特效状态发生变化，请重新读取。");
        Directory.CreateDirectory(modRoot); Directory.CreateDirectory(History);
        var journal = new EffectTransaction { Root = game.Root, Before = before, After = desired };
        string log = Safe(History, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        void Save() => File.WriteAllText(log, JsonSerializer.Serialize(journal, new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }));
        Save(); var done = new List<string>();
        try
        {
            foreach (var group in changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Parked(group))!);
                Directory.Move(desired.Enabled(group) ? Parked(group) : Active(group), desired.Enabled(group) ? Active(group) : Parked(group));
                done.Add(group);
            }
            journal.State = "applied"; Save();
        }
        catch
        {
            foreach (var group in done.AsEnumerable().Reverse()) Directory.Move(desired.Enabled(group) ? Active(group) : Parked(group), desired.Enabled(group) ? Parked(group) : Active(group));
            journal.State = "rolled-back"; Save(); throw;
        }
        return "游戏内特效模式已保存，下次启动生效。\r\n还原画面：取消全部特效勾选并应用。\r\n原始 IFS、谱面、音频和 DLL 均未修改。";
    }
}

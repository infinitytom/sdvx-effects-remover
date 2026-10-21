using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SdvxSmooth;

static class Program
{
    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SdvxSmooth", "game-path.txt");
    public static string DefaultRoot { get; } = FindGameRoot();
    static string FindGameRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("SDVX_GAME_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        try { if (File.Exists(SettingsPath)) return File.ReadAllText(SettingsPath).Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Path.Combine(AppContext.BaseDirectory, "LAZY_KFC");
    }
    public static void RememberRoot(string root)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, Path.GetFullPath(root)); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--restore-effects"))
        {
            try
            {
                var effects = new Effects(new Game(DefaultRoot), EffectCatalog.LoadUnsafe());
                string result = effects.Apply(EffectMode.Original);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restore-result.txt"), result);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restore-result.txt"), e.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Contains("--self-test")) { Tests.Run(); return; }
        if (args.Contains("--inspect"))
        {
            var root = args.SkipWhile(a => a != "--inspect").Skip(1).FirstOrDefault() ?? DefaultRoot;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "inspection.txt"), new Game(root).Describe());
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--render-preview"))
        {
            using var form = new MainForm(); form.RenderPreview(); return;
        }
        Application.Run(new MainForm());
    }
}

record Choices(bool Redraw, bool ReducedLogs, bool Overlay, int Fps);
record Change(string Relative, byte[] Before, byte[] After);
record Entry(string Relative, string BeforeHash, string AfterHash);
sealed class Journal
{
    public string Root { get; set; } = "";
    public string State { get; set; } = "pending";
    public List<Entry> Files { get; set; } = new();
}

sealed class Game
{
    public const string XmlRel = "contents/lazy/spicetools.xml";
    public const string PatchRel = "contents/lazy/spicetools_patch_manager.json";
    public string Root { get; }
    readonly Action ensureStopped;
    public string Backups => Path.Combine(Root, ".sdvx-smooth", "backups");
    public Game(string root, Action? processGuard = null)
    {
        ensureStopped = processGuard ?? EnsureStopped;
        Root = Path.GetFullPath(root.Trim());
        foreach (var file in new[] { XmlRel, PatchRel, "contents/modules/soundvoltex.dll", "contents/spice64.exe" })
            if (!File.Exists(PathOf(file))) throw new IOException("不是支持的 LAZY_KFC 目录，缺少：" + file);
        _ = Options(XDocument.Load(PathOf(XmlRel)));
    }
    public string PathOf(string relative)
    {
        if (relative != XmlRel && relative != PatchRel && relative != "contents/modules/soundvoltex.dll" && relative != "contents/spice64.exe")
            throw new IOException("不允许访问此配置路径。");
        return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    static XElement Options(XDocument doc) => doc.Root?.Elements("game").SingleOrDefault(g => (string?)g.Attribute("name") == "Sound Voltex")?.Element("options")
        ?? throw new IOException("未找到唯一的 Sound Voltex 配置。");
    static string Value(XElement o, string key) => (string?)o.Elements("option").LastOrDefault(e => (string?)e.Attribute("name") == key)?.Attribute("value") ?? "";
    static void Set(XElement o, string key, string value)
    {
        var matches = o.Elements("option").Where(e => (string?)e.Attribute("name") == key).ToList();
        if (matches.Count == 0) o.Add(new XElement("option", new XAttribute("name", key), new XAttribute("value", value)));
        else foreach (var e in matches) e.SetAttributeValue("value", value);
    }
    public Choices Read()
    {
        var o = Options(XDocument.Load(PathOf(XmlRel)));
        return new(Value(o, "sp2x-sdvxsubredraw") == "/ENABLED" || Value(o, "sdvxsubredraw") == "/ENABLED",
            Value(o, "loglevel") == "warning", Value(o, "overlaydisable") != "/ENABLED", 0);
    }
    public string Identifier()
    {
        using var reader = new BinaryReader(File.OpenRead(PathOf("contents/modules/soundvoltex.dll")));
        if (reader.ReadUInt16() != 0x5a4d) throw new IOException("游戏 DLL 的 PE 格式无效。");
        reader.BaseStream.Position = 0x3c; int pe = reader.ReadInt32();
        if (pe < 64 || pe > reader.BaseStream.Length - 64) throw new IOException("游戏 DLL 的 PE 头无效。");
        reader.BaseStream.Position = pe; if (reader.ReadUInt32() != 0x4550) throw new IOException("游戏 DLL 签名无效。");
        reader.BaseStream.Position = pe + 8; uint stamp = reader.ReadUInt32();
        reader.BaseStream.Position = pe + 24 + 16; uint entry = reader.ReadUInt32();
        return $"KFC-{stamp:x}_{entry:x}";
    }
    public static string PatchHash(JsonObject p, string id)
    {
        using var data = new MemoryStream();
        void Str(string s) => data.Write(Encoding.UTF8.GetBytes(s));
        Str(p["gameCode"]!.GetValue<string>());
        int min = (int?)p["dateCode"] ?? (int?)p["dateCodeMin"] ?? 0;
        int max = (int?)p["dateCode"] ?? (int?)p["dateCodeMax"] ?? 0;
        if (min != 0 || max != 0) { data.Write(BitConverter.GetBytes(min)); data.Write(BitConverter.GetBytes(max)); }
        Str((string?)p["peIdentifier"] ?? id); Str((string)p["name"]!); Str((string?)p["description"] ?? "");
        return Convert.ToHexString(SHA256.HashData(data.ToArray())).ToLowerInvariant();
    }
    JsonArray Definitions()
    {
        var path = Path.Combine(Root, "contents", "patches", Identifier() + ".json");
        if (!File.Exists(path)) throw new IOException("缺少匹配当前 DLL 的补丁定义；保持帧率不变仍可使用其他开关。");
        return JsonNode.Parse(File.ReadAllBytes(path))?.AsArray() ?? throw new IOException("补丁定义无效。");
    }
    public string FpsDescription()
    {
        try
        {
            var definitions = Definitions(); var state = JsonNode.Parse(File.ReadAllBytes(PathOf(PatchRel)))!.AsObject();
            return string.Join(" / ", new[] { "Game FPS Target", "Note FPS Target" }.Select(name => {
                var p = definitions.OfType<JsonObject>().Single(x => (string?)x["name"] == name);
                return (name.StartsWith("Game") ? "游戏：" : "音符：") + ((string?)state["union_patches_enabled"]?[PatchHash(p, Identifier())] ?? "未配置（游戏默认）");
            }));
        }
        catch (Exception e) { return "帧率识别失败：" + e.Message; }
    }
    public string Describe()
    {
        var o = Options(XDocument.Load(PathOf(XmlRel)));
        string hooks = string.Join(" ", new[] { Value(o, "k"), Value(o, "z") }).Trim();
        return "DLL 版本：" + Identifier() + "\r\n" + FpsDescription() + "\r\n配置的注入组件：" + hooks +
            "\r\nIFS Hook / Omnimix：保留，关闭可能影响额外曲库。\r\nnear_link / shader_fix：仅有文件不等于已启用，本工具不移动或删除 DLL。\r\n" +
            "开关在下次启动游戏时生效。请先退出游戏和 LAZY 启动器再应用。\r\n减少附加开销不保证达到目标 FPS；请用相同歌曲比较帧时间和声音同步。";
    }
    public List<Change> Plan(Choices choice)
    {
        var changes = new List<Change>();
        byte[] before = File.ReadAllBytes(PathOf(XmlRel));
        using var source = new MemoryStream(before);
        var doc = XDocument.Load(source, LoadOptions.PreserveWhitespace); var o = Options(doc);
        var current = Read();
        if (current.Redraw != choice.Redraw)
        {
            Set(o, "sp2x-sdvxsubredraw", choice.Redraw ? "/ENABLED" : "");
            foreach (var alias in o.Elements("option").Where(e => (string?)e.Attribute("name") == "sdvxsubredraw")) alias.SetAttributeValue("value", choice.Redraw ? "/ENABLED" : "");
        }
        if (current.ReducedLogs != choice.ReducedLogs) Set(o, "loglevel", choice.ReducedLogs ? "warning" : "");
        if (current.Overlay != choice.Overlay) Set(o, "overlaydisable", choice.Overlay ? "" : "/ENABLED");
        if (current != choice with { Fps = 0 })
        {
            using var dest = new MemoryStream(); doc.Save(dest, SaveOptions.DisableFormatting);
            changes.Add(new(XmlRel, before, dest.ToArray()));
        }
        if (choice.Fps != 0)
        {
            if (choice.Fps != 60 && choice.Fps != 120) throw new IOException("只支持 60 / 120 FPS。");
            var definitions = Definitions(); string id = Identifier();
            var original = File.ReadAllBytes(PathOf(PatchRel)); var state = JsonNode.Parse(original)!.AsObject();
            var unions = state["union_patches_enabled"]?.AsObject() ?? throw new IOException("帧率配置结构不受支持。");
            byte[] dll = File.ReadAllBytes(PathOf("contents/modules/soundvoltex.dll"));
            foreach (string name in new[] { "Game FPS Target", "Note FPS Target" })
            {
                var p = definitions.OfType<JsonObject>().Single(x => (string?)x["name"] == name && (string?)x["type"] == "union");
                var variants = p["patches"]!.AsArray().OfType<JsonObject>().ToArray();
                string target = choice.Fps == 60 ? "60 FPS (Default)" : "120 FPS";
                _ = variants.Single(x => (string?)x["name"] == target);
                // Refuse mismatched/prepatched binaries rather than writing unknown patches.
                if (!variants.Any(v => {
                    var patch = v["patch"]!;
                    if ((string?)patch["dllName"] != "soundvoltex.dll") return false;
                    int offset = (int)patch["offset"]!; byte[] bytes = Convert.FromHexString((string)patch["data"]!);
                    return offset >= 0 && offset <= dll.Length - bytes.Length && dll.AsSpan(offset, bytes.Length).SequenceEqual(bytes);
                })) throw new IOException("DLL 与帧率补丁字节不匹配，已拒绝修改。其他开关可单独应用。");
                unions[PatchHash(p, id)] = target;
            }
            // Preserve the game's existing auto-apply list; add only its current full identity.
            var ident = XDocument.Load(Path.Combine(Root, "contents", "prop", "ea3-ident.xml")).Root!.Element("soft")!;
            string fullId = string.Join(":", new[] { "model", "dest", "spec", "rev", "ext" }.Select(k => ident.Element(k)?.Value ?? throw new IOException("缺少版本标识。")));
            var auto = state["auto_apply"]?.AsArray() ?? throw new IOException("缺少自动应用列表。");
            if (!auto.Any(x => (string?)x == fullId)) auto.Add(fullId);
            var after = Encoding.UTF8.GetBytes(state.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }));
            if (!JsonNode.DeepEquals(JsonNode.Parse(original), state)) changes.Add(new(PatchRel, original, after));
        }
        return changes;
    }
    public static void EnsureStopped()
    {
        foreach (var name in new[] { "spice64", "spice", "spicecfg", "LazyBootstrap", "启动" })
            foreach (var process in Process.GetProcessesByName(name))
            { using (process) throw new IOException("请先关闭游戏及 LAZY 启动器（检测到 " + name + "），避免配置被覆盖。"); }
    }
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default) { WriteIndented = true };
    static void SaveJournal(string folder, Journal journal)
    {
        string target = Path.Combine(folder, "journal.json");
        string temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temp, target, true);
    }
    public string Apply(List<Change> changes)
    {
        ensureStopped();
        if (changes.Count == 0) return "没有需要更改的设置。";
        string folder = Path.Combine(Backups, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        var journal = new Journal { Root = Root };
        foreach (var c in changes)
        {
            string backup = Path.Combine(folder, c.Relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.WriteAllBytes(backup, c.Before);
            journal.Files.Add(new(c.Relative, Hash(c.Before), Hash(c.After)));
        }
        SaveJournal(folder, journal);
        var opened = new List<(Change Change, FileStream File)>();
        var touched = new HashSet<string>();
        try
        {
            foreach (var c in changes)
            {
                var stream = new FileStream(PathOf(c.Relative), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                opened.Add((c, stream)); using var copy = new MemoryStream(); stream.CopyTo(copy);
                if (!copy.ToArray().SequenceEqual(c.Before)) throw new IOException("配置在预览后发生变化，请重新读取。");
            }
            ensureStopped();
            foreach (var (c, stream) in opened) { touched.Add(c.Relative); stream.Position = 0; stream.Write(c.After); stream.SetLength(c.After.Length); stream.Flush(true); }
            journal.State = "applied"; SaveJournal(folder, journal);
        }
        catch
        {
            foreach (var (c, stream) in opened)
            {
                // Do not overwrite a file that failed the pre-write conflict check.
                if (touched.Contains(c.Relative)) { stream.Position = 0; stream.Write(c.Before); stream.SetLength(c.Before.Length); stream.Flush(true); }
            }
            throw;
        }
        finally { foreach (var item in opened) item.File.Dispose(); }
        return "设置已保存，下次启动生效。\r\n备份：" + folder;
    }
    public string Restore(string folder)
    {
        ensureStopped();
        string full = Path.GetFullPath(folder);
        if (!full.StartsWith(Path.GetFullPath(Backups) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("无效的备份目录。");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(Path.Combine(full, "journal.json"))) ?? throw new IOException("备份记录无效。");
        if (!string.Equals(journal.Root, Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("备份属于其他游戏目录。");
        if (journal.State == "restored") throw new IOException("此备份已经恢复。");
        var changes = new List<Change>();
        foreach (var entry in journal.Files)
        {
            var live = File.ReadAllBytes(PathOf(entry.Relative));
            var backup = File.ReadAllBytes(Path.Combine(full, entry.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (Hash(backup) != entry.BeforeHash) throw new IOException("备份校验失败，已停止恢复。");
            if (Hash(live) == entry.BeforeHash) continue;
            if (Hash(live) != entry.AfterHash) throw new IOException("配置在应用后被其他程序修改。为保留新配置，已停止恢复；请手动比较备份。");
            changes.Add(new(entry.Relative, live, backup));
        }
        // Recovery itself is a backed-up transaction, so it can also be undone.
        Apply(changes); journal.State = "restored"; SaveJournal(full, journal);
        return "已恢复选定备份，其他配置未覆盖。";
    }
}

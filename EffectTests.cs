using System.Security.Cryptography;

namespace SdvxSmooth;

static class EffectTests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Refuses(Action action)
    {
        try { action(); } catch (IOException) { return; } throw new Exception("Effect conflict was not refused");
    }
    public static void Run(string fixtureRoot, List<string> results)
    {
        var game = new Game(fixtureRoot, () => { });
        byte[] geometry = new byte[160]; "GE2D"u8.CopyTo(geometry); geometry[15] = 160; geometry[21] = 4; geometry[29] = 1; geometry[35] = 64; geometry[51] = 112;
        for (int i = 0; i < 4; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(geometry.AsSpan(64 + i * 8, 4), BitConverter.SingleToInt32Bits(i % 2 * 10f));
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(geometry.AsSpan(68 + i * 8, 4), BitConverter.SingleToInt32Bits(i / 2 * 10f));
        }
        byte[] broken = (byte[])geometry.Clone(); broken[29] = 0;
        Refuses(() => Effects.StripGeometry(broken));
        byte[] source = new byte[400]; geometry.CopyTo(source, 100);
        string sourcePath = Path.Combine(fixtureRoot, "contents", "data", "graphics", "fixture.ifs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!); File.WriteAllBytes(sourcePath, source);
        var catalog = new EffectCatalog { Version = 2, GameIdentifier = game.Identifier() };
        catalog.Sources.Add(new("graphics/fixture.ifs", Game.Hash(source), source.Length));
        foreach (string group in Effects.Groups)
            catalog.Items.Add(new(group, "graphics/fixture.ifs", 100, geometry.Length, $"graphics/fixture_ifs/geo/{group}_shape5", Game.Hash(geometry), Game.Hash(Effects.StripGeometry(geometry))));
        var engine = new Effects(game, catalog, () => { });
        var all = new EffectMode(true, true, true, true, true); var original = new EffectMode(false, false, false, false, false);
        Check(engine.Read() == original, "fixture initially enabled");
        engine.Apply(all); Check(engine.Read() == all, "effects not enabled");
        Check(File.ReadAllBytes(sourcePath).SequenceEqual(source), "original effects resource changed");
        foreach (var item in catalog.Items)
        {
            byte[] modified = File.ReadAllBytes(Path.Combine(fixtureRoot, "contents", "data_mods", "zzzz_sdvx_smooth_" + item.Group, item.Output));
            Check(modified[28] == 0 && modified[29] == 1, "native draw-count invariant broken");
            Check(modified.AsSpan(0, 64).SequenceEqual(geometry.AsSpan(0, 64)) && modified.AsSpan(96).SequenceEqual(geometry.AsSpan(96)), "non-vertex data changed");
            for (int i = 0; i < 4; i++) Check(modified.AsSpan(64 + i * 8, 8).SequenceEqual(geometry.AsSpan(64, 8)), "vertices not degenerate");
        }
        engine.Apply(all); engine.Apply(original); Check(engine.Read() == original, "effects restore failed");
        engine.Apply(all); Check(engine.Read() == all, "effects re-enable failed");
        var showJudgment = all with { Judgment = false };
        engine.Apply(showJudgment); Check(engine.Read() == showJudgment, "judgments cannot be restored independently");
        Check(engine.Read().Background && engine.Read().Extra && engine.Read().Hit, "judgment restore changed other groups");
        engine.Apply(all);
        results.Add("PASS: zero-draw-count regression refused; native draw count stays 1; only vertex positions change; overrides apply/restore with original data byte-exact");
        results.Add("PASS: judgment display toggles independently while background and other effects remain simplified");
        var legacy = new EffectCatalog { Version = catalog.Version, GameIdentifier = catalog.GameIdentifier, Sources = catalog.Sources, Items = catalog.Items.ToList() };
        var retired = catalog.Items[1] with { Output = "graphics/fixture_ifs/geo/old_button_hit_shape5" };
        legacy.Items.Add(retired);
        string retiredPath = Path.Combine(fixtureRoot, "contents", "data_mods", "zzzz_sdvx_smooth_hit", retired.Output);
        File.WriteAllBytes(retiredPath, Effects.StripGeometry(geometry));
        Check(engine.UpgradeFeedback(legacy), "old feedback override not withdrawn");
        Check(!File.Exists(retiredPath) && engine.Read() == all, "upgrade changed mode or kept old feedback override");
        Check(!engine.UpgradeFeedback(legacy), "upgrade not idempotent");
        Check(Directory.GetFiles(Path.Combine(fixtureRoot, ".sdvx-smooth", "effects-v2", "feedback-backups"), "old_button_hit_shape5", SearchOption.AllDirectories).Length == 1, "withdrawn override not archived");
        results.Add("PASS: legacy hit overrides withdrawn into backup; existing effects mode preserved; upgrade idempotent");
        string hitFile = Path.Combine(fixtureRoot, "contents", "data_mods", "zzzz_sdvx_smooth_hit", catalog.Items[1].Output);
        File.AppendAllText(hitFile, "changed externally");
        Refuses(() => engine.Apply(original)); Check(engine.Read() == all, "partial disable before validation");
        File.WriteAllBytes(hitFile, Effects.StripGeometry(geometry)); engine.Apply(original);
        string conflict = Path.Combine(fixtureRoot, "contents", "data_mods", "another_mod", "graphics", "fixture.ifs");
        Directory.CreateDirectory(Path.GetDirectoryName(conflict)!); File.WriteAllBytes(conflict, source);
        Refuses(() => engine.Apply(all)); Check(engine.Read() == original, "conflicting override enabled");
        File.Delete(conflict);
        source[0] = 1; File.WriteAllBytes(sourcePath, source);
        Refuses(() => engine.Apply(all)); Check(engine.Read() == original, "parked effects bypassed source validation");
        source[0] = 0; File.WriteAllBytes(sourcePath, source);
        results.Add("PASS: changed mod files and overlapping mods are refused before changing active mode");
        // Independently re-read every source and every geometry in the actual install, without writing it.
        if (Directory.Exists(Program.DefaultRoot))
        {
            byte[] renderer = File.ReadAllBytes(Path.Combine(Program.DefaultRoot, "contents", "modules", "afp-utils.dll"));
            int nativeCheck = renderer.AsSpan().IndexOf(Convert.FromHexString("66837E1C01"));
            Check(nativeCheck >= 0 && renderer.AsSpan(nativeCheck, Math.Min(64, renderer.Length - nativeCheck)).IndexOf(Convert.FromHexString("BA7B040000")) >= 0, "native line-1147 draw-count guard changed; re-audit renderer");
            results.Add("PASS: installed native renderer has cmp [geometry+0x1c],1 before line-1147 assertion; corrected patches preserve that field");
            var actual = EffectCatalog.Load(); string dataRoot = Path.Combine(Program.DefaultRoot, "contents", "data");
            Check(actual.Items.Where(i => i.Group == "hit").All(i => Path.GetFileName(i.Output).StartsWith("efhg_dust_")), "button/hold feedback targeted");
            Check(actual.Items.All(i => !i.Source.EndsWith("/game_lane.ifs")), "keybeam package targeted");
            Check(actual.Items.Any(i => i.Group == "judgment" && i.Source.EndsWith("/hantei.ifs")), "judgment banners not covered");
            Check(actual.Items.Any(i => i.Group == "judgment" && i.Output.Contains("x_crifont")), "critical text not covered");
            var protectedOutputs = actual.ProtectedOutputs.ToHashSet();
            Check(protectedOutputs.Count > 100 && actual.Items.All(i => !protectedOutputs.Contains(i.Output)), "required hit/hold/cursor geometry targeted");
            int count = 0;
            foreach (var package in actual.Sources)
            {
                using var stream = File.OpenRead(Path.Combine(dataRoot, package.Path));
                Check(Convert.ToHexString(SHA256.HashData(stream)) == package.Sha256, "source hash mismatch");
                foreach (var item in actual.Items.Where(i => i.Source == package.Path))
                {
                    stream.Position = item.Offset; byte[] bytes = new byte[item.Size]; stream.ReadExactly(bytes);
                    Check(Game.Hash(bytes) == item.BeforeHash, "geometry source mismatch");
                    byte[] stripped = Effects.StripGeometry(bytes); Check(Game.Hash(stripped) == item.AfterHash, "patched geometry mismatch");
                    int offset = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(32, 4)));
                    int vertices = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(20, 2));
                    Check(bytes.AsSpan(0, offset).SequenceEqual(stripped.AsSpan(0, offset)) && bytes.AsSpan(offset + vertices * 8).SequenceEqual(stripped.AsSpan(offset + vertices * 8)), "geometry header, draw records or other stream modified");
                    Check(!item.Output.Contains("game_notes") && !item.Output.Contains("vol_cursor"), "protected geometry included");
                    Check(!item.Output.Contains("hantei") || item.Group == "judgment", "judgment controlled by wrong group");
                    ++count;
                }
            }
            results.Add($"PASS: all {count} actual geometry patches validated read-only across {actual.Sources.Count} IFS files; {protectedOutputs.Count} hit/hold/cursor entries excluded, judgments independently controlled");
        }
    }
}

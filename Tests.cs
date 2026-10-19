using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SdvxSmooth;

static class Tests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Refuses(Action action, string message)
    {
        try { action(); } catch (IOException) { return; } throw new Exception(message);
    }
    public static void Run()
    {
        string log = Path.Combine(AppContext.BaseDirectory, "self-test.txt");
        var results = new List<string>();
        string temporary = Path.Combine(Path.GetTempPath(), "sdvx-smooth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            // Synthetic PE + configuration: no game file is ever written by these tests.
            foreach (string rel in new[] { Game.XmlRel, Game.PatchRel, "contents/modules/soundvoltex.dll", "contents/spice64.exe", "contents/patches/KFC-1234_5678.json", "contents/prop/ea3-ident.xml" })
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temporary, rel))!);
            byte[] pe = new byte[1024]; pe[0] = 0x4d; pe[1] = 0x5a;
            BitConverter.GetBytes(128).CopyTo(pe, 60); BitConverter.GetBytes(0x4550).CopyTo(pe, 128);
            BitConverter.GetBytes(0x1234u).CopyTo(pe, 136); BitConverter.GetBytes(0x5678u).CopyTo(pe, 168);
            pe[700] = 0x4e; pe[710] = 0x60;
            File.WriteAllBytes(Path.Combine(temporary, "contents/modules/soundvoltex.dll"), pe);
            File.WriteAllBytes(Path.Combine(temporary, "contents/spice64.exe"), new byte[] { 0 });
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><games><!--原配置--><game name=\"Other\"><options><option name=\"loglevel\" value=\"all\"/></options></game><game name=\"Sound Voltex\"><buttons><button name=\"BT-A\" vkey=\"68\"/></buttons><options><option name=\"k\" value=\"ifs_hook.dll\"/><option name=\"sp2x-sdvxsubredraw\" value=\"/ENABLED\"/><option name=\"custom\" value=\"keep\"/></options></game></games>";
            File.WriteAllText(Path.Combine(temporary, Game.XmlRel), xml, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temporary, Game.PatchRel), "{\"auto_apply\":[],\"patches_enabled\":[\"keep\"],\"union_patches_enabled\":{},\"unknown\":123}");
            File.WriteAllText(Path.Combine(temporary, "contents/prop/ea3-ident.xml"), "<ea3_conf><soft><model>KFC</model><dest>J</dest><spec>G</spec><rev>A</rev><ext>2026091500</ext></soft></ea3_conf>");
            var defs = new JsonArray();
            foreach (var (name, offset, low, high) in new[] { ("Game FPS Target", 700, "4E", "5E"), ("Note FPS Target", 710, "60", "78") })
            {
                var variants = new JsonArray();
                foreach (var (label, data) in new[] { ("60 FPS (Default)", low), ("120 FPS", high) })
                    variants.Add(new JsonObject { ["name"] = label, ["patch"] = new JsonObject { ["offset"] = offset, ["dllName"] = "soundvoltex.dll", ["data"] = data } });
                defs.Add(new JsonObject { ["name"] = name, ["gameCode"] = "KFC", ["type"] = "union", ["description"] = name, ["patches"] = variants });
            }
            File.WriteAllText(Path.Combine(temporary, "contents/patches/KFC-1234_5678.json"), defs.ToJsonString());
            var g = new Game(temporary, () => { }); Check(g.Identifier() == "KFC-1234_5678", "PE identifier");
            results.Add("PASS: PE version matching");
            byte[] originalXml = File.ReadAllBytes(g.PathOf(Game.XmlRel)); byte[] originalPatch = File.ReadAllBytes(g.PathOf(Game.PatchRel));
            var plan = g.Plan(new(false, true, true, 60));
            Check(plan.Count == 2, "two-file plan"); Check(File.ReadAllBytes(g.PathOf(Game.XmlRel)).SequenceEqual(originalXml), "preview mutated files");
            var changedXml = XDocument.Parse(Encoding.UTF8.GetString(plan.Single(c => c.Relative == Game.XmlRel).After).TrimStart('\uFEFF'));
            Check(changedXml.Descendants("button").Single().Attribute("vkey")!.Value == "68", "input changed");
            Check(changedXml.Descendants("option").Any(e => (string?)e.Attribute("name") == "k" && (string?)e.Attribute("value") == "ifs_hook.dll"), "IFS changed");
            Check(changedXml.Descendants("option").Any(e => (string?)e.Attribute("name") == "custom" && (string?)e.Attribute("value") == "keep"), "unknown option changed");
            var changedPatch = JsonNode.Parse(plan.Single(c => c.Relative == Game.PatchRel).After)!;
            Check(changedPatch["union_patches_enabled"]!.AsObject().Count == 2, "FPS pair missing");
            Check((int?)changedPatch["unknown"] == 123 && (string?)changedPatch["patches_enabled"]![0] == "keep", "unrelated patches changed");
            results.Add("PASS: preview is read-only; input, IFS and unknown settings preserved; FPS pair updated together");
            g.Apply(plan); Check(g.Read() == new Choices(false, true, true, 0), "apply options");
            string backup = Directory.GetDirectories(g.Backups).Single(); g.Restore(backup);
            Check(File.ReadAllBytes(g.PathOf(Game.XmlRel)).SequenceEqual(originalXml), "XML restore not byte exact");
            Check(File.ReadAllBytes(g.PathOf(Game.PatchRel)).SequenceEqual(originalPatch), "JSON restore not byte exact");
            Check(File.ReadAllBytes(g.PathOf("contents/modules/soundvoltex.dll")).SequenceEqual(pe), "DLL changed");
            results.Add("PASS: apply and byte-exact restore, DLL unchanged");
            plan = g.Plan(new(false, true, true, 0)); g.Apply(plan);
            string newest = Directory.GetDirectories(g.Backups).OrderDescending().First();
            File.AppendAllText(g.PathOf(Game.XmlRel), "<!--later edit-->");
            Refuses(() => g.Restore(newest), "restore overwrote later edits");
            File.WriteAllBytes(g.PathOf(Game.XmlRel), originalXml);
            results.Add("PASS: restore refuses later external edits");
            plan = g.Plan(new(false, true, true, 60));
            File.AppendAllText(g.PathOf(Game.PatchRel), " ");
            Refuses(() => g.Apply(plan), "apply ignored preview conflict");
            Check(File.ReadAllBytes(g.PathOf(Game.XmlRel)).SequenceEqual(originalXml), "partial transaction modified XML");
            File.WriteAllBytes(g.PathOf(Game.PatchRel), originalPatch);
            results.Add("PASS: preview conflict aborts whole transaction");
            pe[700] = 0xff; File.WriteAllBytes(g.PathOf("contents/modules/soundvoltex.dll"), pe);
            Refuses(() => g.Plan(new(false, true, true, 60)), "unknown DLL accepted");
            Check(g.Plan(new(false, true, true, 0)).Count == 1, "options blocked by unknown patch bytes");
            Refuses(() => g.PathOf("../../outside"), "path traversal accepted");
            results.Add("PASS: mismatched DLL refused, normal options still usable, backup paths restricted");
            EffectTests.Run(temporary, results);
            // Read-only compatibility check against the user's real installed game.
            if (Directory.Exists(Program.DefaultRoot))
            {
                var real = new Game(Program.DefaultRoot); var realPlan = real.Plan(real.Read() with { Fps = 60 });
                Check(realPlan.Any(c => c.Relative == Game.PatchRel), "real FPS plan missing");
                var realDefs = JsonNode.Parse(File.ReadAllBytes(Path.Combine(real.Root, "contents/patches", real.Identifier() + ".json")))!.AsArray();
                var hide = realDefs.OfType<JsonObject>().Single(x => (string?)x["name"] == "Hide Timers");
                var state = JsonNode.Parse(File.ReadAllBytes(real.PathOf(Game.PatchRel)))!;
                Check(state["patches_enabled"]!.AsArray().Any(x => (string?)x == Game.PatchHash(hide, real.Identifier())), "hash incompatible with real spice2x config");
                results.Add("PASS: real installation read-only validation, FPS bytes match, patch hash agrees with enabled Hide Timers");
            }
            results.Add("All tests passed. Fixtures: " + temporary);
            File.WriteAllLines(log, results);
        }
        catch (Exception e) { results.Add("FAIL: " + e); File.WriteAllLines(log, results); Environment.ExitCode = 1; }
    }
}

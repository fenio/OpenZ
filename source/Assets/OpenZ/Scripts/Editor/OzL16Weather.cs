using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 weather, the import step (menu OpenZ/L16 Jungle/9 Weather ...). ASSETS ONLY: it changes import settings and
    /// reads; it never opens, changes or saves a scene, and it can be run as often as wanted.
    /// <list type="bullet">
    /// <item>the rain and wind sounds: each file there, each on its event of the sound table, loops streamed and the bed in
    /// stereo (the audio import rule of the project sets that from the table; a file that came in before its row is imported
    /// again);</item>
    /// <item>the plant kit: its meshes readable (the wind measures each shape from its own points), every near shape known
    /// to the wind's table (moves / stands still), none unknown;</item>
    /// <item>the shaders that were hooked compile, and carry the hooks;</item>
    /// <item>the materials: swamp and stream water on the game's stylized water (it draws the rain rings), the three ground
    /// materials on the layered ground;</item>
    /// <item>if the level 16 scene is the open scene: what stands in it (plant cells, plants, static flags), read only.</item>
    /// </list>
    /// </summary>
    public static class OzL16Weather
    {
        const string Kit = "Assets/OpenZ/Models/L16/Flora/L16_flora_kit.fbx";
        const string Amb = "Assets/OpenZ/Resources/OpenZ/Audio/Ambience/";
        const string Table = "Assets/OpenZ/Resources/OpenZ/Audio/openz_sound_events.json";
        static readonly string[] Loops = { "sd_rain_jungle_downpour_loop", "sd_rain_on_leaves_loop", "sd_rain_on_water_loop", "sd_rain_on_metal_loop", "sd_drips_after_rain_loop" };
        static readonly string[] Shots = { "sd_wind_gust_trees_a", "sd_wind_gust_trees_b", "sd_thunder_day_far_a", "sd_thunder_day_far_b" };
        static readonly string[] Events = { OzJungleWeather.BedId, OzJungleWeather.LeavesId, OzJungleWeather.WaterId, OzJungleWeather.MetalId, OzJungleWeather.DripsId, OzJungleWeather.GustId, OzJungleWeather.ThunderId };
        static readonly string[] Waiting = { OzJungleWeather.WindLoopId, OzJungleWeather.FirstDropsId, OzJungleWeather.PalmRattleId, OzJungleWeather.FlagSnapId, OzJungleWeather.PlanksId };

        /// <summary></summary>
        public static string OutDir()
        {
            string all = Path.Combine(OpenZPaths.RepoRoot, "work", "records", "milestones"), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            string dir = Path.Combine(all, stage, "J9");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [MenuItem("OpenZ/L16 Jungle/9 Weather: sounds, plant kit, shaders (assets only; no scene is changed)")]
        public static void Menu()
        {
            string text = Import();
            File.WriteAllText(Path.Combine(OutDir(), "import_report.txt"), text);
            if (text.StartsWith("OK")) Debug.Log("[OpenZ L16] " + text); else Debug.LogError("[OpenZ L16] " + text);
        }

        public static string Import()
        {
            var sb = new StringBuilder(); int ok = 0, total = 0;
            void Line(bool good, string text) { total++; if (good) ok++; sb.Append(good ? "ok    " : "WRONG ").Append(text).Append('\n'); }

            // ---- sounds
            var table = File.Exists(Table) ? JsonUtility.FromJson<SoundTable>(File.ReadAllText(Table)) : null;
            var rows = new HashSet<string>(); if (table?.events != null) foreach (var e in table.events) rows.Add(e.id);
            int rowsIn = 0; foreach (var id in Events) if (rows.Contains(id)) rowsIn++;
            Line(rowsIn == Events.Length, $"sound table: {rowsIn} of {Events.Length} rain and wind events are in ({string.Join(", ", Events)})");
            var later = new List<string>(); foreach (var id in Waiting) if (!rows.Contains(id)) later.Add(id);
            sb.Append($"      waiting for their files (named and silent in the game): {(later.Count == 0 ? "none" : string.Join(", ", later))}\n");
            int files = 0, fixedN = 0; var notes = new List<string>();
            foreach (var group in new[] { Loops, Shots })
                foreach (var name in group)
                {
                    string path = Amb + name + ".wav";
                    var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (ai == null) { notes.Add(name + " MISSING"); continue; }
                    files++;
                    bool loop = group == Loops;
                    var ss = ai.defaultSampleSettings;
                    bool want = loop ? ss.loadType == AudioClipLoadType.Streaming && !ai.forceToMono : ss.loadType == AudioClipLoadType.DecompressOnLoad;
                    if (!want && rowsIn == Events.Length)
                    {   // imported before its table row was there: once more, the project's own audio import rule sets it from the table
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        ai = AssetImporter.GetAtPath(path) as AudioImporter; ss = ai.defaultSampleSettings; fixedN++;
                        want = loop ? ss.loadType == AudioClipLoadType.Streaming && !ai.forceToMono : ss.loadType == AudioClipLoadType.DecompressOnLoad;
                    }
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    notes.Add($"{name}: {ss.loadType}, {(clip ? clip.channels + " ch, " + clip.length.ToString("0.0") + " s" : "no clip")}{(want ? "" : " NOT AS WANTED")}");
                }
            bool soundsOk = files == Loops.Length + Shots.Length && !string.Join(" ", notes).Contains("NOT AS WANTED");
            Line(soundsOk, $"sound files: {files} of {Loops.Length + Shots.Length} in {Amb} ({fixedN} imported again); loops streamed and not forced to one channel, single sounds decompressed");
            foreach (var n in notes) sb.Append("      ").Append(n).Append('\n');

            // ---- the plant kit
            var mi = AssetImporter.GetAtPath(Kit) as ModelImporter;
            if (mi == null) Line(false, "plant kit missing: " + Kit + " (step J6 brings it in)");
            else
            {
                if (!mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); sb.Append("      plant kit set readable and imported again\n"); }
                var kit = AssetDatabase.LoadAssetAtPath<GameObject>(Kit);
                int moving = 0, still = 0, unknown = 0, unreadable = 0; var unk = new List<string>(); var perKind = new SortedDictionary<string, int>();
                foreach (Transform t in kit.transform)
                {
                    if (t.name.EndsWith("_far")) continue;
                    var mf = t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                    int kind = OzFloraWindMath.KindOf(mf.sharedMesh.name, out _, out bool known);
                    if (!known) { unknown++; unk.Add(mf.sharedMesh.name); continue; }
                    if (!mf.sharedMesh.isReadable) unreadable++;
                    string kn = OzFloraWindMath.KindName(kind); perKind[kn] = perKind.TryGetValue(kn, out int c) ? c + 1 : 1;
                    if (kind == OzFloraWindMath.Still) still++; else moving++;
                }
                var kinds = new List<string>(); foreach (var kv in perKind) kinds.Add(kv.Key + " " + kv.Value);
                Line(unknown == 0 && unreadable == 0 && moving > 0, $"plant kit: {moving} shapes move in the wind, {still} stand still by the table, {unknown} unknown to the table, {unreadable} not readable ({string.Join(", ", kinds)}){(unk.Count > 0 ? "; unknown: " + string.Join(", ", unk) : "")}");
            }

            // ---- shaders
            foreach (var (name, file, mark) in new[] { ("OpenZ/TeamLit", "Assets/OpenZ/Shaders/TeamLit.shader", "OZFloraWind(pw)"), ("OpenZ/JungleGround", "Assets/OpenZ/Shaders/JungleGround.shader", "OZJWetAlbedo("),
                                                    ("", "Assets/OpenZ/Shaders/AtmosphereFullscreen.shader", "_OZFogShift.xyz") })
            {
                var sh = string.IsNullOrEmpty(name) ? AssetDatabase.LoadAssetAtPath<Shader>(file) : Shader.Find(name);
                bool hook = File.Exists(file) && File.ReadAllText(file).Contains(mark);
                bool err = sh == null || ShaderUtil.ShaderHasError(sh);
                string msgs = "";
                if (sh != null) foreach (var m in ShaderUtil.GetShaderMessages(sh)) if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) msgs += " | " + m.message + " (" + m.file + ":" + m.line + ")";
                Line(sh != null && !err && hook, $"shader {(sh ? sh.name : file)}: {(sh == null ? "NOT FOUND" : err ? "HAS ERRORS" + msgs : "compiles")}, hook {(hook ? "in" : "NOT IN")} ({mark})");
            }
            foreach (var f in new[] { "Assets/OpenZ/Shaders/OpenZFloraWind.hlsl", "Assets/OpenZ/Shaders/OpenZJungleWet.hlsl" }) Line(File.Exists(f), "shader part " + f + (File.Exists(f) ? " is there" : " MISSING"));

            // ---- materials
            foreach (var g in AssetDatabase.FindAssets("M_Water_Swamp t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); if (!m) continue;
                Line(m.shader && m.shader.name == "OpenZ/WaterStylized", $"water material {m.name}: shader {(m.shader ? m.shader.name : "none")} (the stylized water draws the rain's drop rings from the rain strength)");
            }
            foreach (var n in new[] { "M_L16_Ground", "M_Cliff_JungleRock", "M_L16_RimBed" })
                foreach (var g in AssetDatabase.FindAssets(n + " t:Material"))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); if (!m || m.name != n) continue;
                    Line(m.shader && m.shader.name == "OpenZ/JungleGround", $"ground material {m.name}: shader {(m.shader ? m.shader.name : "none")}");
                }

            // ---- the open scene, read only
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name.StartsWith("L16"))
            {
                var env = GameObject.Find("Environment"); var flora = env ? env.transform.Find("L16_Flora") : null;
                if (!flora) Line(false, "the open level 16 scene has no Environment/L16_Flora (step J6)");
                else
                {
                    int cells = 0, plants = 0, staticN = 0, moving = 0;
                    foreach (Transform c in flora)
                    {
                        if (c.GetComponent<LODGroup>()) cells++;
                        foreach (Transform p in c)
                        {
                            var mf = p.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || p.name == "FAR") continue;
                            plants++;
                            if (GameObjectUtility.AreStaticEditorFlagsSet(p.gameObject, StaticEditorFlags.BatchingStatic)) staticN++;
                            if (OzFloraWindMath.KindOf(mf.sharedMesh.name, out _, out _) != OzFloraWindMath.Still) moving++;
                        }
                    }
                    Line(staticN == 0 && moving > 0, $"open scene {scene.name}: {cells} plant cells, {plants} plants, {moving} of them move in the wind, {staticN} marked for static batching (must be 0: a batched plant cannot move)");
                }
                var at = Object.FindFirstObjectByType<Atmosphere>();
                sb.Append($"      the scene's air preset is {(at ? at.preset.ToString() : "none")} (the level's weather object sets Jungle when the level starts; the scene is not changed)\n");
            }
            else sb.Append("      the level 16 scene is not the open scene: its plants were not looked at (open it and run this again, or see the Play check)\n");

            return (ok == total ? "OK " : "PROBLEMS ") + ok + "/" + total + " lines: level 16 weather import (assets only)\n" + sb;
        }
    }
}

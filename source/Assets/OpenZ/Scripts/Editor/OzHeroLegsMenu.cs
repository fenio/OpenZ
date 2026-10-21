using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Hero legs (item 209): the switch, the data check and the Play numbers.
    /// "OpenZ/Legs/Hero legs (oz_hero_legs)": turns the switch on / off, now (OzHeroLegs.On, also in Play) and for every next start
    /// (PlayerPrefs oz_hero_legs, 1 = on, the default). Off = the heroes' legs exactly as before the component.
    /// "OpenZ/Legs/Hero legs: check data (EditMode)": both members' data load, every bone resolves on the hero prefabs, every program is
    /// complete, the state table against the grunt controller's states, the two hooks in the source, the skin copy's and the cutscene
    /// files' md5, the size line from the standing pose.
    /// and the control grunts 12 m out and back.
    /// </summary>
    public static class OzHeroLegsMenu
    {
        const string SwitchMenu = "OpenZ/Legs/Hero legs (oz_hero_legs)";
        const string Controller = "Assets/OpenZ/Animation/RobotBS01.controller";
        const string SkinCopyMd5 = "36905598a703892add2e018829954e71", AlanCutsceneMd5 = "4479cb6078f9577436bcb0e6e336a0a7", BradCutsceneMd5 = "9170544493d58b10bb9775e43a69757b";
        static readonly string[] Members = { "alan", "brad" };
        static readonly string[] Programs = { "walk", "run", "back", "strafe_l", "strafe_r", "stand", "lie", "lie_down", "get_up", "blast_airborne", "death_back", "death_side", "landing", "jump_takeoff", "jump_air", "climb_ladder", "seated_driver", "seated_gunner" };

        [MenuItem(SwitchMenu)]
        static void Toggle()
        {
            bool on = !OzHeroLegs.On;
            OzHeroLegs.On = on;
            PlayerPrefs.SetInt(OzHeroLegs.Pref, on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log("[OpenZ HeroLegs] " + (on ? "ON: the heroes' legs written from their own baked programs (no IK)" : "OFF: the heroes' legs as before") + " (" + OzHeroLegs.Pref + " = " + (on ? 1 : 0) + ", now and at every next start)");
        }

        [MenuItem(SwitchMenu, true)]
        static bool ToggleCheck() { Menu.SetChecked(SwitchMenu, OzHeroLegs.On); return true; }

        [MenuItem("OpenZ/Legs/Hero legs: check data (EditMode)")]
        static void CheckMenu() => Check();

        [MenuItem("OpenZ/Legs/Hero legs: Play numbers (60 s, watch)")]
        static void PlayWatch() => Run(false);

        [MenuItem("OpenZ/Legs/Hero legs: Play numbers (60 s, walks the heroes and the control grunts 12 m out and back)")]
        static void PlayDrive() => Run(true);

        static void Run(bool drive)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ HeroLegs] enter Play first (Level 16 from its scene, the heroes in view, a grunt squad beside them)"); return; }
            OzHeroLegsProbe.Begin(60f, drive);
        }

        static string V(bool ok, string s) => (ok ? "PASS  " : "FAIL  ") + s;

        static string Md5(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return System.BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>The data check (also callable from other editor code); returns the lines it printed.</summary>
        public static string Check()
        {
            var sb = new StringBuilder();
            OzHeroLegs.ForgetData();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            var ctrlStates = new HashSet<string>();
            if (ctrl && ctrl.layers.Length > 0) foreach (var s in ctrl.layers[0].stateMachine.states) ctrlStates.Add(s.state.name);
            sb.AppendLine(V(ctrl && ctrlStates.Count > 10, $"{Controller}: {ctrlStates.Count} base-layer states"));
            var tops = new Dictionary<string, float>();
            foreach (var m in Members)
            {
                var d = OzHeroLegs.Load(m);
                bool loads = d != null && d.Error == null && d.Programs != null && d.Programs.Length >= Programs.Length && d.Bones != null && d.Bones.Length == 12;
                sb.AppendLine(V(loads, $"{m}: legs data loads ({(d != null && d.Programs != null ? d.Programs.Length : 0)} programs, {(d != null ? d.ByState.Count : 0)} states, {(d != null && d.Bones != null ? d.Bones.Length : 0)} bones{(d != null && d.Error != null ? "; " + d.Error : "")})"));
                if (d == null || d.Programs == null) continue;
                var missing = Programs.Where(p => d.Find(p) == null).ToList();
                var bad = new List<string>();
                foreach (var p in d.Programs)
                {
                    if (p.Frames <= 0 || p.Fps <= 0 || p.NBones < 9 || p.Q == null || p.P == null) bad.Add(p.Name + ": frames / fps / bones");
                    if (p.IsGait && (p.Stride <= 0f || p.Period <= 0f || p.StanceL == null || p.StanceL.Length != 2 || p.StanceR == null || p.StanceR.Length != 2 || !p.Loop)) bad.Add(p.Name + ": gait fields");
                    if (p.IsStatic && p.Frames != 1) bad.Add(p.Name + ": a static program must have one frame");
                    if (p.IsClip && p.Fps != 24) bad.Add(p.Name + ": a clip runs at 24 fps");
                    for (int i = 0; i < p.Q.Length; i++) if (Mathf.Abs(p.Q[i].x * p.Q[i].x + p.Q[i].y * p.Q[i].y + p.Q[i].z * p.Q[i].z + p.Q[i].w * p.Q[i].w - 1f) > 1e-3f) { bad.Add(p.Name + ": a rotation is not unit length"); break; }
                }
                sb.AppendLine(V(missing.Count == 0 && bad.Count == 0, $"{m}: every program present and complete" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : "") + (bad.Count > 0 ? " (faults: " + string.Join("; ", bad) + ")" : "")));
                var noProgram = ctrlStates.Where(s => !d.StateNames.Contains(s)).OrderBy(s => s).ToList();
                var unknown = d.StateNames.Where(s => !ctrlStates.Contains(s)).ToList();
                sb.AppendLine(V(unknown.Count == 0, $"{m}: the state table names {d.StateNames.Count} controller states" + (unknown.Count > 0 ? ", NOT in the controller: " + string.Join(", ", unknown) : "") + $"; controller states with no program (the player writes nothing there): {string.Join(", ", noProgram)}"));
                var prefab = Resources.Load<GameObject>(OzHeroSquadView.PrefabDir + "Z_hero_" + m);
                if (!prefab) { sb.AppendLine(V(false, $"{m}: prefab Resources/{OzHeroSquadView.PrefabDir}Z_hero_{m} missing")); continue; }
                var an = prefab.GetComponentInChildren<Animator>(true);
                var all = an ? an.GetComponentsInChildren<Transform>(true) : new Transform[0];
                var notFound = new List<string>(); var dup = new List<string>();
                foreach (var b in d.Bones) { int n = all.Count(t => t.name == b); if (n == 0) notFound.Add(b); else if (n > 1) dup.Add(b + " x" + n); }
                sb.AppendLine(V(an && notFound.Count == 0 && dup.Count == 0, $"{m}: every bone resolves under the prefab's Animator ({(an ? an.name : "no Animator")})" + (notFound.Count > 0 ? "; not found: " + string.Join(", ", notFound) : "") + (dup.Count > 0 ? "; found more than once: " + string.Join(", ", dup) : "")));
                sb.AppendLine(V(an && an.avatar && an.avatar.isHuman, $"{m}: the prefab's Animator is humanoid (the grunt controller plays the upper body)"));
                tops[m] = d.J.standTop * d.J.modelScale;
            }
            string rl = "Assets/OpenZ/Scripts/View/RobotLegIK.cs", sv = "Assets/OpenZ/Scripts/View/OzHeroSquadView.cs";
            string rlText = File.Exists(rl) ? File.ReadAllText(rl) : "", svText = File.Exists(sv) ? File.ReadAllText(sv) : "";
            int h1 = Count(rlText, "if (OzHeroLegs.Drives(view)) { Release(); return; }"), h2 = Count(svText, "OzHeroLegs.Attach(v, member);");
            sb.AppendLine(V(h1 == 1, $"hook in {rl}: 'if (OzHeroLegs.Drives(view)) {{ Release(); return; }}' found {h1} time(s) (wanted 1, right after 'if (layer != 0 || !anim) return;')"));
            sb.AppendLine(V(h2 == 1, $"hook in {sv}: 'OzHeroLegs.Attach(v, member);' found {h2} time(s) (wanted 1, right after 'v.Bind(g, o);')"));
            string skin = Md5("Assets/OpenZ/Models/HeroSquad/Hero_Alan.fbx");
            sb.AppendLine(V(skin == SkinCopyMd5, $"Assets/OpenZ/Models/HeroSquad/Hero_Alan.fbx md5 {skin} (the skin copy: {SkinCopyMd5}; the earlier file: {AlanCutsceneMd5})"));
            string ca = Md5("Assets/OpenZ/Models/ZodBoys/Alan.fbx"), cb = Md5("Assets/OpenZ/Models/ZodBoys/Brad.fbx"), hb = Md5("Assets/OpenZ/Models/HeroSquad/Hero_Brad.fbx");
            sb.AppendLine(V(ca == AlanCutsceneMd5 && cb == BradCutsceneMd5, $"the cutscene files untouched: Models/ZodBoys/Alan.fbx {ca}, Brad.fbx {cb}"));
            sb.AppendLine(V(hb == BradCutsceneMd5, $"Models/HeroSquad/Hero_Brad.fbx untouched: {hb}"));
            if (tops.ContainsKey("alan") && tops.ContainsKey("brad") && tops["brad"] > 0f)
            {
                float ratio = tops["alan"] / tops["brad"];
                sb.AppendLine(V(ratio >= 0.90f && ratio <= 0.98f, $"size line from the standing pose: Alan {tops["alan"]:0.00} m over Brad {tops["brad"]:0.00} m = {ratio:0.000} (wanted 0.90-0.98; outside: Brad's Model scale is the one number to set, then OpenZ/Hero Squad/3 Check again)"));
            }
            sb.AppendLine($"switch now: oz_hero_legs {(OzHeroLegs.On ? "ON" : "OFF")} (PlayerPrefs {PlayerPrefs.GetInt(OzHeroLegs.Pref, 1)}), foot lock v2 {(RobotLegIK.FootLockV2 ? "ON" : "OFF")}");
            string text = sb.ToString();
            Debug.Log("[OpenZ HeroLegs] check data:\n" + text);
            return text;
        }

        static int Count(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}

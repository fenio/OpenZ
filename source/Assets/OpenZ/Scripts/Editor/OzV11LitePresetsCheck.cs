using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the lighter MEDIUM / LOW presets (View/OzLitePresets.cs, View/OzLiteTable.cs). Editor only.
    /// Menu: OpenZ/V1.1/Check Lighter Presets (f41, Play mode). Start Play IN A LEVEL WITH GRAPHICS QUALITY ON HIGH and run it
    /// before anything else changes the preset: its first lines prove that nothing of the lighter presets has run on HIGH.
    /// It ends with one log line "[OpenZ lighter presets] PASS n/n checks (...)" or "... FAIL ..." and writes check_LEVEL.txt into
    /// the kit's record folder (OzV11LitePresetsMeasure.OutDir). The rules stand still while it runs (about 40 seconds); the
    /// preset, the pause, the camera and the day / night clock are put back.
    ///  H  HIGH is what it was: the two settings files of HIGH byte for byte (SHA-256 against the numbers taken when this change
    ///     was written), and 12 values read out of the file
    ///  K  the three one-line hooks stand in the two game files, each once
    ///  S  the session came up on HIGH and no line of the lighter presets has run (counter 0, no object), also 60 frames later
    ///  M  MEDIUM: every lowered number is the table's; the shadow reach is the table's rule at three camera heights
    ///  L  LOW: the same with LOW's numbers; one shadow step, in the game and in LOW's settings file
    ///  A  LOW with every cut switched off (the measuring switch): every number is HIGH's again
    ///  N  at night, in the game's own night-light budget: after HIGH to MEDIUM and after HIGH to LOW the budget grants no more
    ///     real lights than the preset allows, and no more nearby lights hold a shadow than it allows (the two hooks in
    ///     NightLightBudget.cs at work, not only the numbers they read)
    ///  B  back on HIGH: every number of the first snapshot again, the object gone, the counter standing still for 60 frames,
    ///     the MEDIUM and LOW settings files in memory as on disk
    ///  R  the rules' state (clock, every object's place and health) is the same before and after
    ///  T  the hint of the GRAPHICS QUALITY row says what MEDIUM and LOW give up
    /// Second menu, NOT in Play: OpenZ/V1.1/Lighter Presets: Write LOW Settings File (f41) sets the one changed value of LOW's
    /// settings file (one shadow step) without making the files anew.
    /// </summary>
    public static class OzV11LitePresetsCheck
    {
        const string Tag = "[OpenZ lighter presets]";
        public static string Report = "not run";
        // the two settings files of HIGH as they were when this change was written
        const string HighAsset = "Settings/PC_RPAsset.asset", HighRenderer = "Settings/PC_Renderer.asset";
        const string HighAssetSha = "47cb261811be0642175de12e4dba2f3533a6561792677f9ca2d715de9c7e1ee7", HighRendererSha = "b8d1ad94aabd8e9d673af826c063b699f9e91d90c3790d7651fac5871af7fb80";
        const string LowAssetPath = "Assets/OpenZ/Resources/OpenZ/Quality/OZ_URP_Low.asset", MediumAssetPath = "Assets/OpenZ/Resources/OpenZ/Quality/OZ_URP_Medium.asset";
        static readonly (string key, string want)[] HighValues =
        {
            ("m_MSAA", "4"), ("m_RenderScale", "1"), ("m_MainLightShadowmapResolution", "4096"), ("m_AdditionalLightsShadowmapResolution", "4096"),
            ("m_ShadowCascadeCount", "4"), ("m_ShadowDistance", "260"), ("m_SoftShadowQuality", "3"), ("m_AdditionalLightsPerObjectLimit", "8"),
            ("m_SupportsHDR", "1"), ("m_RequireDepthTexture", "1"), ("m_RequireOpaqueTexture", "1"), ("m_GPUResidentDrawerMode", "0"),
        };

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad; static float waitUntil;
        static readonly StringBuilder text = new StringBuilder();
        static bool kept, paused0; static int level0; static float camH0; static Vector3 camF0;
        static bool dnKept, dnRunning0; static float dnClock0;

        [MenuItem("OpenZ/V1.1/Check Lighter Presets (f41, Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning(Tag + " enter Play mode in a level first (GRAPHICS QUALITY on HIGH)"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            ok = bad = 0; text.Clear(); Report = "running";
            stack.Push(All());
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        [MenuItem("OpenZ/V1.1/Lighter Presets: Write LOW Settings File (f41)")]
        public static void WriteLow()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning(Tag + " stop Play first"); return; }
            var a = AssetDatabase.LoadAssetAtPath<Object>(LowAssetPath);
            if (!a) { Debug.LogError(Tag + " " + LowAssetPath + " is missing (menu OpenZ/Perf/Make quality presets makes it)"); return; }
            var so = new SerializedObject(a); var p = so.FindProperty("m_ShadowCascadeCount");
            if (p == null) { Debug.LogError(Tag + " the settings file has no m_ShadowCascadeCount"); return; }
            int was = p.intValue; p.intValue = OzLiteTable.SunSteps[2];
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} {LowAssetPath}: shadow steps {was} -> {OzLiteTable.SunSteps[2]}; nothing else in it changed; HIGH's files are not opened");
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) EditorApplication.update -= Step; return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what) { if (pass) ok++; else bad++; text.Append(pass ? "PASS  " : "FAIL  ").Append(what).Append('\n'); }
        static void Note(string what) { text.Append("      ").Append(what).Append('\n'); }

        static void PutBack()
        {
            if (!kept) return; kept = false;
            if (EditorApplication.isPlaying)
            {
                if (OzLite.Active || OzLite.Off != 0) OzLite.SetOff(0);
                if (OzQuality.Level != level0) OzQuality.Level = level0;
                var g = ZGame.I; if (g) g.paused = paused0;
                var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = camF0; rts.SetHeightImmediate(camH0); }
                var dn = Object.FindFirstObjectByType<DayNight>(); if (dnKept && dn) { dn.running = dnRunning0; dn.timeOfDay = dnClock0; dn.Apply(dnClock0); }
            }
            else { PlayerPrefs.SetInt(OzQuality.Pref, level0); PlayerPrefs.Save(); }
            dnKept = false;
        }

        // ------------------------------------------------------------------ small tools
        static string Sha(string underAssets)
        {
            string p = Path.Combine(Application.dataPath, underAssets);
            if (!File.Exists(p)) return "missing";
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(File.ReadAllBytes(p))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        static string FileValue(string file, string key)
        {
            if (!File.Exists(file)) return "missing";
            var m = Regex.Match(File.ReadAllText(file), @"^\s*" + Regex.Escape(key) + @":\s*(\S+)\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : "none";
        }
        static string ProjectFile(string assetPath) => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", assetPath);
        /// <summary>A game script without its spaces and line ends (to look for a hook line whatever its spacing).</summary>
        static string Packed(string underAssets)
        {
            string p = Path.Combine(Application.dataPath, underAssets);
            return File.Exists(p) ? Regex.Replace(File.ReadAllText(p), @"\s+", "") : "";
        }
        static int Times(string hay, string needle)
        {
            int c = 0, i = 0;
            while ((i = hay.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { c++; i += needle.Length; }
            return c;
        }

        /// <summary>Every number the lighter presets may touch, and the preset's own, as text.</summary>
        static List<KeyValuePair<string, string>> Snap()
        {
            var l = new List<KeyValuePair<string, string>>();
            void A(string k, object v) => l.Add(new KeyValuePair<string, string>(k, System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));
            A("night shadows, nearby lights", NightShadows.NearbySlots); A("night shadows, sharp ones", NightShadows.HighTierCount);
            A("shadow reach follows the view", ShadowDistanceByHeight.On); A("shadow reach least", ShadowDistanceByHeight.Min); A("shadow reach most", ShadowDistanceByHeight.Max);
            A("far-shape distance factor", QualitySettings.lodBias.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            var n = NightLights.I; A("night lights, real", n ? n.maxRealLights : -1); A("night lights, searchlight shadows", n ? n.maxShadowed : -1);
            A("night lights, glow drawn", n ? n.halos.ToString() : "none"); A("night lights, beams drawn", n ? n.beams.ToString() : "none");
            var v = VehicleLights.I; A("headlights, real", v ? v.maxRealLights : -1); A("headlights, cones", v ? v.maxEffectVehicles : -1);
            var w = OzWeather.I; A("city rain, drops", w ? w.dropsAtFull : -1); A("city rain, splashes a second", w ? w.splashesPerSecond : -1f);
            var j = OzJungleWeather.I; A("jungle rain, drops", j ? j.dropsAtFull : -1); A("jungle rain, splashes a second", j ? j.splashesPerSecond : -1f); A("jungle rain, drips a second", j ? j.dripsPerSecond : -1f);
            A("jungle rain, mid layer drops", j ? j.midDrops : -1); A("jungle rain, near layer drops", j ? j.nearDrops : -1);
            var u = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            A("settings file in force", u ? u.name : "none"); A("anti-aliasing", u ? u.msaaSampleCount : -1); A("sun shadow map", u ? u.mainLightShadowmapResolution : -1);
            A("shadow steps", u ? u.shadowCascadeCount : -1); A("shadow reach now", u ? u.shadowDistance : -1f); A("render scale", u ? u.renderScale : -1f);
            A("fog steps", Shader.GetGlobalFloat("_AtmoSteps"));
            return l;
        }
        static string Get(List<KeyValuePair<string, string>> s, string k) { foreach (var kv in s) if (kv.Key == k) return kv.Value; return "?"; }

        static float FarGround()
        {
            var cam = Camera.main; if (!cam) return 0f;
            float far = 0f; var plane = new Plane(Vector3.up, Vector3.zero);
            foreach (var vp in new[] { new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0.5f, 1f, 0f) })
            {
                var ray = cam.ViewportPointToRay(vp);
                far = Mathf.Max(far, plane.Raycast(ray, out float d) && d > 0f ? d : cam.farClipPlane * 0.3f);
            }
            return far;
        }

        static long RulesState()
        {
            var w = ZGame.I.World; long h = 1469598103934665603L;
            unchecked
            {
                h = (h ^ System.BitConverter.DoubleToInt64Bits(w.Time)) * 1099511628211L;
                foreach (var o in w.Objects)
                {
                    if (o == null) continue;
                    h = (h ^ o.Id) * 1099511628211L; h = (h ^ System.BitConverter.DoubleToInt64Bits(o.PosX)) * 1099511628211L;
                    h = (h ^ System.BitConverter.DoubleToInt64Bits(o.PosY)) * 1099511628211L; h = (h ^ System.BitConverter.DoubleToInt64Bits(o.Health)) * 1099511628211L;
                }
            }
            return h;
        }

        /// <summary>The lowered numbers of preset l against the first snapshot s0 (allOff: the measuring switch has every cut off).</summary>
        static void Numbers(string who, int l, List<KeyValuePair<string, string>> s0, bool allOff)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var s = Snap(); int good = 0, all = 0; var wrong = new StringBuilder();
            void E(string k, string want)
            {
                all++; string got = Get(s, k);
                bool eq = got == want || (float.TryParse(got, System.Globalization.NumberStyles.Float, ci, out float x) && float.TryParse(want, System.Globalization.NumberStyles.Float, ci, out float y)
                                          && Mathf.Abs(x - y) <= 0.001f * Mathf.Max(1f, Mathf.Abs(y)));
                if (eq) good++; else wrong.Append($" [{k}: {got}, wanted {want}]");
            }
            int I0(string k) => int.Parse(Get(s0, k), ci); float F0(string k) => float.Parse(Get(s0, k), ci);
            string MinI(string k, int table) { int h = I0(k); return (h < 0 ? h : allOff ? h : Mathf.Min(h, table)).ToString(ci); }
            E("night shadows, nearby lights", MinI("night shadows, nearby lights", OzLiteTable.NearbyShadows[l]));
            E("night shadows, sharp ones", MinI("night shadows, sharp ones", OzLiteTable.SharpShadows[l]));
            E("shadow reach follows the view", "False");                                              // the lighter presets set the reach themselves
            E("far-shape distance factor", Get(s0, "far-shape distance factor"));                       // the engine-wide factor of far shapes is never touched
            E("night lights, real", MinI("night lights, real", OzLiteTable.NightReal[l]));
            E("night lights, searchlight shadows", MinI("night lights, searchlight shadows", OzLiteTable.SearchShadows[l]));
            // the two switches: off only where HIGH draws them and the table says so
            string Sw(string k, bool table) { string h = Get(s0, k); return h == "True" ? (allOff || table).ToString() : h; }
            E("night lights, glow drawn", Sw("night lights, glow drawn", OzLiteTable.Halos[l]));
            E("night lights, beams drawn", Sw("night lights, beams drawn", OzLiteTable.Beams[l]));
            E("headlights, real", MinI("headlights, real", OzLiteTable.Headlights[l]));
            E("headlights, cones", MinI("headlights, cones", OzLiteTable.HeadlightCones[l]));
            float rain = allOff ? 1f : OzLiteTable.Rain[l];
            E("city rain, drops", I0("city rain, drops") < 0 ? "-1" : Mathf.RoundToInt(I0("city rain, drops") * rain).ToString(ci));
            E("city rain, splashes a second", F0("city rain, splashes a second") < 0f ? "-1" : System.Convert.ToString(F0("city rain, splashes a second") * rain, ci));
            E("jungle rain, drops", I0("jungle rain, drops") < 0 ? "-1" : Mathf.RoundToInt(I0("jungle rain, drops") * rain).ToString(ci));
            E("jungle rain, splashes a second", F0("jungle rain, splashes a second") < 0f ? "-1" : System.Convert.ToString(F0("jungle rain, splashes a second") * rain, ci));
            E("jungle rain, drips a second", F0("jungle rain, drips a second") < 0f ? "-1" : System.Convert.ToString(F0("jungle rain, drips a second") * rain, ci));
            E("jungle rain, mid layer drops", I0("jungle rain, mid layer drops") < 0 ? "-1" : Mathf.RoundToInt(I0("jungle rain, mid layer drops") * rain).ToString(ci));   // the rain's depth layers keep their proportions
            E("jungle rain, near layer drops", I0("jungle rain, near layer drops") < 0 ? "-1" : Mathf.RoundToInt(I0("jungle rain, near layer drops") * rain).ToString(ci));
            E("settings file in force", l == 1 ? OzQuality.MediumAsset : OzQuality.LowAsset);
            E("shadow steps", (allOff ? OzLiteTable.SunStepsBefore[l] : OzLiteTable.SunSteps[l]).ToString(ci));
            Check(good == all, $"{who}: {good} of {all} numbers are the table's{(wrong.Length > 0 ? ":" + wrong : "")}  ({OzLite.Describe()})");
        }

        /// <summary>The shadow reach at three camera heights against the table's rule, started fresh at each height.</summary>
        static IEnumerator Reach(string who, int l, bool cut)
        {
            var rts = Object.FindFirstObjectByType<RtsCamera>(); var u = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var d = Object.FindFirstObjectByType<OzLiteDriver>();
            if (!rts || !u || !d) { Check(false, who + ": no camera, settings file or lighter-preset object to read the shadow reach from"); yield break; }
            int good = 0; var sb = new StringBuilder();
            foreach (float h in new[] { 22f, 70f, 230f })
            {
                OzLite.SetOff(cut ? 0 : ~0);                                                               // also starts the reach fresh
                rts.SetHeightImmediate(h);
                yield return 3;
                float far = FarGround();
                float want = OzLiteTable.Reach(l, cut, far, 0f, ShadowDistanceByHeight.Min, ShadowDistanceByHeight.Max, ShadowDistanceByHeight.Margin, ShadowDistanceByHeight.Step);
                bool same = Mathf.Abs(u.shadowDistance - want) < 0.6f && Mathf.Abs(d.Reach - want) < 0.6f;
                if (same) good++;
                sb.Append($" camera {h:0} m: farthest ground {far:0} m, reach {u.shadowDistance:0} m (rule {want:0})");
            }
            Check(good == 3, $"{who}: the shadow reach is the rule's at 3 of 3 camera heights (got {good}):{sb}");
        }

        // ------------------------------------------------------------------ the run
        static IEnumerator All()
        {
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            level0 = OzQuality.Level; paused0 = g.paused; camF0 = rts ? rts.focus : Vector3.zero; camH0 = rts ? rts.height : 70f; kept = true;
            int runs0 = OzLite.Runs; bool cleanStart = level0 == 0 && runs0 == 0 && !OzLite.Active && !GameObject.Find("OZ_LitePresets");

            // ---- H: HIGH's own files
            string sa = Sha(HighAsset), sr = Sha(HighRenderer);
            Check(sa == HighAssetSha, $"H1 HIGH's settings file Assets/{HighAsset} is byte for byte what it was (SHA-256 {sa.Substring(0, System.Math.Min(12, sa.Length))}...)");
            Check(sr == HighRendererSha, $"H2 HIGH's renderer file Assets/{HighRenderer} is byte for byte what it was (SHA-256 {sr.Substring(0, System.Math.Min(12, sr.Length))}...)");
            int hv = 0; var hbad = new StringBuilder(); string hf = Path.Combine(Application.dataPath, HighAsset);
            foreach (var (key, want) in HighValues) { string got = FileValue(hf, key); if (got == want) hv++; else hbad.Append($" [{key}: {got}, wanted {want}]"); }
            Check(hv == HighValues.Length, $"H3 {hv} of {HighValues.Length} values of HIGH read out of its file are what they were (anti-aliasing 4x, shadow map 4096, 4 shadow steps, 260 m, soft shadows 3, render scale 1 ...){hbad}");

            // ---- K: the three hooks are in the game's two files
            string qs = Packed("OpenZ/Scripts/View/OzQuality.cs"), bs = Packed("OpenZ/Scripts/View/NightLightBudget.cs");
            int k1 = Times(qs, "if(l>0||OzLite.Active)OzLite.Apply(l);"), k2 = Times(bs, "EffectiveMax=OzLite.Active?OzLite.NightMax(max,MaxLights):Mathf.Clamp(max,16,MaxLights);"),
                k3 = Times(bs, "if(OzLite.Active)OzLite.TrimNearby(nearHeld);");
            Check(k1 == 1 && k2 == 1 && k3 == 1, $"K1 the three one-line hooks stand in the two game files, each once (spacing aside): preset change {k1}, night-light limit {k2}, nearby shadows {k3}");

            // ---- S: nothing of the lighter presets has run on HIGH
            Check(cleanStart, $"S1 the session came up on HIGH and no line of the lighter presets has run: preset {OzQuality.Names[level0]}, counter {runs0}, active {OzLite.Active}" +
                              (cleanStart ? "" : "  (start Play with GRAPHICS QUALITY on HIGH and run this check before the preset is changed)"));
            g.paused = true;
            yield return 60;
            Check(OzLite.Runs == runs0 && !OzLite.Active, $"S2 60 frames later on HIGH: counter still {OzLite.Runs} (was {runs0})");
            long rules0 = RulesState();
            var s0 = Snap();
            Note("HIGH as found: " + string.Join("; ", s0.ConvertAll(kv => kv.Key + " " + kv.Value)));

            // ---- M: MEDIUM
            OzQuality.Level = 1; yield return 0.8f; yield return 3;
            Check(OzLite.Active && OzLite.Level == 1 && GameObject.Find("OZ_LitePresets"), $"M1 MEDIUM: the lighter presets are on (level {OzLite.Level}), their object is there");
            Numbers("M2 MEDIUM", 1, s0, false);
            yield return Reach("M3 MEDIUM", 1, true);

            // ---- L: LOW
            OzLite.SetOff(0); OzQuality.Level = 2; yield return 0.8f; yield return 3;
            Check(OzLite.Active && OzLite.Level == 2, $"L1 LOW: the lighter presets are on (level {OzLite.Level})");
            Numbers("L2 LOW", 2, s0, false);
            yield return Reach("L3 LOW", 2, true);
            string lowSteps = FileValue(ProjectFile(LowAssetPath), "m_ShadowCascadeCount");
            Check(lowSteps == OzLiteTable.SunSteps[2].ToString(), $"L4 LOW's settings file holds {OzLiteTable.SunSteps[2]} shadow step (the built game needs it in the file): it holds {lowSteps}" +
                  (lowSteps == OzLiteTable.SunSteps[2].ToString() ? "" : "  (menu OpenZ/V1.1/Lighter Presets: Write LOW Settings File (f41), outside Play)"));

            // ---- A: LOW with every cut switched off = HIGH's numbers
            OzLite.SetOff(~0); yield return 3;
            Numbers("A1 LOW, every cut switched off", 2, s0, true);
            yield return Reach("A2 LOW, every cut switched off (the reach is HIGH's rule)", 2, false);
            OzLite.SetOff(0);

            // ---- N: the two hooks of the night-light budget at work, in the game's own budget
            var dn = Object.FindFirstObjectByType<DayNight>();
            if (!NightLights.I || !dn) Note("N  this level has no night lights or no day / night clock: the four night lines N1 to N4 are left out");
            else
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                dnRunning0 = dn.running; dnClock0 = dn.timeOfDay; dnKept = true;
                float night = OzV11LitePresetsMeasure.NightTod;
                dn.running = false; dn.timeOfDay = night; dn.Apply(night);
                // the player's fort from the height the camera had (40 m at least): at that height HIGH's own reach rule keeps its state
                Vector3 fort = camF0;
                foreach (var o in g.World.Objects) if (o != null && !o.Destroyed && o.IsFort && o.Owner == g.player) { fort = g.CenterWorld(o, false); break; }
                if (rts) { rts.focus = fort; rts.SetHeightImmediate(Mathf.Max(40f, camH0)); }
                int realHigh = int.Parse(Get(s0, "night lights, real"), ci), slotsHigh = int.Parse(Get(s0, "night shadows, nearby lights"), ci);
                for (int l = 1; l <= 2; l++)
                {
                    OzQuality.Level = 0; yield return l == 1 ? 6f : 3f; yield return 3;                     // lamps need 3.2 s to warm up
                    var bh = NightLights.I.Budget; int heldHigh = bh.NearbyShadows, grantedHigh = bh.Granted, maxHigh = bh.EffectiveMax;
                    OzQuality.Level = l; yield return 1f; yield return 3;                                  // the budget counts five times a second
                    var b = NightLights.I.Budget; string who = OzQuality.Names[l];
                    int lim = Mathf.Min(realHigh, OzLiteTable.NightReal[l]), want = OzLiteTable.NightMax(l, true, lim, lim), old = Mathf.Clamp(lim, 16, lim);
                    Check(b.EffectiveMax == want && b.Granted <= want,
                          $"N{2 * l - 1} {who} at night, after HIGH: the budget grants at most {want} real night lights (it says {b.EffectiveMax}, {b.Granted} burn; HIGH: {maxHigh}, {grantedHigh} burned)" +
                          (old == want ? $"  [the game's old line gives {old} too: on {who} this line cannot tell whether the limit hook is in; LOW's can]" : $"  [without the limit hook it would say {old}]"));
                    int slots = Mathf.Min(slotsHigh, OzLiteTable.NearbyShadows[l]); bool tells = NightShadows.Mode == 2 && heldHigh > slots;
                    Check(b.NearbyShadows <= slots,
                          $"N{2 * l} {who} at night, one second after HIGH: at most {slots} nearby lights hold a shadow (now {b.NearbyShadows}; HIGH held {heldHigh})" +
                          (tells ? "  [this view can tell: without the nearby-shadow hook a light that holds a shadow keeps it]"
                                 : $"  [THIS VIEW CANNOT TELL whether the nearby-shadow hook is in: {(NightShadows.Mode != 2 ? "NIGHT LIGHT SHADOWS is not on NEARBY LIGHTS" : "HIGH held no more than the preset allows")}; K1 and another level decide]"));
                }
                dn.running = dnRunning0; dn.timeOfDay = dnClock0; dn.Apply(dnClock0); dnKept = false;
                yield return 2f;
            }

            // ---- B: back on HIGH
            if (rts) { rts.focus = camF0; rts.SetHeightImmediate(camH0); }
            OzQuality.Level = 0; yield return 3;
            int runs1 = OzLite.Runs;
            var s1 = Snap(); int same = 0; var diff = new StringBuilder();
            for (int i = 0; i < s0.Count; i++) { if (s0[i].Value == s1[i].Value) same++; else diff.Append($" [{s0[i].Key}: {s1[i].Value}, was {s0[i].Value}]"); }
            Check(same == s0.Count, $"B1 back on HIGH: {same} of {s0.Count} numbers are the first snapshot's{diff}");
            Check(!OzLite.Active && OzLite.Level == 0 && OzLite.Off == 0 && !GameObject.Find("OZ_LitePresets"), $"B2 the lighter presets are off and their object is gone (active {OzLite.Active})");
            yield return 60;
            Check(OzLite.Runs == runs1, $"B3 60 frames on HIGH again: the counter stands still ({OzLite.Runs}, was {runs1})");
            int files = 0; var fbad = new StringBuilder();
            foreach (string path in new[] { MediumAssetPath, LowAssetPath })
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path); string f = ProjectFile(path);
                string steps = FileValue(f, "m_ShadowCascadeCount"), dist = FileValue(f, "m_ShadowDistance");
                bool fine = a && a.shadowCascadeCount.ToString() == steps && Mathf.Abs(a.shadowDistance - float.Parse(dist == "none" || dist == "missing" ? "-1" : dist, System.Globalization.CultureInfo.InvariantCulture)) < 0.01f;
                if (fine) files++; else fbad.Append($" [{path}: in memory {(a ? a.shadowCascadeCount + " steps, " + a.shadowDistance + " m" : "none")}, on disk {steps} steps, {dist} m]");
            }
            Check(files == 2, $"B4 the MEDIUM and LOW settings files in memory are as on disk again ({files} of 2){fbad}");

            // ---- R: the rules were not touched
            long rules1 = RulesState();
            Check(rules0 == rules1, $"R1 the rules' state (clock, {g.World.Objects.Count} objects: place and health) is the same before and after all the preset changes");

            // ---- T: the hint
            string tip = OzQuality.Tip ?? "", low = tip.ToLowerInvariant();
            // a cut that is in the table must be named in the hint, and a cut that was taken out of the table must not be
            bool rainCut = OzLiteTable.Rain[1] < 1f || OzLiteTable.Rain[2] < 1f;
            bool glowCut = !OzLiteTable.Halos[1] || !OzLiteTable.Halos[2], beamCut = !OzLiteTable.Beams[1] || !OzLiteTable.Beams[2];
            bool nightCut = OzLiteTable.NightReal[1] < OzLiteTable.NightReal[0] || OzLiteTable.NightReal[2] < OzLiteTable.NightReal[0]
                         || OzLiteTable.NearbyShadows[1] < OzLiteTable.NearbyShadows[0] || OzLiteTable.NearbyShadows[2] < OzLiteTable.NearbyShadows[0];
            var words = new List<string> { "medium", "low", "shadows", "never change" }; var gone = new List<string>();
            (rainCut ? words : gone).Add("rain"); (nightCut ? words : gone).Add("night lights");
            (glowCut ? words : gone).Add("glow"); (beamCut ? words : gone).Add("beams");
            gone.Add("plants");                                                                         // plants are not cut: lowering their far-shape distance hides things
            int found = 0, wrongly = 0; var miss = new StringBuilder();
            foreach (var wd in words) { if (low.Contains(wd)) found++; else miss.Append(" [missing: " + wd + "]"); }
            foreach (var wd in gone) if (low.Contains(wd)) { wrongly++; miss.Append(" [names a cut that is not in the table: " + wd + "]"); }
            Check(tip.StartsWith("GRAPHICS QUALITY|") && tip.EndsWith("|") && found == words.Count && wrongly == 0 && tip == OzSettingTips.For(OzQuality.Label) && tip.Length <= 460,
                  $"T1 the hint of the GRAPHICS QUALITY row names what MEDIUM and LOW give up, and only that ({found} of {words.Count} words{miss}; {tip.Length} characters; the same text in both Options screens)");
            Note("the hint: " + tip);

            PutBack();
            Report = (bad == 0 ? "PASS " : "FAIL ") + ok + "/" + (ok + bad) + " checks (level " + g.level + ", Game view " + Screen.width + " x " + Screen.height + ")\n" + text;
            File.WriteAllText(Path.Combine(OzV11LitePresetsMeasure.OutDir(), "check_" + g.level + ".txt"), Report);
            if (bad == 0) Debug.Log(Tag + " " + Report); else Debug.LogError(Tag + " " + Report);
        }
    }
}

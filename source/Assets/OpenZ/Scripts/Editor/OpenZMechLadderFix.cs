using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (mech_EntryRung ... .010) under MechWaistYaw. In mech.glb the rails hang from the roof (4.05 m) down to 0.22 m above the ground,
    /// 0.29 m behind the torso's back plate (plate z 0.90, ladder z 1.207, waist space) and the rungs came out VERTICAL (the beam
    /// rotation was lost in the export), so the "ladder" reads as three loose vertical pipes hanging behind the legs and swinging
    /// the ladder is mounted on the back plate (rails 2.5 cm off it on 3 brackets each, z 1.00) over the torso's height only
    /// (0.38 .. 1.82 m waist space, 0.12 m of hand-holds above the roof), 5 rungs horizontal (0.30 m pitch), 2 step bars bolted to
    /// the pelvis back (2 brackets each, they do not yaw) as the first steps; the 4 surplus rungs become the brackets; the boarding
    /// Z_tough_mech (the same model).
    /// Check: "2 Render ladder check views" renders the mech from front, back, left, right, above-behind and above-front-left at
    /// rest, two walk frames (MechWalk's own leg IK), a torso turn and a firing pose (gun recoil + barrel spin) into
    /// </summary>
    public static class OpenZMechLadderFix
    {
        const string Marker = "OZ_LadderFixed_v1";
        static readonly string[] Prefabs = { "Assets/OpenZ/Prefabs/Z_medium.prefab", "Assets/OpenZ/Prefabs/Z_tough_mech.prefab" };

        // Unity axes = glTF with x negated (glTFast). Measured on mech.glb (tools/back_probe.py): torso back plate z 0.90-0.915 for
        // waist y 0.40-1.60 across the ladder width; pelvis back z 0.83 (y +0.15) and 0.68 (y -0.15), pelvis space.
        const float RailZ = 1.00f, RailY0 = 0.38f, RailY1 = 1.82f, RailLen = 3.83f, CenterX = 0.10f;
        static readonly float[] RungY = { 0.50f, 0.80f, 1.10f, 1.40f, 1.66f };
        static readonly float[] BracketY = { 0.55f, 1.10f, 1.58f };

        [MenuItem("OpenZ/Mech/1 Fix back ladder (deaths_mech_v1)")]
        public static void FixAll()
        {
            foreach (var p in Prefabs) if (File.Exists(p)) Debug.Log("[OpenZ] mech ladder: " + Fix(p));
            AssetDatabase.SaveAssets();
        }

        static string Fix(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var waist = all.FirstOrDefault(t => t.name == "MechWaistYaw"); var pelvis = all.FirstOrDefault(t => t.name == "MechPelvis");
                if (!waist || !pelvis) return path + ": no MechWaistYaw / MechPelvis";
                if (waist.Find(Marker)) return path + ": already fixed";
                var rails = all.Where(t => t.name.StartsWith("mech_EntryRail")).OrderBy(t => t.name).ToList();
                var rungs = all.Where(t => t.name.StartsWith("mech_EntryRung")).OrderBy(t => t.localPosition.y).ToList();
                if (rails.Count != 2 || rungs.Count < 5) return $"{path}: expected 2 rails + 11 rungs, found {rails.Count} + {rungs.Count}";
                // rails: on the plate, torso height only
                foreach (var r in rails)
                {
                    var lp = r.localPosition;
                    r.localPosition = new Vector3(lp.x, (RailY0 + RailY1) * 0.5f, RailZ);
                    r.localScale = new Vector3(1f, (RailY1 - RailY0) / RailLen, 1f);
                }
                var railX = rails.Select(r => r.localPosition.x).ToArray();
                // rungs: horizontal between the rails
                int k = 0;
                for (; k < RungY.Length; k++)
                {
                    var g = rungs[k];
                    g.localPosition = new Vector3(CenterX, RungY[k], RailZ); g.localRotation = Quaternion.Euler(0f, 0f, 90f); g.localScale = Vector3.one;
                    g.name = "mech_LadderRung_" + k;
                }
                // the surplus rungs + copies: brackets (short rods along z from inside the plate to the rail) and the pelvis steps
                var pool = new Queue<Transform>(rungs.Skip(k));
                Transform Next(Transform parent)
                {
                    var t = pool.Count > 0 ? pool.Dequeue() : Object.Instantiate(rungs[0].gameObject, parent).transform;
                    t.SetParent(parent, false); return t;
                }
                int b = 0;
                foreach (float x in railX)
                    foreach (float y in BracketY)
                    {
                        var t = Next(waist); t.name = "mech_LadderBracket_" + b++;
                        t.localPosition = new Vector3(x, y, (0.89f + RailZ) * 0.5f); t.localRotation = Quaternion.Euler(90f, 0f, 0f); t.localScale = new Vector3(1.2f, 0.2f, 1.2f);
                    }
                // pelvis step bars (pelvis space): y +0.15 at z 0.93, y -0.15 at z 0.78, each on two brackets
                (float y, float z, float surfA, float surfB)[] steps = { (0.15f, 0.93f, 0.833f, 0.80f), (-0.15f, 0.78f, 0.684f, 0.684f) };
                int s = 0;
                foreach (var st in steps)
                {
                    var bar = Next(pelvis); bar.name = "mech_PelvisStep_" + s;
                    bar.localPosition = new Vector3(CenterX, st.y, st.z); bar.localRotation = Quaternion.Euler(0f, 0f, 90f); bar.localScale = Vector3.one;
                    float[] bx = { -0.10f, 0.28f }; float[] surf = { st.surfA, st.surfB };
                    for (int i = 0; i < 2; i++)
                    {
                        var t = Next(pelvis); t.name = $"mech_PelvisStepBracket_{s}_{i}";
                        float z0 = surf[i] - 0.02f, len = st.z - z0;
                        t.localPosition = new Vector3(bx[i], st.y, z0 + len * 0.5f); t.localRotation = Quaternion.Euler(90f, 0f, 0f); t.localScale = new Vector3(1.2f, len / 0.58f, 1.2f);
                    }
                    s++;
                }
                while (pool.Count > 0) Object.DestroyImmediate(pool.Dequeue().gameObject);
                float dz = RailZ - 1.2068498f;
                foreach (var n in new[] { "mech_Board_Approach", "mech_Board_LadderBottom", "mech_Board_LadderTop" })
                {
                    var t = all.FirstOrDefault(x => x.name == n); if (!t) continue;
                    var lp = t.localPosition; t.localPosition = new Vector3(lp.x, lp.y, lp.z + dz);
                }
                new GameObject(Marker).transform.SetParent(waist, false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{path}: ladder on the back plate (2 rails {RailY0:0.00}-{RailY1:0.00} m, {RungY.Length} rungs, {b} brackets), 2 pelvis steps, anchors moved {dz:+0.00;-0.00} m";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------------------------------------ check renders
        [MenuItem("OpenZ/Mech/2 Render ladder check views (edit mode)")]
        public static void RenderCheck()
        {
            if (Application.isPlaying) { Debug.LogWarning("[OpenZ] mech check: edit mode only"); return; }
            int temp = GpuTemp();
            if (temp >= 76) { Debug.LogWarning($"[OpenZ] mech check: GPU {temp} C (>= 76): wait until it cools below 75 C"); return; }
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[0]);
            if (!pf) { Debug.LogError("[OpenZ] mech check: Z_medium.prefab missing"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            go.hideFlags = HideFlags.DontSave; go.transform.position = new Vector3(0f, 600f, 0f);
            var camGo = new GameObject("OZ_MechCheckCam") { hideFlags = HideFlags.DontSave };
            var lightGo = new GameObject("OZ_MechCheckLight") { hideFlags = HideFlags.DontSave };
            const int S = 360; var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            try
            {
                var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 30f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.8f, 0.77f, 0.68f);
                cam.targetTexture = rt; cam.nearClipPlane = 0.3f; cam.farClipPlane = 200f;
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
                var all = go.GetComponentsInChildren<Transform>(true);
                Transform T(string n) => all.FirstOrDefault(t => t.name == n);
                var waist = T("MechWaistYaw"); var recoil = T("MechGunRecoil"); var spin = T("MechBarrelSpin");
                Quaternion waistRest = waist ? waist.localRotation : Quaternion.identity; Vector3 recoilRest = recoil ? recoil.localPosition : Vector3.zero; Quaternion spinRest = spin ? spin.localRotation : Quaternion.identity;
                var legs = new[] { Leg(all, "L"), Leg(all, "R") };
                float sign = go.GetComponentInChildren<MechWalk>() ? go.GetComponentInChildren<MechWalk>().sign : 1f;
                var poses = new (string name, System.Action set)[]
                {
                    ("rest", () => { foreach (var l in legs) l?.Pose(0f, 0f, sign); }),
                    ("walk A", () => { foreach (var l in legs) l?.Pose(0.9f, 1f, sign); }),
                    ("walk B", () => { foreach (var l in legs) l?.Pose(2.5f, 1f, sign); }),
                    ("turn", () => { foreach (var l in legs) l?.Pose(0f, 0f, sign); if (waist) waist.localRotation = waistRest * Quaternion.Euler(0f, 40f, 0f); }),
                    ("fire", () => { if (waist) waist.localRotation = waistRest * Quaternion.Euler(0f, -30f, 0f); if (recoil) recoil.localPosition = recoilRest + new Vector3(0f, 0f, 0.2f); if (spin) spin.localRotation = spinRest * Quaternion.Euler(0f, 0f, 35f); }),
                };
                var views = new (string name, float yaw, float pitch)[] { ("front", 180f, 8f), ("back", 0f, 8f), ("left", 90f, 8f), ("right", 270f, 8f), ("above-behind", 20f, 42f), ("above-front-left", 225f, 32f) };
                var sheet = new Texture2D(S * views.Length, S * poses.Length, TextureFormat.RGB24, false);
                var gaps = new List<string>();
                for (int pi = 0; pi < poses.Length; pi++)
                {
                    if (waist) waist.localRotation = waistRest; if (recoil) recoil.localPosition = recoilRest; if (spin) spin.localRotation = spinRest;
                    poses[pi].set();
                    var centre = go.transform.position + Vector3.up * 2.2f;
                    for (int vi = 0; vi < views.Length; vi++)
                    {
                        // models face -z: "front" looks from -z; yaw 0 = the camera behind the mech (+z)
                        var dir = Quaternion.Euler(-views[vi].pitch, views[vi].yaw, 0f) * Vector3.forward;
                        cam.transform.position = centre + dir * 14f; cam.transform.LookAt(centre);
                        cam.Render();
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, S, S), vi * S, (poses.Length - 1 - pi) * S); RenderTexture.active = prev;
                    }
                    gaps.Add($"{poses[pi].name}: {LadderGaps(all)}");
                }
                sheet.Apply();
                string dir2 = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones"));
                Directory.CreateDirectory(dir2);
                string file = Path.Combine(dir2, System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_mech_ladder_check.png");
                File.WriteAllBytes(file, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log($"[OpenZ] mech ladder check: {file}\nrows (top to bottom): {string.Join(", ", poses.Select(p => p.name))}; columns: {string.Join(", ", views.Select(v => v.name))}\n" + string.Join("\n", gaps));
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); rt.Release(); Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Largest gap from a ladder part (rails, rungs, brackets, steps) to the hull (torso / pelvis renderer bounds, AABB).</summary>
        static string LadderGaps(Transform[] all)
        {
            var hull = all.Where(t => t.name == "Mech_body" || t.name == "Mech_pelvis").Select(t => t.GetComponent<Renderer>()).Where(r => r).ToList();
            if (hull.Count == 0) return "no hull renderer";
            float worst = 0f; string who = "-"; int n = 0;
            foreach (var t in all)
            {
                if (!(t.name.StartsWith("mech_Ladder") || t.name.StartsWith("mech_EntryRail") || t.name.StartsWith("mech_PelvisStep"))) continue;
                var r = t.GetComponent<Renderer>(); if (!r) continue; n++;
                float d = hull.Min(h => Mathf.Sqrt(h.bounds.SqrDistance(r.bounds.ClosestPoint(h.bounds.center))));
                if (d > worst) { worst = d; who = t.name; }
            }
            return $"{n} ladder parts, worst gap {worst:0.00} m ({who}) -> {(worst < 0.12f ? "PASS" : "FAIL")}";
        }

        sealed class LegIK
        {
            public Transform hip, knee, ankle; public Vector2 a, b, target; public float restA, restB; public Quaternion hipRest, kneeRest, ankleRest; public bool left;
            /// <summary>MechWalk.LateUpdate's leg solve for one phase (moving 0..1).</summary>
            public void Pose(float time, float moving, float sign)
            {
                float phase = time * 2f + (left ? 0f : Mathf.PI), lift = Mathf.Max(0f, Mathf.Sin(phase)) * 0.33f * moving;
                var tg = target; tg.y += lift; tg.x += Mathf.Sin(phase) * 0.20f * moving;
                float A = a.magnitude, B = b.magnitude, dist = Mathf.Clamp(tg.magnitude, Mathf.Abs(A - B) + 0.001f, A + B - 0.001f);
                float elbow = Mathf.Acos(Mathf.Clamp((dist * dist - A * A - B * B) / (2 * A * B), -1f, 1f));
                float restBend = restB - restA, bend = restBend < 0 ? -elbow : elbow;
                float angle = Mathf.Atan2(tg.y, tg.x) - Mathf.Atan2(B * Mathf.Sin(bend), A + B * Mathf.Cos(bend));
                float hipX = -(angle - restA) * sign * Mathf.Rad2Deg, kneeX = -(bend - restBend) * sign * Mathf.Rad2Deg;
                hip.localRotation = hipRest * Quaternion.AngleAxis(hipX, Vector3.right);
                knee.localRotation = kneeRest * Quaternion.AngleAxis(kneeX, Vector3.right);
                ankle.localRotation = ankleRest * Quaternion.AngleAxis(-hipX - kneeX, Vector3.right);
            }
        }

        static LegIK Leg(Transform[] all, string side)
        {
            Transform hip = all.FirstOrDefault(t => t.name == "MechHip_" + side), knee = all.FirstOrDefault(t => t.name == "MechKnee_" + side), ankle = all.FirstOrDefault(t => t.name == "MechAnkle_" + side);
            if (!hip || !knee || !ankle) return null;
            var g = new LegIK { hip = hip, knee = knee, ankle = ankle, left = side == "L", hipRest = hip.localRotation, kneeRest = knee.localRotation, ankleRest = ankle.localRotation };
            g.a = new Vector2(knee.localPosition.z, knee.localPosition.y); g.b = new Vector2(ankle.localPosition.z, ankle.localPosition.y);
            g.restA = Mathf.Atan2(g.a.y, g.a.x); g.restB = Mathf.Atan2(g.b.y, g.b.x); g.target = g.a + g.b;
            return g;
        }

        internal static int GpuTemp()
        {
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                if (p == null || !p.WaitForExit(3000)) return 0;
                return int.TryParse(p.StandardOutput.ReadToEnd().Trim().Split('\n')[0], out var t) ? t : 0;
            }
            catch { return 0; }
        }
    }
}

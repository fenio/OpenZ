using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// tower gun on the upper-left of the dark cap plate, no plinth, barrel toward the fort). Cause: two placement rules. UnitView seats
    /// a tower cannon at run time (TowerMounts: base plate / turn axis on the measured plate centre, on the plinth, outward), but the
    /// saved scenes still hold OpenZSetup.FortRoofY's placement (root on a 0.5 m raycast average of the cap's corner region, 35 cm
    /// below the plate, root +z outward) and the plinths only exist at run time. Every edit-mode Game / Scene view, every edit-mode
    /// capture and the first frames of a level show that: L02 gun_49_72 59 cm off (20 W, 56 N), L17 howitzers 23-86 cm off, all 35 cm
    /// sunk and turned 84-175 deg off the tower diagonal. The socket audit only printed it as INFO ("UnitView reseats it at run time").
    ///
    /// This seats every tower cannon of the open scene with the SAME rule as Play (TowerMounts.ApplySeat), bakes the 4 plinths per
    /// fort into the scene (root OZ_TowerMounts_Baked, hidden by TowerMounts.Build in Play, which builds its own identical ones),
    /// and gates both: OpenZ > Tower Guns > Gate (edit) here, and the Play gate (TowerGunGate: 4 types x every pad, top-down captures).
    /// OpenZSetup.BuildLevel calls SeatAll too, so a level rebuild cannot bring the old placement back.
    /// </summary>
    public static class TowerGunSeat
    {
        public const float TolXZ = 0.05f, TolY = 0.01f, TolDeg = 1f, TolPlinth = 0.01f;
        public const string MeshPath = "Assets/OpenZ/Models/Generated/OZ_TowerMount_v2.asset";
        public const string MatPath = "Assets/OpenZ/Materials/Env/OZ_TowerMount.mat", MatDarkPath = "Assets/OpenZ/Materials/Env/OZ_TowerMountDark.mat";
        static readonly Regex FortRx = new Regex(@"^fort_(front|back)_(\d+)_(\d+)$"), CannonRx = new Regex(@"^(gun|gatling|howitzer|missile_cannon)_(\d+)_(\d+)$");
        static readonly string[] Levels = { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity" };

        struct Fort { public Transform t; public int x, y; }
        struct Gun { public Transform t; public string type; public int x, y; public Fort fort; public int pad; }

        static List<Fort> Forts(Transform zroot)
        {
            var l = new List<Fort>();
            foreach (Transform t in zroot) { var m = FortRx.Match(t.name); if (m.Success) l.Add(new Fort { t = t, x = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value) }); }
            return l;
        }

        /// <summary>The scene's tower cannons: named type_x_y by OpenZSetup (= ZGame.BindViews' key), on a Zod fort turret spot
        /// (bfort.cpp:534-548), pad by TowerMounts.PadForSpot exactly as TowerMounts.Resolve does in Play.</summary>
        static List<Gun> TowerGuns(Transform zroot, List<Fort> forts)
        {
            var l = new List<Gun>();
            foreach (Transform t in zroot)
            {
                var m = CannonRx.Match(t.name); if (!m.Success) continue;
                int x = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
                foreach (var f in forts)
                    if (TowerMounts.IsTurretTile(f.x, f.y, x, y))
                    { l.Add(new Gun { t = t, type = m.Groups[1].Value, x = x, y = y, fort = f, pad = TowerMounts.PadForSpot(f.t, (x - f.x) * 16, (y - f.y) * 16) }); break; }
            }
            return l;
        }

        static Transform Axis(Transform unit) => unit.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith("TurretYaw"));

        /// <summary>Base-plate centre measured from the base part's own geometry (the largest-footprint renderer that does not turn
        /// </summary>
        public static bool BaseCentre(Transform unit, out Vector3 centre, out float bottom)
        {
            centre = default; bottom = 0f; float best = -1f; var axis = Axis(unit);
            foreach (var mf in unit.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!mf.sharedMesh || (axis && mf.transform.IsChildOf(axis))) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled) continue;
                var m = mf.sharedMesh; var b = m.subMeshCount > 0 ? m.GetSubMesh(0).bounds : m.bounds;
                for (int s = 1; s < m.subMeshCount; s++) b.Encapsulate(m.GetSubMesh(s).bounds);
                if (b.size.sqrMagnitude < 1e-8f) b = m.bounds;
                var w = mf.transform.TransformVector(new Vector3(b.size.x, 0, 0)).magnitude * mf.transform.TransformVector(new Vector3(0, 0, b.size.z)).magnitude;
                if (w <= best) continue;
                best = w; centre = mf.transform.TransformPoint(b.center); bottom = mf.transform.TransformPoint(b.center - new Vector3(0, b.extents.y, 0)).y;
            }
            return best > 0f;
        }

        // ------------------------------------------------------------------ seat + bake
        [MenuItem("OpenZ/Tower Guns/Seat Scene Tower Guns (open scene)")]
        public static void SeatOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[TowerGunSeat] stop Play first (the scene is saved)"); return; }
            var z = GameObject.Find("ZodObjects"); if (!z) { Debug.LogWarning("[TowerGunSeat] no ZodObjects in the open scene"); return; }
            Debug.Log(SeatAll(z.transform));
            EditorSceneManager.MarkSceneDirty(z.scene); EditorSceneManager.SaveScene(z.scene); AssetDatabase.SaveAssets();
            Debug.Log(Gate(false));
        }

        [MenuItem("OpenZ/Tower Guns/Seat Both Levels (L02 + L17)")]
        public static void SeatBothLevels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[TowerGunSeat] stop Play first"); return; }
            EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();                             // save rule (the project rules), no modal dialog (editor scripts)
            string back = SceneManager.GetActiveScene().path;
            foreach (var lv in Levels)
            {
                var sc = EditorSceneManager.OpenScene(lv, OpenSceneMode.Single);
                var z = GameObject.Find("ZodObjects"); if (!z) { Debug.LogWarning("[TowerGunSeat] no ZodObjects in " + lv); continue; }
                Debug.Log(SeatAll(z.transform));
                EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
                Debug.Log(Gate(false));
            }
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
        }

        /// <summary>Seat every tower cannon under zroot with TowerMounts.ApplySeat (the run-time rule) and (re)bake the plinths. Also
        /// called by OpenZSetup.BuildLevel. Returns a log line per cannon (before -> after, cm / deg).</summary>
        public static string SeatAll(Transform zroot)
        {
            var sb = new StringBuilder("[TowerGunSeat] tower guns seated (tower_gun_fix):");
            if (!zroot) return sb.Append(" no ZodObjects").ToString();
            var forts = Forts(zroot);
            foreach (var g in TowerGuns(zroot, forts))
            {
                var pw = TowerMounts.PadWorld(g.fort.t, g.pad);
                string Off() { var a = Axis(g.t); var p = a ? a.position : g.t.position; return $"{new Vector2(p.x - pw.x, p.z - pw.z).magnitude * 100f:F0} cm, root {(g.t.position.y - TowerMounts.SeatWorld(g.fort.t, g.pad).y) * 100f:+0;-0} cm, {Mathf.Abs(Mathf.DeltaAngle(g.t.eulerAngles.y, TowerMounts.SeatYaw(g.fort.t, g.pad, g.type))):F0} deg"; }
                string before = Off();
                Undo.RecordObject(g.t, "Seat tower gun");
                TowerMounts.ApplySeat(g.t, g.fort.t, g.pad, g.type);
                if (PrefabUtility.IsPartOfPrefabInstance(g.t)) PrefabUtility.RecordPrefabInstancePropertyModifications(g.t);
                if (g.t.TryGetComponent<TeamPaint>(out var tp) && !tp.FortPalette)                  // TG2: the fort's palette in the editor / first frame too
                {
                    Undo.RecordObject(tp, "Tower gun fort palette"); tp.FortPalette = true; EditorUtility.SetDirty(tp);
                    if (PrefabUtility.IsPartOfPrefabInstance(tp)) PrefabUtility.RecordPrefabInstancePropertyModifications(tp);
                }
                sb.Append($"\n  {g.t.name} on {g.fort.t.name} pad {g.pad}: {before} -> {Off()}");
            }
            sb.Append("\n  ").Append(BakePlinths(zroot.gameObject.scene, forts));
            return sb.ToString();
        }

        static Mesh PlinthMeshAsset()
        {
            var fresh = TowerMounts.MountMesh();
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (!m) { Directory.CreateDirectory(Path.GetDirectoryName(MeshPath)); AssetDatabase.CreateAsset(fresh, MeshPath); return fresh; }
            EditorUtility.CopySerialized(fresh, m); m.name = "OZ_TowerMount_v2"; Object.DestroyImmediate(fresh); EditorUtility.SetDirty(m);   // follows TowerMounts.Plinth
            return m;
        }

        static Material PlinthMaterial(string path, Color col)
        {
            var d = new GameObject("tmp").AddComponent<TowerMounts>();                                     // its serialized defaults (metallic / smoothness)
            float met = d.metallic, sm = d.smoothness; Object.DestroyImmediate(d.gameObject);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
                m = new Material(sh) { name = Path.GetFileNameWithoutExtension(path) };
                Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", met);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", sm);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The run-time plinths (TowerMounts.Build: PadWorld, fort yaw, MountMesh, steel + dark) baked into the scene so edit
        /// mode shows what Play shows. Rebuilt from scratch each time (idempotent).</summary>
        static string BakePlinths(Scene scene, List<Fort> forts)
        {
            foreach (var r in scene.GetRootGameObjects()) if (r.name == TowerMounts.BakedRootName) Undo.DestroyObjectImmediate(r);
            if (forts.Count == 0) return "no forts: no plinths baked";
            var mesh = PlinthMeshAsset();
            var tmp = new GameObject("tmp").AddComponent<TowerMounts>(); Color steel = tmp.mountColor, dark = tmp.darkColor; Object.DestroyImmediate(tmp.gameObject);
            var mats = new[] { PlinthMaterial(MatPath, steel), PlinthMaterial(MatDarkPath, dark) };
            var root = new GameObject(TowerMounts.BakedRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Bake tower plinths");
            int n = 0;
            foreach (var f in forts)
                for (int i = 0; i < 4; i++)
                {
                    var go = new GameObject($"TowerMountBaked_{f.t.name}_{i}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(TowerMounts.PadWorld(f.t, i), Quaternion.Euler(0f, f.t.eulerAngles.y, 0f));
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
                    n++;
                }
            return $"{n} plinths baked under {TowerMounts.BakedRootName} ({forts.Count} forts; hidden in Play by TowerMounts.Build)";
        }

        // ------------------------------------------------------------------ edit gate
        [MenuItem("OpenZ/Tower Guns/Gate (edit mode, open scene)")]
        public static void GateMenu() => Debug.Log(Gate(true));

        [MenuItem("OpenZ/Tower Guns/Gate (Play: 4 types x every pad + top-down captures)")]
        public static void PlayGateMenu()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[TowerGunGate] enter Play on L02 or L17 first (full look, 60 fps cap)"); return; }
            TowerGunGate.Run();
        }

        /// <summary>Edit-mode gate for the open level: every scene tower cannon's turn axis and base plate within 5 cm of its pad
        /// centre, root on the plinth top within 1 cm, rest heading on the tower diagonal within 1 deg; 4 baked plinths per fort on
        /// </summary>
        public static string Gate(bool write)
        {
            var sb = new StringBuilder(); int fail = 0, n = 0;
            string level = SceneManager.GetActiveScene().name;
            var z = GameObject.Find("ZodObjects");
            sb.AppendLine($"# Tower gun gate, {level}, edit mode (tower_gun_fix)\n");
            if (!z) { sb.AppendLine("FAIL: no ZodObjects"); return sb.ToString(); }
            var forts = Forts(z.transform);
            sb.AppendLine("| status | tower cannon | fort / pad | turn axis off (cm) | base plate off (cm) | root vs plinth top (cm) | base bottom vs plinth top (cm) | heading off (deg) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var g in TowerGuns(z.transform, forts))
            {
                n++;
                var pw = TowerMounts.PadWorld(g.fort.t, g.pad); var seat = TowerMounts.SeatWorld(g.fort.t, g.pad);
                var a = Axis(g.t); var ap = a ? a.position : g.t.position;
                float axisOff = new Vector2(ap.x - pw.x, ap.z - pw.z).magnitude;
                bool hasBase = BaseCentre(g.t, out var bc, out var bottom);
                float baseOff = hasBase ? new Vector2(bc.x - pw.x, bc.z - pw.z).magnitude : 99f;
                float dy = g.t.position.y - seat.y, dBottom = hasBase ? bottom - (pw.y + TowerMounts.MountHeight) : 99f;
                float hdg = Mathf.Abs(Mathf.DeltaAngle(g.t.eulerAngles.y, TowerMounts.SeatYaw(g.fort.t, g.pad, g.type)));
                bool ok = axisOff <= TolXZ && baseOff <= TolXZ && Mathf.Abs(dy) <= TolY && hdg <= TolDeg && dBottom <= 0.005f && dBottom >= -0.03f;
                if (!ok) fail++;
                sb.AppendLine($"| {(ok ? "PASS" : "FAIL")} | {g.t.name} | {g.fort.t.name} / {g.pad} | {axisOff * 100f:F1} | {baseOff * 100f:F1} | {dy * 100f:+0.0;-0.0} | {dBottom * 100f:+0.0;-0.0} | {hdg:F1} |");
            }
            if (n == 0) sb.AppendLine("| INFO | no tower cannon in this scene | | | | | | |");
            var baked = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(r => r.name == TowerMounts.BakedRootName);
            sb.AppendLine("\n| status | plinth | off (cm) |\n|---|---|---|");
            foreach (var f in forts)
                for (int i = 0; i < 4; i++)
                {
                    var pw = TowerMounts.PadWorld(f.t, i);
                    var p = baked ? baked.transform.Cast<Transform>().FirstOrDefault(t => t.name == $"TowerMountBaked_{f.t.name}_{i}") : null;
                    float off = p ? (p.position - pw).magnitude : 99f;
                    bool ok = p && p.gameObject.activeInHierarchy && off <= TolPlinth && p.GetComponent<MeshFilter>() && p.GetComponent<MeshFilter>().sharedMesh;
                    if (!ok) fail++;
                    sb.AppendLine($"| {(ok ? "PASS" : "FAIL")} | {f.t.name} pad {i} | {(p ? (off * 100f).ToString("F1") : "missing")} |");
                }
            sb.Insert(0, $"TOWER GUN GATE {level} edit: {(fail == 0 ? "PASS" : $"FAIL ({fail})")}, {n} tower cannons, {forts.Count * 4} pads\n");
            if (write)
            {
                var dir = Path.Combine(OpenZPaths.Records, "qc"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"tower_gun_gate_{level}_edit.md"), sb.ToString());
            }
            return sb.ToString();
        }
    }
}

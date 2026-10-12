using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// is never read or written. The palms_v3 palms (DetailProp kind "palm"; BODY_ = the whole intact palm in one mesh) move in the
    /// level wind:
    /// <list type="bullet">
    /// <item>the crown sways about the trunk top: a downwind lean that follows the gusts plus layered sines (about 1.5-3 deg in full
    /// desert wind) and a smaller cross sway;</item>
    /// <item>the fronds flutter: up / down, a sideways flick round the trunk top and a push downwind, growing to the tips, each frond
    /// on its own phase with a wave running out to the tip;</item>
    /// <item>the trunk bends a little: about 0.5 deg at the top, and the foot stays.</item>
    /// </list>
    /// The motion is computed in the vertex stage of OpenZ/TeamLit (OZPalmWind, the same function in the colour, shadow, depth and
    /// depth-normals passes). A palm off screen therefore costs nothing, and the shadow sways with the palm. Each palm has its own
    /// phase, tempo and amplitude, hashed from its foot position (deterministic: the same every run, in the editor and the player).
    /// The wind is the flags' and banners' wind, read the way BuildingAnimView reads it: the direction comes from the Atmosphere's
    /// _FogWind (else the flags' fallback for the level), and the strength and gust come from FlagView's _OZFlagWind (else
    /// FlagView's own formulas when no FlagView runs).
    /// This manager runs once per game scene, on the ZGame object, and is the one loop for all palms:
    /// <list type="bullet">
    /// <item>it gives each palm's BODY_ renderer a run-time copy of its material, with the crown pivot (the top of the highest
    /// BREAK_Seg part) and the crown radius (BREAK_Crown) set. It makes one copy per palm variant, so the palms still batch;</item>
    /// <item>every other renderer keeps its material, so the stump and the BREAK_ parts never move in the wind. When a palm breaks,
    /// DetailProp hides BODY_ and PalmFall's physics owns the parts;</item>
    /// <item>per frame it only sets two global vectors (no allocations). New palms are picked up every 2 s.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalmWind : MonoBehaviour
    {
        public static PalmWind I { get; private set; }
        /// <summary>Tests can stop the automatic install (as BuildingAnimView.AutoBoot).</summary>
        public static bool AutoBoot = true;

        [Header("Look in full desert wind at gust 1 (the flags' strength scales it: city 0.4)")]
        [Tooltip("crown sway about the trunk top (deg)")] [Range(0f, 6f)] public float swayDegrees = 4.5f;
        [Tooltip("trunk bend at the top (deg); the foot stays")] [Range(0f, 2f)] public float bendDegrees = 0.8f;
        [Tooltip("frond tip flutter (m)")] [Range(0f, 0.4f)] public float flutterMetres = 0.22f;
        [Tooltip("x the flags' wind strength (0 = the palms stand still)")] [Range(0f, 2f)] public float strength = 1f;
        [Tooltip("used when no Atmosphere sets _FogWind (m/s), = FlagView / BuildingAnimView")]
        public Vector3 fallbackWind = new Vector3(3f, 0, 1.5f), fallbackWindCity = new Vector3(1.2f, 0, 0.6f);

        /// <summary>Play-check counters: palms hooked, BODY_ renderers on a wind copy, palms skipped (no BODY_ / shader without the wind).</summary>
        public static int Palms, Bodies, Skipped;
        public static string LastLog = "";

        static readonly int FogWindId = Shader.PropertyToID("_FogWind"), FlagWindId = Shader.PropertyToID("_OZFlagWind"),
            WindId = Shader.PropertyToID("_OZPalmWind"), WindKId = Shader.PropertyToID("_OZPalmWindK"),
            PivotId = Shader.PropertyToID("_OzWindPalm"), CrownId = Shader.PropertyToID("_OzWindPalm2");

        ZGame game;
        readonly HashSet<DetailProp> seen = new HashSet<DetailProp>();
        readonly Dictionary<(Material, Mesh), Material> copies = new Dictionary<(Material, Mesh), Material>();
        readonly List<Renderer> swapped = new List<Renderer>();
        readonly List<Material[]> originals = new List<Material[]>();
        Vector2 windDir = new Vector2(1f, 0f);
        float windS, gust, nextScan;

        // ------------------------------------------------------------------ boot (same pattern as FlagView)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Ensure();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

        /// <summary>Adds a PalmWind to a game scene (one with a ZGame) unless one is already there.</summary>
        public static void Ensure()
        {
            if (!AutoBoot) return;
            var g = ZGame.I; if (!g || FindFirstObjectByType<PalmWind>()) return;
            g.gameObject.AddComponent<PalmWind>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this; Palms = Bodies = Skipped = 0; LastLog = "";
        }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            UpdateWind();
            Scan();
        }

        void OnDisable() { Shader.SetGlobalVector(WindId, Vector4.zero); }   // switched off: the palms stand still (rest pose)

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            Shader.SetGlobalVector(WindId, Vector4.zero);
            for (int i = 0; i < swapped.Count; i++) if (swapped[i]) swapped[i].sharedMaterials = originals[i];   // a palm that outlives the manager
            foreach (var c in copies.Values) if (c) Destroy(c);
            copies.Clear(); swapped.Clear(); originals.Clear(); seen.Clear();
        }

        // ------------------------------------------------------------------ frame: two globals, nothing per palm
        void LateUpdate()
        {
            UpdateWind();
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 2f; Scan(); }
        }

        void UpdateWind()
        {
            Vector4 fw = Shader.GetGlobalVector(FogWindId);                            // Atmosphere.cs: desert (3, 0, 1.5), city (1.2, 0, 0.6)
            var wv = new Vector2(fw.x, fw.z);
            if (wv.sqrMagnitude < 0.01f) { var fb = game && game.level == "L17" ? fallbackWindCity : fallbackWind; wv = new Vector2(fb.x, fb.z); }
            float speed = Mathf.Max(wv.magnitude, 0.01f); windDir = wv / speed;
            Vector4 fl = Shader.GetGlobalVector(FlagWindId);                           // FlagView: (strength, gust, phase, time)
            if (fl.w > 0.01f) { windS = fl.x; gust = fl.y; }
            else
            {   // no FlagView: its own formulas (FlagView.UpdateWind)
                float now = Time.time;
                windS = Mathf.Clamp(speed / 3.35f, 0.25f, 1.6f);
                gust = Mathf.Clamp01(0.45f + 0.4f * Mathf.Sin(now * 0.23f) * Mathf.Sin(now * 0.61f + 1.3f) + 0.3f * (Mathf.PerlinNoise(now * 0.37f, 7.1f) - 0.5f));
            }
            Shader.SetGlobalVector(WindId, new Vector4(windDir.x, windDir.y, windS * strength, gust));
            Shader.SetGlobalVector(WindKId, new Vector4(swayDegrees * Mathf.Deg2Rad, bendDegrees * Mathf.Deg2Rad, flutterMetres, 0f));
        }

        // ------------------------------------------------------------------ palms
        void Scan()
        {
            var all = DetailProp.All;
            int added = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p || p.Broken || p.kind != "palm" || seen.Contains(p)) continue;
                seen.Add(p);
                if (Hook(p)) { Palms++; added++; } else Skipped++;
            }
            if (added > 0) Log("+" + added + " palm(s): " + Status());
        }

        /// <summary>Puts BODY_ on the wind copy of its material (made once per material and mesh). False if the palm has no BODY_ on
        /// a TeamLit material that knows the wind (shader not updated).</summary>
        bool Hook(DetailProp p)
        {
            bool any = false;
            foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!DetailKit.Base(r.name).StartsWith("BODY_", System.StringComparison.Ordinal)) continue;
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                if (!mesh) continue;
                if (r.isPartOfStaticBatch) { Log(p.id + ": BODY_ is static-batched (world-space mesh): stays still"); continue; }   // the shader needs BODY_'s own matrix
                var mats = r.sharedMaterials; Material[] orig = null;
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = mats[k];
                    if (!m || !m.HasProperty(PivotId)) continue;
                    if (!copies.TryGetValue((m, mesh), out var c))
                    {
                        Shape(p.transform, r.transform, mesh, out var pivot, out float radius, out string from);
                        c = new Material(m) { name = m.name + " (palm wind)", hideFlags = HideFlags.DontSave };
                        c.SetVector(PivotId, new Vector4(pivot.x, pivot.y, pivot.z, 1f));
                        c.SetVector(CrownId, new Vector4(radius, 0f, 0f, 0f));
                        copies[(m, mesh)] = c;
                        Log(p.id + ": " + m.name + " / " + mesh.name + ": crown pivot " + pivot.ToString("F2") + " (" + from + "), crown radius " + radius.ToString("F2") + " (mesh space)");
                    }
                    if (orig == null) orig = (Material[])mats.Clone();
                    mats[k] = c;
                }
                if (orig == null) continue;
                r.sharedMaterials = mats;
                swapped.Add(r); originals.Add(orig);
                Bodies++; any = true;
            }
            if (!any) Log(p.id + " (" + p.name + "): no BODY_ renderer on a TeamLit material with _OzWindPalm (shader not updated?): stays still");
            return any;
        }

        /// <summary>The crown pivot (the top centre of the highest BREAK_Seg trunk part) and the crown radius (BREAK_Crown's half
        /// width across the trunk) in BODY_'s own mesh space; without BREAK_ parts, from BODY_'s bounds (pivot at 68 % of the height).</summary>
        static void Shape(Transform root, Transform body, Mesh mesh, out Vector3 pivot, out float radius, out string from)
        {
            var up = body.InverseTransformDirection(root.up);
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            float best = float.NegativeInfinity; pivot = Vector3.zero; radius = 0f; from = "mesh bounds";
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                string n = DetailKit.Base(mf.name);
                bool seg = n.StartsWith("BREAK_Seg", System.StringComparison.Ordinal), crown = n.StartsWith("BREAK_Crown", System.StringComparison.Ordinal);
                if (!seg && !crown) continue;
                var b = m.bounds; var t = mf.transform;
                var c = body.InverseTransformPoint(t.TransformPoint(b.center));
                var ax = body.InverseTransformVector(t.TransformVector(new Vector3(b.extents.x, 0f, 0f)));
                var ay = body.InverseTransformVector(t.TransformVector(new Vector3(0f, b.extents.y, 0f)));
                var az = body.InverseTransformVector(t.TransformVector(new Vector3(0f, 0f, b.extents.z)));
                if (seg)
                {
                    float half = Mathf.Abs(Vector3.Dot(ax, up)) + Mathf.Abs(Vector3.Dot(ay, up)) + Mathf.Abs(Vector3.Dot(az, up));
                    var tc = c + up * half;
                    if (Vector3.Dot(tc, up) > best) { best = Vector3.Dot(tc, up); pivot = tc; from = "top of " + n; }
                }
                else radius = Mathf.Max(radius, Across(ax, up), Across(ay, up), Across(az, up));
            }
            var mb = mesh.bounds;
            if (float.IsNegativeInfinity(best))
            {
                float half = Mathf.Abs(mb.extents.x * up.x) + Mathf.Abs(mb.extents.y * up.y) + Mathf.Abs(mb.extents.z * up.z);
                float lo = Vector3.Dot(mb.center, up) - half;
                pivot = mb.center - up * Vector3.Dot(mb.center, up) + up * (lo + 0.68f * 2f * half);
            }
            if (radius <= 0f)
            {
                radius = Mathf.Max(Across(new Vector3(mb.extents.x, 0f, 0f), up), Across(new Vector3(0f, mb.extents.y, 0f), up), Across(new Vector3(0f, 0f, mb.extents.z), up));
                from += ", radius from BODY_ bounds";
            }
        }

        static float Across(Vector3 v, Vector3 up) { return (v - up * Vector3.Dot(v, up)).magnitude; }

        // ------------------------------------------------------------------ status / log
        /// <summary>One line for the Play check (OpenZ > Palms v3 > Play: wind status).</summary>
        public static string Status()
        {
            var w = I;
            if (!w) return "PalmWind not running";
            return "palms " + Palms + " (" + Bodies + " BODY_ on " + w.copies.Count + " wind material(s), " + Skipped + " skipped), wind dir ("
                   + w.windDir.x.ToString("F2") + ", " + w.windDir.y.ToString("F2") + ") strength " + (w.windS * w.strength).ToString("F2") + " gust " + w.gust.ToString("F2")
                   + (w.enabled ? "" : " (manager OFF: still)") + ", sway " + w.swayDegrees.ToString("0.0#") + " deg, bend " + w.bendDegrees.ToString("0.0#") + " deg, flutter " + w.flutterMetres.ToString("0.0#") + " m";
        }

        static void Log(string s)
        {
            LastLog = s;
            if (Debug.isDebugBuild) Debug.Log("[OpenZ PalmWind] " + s);
        }
    }
}

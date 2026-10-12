using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    // ------------------------------------------------------------------------------------------------ data (JSON)
    // on the imported building meshes).
    [Serializable]
    public class NightV2LampSpec
    {
        public string id, kind, face;
        public float[] p, n, pm, color;
        public float size = 1f, intensity = 1f, wash = 2.6f, flat;
    }
    [Serializable] public class NightV2BuildingSpec { public string key; public float[] boundsMin, boundsMax; public NightV2LampSpec[] lamps; }
    [Serializable] public class NightV2File { public int version; public string note; public NightV2BuildingSpec[] buildings; }

    /// <summary>
    /// View side only, auto-boots in every level scene (ZGame).
    /// <list type="bullet">
    /// <item><b>Night classes</b>: pushes the per-class night terms OpenZ/TeamLit reads (OpenZNightV2.hlsl): the moonlit
    /// rim + fill that keeps units readable now reaches units only; buildings keep a faint rim (their lamps and floods light
    /// them), crates / huts a soft rim + a little fill, rocks, cliffs, bridges and props none (they follow the moon like the
    /// ground). Class per material: <c>_NightClass</c> (-1 auto, 0 environment, 1 unit, 2 building, 3 map item).</item>
    /// <item><b>Day-sky reflections</b>: the skybox reflection is generated once from the day sky; DayNight only recolours
    /// the sky material, so smooth and metallic surfaces reflected daylight all night. The sky ratio (current sky ambient /
    /// the day sky ambient) scales that reflection away in TeamLit, Sand and FlagCloth (units keep theirs: tuned look).</item>
    /// <item><b>Building lamps</b>: small bulkhead wall lamps on the forts, factories, repair bays and radars at sockets
    /// measured on the meshes (Resources/OpenZ/Night/night_v2_lamps.json): gunmetal housing, glowing lens + bloom halo, and
    /// a light wash on the wall and the ground below (OpenZ/NightWash, no real Light, no budget). Owned buildings only
    /// (neutral stays dark), switched on at dusk with a stagger, off at dawn; a capture blacks them out for a moment,
    /// damage makes them flicker, a destroyed building's lamps are gone (back when it is rebuilt).</item>
    /// </list>
    /// Debug: Ctrl+Alt+F9 logs Status(), Ctrl+Shift+F9 runs the luminance check (NightLumaCheck); editor menu OpenZ/Night.
    /// </summary>
    [DefaultExecutionOrder(95)]
    public class NightV2 : MonoBehaviour
    {
        public static NightV2 I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add NightV2 by hand.</summary>
        public static bool AutoBoot = true;

        public const int ClassEnvironment = 0, ClassUnit = 1, ClassBuilding = 2, ClassItem = 3;
        public static readonly string[] ClassNames = { "environment", "unit", "building", "map item" };

        [Header("Night classes (x environment, y unit, z building, w map item)")]
        [Tooltip("Moonlit rim at night per class (units 1 = the readability tuning)")]
        public Vector4 rimK = new Vector4(0f, 0f, 0.3f, 0.5f);
        [Tooltip("Moonlit fill (x albedo) at night per class")]
        public Vector4 fillK = new Vector4(0f, 1f, 0f, 0.3f);
        [Tooltip("How much of the sky ratio each class's skybox reflection follows (units 0 = kept as tuned)")]
        public Vector4 envFollow = new Vector4(1f, 0f, 1f, 1f);
        [Tooltip("Scale the day-sky reflection with the current sky")] public bool envFix = true;
        [Range(0.02f, 1f)] public float minSkyRatio = 0.05f;

        [Header("Building lamps")]
        public bool buildingLamps = true;
        [Range(0f, 4f)] public float lensGain = 1f, haloGain = 1f, washGain = 1f;
        [Tooltip("Lamps switch on from (+ up to 0.012 stagger) / off at (DayNight time of day)")]
        public float switchOnAt = 0.345f, switchOffAt = 0.895f;
        [Tooltip("Lamp light colour (sRGB), warm sodium white")] public Color lampColor = new Color(1f, 0.8f, 0.55f, 1f);

        // sand colour, so it ignored the moon and stayed day-bright at night (only its emission rate drops). Scaled here per
        // renderer (MaterialPropertyBlock _BaseColor, linear multiplier; the Atmosphere material itself is untouched).
        [Header("Unlit ambient dust (Atmosphere sand streaks / dust devils)")]
        [Tooltip("Brightness of the unlit ambient dust at full night, linear (1 = as by day)"), Range(0f, 1f)]
        public float unlitDustNight = 0.22f;

        [Header("Debug")] public bool debugKeys = true;

        /// <summary>Last pushed sky ratio (reflection scale) and night level.</summary>
        public float SkyRatio { get; private set; } = 1f;
        public float Night { get; private set; }
        public int LampCount { get { int n = 0; foreach (var r in rigs) n += r.Lamps.Count; return n; } }
        public int LampsLit { get; private set; }
        public int RigCount => rigs.Count;
        public IEnumerable<Renderer> LampRenderers() { foreach (var r in rigs) foreach (var l in r.Lamps) { if (l.Lens) yield return l.Lens; if (l.Halo) yield return l.Halo; if (l.Wash) yield return l.Wash; } }

        static readonly int IdV2 = Shader.PropertyToID("_OZNightV2"), IdRim = Shader.PropertyToID("_OZNightRimK"),
            IdFill = Shader.PropertyToID("_OZNightFillK"), IdEnvW = Shader.PropertyToID("_OZNightEnvW"), IdColor = Shader.PropertyToID("_Color");
        static readonly Color DaySkySrgb = new Color(0.58f, 0.74f, 0.96f, 1f);   // DayNight key t = 0 ambient sky (the reflection's sky)

        sealed class Lamp
        {
            public NightV2LampSpec Spec; public Transform Root; public Renderer Lens, Halo, Wash; public float Level, Stagger, Seed, Shown = -1f, Gain = 1f;
        }
        sealed class Rig
        {
            public ZObject B; public string Key; public Transform Root; public Bounds MB; public GameObject View;
            public readonly List<Lamp> Lamps = new List<Lamp>();
            public Team LastOwner; public float BlackoutUntil; public bool Shown = true;
        }

        ZGame game; DayNight dayNight; NightV2File data; Material washMat;
        readonly List<Rig> rigs = new List<Rig>();
        MaterialPropertyBlock mpb;
        bool built; float nextBuildTry; int buildTries;

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        /// <summary>Level scenes only (ZGame present).</summary>
        public static void TryAttach()
        {
            if (!AutoBoot) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || FindFirstObjectByType<NightV2>()) return;
            new GameObject("NightV2").AddComponent<NightV2>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            mpb = new MaterialPropertyBlock();
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            dayNight = FindFirstObjectByType<DayNight>();
            var ta = Resources.Load<TextAsset>("OpenZ/Night/night_v2_lamps");
            data = ta ? JsonUtility.FromJson<NightV2File>(ta.text) : null;
            if (data == null) Debug.LogWarning("[OpenZ] NightV2: Resources/OpenZ/Night/night_v2_lamps.json missing (README_IMPORT.md)");
            var wm = Resources.Load<Material>("OpenZ/Night/M_NightWash");
            if (wm) washMat = new Material(wm) { name = "M_NightWash (night_v2)", hideFlags = HideFlags.DontSave };
            else
            {
                var s = Shader.Find("OpenZ/NightWash");
                if (s) washMat = new Material(s) { name = "NightWash (night_v2)", hideFlags = HideFlags.DontSave };
                else Debug.LogWarning("[OpenZ] NightV2: shader OpenZ/NightWash missing (README_IMPORT.md): lamps without a wall wash");
            }
            PushGlobals(0f);
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            Shader.SetGlobalVector(IdV2, Vector4.zero);                    // edit mode / other scenes: TeamLit defaults, no reflection change
            if (washMat) Destroy(washMat);
        }

        void OnDisable() { if (I == this) Shader.SetGlobalVector(IdV2, Vector4.zero); }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            float t = dayNight ? dayNight.timeOfDay : NightGrade.TimeOfDay;
            Night = NightGrade.Night(t);
            PushGlobals(Night);
            UpdateUnlitDust(Night);
            if (!built && buildingLamps && Time.unscaledTime >= nextBuildTry) TryBuild();
            UpdateLamps(t);
            if (debugKeys && !global::OpenZ.OzRelease.IsRelease) DebugKeys();
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Push the class / reflection globals right now (NightLumaCheck toggles the unit terms between renders).</summary>
        public void PushNow() => PushGlobals(Night);

        void PushGlobals(float night)
        {
            // L17 day sky (warmth 0.35: lum 0.474 vs 0.492) gave a sky ratio of 0.96 BY DAY (day reflections cut by 4 %)
            var daySky = DaySkySrgb;
            if (dayNight) { float g = daySky.grayscale; daySky = Color.Lerp(new Color(g * 0.96f, g, g * 1.06f), daySky, dayNight.warmth); }
            float dayLum = Lum(daySky.linear);
            float now = Lum(RenderSettings.ambientSkyColor.linear);
            SkyRatio = Mathf.Clamp(now / Mathf.Max(1e-4f, dayLum), minSkyRatio, 1f);
            Shader.SetGlobalVector(IdV2, new Vector4(1f, envFix ? SkyRatio : 1f, night, 0f));
            Shader.SetGlobalVector(IdRim, rimK);
            Shader.SetGlobalVector(IdFill, fillK);
            Shader.SetGlobalVector(IdEnvW, envFollow);
        }

        static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
        readonly List<Renderer> unlitDust = new List<Renderer>();
        float nextDustScan, dustShown = -1f;
        public int UnlitDustCount => unlitDust.Count;
        public float UnlitDustK => dustShown;

        /// <summary>Atmosphere's particles are created lazily (Play) with HideFlags.DontSave, so they are found through the
        /// Atmosphere component (FindObjectsByType skips DontSave objects); rescanned every 2 s.</summary>
        public static void CollectUnlitDust(List<Renderer> into)
        {
            var atmo = FindFirstObjectByType<Atmosphere>();
            if (atmo)
                foreach (var r in atmo.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m && m.shader && m.shader.name.Contains("Particles/Unlit")) into.Add(r);
                }
            // other unlit sand / smoke colour in the level: same night scale
            // Every alpha-blended URP Particles/Unlit renderer in the
            // scene (dust, smoke, sand kicks, pyro antenna smoke); additive ones (fire, sparks, glows) keep their own light
            foreach (var r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (!m || into.Contains(r)) continue;
                if (m.name == "BS01Kit_Smoke") { into.Add(r); continue; }
                if (m.shader && m.shader.name.Contains("Particles/Unlit") && (!m.HasProperty("_Blend") || m.GetFloat("_Blend") < 1.5f)) into.Add(r);
            }
        }

        /// <summary>Last dust tint (linear RGB multiplier; checks).</summary>
        public static Vector3 DustTint { get; private set; } = Vector3.one;
        static Vector3 dayRef = Vector3.zero;

        /// <summary>Scene light on a dust puff: sun colour x intensity x sun height + ambient sky / equator, relative to the brightest
        /// (noon) value seen this session (first measured when the sun is high).</summary>
        static Vector3 SceneLightTint()
        {
            var sun = RenderSettings.sun;
            Vector3 L = Vector3.zero;
            if (sun && sun.enabled)
            {
                var c = sun.color.linear * sun.intensity; float up = Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) + 0.1f);
                L += new Vector3(c.r, c.g, c.b) * Mathf.Sqrt(up) * 0.55f;
            }
            var sky = RenderSettings.ambientSkyColor.linear; var eq = RenderSettings.ambientEquatorColor.linear;
            L += new Vector3(sky.r * 0.6f + eq.r * 0.4f, sky.g * 0.6f + eq.g * 0.4f, sky.b * 0.6f + eq.b * 0.4f);
            float lum = L.x * 0.2126f + L.y * 0.7152f + L.z * 0.0722f, refLum = dayRef.x * 0.2126f + dayRef.y * 0.7152f + dayRef.z * 0.0722f;
            if (lum > refLum) dayRef = L;                                                    // the day reference = the brightest light so far
            if (refLum <= 1e-4f) return Vector3.one;
            float r = lum / Mathf.Max(1e-4f, Mathf.Max(refLum, lum));
            // colour: the light's own hue relative to the reference hue, at the relative brightness
            var hue = new Vector3(L.x / Mathf.Max(1e-4f, lum), L.y / Mathf.Max(1e-4f, lum), L.z / Mathf.Max(1e-4f, lum));
            var refHue = new Vector3(dayRef.x / refLum, dayRef.y / refLum, dayRef.z / refLum);
            return new Vector3(hue.x / refHue.x, hue.y / refHue.y, hue.z / refHue.z) * r;
        }

        void UpdateUnlitDust(float night)
        {
            if (Time.unscaledTime >= nextDustScan)
            {
                nextDustScan = Time.unscaledTime + 2f;
                unlitDust.Clear(); CollectUnlitDust(unlitDust);
                dustShown = -1f;                                                             // apply again: new renderers (spawned pyros) get it too
            }
            // lit by the scene: the sun (colour x intensity x how high it stands) + the ambient sky / horizon, relative to noon;
            // so the dust darkens and warms at dusk and is faint at night (was: a night-only scale, day-bright through the evening)
            var tint = SceneLightTint();
            float k = Mathf.Lerp(1f, Mathf.Clamp01(unlitDustNight), Mathf.Clamp01(night));
            tint = new Vector3(Mathf.Min(tint.x, 1.05f), Mathf.Min(tint.y, 1.05f), Mathf.Min(tint.z, 1.05f)) * Mathf.Max(k, 0.35f);
            tint = Vector3.Max(tint, Vector3.one * Mathf.Clamp01(unlitDustNight) * 0.8f);
            float key = tint.x + tint.y * 1.7f + tint.z * 0.6f;
            if (dustShown >= 0f && Mathf.Abs(key - dustShown) < 0.006f) return;
            dustShown = key; DustTint = tint;
            foreach (var r in unlitDust)
            {
                if (!r) continue;
                if (tint.x >= 0.995f && tint.y >= 0.995f && tint.z >= 0.995f) { r.SetPropertyBlock(null); continue; }   // noon: the authored look
                mpb.Clear(); mpb.SetVector(IdBaseColor, new Vector4(tint.x, tint.y, tint.z, 1f)); r.SetPropertyBlock(mpb);   // SetVector: linear
            }
        }

        // ------------------------------------------------------------------ building lamps
        NightV2BuildingSpec SpecFor(string key)
        {
            if (data?.buildings == null) return null;
            foreach (var b in data.buildings) if (b != null && b.key == key) return b;
            return null;
        }

        static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds();
            var w2l = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                var mbb = m.bounds; var M = w2l * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mbb.center + Vector3.Scale(mbb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = M.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Vector3 V3(float[] a, Vector3 fallback) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

        void TryBuild()
        {
            nextBuildTry = Time.unscaledTime + 0.5f;
            if (game == null || game.World == null || data?.buildings == null) { if (++buildTries > 20) built = true; return; }
            if (game.StaticViews.Count == 0 && ++buildTries <= 20) return;                    // views not bound yet
            built = true;
            var seen = new HashSet<int>();
            int lampTotal = 0;
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building) continue;
                string key = o.Type == "fort_front" || o.Type == "fort_back" ? "fort" : o.Type;
                var spec = SpecFor(key); if (spec == null || spec.lamps == null || spec.lamps.Length == 0) continue;
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go || !seen.Add(go.GetInstanceID())) continue;
                var rig = new Rig { B = o, Key = key, View = go, LastOwner = o.Owner };
                var root = new GameObject($"NightV2Lamps_{o.Type}_{o.Id}").transform; root.SetParent(transform, false);
                root.SetPositionAndRotation(go.transform.position, Quaternion.Euler(0, go.transform.eulerAngles.y, 0));
                rig.Root = root; rig.MB = LocalBounds(go.transform);
                if (rig.MB.size.x < 0.5f || rig.MB.size.y < 0.5f) { Debug.LogWarning($"[OpenZ] NightV2: no model bounds on {go.name}"); Destroy(root.gameObject); continue; }
                // measured sockets are exact in prefab-root metres (pm) while the model matches the measured bounds; a rescaled
                // or swapped model falls back to the bounds fractions (p), like the night package rigs
                var want = V3(spec.boundsMax, Vector3.one) - V3(spec.boundsMin, Vector3.zero);
                var got = rig.MB.size;
                // footprint only: the height can differ while a split part (fort gun, radar dish) is re-parented or animated
                bool exact = Mathf.Abs(got.x / want.x - 1f) < 0.03f && Mathf.Abs(got.z / want.z - 1f) < 0.03f && Mathf.Abs(got.y / want.y - 1f) < 0.35f;
                if (!exact) Debug.LogWarning($"[OpenZ] NightV2: {go.name} model bounds {got} differ from the measured {want}: lamps placed by fractions, rerun tools/lamp_sockets.py");
                int k = 0;
                foreach (var ls in spec.lamps)
                {
                    if (ls == null || ls.p == null || ls.p.Length < 3) continue;
                    var local = exact && ls.pm != null && ls.pm.Length >= 3 ? V3(ls.pm, Vector3.zero)
                                                                            : rig.MB.min + Vector3.Scale(V3(ls.p, Vector3.zero), rig.MB.size);
                    var n = V3(ls.n, Vector3.forward).normalized;
                    rig.Lamps.Add(BuildLamp(rig, ls, local, n, k++));
                }
                lampTotal += rig.Lamps.Count;
                rigs.Add(rig);
            }
            Debug.Log($"[OpenZ] NightV2 {(game ? game.level : "?")}: {rigs.Count} buildings, {lampTotal} wall lamps; night classes rim {rimK} fill {fillK}, reflection follow {envFollow}");
        }

        Lamp BuildLamp(Rig rig, NightV2LampSpec s, Vector3 local, Vector3 n, int idx)
        {
            float size = Mathf.Clamp(s.size, 0.5f, 2f);
            var up = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
            var rot = Quaternion.LookRotation(n, up);
            var root = new GameObject("Lamp_" + s.id).transform; root.SetParent(rig.Root, false);
            root.localPosition = local + n * 0.015f; root.localRotation = rot;
            // bulkhead housing (gunmetal, the night package's fixture material = building class)
            NightMeshes.Part("Housing", root, HousingMesh(), NightMeshes.Housing(), true, Vector3.zero, Quaternion.identity, Vector3.one * size);
            var lamp = new Lamp { Spec = s, Root = root, Seed = Hash(rig.B.Id * 131 + idx), Gain = Mathf.Max(0f, s.intensity) };
            lamp.Stagger = lamp.Seed * 0.012f;
            var lensPos = new Vector3(0f, -0.005f, 0.115f) * size;                       // 1.5 cm proud of the body front: never co-planar
            lamp.Lens = NightMeshes.Part("Lens", root, NightMeshes.Quad(), NightMeshes.Lens(), false, lensPos, Quaternion.identity, new Vector3(0.085f, 0.05f, 1f) * size).GetComponent<Renderer>();
            lamp.Halo = NightMeshes.Part("Halo", root, NightMeshes.Quad(), NightMeshes.Halo(), false, lensPos + Vector3.forward * 0.12f, Quaternion.identity, Vector3.one * 0.5f * size).GetComponent<Renderer>();
            if (washMat)
            {
                // the wash volume: centred on the lens, its +z tilted down so the lamp lights the wall below it and the ground in front
                float reach = Mathf.Clamp(s.wash, 0.8f, 6f);
                var fwdLocal = (Vector3.forward * 0.8f + (Quaternion.Inverse(rot) * Vector3.down) * 0.6f).normalized;
                lamp.Wash = NightMeshes.Part("Wash", root, NightMeshes.Cube(), washMat, false, lensPos, Quaternion.LookRotation(fwdLocal, Vector3.up), Vector3.one * reach * 2f).GetComponent<Renderer>();
            }
            SetLevel(lamp, 0f, true);
            return lamp;
        }

        static Mesh housing;
        /// <summary>Bulkhead lamp: back plate on the wall, body, hood over the lens (+z = out of the wall), 0.3 x 0.22 m.</summary>
        static Mesh HousingMesh()
        {
            if (housing) return housing;
            var v = new List<Vector3>(); var nn = new List<Vector3>(); var t = new List<int>();
            Box(v, nn, t, new Vector3(0f, 0f, 0.015f), new Vector3(0.30f, 0.22f, 0.03f));        // wall plate
            Box(v, nn, t, new Vector3(0f, -0.005f, 0.06f), new Vector3(0.22f, 0.13f, 0.08f));     // body (the lens sits on its front)
            Box(v, nn, t, new Vector3(0f, 0.075f, 0.075f), new Vector3(0.26f, 0.025f, 0.13f));    // hood
            housing = new Mesh { name = "NightV2_Bulkhead", hideFlags = HideFlags.DontSave };
            housing.SetVertices(v); housing.SetNormals(nn); housing.SetTriangles(t, 0); housing.RecalculateBounds();
            return housing;
        }

        static void Box(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            Vector3[] N = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nn in N)
            {
                Vector3 u = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up, w = Vector3.Cross(nn, u);
                int b = v.Count;
                Vector3 fc = c + Vector3.Scale(nn, h), uu = Vector3.Scale(u, h), ww = Vector3.Scale(w, h);
                v.Add(fc - uu - ww); v.Add(fc + uu - ww); v.Add(fc + uu + ww); v.Add(fc - uu + ww);
                for (int i = 0; i < 4; i++) n.Add(nn);
                if (Vector3.Dot(Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]), nn) >= 0) t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                else t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
        }

        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }

        void UpdateLamps(float t)
        {
            int lit = 0;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            foreach (var rig in rigs)
            {
                var b = rig.B;
                bool gone = b.Destroyed || !rig.View || !rig.View.activeInHierarchy;
                if (gone != !rig.Shown) { rig.Shown = !gone; rig.Root.gameObject.SetActive(rig.Shown); if (rig.Shown) foreach (var l in rig.Lamps) { l.Level = 0f; SetLevel(l, 0f, true); } }
                if (gone) continue;
                if (b.Owner != rig.LastOwner) { rig.LastOwner = b.Owner; rig.BlackoutUntil = Time.time + 0.9f; }   // capture: short blackout, then the new owner's lamps
                bool owned = b.Owner != Team.Neutral && buildingLamps;
                bool damaged = b.MaxHealth > 0 && b.Health < b.MaxHealth * 0.4f;
                foreach (var l in rig.Lamps)
                {
                    bool on = owned && Time.time >= rig.BlackoutUntil + l.Stagger * 40f && NightGrade.LightsOn(t, switchOnAt + l.Stagger, switchOffAt - l.Stagger * 0.5f);
                    float target = on ? 1f : 0f;
                    float rate = target > l.Level ? 1f / 0.6f : 1f / 0.3f;
                    l.Level = Mathf.MoveTowards(l.Level, target, dt * rate);
                    float k = l.Level;
                    if (k > 0f && k < 1f && target > 0f) k *= 0.55f + 0.45f * Mathf.PerlinNoise(l.Seed * 17f, Time.time * 9f);   // warm-up flicker
                    if (damaged && Mathf.PerlinNoise(l.Seed * 23f, Time.time * 5f) < 0.3f) k *= 0.12f;                        // damaged: drop-outs
                    if (k > 0.02f) lit++;
                    SetLevel(l, k, false);
                }
            }
            LampsLit = lit;
        }

        void SetLevel(Lamp l, float k, bool force)
        {
            if (!force && Mathf.Abs(k - l.Shown) < 0.004f) return;
            l.Shown = k;
            bool vis = k > 0.002f;
            // linear project (FxManager.cs:45), which turned the 5.5 lens into ~47 / 14 / 2 (orange-red) and the 1.6 wash into
            // ~2.8 / 0.9 / 0.15. SetVector hands the shader exactly the linear gains the CPU preview (night_preview.py) used.
            var c = lampColor.linear * l.Gain;
            if (l.Lens) { l.Lens.enabled = vis; if (vis) { mpb.Clear(); mpb.SetVector(IdColor, c * (5.5f * k * lensGain)); l.Lens.SetPropertyBlock(mpb); } }
            if (l.Halo) { l.Halo.enabled = vis; if (vis) { mpb.Clear(); mpb.SetVector(IdColor, c * (0.9f * k * haloGain)); l.Halo.SetPropertyBlock(mpb); } }
            if (l.Wash) { l.Wash.enabled = vis; if (vis) { mpb.Clear(); mpb.SetVector(IdColor, c * (1.6f * k * washGain)); l.Wash.SetPropertyBlock(mpb); } }
        }

        // ------------------------------------------------------------------ debug
        void DebugKeys()
        {
            var kb = Keyboard.current; if (kb == null || !kb.f9Key.wasPressedThisFrame || !kb.ctrlKey.isPressed) return;
            if (kb.altKey.isPressed) Debug.Log(Status());
            else if (kb.shiftKey.isPressed) NightLumaCheck.Run();
        }

        /// <summary>Night class of a material as OpenZ/TeamLit resolves it (-1 = not a TeamLit material).</summary>
        public static int ClassOf(Material m)
        {
            if (!m || !m.shader || m.shader.name != "OpenZ/TeamLit") return -1;
            float c = m.HasProperty("_NightClass") ? m.GetFloat("_NightClass") : -1f;
            if (c > -0.5f) return Mathf.Clamp(Mathf.RoundToInt(c), 0, 3);
            float F(string p) => m.HasProperty(p) ? m.GetFloat(p) : 0f;
            var rim = m.HasProperty("_RimColor") ? m.GetColor("_RimColor") : Color.black;
            // a linear project hands the shader rim.linear): unit rims 0.22-0.28, rocks / cliffs v3 0.011 -> threshold 0.05
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) rim = rim.linear;
            bool unit = Mathf.Max(F("_Stylize"), F("_Accent")) > 0.5f && Lum(rim) >= 0.05f && F("_Triplanar") < 0.5f && F("_SeeThru") < 0.5f;
            return unit ? ClassUnit : ClassEnvironment;
        }

        public string Status()
        {
            float t = dayNight ? dayNight.timeOfDay : NightGrade.TimeOfDay;
            var sb = new StringBuilder();
            sb.Append($"[OpenZ] NightV2 {(game ? game.level : "?")}  t {t:F3}  night {Night:F2}  sky ratio {SkyRatio:F3} (reflection scale, {(envFix ? "on" : "OFF")})\n");
            sb.Append($"  classes rim {rimK}  fill {fillK}  reflection follow {envFollow}\n");
            var probeUp = new Color[1]; RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up }, probeUp);
            sb.Append($"  ambient sky {RenderSettings.ambientSkyColor} (linear lum {Lum(RenderSettings.ambientSkyColor.linear):F4}), ambient probe up {probeUp[0]} (lum {Lum(probeUp[0]):F4}; tracks DayNight when it follows the sky)\n");
            sb.Append($"  default reflection: {(ReflectionProbe.defaultTexture ? ReflectionProbe.defaultTexture.name + " " + ReflectionProbe.defaultTexture.width + " px" : "none")}, mode {RenderSettings.defaultReflectionMode}, intensity {RenderSettings.reflectionIntensity:F2}\n");
            sb.Append($"  wall lamps {LampsLit}/{LampCount} lit on {rigs.Count} buildings (wash {(washMat ? "on" : "MISSING")})\n");
            sb.Append($"  unlit ambient dust: {unlitDust.Count} particle renderers at x{(dustShown < 0f ? 1f : dustShown):F2} (night target x{unlitDustNight:F2})\n");
            foreach (var r in rigs)
            {
                int lit = 0; foreach (var l in r.Lamps) if (l.Shown > 0.02f) lit++;
                sb.Append($"   {r.B.Type}#{r.B.Id} {r.B.Owner}{(r.B.Destroyed ? " DESTROYED" : "")}: {lit}/{r.Lamps.Count} lit\n");
            }
            return sb.ToString();
        }

        /// <summary>Static entry for the editor menu.</summary>
        public static string StatusText() => I ? I.Status() : "[OpenZ] NightV2 not running (no level scene, AutoBoot off, or not imported)";
    }
}

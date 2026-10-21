using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Three cues, every value tunable live on this component in Play mode:
    /// <list type="number">
    /// <item>TeamLit globals (_OZTeamLook, _OZTeamRim): vivid team accents, a team panel on turret tops / roofs
    /// (TeamPaint.PanelRules), a light livery tint on owned paint, flat grey paint and dark unlit accents on neutral (empty)
    /// objects, a thin team rim by day on vehicles / cannons / buildings and a stronger team rim at night (robots too).</item>
    /// <item>Ground team discs under every owned vehicle, cannon and robot: a soft fill and a ring, drawn on the ground under
    /// the unit (depth tested, the unit covers its middle), a little brighter at night.</item>
    /// <item>The empty marker: a dashed grey ring under every neutral (empty) vehicle and cannon, the ones any robot can take, and
    /// (fainter, just outside the footprint) around every neutral factory, radar and repair station (never forts or bridges).</item>
    /// </list>
    /// The globals are 0 until set, so TeamLit renders exactly the old look without this class. Nothing here touches the sim.
    /// Drawn with Graphics.DrawMesh (no renderer inside the unit, so TeamPaint, UnitView bounds and BuildingBreaker never see
    /// the discs), OpenZ/Overlay material (queue 3049: under the selection rings, 3050).
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class TeamReadability : MonoBehaviour
    {
        [Header("TeamLit: vehicles, cannons, buildings")]
        [Range(0, 1), Tooltip("Accents (lenses, stripes, bands) in the vivid team colour; neutral: dark unlit grey")] public float accentVivid = 1f;
        [Range(0, 0.6f), Tooltip("Team tint on the owned paint (0 = khaki like v1)")] public float livery = 0.18f;
        [Range(0, 1), Tooltip("Team panels on turret tops / roofs (TeamPaint.PanelRules)")] public float panels = 1f;
        [Range(0, 1), Tooltip("Neutral (empty) objects: flat grey paint")] public float neutralGrey = 1f;
        [Header("Team rim (thin outline)")]
        [Range(0, 1)] public float rimDay = 0.4f;
        [Range(0, 1)] public float rimNight = 0.15f;
        [Range(0, 2)] public float rimGain = 0.45f;
        [Range(0, 0.3f), Tooltip("Team panels glow faintly at night")] public float nightPanelLift = 0.10f;
        [Header("Ground discs")]
        // The team discs are off (selection rings + the range rings are the only circles under owned units)
        public bool discs = false;
        public bool robotDiscs = false;
        [Tooltip("Dashed grey ring under neutral (empty) vehicles and cannons")] public bool emptyMarkers = true;
        [Tooltip("Dashed grey ring around neutral factories, radars and repair stations")] public bool emptyBuildings = true;
        [Range(0, 1)] public float fillCentre = 0.10f, fillEdge = 0.20f, ringAlpha = 0.80f, emptyAlpha = 0.8f, robotAlpha = 0.6f;
        [Range(0, 1), Tooltip("Neutral building ring alpha x emptyAlpha")] public float buildingEmptyAlpha = 0.7f;
        [Tooltip("Neutral building ring radius x the building's half footprint (clamped 3..8 m)")] public float buildingRadiusScale = 1.05f;
        [Range(0.5f, 2f), Tooltip("Disc radius x the vehicle footprint (clamped 1.7..3.3 m)")] public float radiusScale = 1.12f;
        // half 2.0 m -> ring 1.4-1.8 m, base half 1.7 m) and the depth test hid almost all of it, the empty marker included
        [Range(0.5f, 2f), Tooltip("Cannons: disc radius x the half footprint (clamped 2.2..4.2 m), so the ring clears the base / sandbags")] public float cannonRadiusScale = 1.25f;
        public float robotRadius = 0.6f, toughRadius = 0.72f;
        public float dayGain = 0.95f, nightGainRed = 0.85f, nightGainBlue = 0.45f, nightGainEmpty = 0.4f;
        [Tooltip("Height above the walk surface (m); the selection ring sits at 0.25")] public float lift = 0.18f;

        public static TeamReadability I { get; private set; }
        public static readonly Vector4 DefaultLook = new Vector4(1f, 0.18f, 1f, 1f), DefaultRim = new Vector4(0.4f, 0.15f, 0.45f, 0.10f);
        static readonly int LookId = Shader.PropertyToID("_OZTeamLook"), RimId = Shader.PropertyToID("_OZTeamRim"), ColorId = Shader.PropertyToID("_Color"),
                            ZTestId = Shader.PropertyToID("_ZTest");
        static bool globalsSet;

        /// <summary>Sets the TeamLit globals once per domain (TeamPaint.Apply calls it, so edit mode shows the v2 look too).</summary>
        public static void EnsureGlobals()
        {
            if (globalsSet) return;
            globalsSet = true;
            Shader.SetGlobalVector(LookId, I ? I.Look : DefaultLook);
            Shader.SetGlobalVector(RimId, I ? I.Rim : DefaultRim);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("TeamReadability");
            DontDestroyOnLoad(go);
            go.AddComponent<TeamReadability>();
        }

        Vector4 Look => new Vector4(accentVivid, livery, panels, neutralGrey);
        Vector4 Rim => new Vector4(rimDay, rimNight, rimGain, nightPanelLift);
        Vector4 shownLook = new Vector4(-1, 0, 0, 0), shownRim;
        Mesh discMesh, dashMesh; float meshKey = -1f;
        Material redMat, blueMat, robotRedMat, robotBlueMat, emptyMat, emptyBuildingMat; bool matsFailed;
        readonly List<UnitView> views = new List<UnitView>();
        readonly Dictionary<int, float> radii = new Dictionary<int, float>();
        readonly Dictionary<int, float> groundY = new Dictionary<int, float>();
        int groundCursor;
        // neutral buildings (rebuilt every 0.5 s: ownership flips with the zone flag): (sim object, ring position on the ground, radius)
        readonly List<(ZObject o, Vector3 p, float r)> neutralBuildings = new List<(ZObject, Vector3, float)>();
        readonly Dictionary<int, (Vector3 off, float r)> buildingRing = new Dictionary<int, (Vector3, float)>();
        float nextBuildingScan;

        void OnEnable() { if (!I) I = this; }
        void OnDisable() { if (I == this) I = null; }

        void PushGlobals()
        {
            var l = Look; var r = Rim;
            if (l == shownLook && r == shownRim) return;
            shownLook = l; shownRim = r; globalsSet = true;
            Shader.SetGlobalVector(LookId, l);
            Shader.SetGlobalVector(RimId, r);
        }

        // ------------------------------------------------------------------ meshes (radial alpha profiles, vertex colour alpha)
        static Mesh Radial(float[] radius, float[] alpha, int seg)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            int rings = radius.Length;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < rings; k++) { v.Add(d * radius[k]); c.Add(new Color(1f, 1f, 1f, alpha[k])); }
                if (i < seg)
                    for (int k = 0; k < rings - 1; k++)
                    {
                        int a0 = i * rings + k, a1 = a0 + 1, b0 = a0 + rings, b1 = b0 + 1;
                        t.AddRange(new[] { a0, b0, b1, a0, b1, a1 });
                    }
            }
            var m = new Mesh { name = "TeamDisc" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh Dashed(int dashes, float duty, int sub)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float[] rr = { 0.79f, 0.82f, 0.97f, 1.0f }; float[] aa = { 0f, 1f, 1f, 0f };
            for (int s = 0; s < dashes; s++)
            {
                float a0 = s * Mathf.PI * 2f / dashes, a1 = a0 + Mathf.PI * 2f / dashes * duty;
                int start = v.Count;
                for (int i = 0; i <= sub; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)sub); var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    for (int k = 0; k < rr.Length; k++) { v.Add(d * rr[k]); c.Add(new Color(1f, 1f, 1f, aa[k])); }
                    if (i < sub)
                        for (int k = 0; k < rr.Length - 1; k++)
                        {
                            int p0 = start + i * rr.Length + k, p1 = p0 + 1, q0 = p0 + rr.Length, q1 = q0 + 1;
                            t.AddRange(new[] { p0, q0, q1, p0, q1, p1 });
                        }
                }
            }
            var m = new Mesh { name = "TeamEmptyRing" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        void EnsureMeshes()
        {
            float key = fillCentre * 7.1f + fillEdge * 3.3f + ringAlpha * 1.7f;
            if (discMesh && Mathf.Approximately(key, meshKey)) return;
            meshKey = key;
            if (discMesh) Destroy(discMesh);
            discMesh = Radial(new[] { 0f, 0.2f, 0.82f, 0.84f, 0.98f, 1.04f }, new[] { fillCentre, fillCentre, fillEdge, ringAlpha, ringAlpha, 0f }, 48);
            if (!dashMesh) dashMesh = Dashed(14, 0.55f, 5);
        }

        bool EnsureMaterials()
        {
            if (redMat) return true;
            if (matsFailed) return false;
            var so = FindAnyObjectByType<SelectionOverlay>();
            Material baseMat = so && so.overlayMaterial ? so.overlayMaterial : null;
            if (!baseMat)
            {
                var sh = Shader.Find("OpenZ/Overlay");
                if (!sh) { matsFailed = true; Debug.LogWarning("[TeamReadability] OpenZ/Overlay shader not found: team discs off"); return false; }
                baseMat = new Material(sh);
            }
            Material Make(string n) { var m = new Material(baseMat) { name = n, renderQueue = 3049, hideFlags = HideFlags.DontSave }; m.SetFloat(ZTestId, 4f); return m; }
            redMat = Make("TeamDiscRed"); blueMat = Make("TeamDiscBlue"); robotRedMat = Make("TeamDiscRobotRed"); robotBlueMat = Make("TeamDiscRobotBlue");
            emptyMat = Make("TeamEmptyRing"); emptyBuildingMat = Make("TeamEmptyRingBuilding");
            return true;
        }

        /// <summary>HDR colour with the gain applied in LINEAR space (SetColor linearises; FxManager.Lin).</summary>
        static Color Lin(Color c, float gain, float a) { var g = (c.linear * gain).gamma; g.a = a; return g; }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            PushGlobals();
            var g = ZGame.I;
            if (!discs || g == null || g.World == null) return;
            if (!EnsureMaterials()) return;
            EnsureMeshes();
            float n = NightGrade.NightK;
            float gR = TeamColors.Swap ? nightGainBlue : nightGainRed, gB = TeamColors.Swap ? nightGainRed : nightGainBlue;
            redMat.SetColor(ColorId, Lin(TeamColors.DiscRed, Mathf.Lerp(dayGain, gR, n), 1f));
            blueMat.SetColor(ColorId, Lin(TeamColors.DiscBlue, Mathf.Lerp(dayGain, gB, n), 1f));
            robotRedMat.SetColor(ColorId, Lin(TeamColors.DiscRed, Mathf.Lerp(dayGain, gR, n), robotAlpha));
            robotBlueMat.SetColor(ColorId, Lin(TeamColors.DiscBlue, Mathf.Lerp(dayGain, gB, n), robotAlpha));
            emptyMat.SetColor(ColorId, Lin(TeamColors.DiscEmpty, Mathf.Lerp(dayGain, nightGainEmpty, n), emptyAlpha));
            emptyBuildingMat.SetColor(ColorId, Lin(TeamColors.DiscEmpty, Mathf.Lerp(dayGain, nightGainEmpty, n), emptyAlpha * buildingEmptyAlpha));
            if (emptyMarkers && emptyBuildings) DrawNeutralBuildings(g);

            views.Clear();
            foreach (var kv in g.Views) if (kv.Value) views.Add(kv.Value);
            // the walk surface under a unit: its own drawn y, or the render ground when that sits higher (12 raycasts a frame)
            for (int k = 0; k < 12 && views.Count > 0; k++)
            {
                var v = views[(groundCursor + k) % views.Count];
                if (v.Obj != null) { var p = v.transform.position; groundY[v.Obj.Id] = g.GroundY(p.x, p.z); }
            }
            groundCursor = views.Count > 0 ? (groundCursor + 12) % views.Count : 0;

            foreach (var v in views)
            {
                if (!v.isActiveAndEnabled || !v.gameObject.activeInHierarchy || v.Dying || ZodBriefing.EndingHides(v)) continue;   // version 1.1: not in the level 16 ending's picture (ZodBriefing.EndingEnemies.cs)
                var o = v.Obj;
                if (o == null || o.Destroyed) continue;
                bool robot = o.Kind == ZKind.Robot;
                if (!robot && o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) continue;
                Material m; Mesh mesh;
                if (o.Owner == Team.Neutral)
                {
                    if (robot || !emptyMarkers) continue;
                    m = emptyMat; mesh = dashMesh;
                }
                else
                {
                    if (robot && !robotDiscs) continue;
                    bool red = o.Owner == Team.Red;
                    m = robot ? (red ? robotRedMat : robotBlueMat) : (red ? redMat : blueMat);
                    mesh = discMesh;
                }
                float r = Radius(v);
                var pos = v.transform.position;
                if (groundY.TryGetValue(o.Id, out var gy) && gy > pos.y && gy - pos.y < 1.5f) pos.y = gy;
                pos.y += lift;
                Graphics.DrawMesh(mesh, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(r, 1f, r)), m, 0, null, 0, null,
                                  ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }

        /// <summary>The empty marker for buildings: a dashed ring just outside every neutral factory / radar / repair station
        /// (forts are never neutral, bridges are not buildings you take). Ring size from the building's renderer bounds, once.</summary>
        void DrawNeutralBuildings(ZGame g)
        {
            if (Time.unscaledTime >= nextBuildingScan)
            {
                nextBuildingScan = Time.unscaledTime + 0.5f;
                neutralBuildings.Clear();
                foreach (var o in g.World.Objects)
                {
                    if (o.Kind != ZKind.Building || o.Owner != Team.Neutral || o.Destroyed || o.IsFort || o.IsBridge) continue;
                    if (!g.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.activeInHierarchy) continue;
                    if (!buildingRing.TryGetValue(o.Id, out var ring))
                    {
                        Bounds b = default; bool any = false;
                        foreach (var rr in go.GetComponentsInChildren<Renderer>())
                        {
                            if (rr is ParticleSystemRenderer || rr is TrailRenderer || rr is LineRenderer) continue;
                            if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds);
                        }
                        if (!any) continue;
                        var off = new Vector3(b.center.x, 0f, b.center.z) - new Vector3(go.transform.position.x, 0f, go.transform.position.z);
                        ring = (off, Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * buildingRadiusScale, 3f, 8f));
                        buildingRing[o.Id] = ring;
                    }
                    var p = go.transform.position + ring.off;
                    p.y = g.GroundY(p.x, p.z) + lift;                                  // buildings never move: one ground sample per scan
                    neutralBuildings.Add((o, p, ring.r));
                }
            }
            foreach (var (o, p, r) in neutralBuildings)
            {
                if (o.Owner != Team.Neutral || o.Destroyed) continue;          // flipped since the last scan
                Graphics.DrawMesh(dashMesh, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(r, 1f, r)), emptyBuildingMat, 0, null, 0, null,
                                  ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }

        float Radius(UnitView v)
        {
            var o = v.Obj;
            if (radii.TryGetValue(o.Id, out var r)) return r;
            if (o.Kind == ZKind.Robot) r = o.Type == "tough" ? toughRadius : robotRadius;
            else
            {
                Bounds b = default; bool any = false;
                foreach (var rr in v.GetComponentsInChildren<Renderer>())
                {
                    if (rr is ParticleSystemRenderer || rr is TrailRenderer || rr is LineRenderer) continue;
                    if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds);
                }
                float half = any ? Mathf.Max(b.extents.x, b.extents.z) : 2f;
                r = o.Kind == ZKind.Cannon ? Mathf.Clamp(half * cannonRadiusScale, 2.2f, 4.2f)   // just outside the base (check fix)
                                           : Mathf.Clamp(half * 0.8f * radiusScale, 1.7f, 3.3f);
            }
            radii[o.Id] = r;
            return r;
        }

        void OnValidate() { meshKey = -1f; radii.Clear(); buildingRing.Clear(); nextBuildingScan = 0f; }
    }
}

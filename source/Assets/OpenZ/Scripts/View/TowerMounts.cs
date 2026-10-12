using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// (fort-local px (16,0) (112,0) (16,48) (112,48), bfort.cpp:534-548; ZWorld.MarkFortTurretCannons / PlaceCannon). The sim
    /// keeps the Zod spot; the VIEW seats each such cannon exactly on the centre of its tower pad of the imported fort (Z_fort,
    /// fort_front and fort_back), on a short gunmetal ring mount, so it reads as mounted. Before this the scene placement
    /// (OpenZSetup.FortRoofY: cap centroid - 0.35 m) was 0.35 m off-centre and 0.35 m sunk, and at run time UnitView put every
    /// cannon on the terrain under its sim centre (3.7 m off the pad and 9 m down, inside the fort).
    ///
    /// (+-6.93, +5.34), about 3.4 m across. The pad of a cannon = the fort-local quadrant of the cannon's sim centre (column 1 =
    /// -x, column 7 = +x, row 0 / row 3 = the two z sides; works for fort_front's 180 deg turn).
    ///
    /// Static helpers (UnitView, BoardingView, the socket audit) + a component on the ZGame object that builds the ring
    /// mounts (one per pad, every fort, OZ_TowerMounts root, hidden with the fort). View only, Zod parity untouched.
    ///
    /// the tower top is an octagonal plate (1.82 m across, top 9.24) at x +-6.986 (not +-6.92: 6.6 cm off), a groove, a flat ledge
    /// ring at 9.00 from r 1.2 to r 1.6-1.8, then the dome. crews_v2's 1.0 m disc covered only the plate, so the 2.1 m square gun base
    /// hung 0.5 m past it at its corners, 0.4 m over the ledge. And FootprintCentreLocal read mesh.bounds, which OpenZTurretSplit sets
    /// to the WHOLE model on every part: it returned the model centre (howitzer x 0), so the howitzer's real plate (centre x -0.538)
    /// sat 0.39 m off the pad centre. Now:
    ///  * pad centres = the measured plate centres (+-1 cm);
    ///  * the mount is a turret-ring plinth standing ON the ledge and covering plate and groove: steel drum r 1.42 (the square base
    ///    only overhangs at its 4 corners, by 12 cm), dark foot flange r 1.48 set 10 cm into the ledge (covers the cap seams), a dark
    ///    groove band with 16 bolt heads, a dark seam ring on its top, top 0.10 m over the plate. Two submeshes (steel, dark);
    ///  * FootprintCentreLocal = the part's own geometry (submesh bounds);
    ///  * rest heading = the tower's exact outward diagonal (fort yaw +-45 / +-135): the four guns are mirror images (was the raw
    ///    centre-to-pad angle, 46.4 / 52.7 deg: a different skew on every tower). PlayerPrefs oz_tower_diag = 0: the old angle;
    ///  * tower gunners come up on the ledge at r 1.75 (LedgeRadius), clear of the plinth (crews_v2's stand at r 1.76 is kept).
    /// HowitzerV2Setup puts the howitzer's turret pivot on its real turntable (-0.42 -> -0.546), so the turret no longer orbits
    /// 9 cm off its ring when it turns.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class TowerMounts : MonoBehaviour
    {
        // ------------------------------------------------------------------ pad data (Z_fort, fort-local space)
        public const float PadTopY = 9.24f;
        public const float PadDiameter = 3.4f;
        /// <summary>Pad centres on the pad top: index = (x &gt;= 0 ? 1 : 0) + (z &gt;= 0 ? 2 : 0). howitzer_towers_v1: the measured centres
        /// of the flat octagonal plates (bounding box of each plate's up-facing faces on FortBase; was +-6.92 / +-6.93, -6.58 / 5.34).</summary>
        public static readonly Vector3[] PadLocal =
        {
            new Vector3(-6.985f, PadTopY, -6.563f), new Vector3(6.987f, PadTopY, -6.564f),
            new Vector3(-6.984f, PadTopY, 5.330f), new Vector3(6.985f, PadTopY, 5.314f),
        };
        /// <summary>Mount: height of its top above the pad (plate) top and its drum radius. crews_v2: a 1.0 m disc on the plate.
        /// howitzer_towers_v1: a plinth standing on the ledge ring (0.24 m below the plate), covering plate and groove: drum r 1.42,
        /// foot flange r 1.48, top 0.10 m over the plate (the square base at 0.72 overhangs only at its corners, r 1.54).</summary>
        public const float MountHeight = 0.10f, MountRadius = 1.42f;
        /// <summary>crews_v2: flat pad top radius, and the ledge ring LedgeDrop below it (flat from r 1.2 to r 1.6-1.8) where tower
        /// gunners come up. howitzer_towers_v1: they come up at r 1.75, outside the plinth's flange; drop measured 0.24.</summary>
        public const float PadFlatRadius = 1.06f, LedgeRadius = 1.75f, LedgeDrop = 0.24f;
        /// <summary>howitzer_towers_v1: plinth foot flange radius and how deep its foot goes below the pad top (10 cm into the ledge).</summary>
        public const float PlinthFootRadius = 1.48f, PlinthFootDepth = 0.34f;
        /// <summary>The cannon's base sits 1 cm into the mount top (never co-planar).</summary>
        public const float SeatSink = 0.01f;
        /// <summary>Tower cannons are drawn at this scale (OpenZSetup: "scaled to the tower top"); run-time placed ones get it too.</summary>
        public const float CannonScale = 0.72f;
        /// <summary>A missile cannon's launcher (two rocket boxes in front of its turn axis) reaches further out than the other tower
        /// guns: at CannonScale its boxes hang over the tower top's rim when it turns. On a tower it is drawn at this scale, so the
        /// whole launcher stays inside the rim at every turn angle; its muzzles, recoil and gunner ride on the model and scale with
        /// it. On the ground it keeps its full size.</summary>
        public const float MissileCannonScale = 0.57f;
        /// <summary>The draw scale of a cannon of this type on a tower (UnitView at run time, the placement ghost, the editor seat).</summary>
        public static float CannonScaleFor(string type) => type == "missile_cannon" ? MissileCannonScale : CannonScale;
        /// <summary>Turn the seating off (PlayerPrefs oz_tower_seat = 0): back to the old ground placement. Read once, lazily (UnitView
        /// binds in ZGame.Awake, before any AfterSceneLoad hook).</summary>
        public static bool Enabled
        {
            get { if (enabledState < 0) enabledState = PlayerPrefs.GetInt("oz_tower_seat", 1) != 0 ? 1 : 0; return enabledState == 1; }
            set => enabledState = value ? 1 : 0;
        }
        static int enabledState = -1;

        /// <summary>Zod fort turret spot test (bfort.cpp:534-548): the cannon's top-left pixel relative to the fort's.</summary>
        public static bool IsTurretSpot(int fortLocX, int fortLocY, int cannonLocX, int cannonLocY)
        {
            int lx = cannonLocX - fortLocX, ly = cannonLocY - fortLocY;
            return (lx == 16 || lx == 112) && (ly == 0 || ly == 48);
        }
        /// <summary>Same in map tiles (level builder names type_x_y).</summary>
        public static bool IsTurretTile(int fortX, int fortY, int cannonX, int cannonY) => IsTurretSpot(fortX * 16, fortY * 16, cannonX * 16, cannonY * 16);

        public static ZObject FortOf(ZWorld w, ZObject cannon)
        {
            if (w == null || cannon == null || cannon.Kind != ZKind.Cannon) return null;
            foreach (var o in w.Objects) if (o.IsFort && IsTurretSpot(o.LocX, o.LocY, cannon.LocX, cannon.LocY)) return o;
            return null;
        }

        public static int PadIndex(Transform fort, Vector3 world)
        {
            var l = fort.InverseTransformPoint(world);
            return (l.x >= 0f ? 1 : 0) + (l.z >= 0f ? 2 : 0);
        }
        /// <summary>The pad for a Zod turret spot (lx 16 / 112 = west / east, ly 0 / 48 = north / south row): the pad whose world
        /// south row (ly 48) on the north pads too (both rows lie north of the fort's centre), two howitzers on one pad.</summary>
        public static int PadForSpot(Transform fort, int lx, int ly)
        {
            float sx = lx >= 64 ? 1f : -1f, sz = ly < 24 ? 1f : -1f;                            // east = +x, north = +z (world z = -px y)
            int best = 0; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var d = PadWorld(fort, i) - fort.position;
                float score = d.x * sx + d.z * sz;                                                  // furthest along the spot's diagonal
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
        public static Vector3 PadWorld(Transform fort, int pad) => fort.TransformPoint(PadLocal[Mathf.Clamp(pad, 0, 3)]);
        /// <summary>Where the cannon's root (its base, the model pivot) goes: pad centre, on top of the ring mount.</summary>
        public static Vector3 SeatWorld(Transform fort, int pad) => PadWorld(fort, pad) + fort.up * (MountHeight - SeatSink);

        /// <summary>
        /// cannon, used by UnitView at run time and by the editor (TowerGunSeat: the saved L02 / L17 scenes and OpenZSetup's level
        /// build), checked by both gates. Until now only UnitView applied it: the scenes kept OpenZSetup.FortRoofY's placement (root on
        /// the cap centre, and that is what every edit-mode Game / Scene view and edit-mode capture shows.
        /// Root = seat - rot * (FootprintCentreLocal x scale): the base plate centre (= the turret's turn axis within 1 cm on every
        /// type) on the measured plate centre, the root on the plinth top.</summary>
        public static Vector3 SeatRoot(Transform fort, int pad, Quaternion rot, Vector3 footprintLocal, float scale) => SeatWorld(fort, pad) - rot * (footprintLocal * scale);
        /// <summary>tower_gun_fix: the root yaw (world, deg) of a seated tower cannon (= UnitView's yaw + modelYaw).</summary>
        public static float SeatYaw(Transform fort, int pad, string type) => OutwardHeading(fort, pad) + RestYawOffset(type);
        /// <summary>tower_gun_fix: seat a cannon transform exactly as UnitView does at run time (size x CannonScale once, outward rest
        /// heading, base plate on the pad centre, root on the plinth top). Edit mode and Play.</summary>
        public static void ApplySeat(Transform unit, Transform fort, int pad, string type)
        {
            if (!unit || !fort) return;
            if (unit.localScale.x > 0.9f) unit.localScale *= CannonScaleFor(type);
            var rot = Quaternion.Euler(0f, SeatYaw(fort, pad, type), 0f);
            unit.rotation = rot;
            unit.position = SeatRoot(fort, pad, rot, FootprintCentreLocal(unit), Mathf.Abs(unit.lossyScale.x));
        }
        /// <summary>tower_gun_fix: scene root of the plinths baked for edit mode (TowerGunSeat). Build() hides it and builds the run-time
        /// plinths (same mesh, same pads), so Play never shows two.</summary>
        public const string BakedRootName = "OZ_TowerMounts_Baked";

        /// <summary>
        /// pointer into a GROUND tile (ZGame.GroundPoint), but a tower pad stands 9.2 m up and 3.7 m from Zod's turret spot: pointing at a
        /// pad gave a tile behind / beside the tower (inside the fort: not placeable), and even the spot's own tile was off by one (the
        /// ghost's top-left was the tile under the pointer, the pad is the centre of a 2x2 spot). This finds the Zod turret spot
        /// (bfort.cpp:534-548 local px (16,0) (112,0) (16,48) (112,48)) whose pad the pointer ray passes over: the ray meets the pad-top
        /// plane within the pad radius + PickSlack m. Returns the spot's top-left tile, the seat (UnitView's tower seating: pad centre on
        /// the mount, the gun's base plate centred, x CannonScale) and the rest heading for a cannon of `type`.</summary>
        public static bool TurretSpotUnderRay(ZGame g, Ray ray, string type, out Vector2Int tile, out Vector3 seat, out float yawDeg)
        {
            tile = default; seat = default; yawDeg = 0f;
            if (!Enabled || !g || g.World == null) return false;
            float best = PadDiameter * 0.5f + PickSlack; bool found = false;
            foreach (var f in g.World.Objects)
            {
                if (!f.IsFort || f.Destroyed || !g.StaticViews.TryGetValue(f.Id, out var go) || !go || !go.activeInHierarchy) continue;
                var fort = go.transform;
                foreach (var (lx, ly) in TurretSpots)
                {
                    int pad = PadForSpot(fort, lx, ly);
                    var pw = PadWorld(fort, pad);
                    var plane = new Plane(fort.up, pw);
                    if (!plane.Raycast(ray, out float d)) continue;
                    var hit = ray.GetPoint(d) - pw; hit -= fort.up * Vector3.Dot(hit, fort.up);
                    if (hit.magnitude >= best) continue;
                    best = hit.magnitude; found = true;
                    tile = new Vector2Int((f.LocX + lx) >> 4, (f.LocY + ly) >> 4);
                    seat = SeatWorld(fort, pad);
                    yawDeg = OutwardHeading(fort, pad) + RestYawOffset(type);
                }
            }
            return found;
        }
        /// <summary>NB28: the four Zod turret spots (fort-local px, bfort.cpp:534-548) and the pick slack (m) round a pad.</summary>
        public static readonly (int lx, int ly)[] TurretSpots = { (16, 0), (112, 0), (16, 48), (112, 48) };
        public const float PickSlack = 1.2f;
        /// <summary>Rest heading of a tower gun (deg, world, atan2(x, z)). howitzer_towers_v1: the tower's exact outward diagonal, fort
        /// yaw +-45 / +-135 (the four guns are mirror images; a square base lines up with the plate and the fort). SnapDiagonal off:
        /// the old raw angle from the fort centre through the pad (46.4 / 52.7 deg: a different skew on every tower).</summary>
        public static float OutwardHeading(Transform fort, int pad)
        {
            if (SnapDiagonal)
            {
                var l = PadLocal[Mathf.Clamp(pad, 0, 3)];
                var f = Vector3.ProjectOnPlane(fort.forward, Vector3.up);
                float fortYaw = f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : fort.eulerAngles.y;
                return fortYaw + Mathf.Atan2(l.x >= 0f ? 1f : -1f, l.z >= 0f ? 1f : -1f) * Mathf.Rad2Deg;
            }
            var d = PadWorld(fort, pad) - fort.position; d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : fort.eulerAngles.y;
        }
        /// <summary>howitzer_towers_v1: rest heading snapped to the tower diagonal (PlayerPrefs oz_tower_diag = 0: the old angle).</summary>
        public static bool SnapDiagonal
        {
            get { if (snapState < 0) snapState = PlayerPrefs.GetInt("oz_tower_diag", 1) != 0 ? 1 : 0; return snapState == 1; }
            set => snapState = value ? 1 : 0;
        }
        static int snapState = -1;
        /// <summary>Root yaw that points the gun's rest front outward: the imported gatling / howitzer / missile cannon face model +x
        /// </summary>
        public static float RestYawOffset(string type) => type == "gatling" || type == "howitzer" || type == "missile_cannon" ? -90f : 180f;

        /// <summary>
        /// The point of a gun that goes on the pad centre, in its root space (y 0): the centre of its fixed base plate
        /// (the split imported "Base" mesh: the howitzer's plate centre is 0.12 m behind its turn axis, the gatling's and missile
        /// </summary>
        public static Vector3 FootprintCentreLocal(Transform unit)
        {
            if (!unit) return Vector3.zero;
            foreach (Transform t in unit)
            {
                if (t.name != "Base") continue;
                var mf = t.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                // howitzer_towers_v1: OpenZTurretSplit gives every part mesh the WHOLE model's bounds (TeamPaint's band), so
                // mesh.bounds.center was the model centre (howitzer x 0: its plate, centre x -0.538, sat 0.39 m off the pad);
                // the submesh bounds hold the part's own geometry
                var sm = mf.sharedMesh; var gb = sm.GetSubMesh(0).bounds;
                for (int s = 1; s < sm.subMeshCount; s++) gb.Encapsulate(sm.GetSubMesh(s).bounds);
                if (gb.size.sqrMagnitude < 1e-8f) gb = sm.bounds;
                var c = unit.InverseTransformPoint(t.TransformPoint(gb.center)); c.y = 0f;
                return c;
            }
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("TurretYaw")) { var c = unit.InverseTransformPoint(t.position); c.y = 0f; return c; }
            return Vector3.zero;
        }

        static readonly Dictionary<Transform, (Transform t, MeshTopSampler s)> fortTops = new Dictionary<Transform, (Transform, MeshTopSampler)>();

        /// <summary>crews_v2: the top of the fort's own mesh (FortBase, Read/Write on) under a world point, at or below maxY (world):
        /// the tower top, its ledge ring and dome, for tower gunners' feet. False when the mesh cannot be sampled.</summary>
        public static bool FortSurface(Transform fort, Vector3 world, float maxY, out float y)
        {
            y = 0f;
            if (!fort) return false;
            if (!fortTops.TryGetValue(fort, out var e) || !e.t)
            {
                Transform fb = null;
                foreach (var t in fort.GetComponentsInChildren<Transform>(true)) if (t.name == "FortBase") { fb = t; break; }
                var mf = fb ? fb.GetComponent<MeshFilter>() : null;
                e = (fb, mf && mf.sharedMesh ? MeshTopSampler.For(mf.sharedMesh) : null);
                fortTops[fort] = e;
            }
            if (!e.t || e.s == null) return false;
            var l = e.t.InverseTransformPoint(new Vector3(world.x, maxY, world.z));
            if (!e.s.Top(l.x, l.z, l.y, out var ly)) return false;
            y = e.t.TransformPoint(new Vector3(l.x, ly, l.z)).y;
            return true;
        }

        /// <summary>Run time: 1 = a tower cannon, seated (fort view + pad), -1 = not one, 0 = the fort's view is not bound yet.</summary>
        public static int Resolve(ZGame g, ZObject cannon, out Transform fort, out int pad)
        {
            fort = null; pad = -1;
            if (!Enabled || !g || g.World == null || cannon == null || cannon.Kind != ZKind.Cannon) return -1;
            var f = FortOf(g.World, cannon); if (f == null) return -1;
            if (!g.StaticViews.TryGetValue(f.Id, out var go) || !go) return 0;
            fort = go.transform;
            pad = PadForSpot(fort, cannon.LocX - f.LocX, cannon.LocY - f.LocY);
            return 1;
        }

        // ------------------------------------------------------------------ install
        public static TowerMounts I { get; private set; }
        public static bool AutoAttach = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach();
        }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach || !Enabled) return;
            var g = ZGame.I ? ZGame.I : Object.FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<TowerMounts>()) g.gameObject.AddComponent<TowerMounts>();
        }

        [Tooltip("Plinth drum / top: warm grey painted steel (sRGB)")] public Color mountColor = new Color(0.52f, 0.53f, 0.55f);
        [Tooltip("howitzer_towers_v1: foot flange, groove band, bolt heads, top seam ring: dark gunmetal (sRGB)")] public Color darkColor = new Color(0.21f, 0.22f, 0.24f);
        [Range(0f, 1f)] public float metallic = 0.4f, smoothness = 0.45f;
        /// <summary>
        /// The plinth's steel group (the top the gun stands on,
        /// the chamfer and the drum) in the fort's own cream as painted concrete with a very subtle grain (CapTexture: faint tonal
        /// variation, sparse pores, one fine joint ring + 4 short radial joints, slight edge wear; no cracks, no dirt). The dark flange,
        /// groove band, 16 bolt heads and top seam ring stay gunmetal. CapConcrete = false: the old grey steel top. View only.</summary>
        public static bool CapConcrete = false;
        /// <summary>
        /// A pad shows its mount ONLY while a living cannon (gun / howitzer / missile cannon) stands on it, so gun-less towers
        /// keep the model's own black tops from the start (no swap when the fort falls, both forts, both levels, day + night); the
        /// mount under a gun is a small neat pedestal: dark gunmetal (the tower top's colour family, never the white cap), radially
        /// PedestalScale x the measured plinth (drum r 1.42 -> 1.28, foot 1.48 -> 1.33: still on the ledge, r 1.2-1.8), the gun's
        /// square base covers most of it. GunPadsOnly = false: the old plinth on every pad.</summary>
        public static bool GunPadsOnly = true;
        public const float PedestalScale = 0.9f;
        [Tooltip("Pedestal steel group, dark gunmetal (sRGB)")] public Color pedestalColor = new Color(0.27f, 0.28f, 0.30f);
        public static int PadsShown { get; private set; }
        public static int PadsHidden { get; private set; }
        [Tooltip("Cap concrete base tone (sRGB), the fort_v2 wall cream (albedo median 0.80 / 0.75 / 0.65) a touch darker")]
        public Color capColor = new Color(0.75f, 0.70f, 0.60f);
        [Range(0f, 1f)] public float capSmoothness = 0.2f;

        ZGame game; Transform root; bool built; float nextSync;
        Material mat, matDark; Mesh mesh; Texture2D capTex, capNrm;
        readonly List<(ZObject fort, GameObject view, GameObject[] mounts)> forts = new List<(ZObject, GameObject, GameObject[])>();

        void Awake() { I = this; game = GetComponent<ZGame>(); if (!game) game = ZGame.I; }
        void OnDestroy()
        {
            if (root) Destroy(root.gameObject);
            if (mat) Destroy(mat);
            if (matDark) Destroy(matDark);
            if (capTex) Destroy(capTex);
            if (capNrm) Destroy(capNrm);
            if (mesh) Destroy(mesh);
            if (I == this) I = null;
        }

        void LateUpdate()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) return;
            if (!built) Build();
            if (Time.unscaledTime < nextSync) return;
            nextSync = Time.unscaledTime + 0.25f;
            if (GunPadsOnly) GunPads();
            int shown = 0, hidden = 0;
            foreach (var (fort, view, mounts) in forts)
            {
                bool on = view && view.activeInHierarchy && !fort.Destroyed;
                for (int i = 0; i < mounts.Length; i++)
                {
                    var m = mounts[i]; if (!m) continue;
                    bool want = on && (!GunPadsOnly || gunPads.Contains((view.transform, i)));
                    if (m.activeSelf != want) m.SetActive(want);
                    if (want) shown++; else hidden++;
                }
            }
            if (shown != PadsShown || hidden != PadsHidden)
            {
                PadsShown = shown; PadsHidden = hidden;
                Debug.Log($"[OpenZ] TowerMounts: {shown} pedestals under tower guns, {hidden} gun-less pads show the model's own black tops");
            }
        }

        void Build()
        {
            built = true;
            var baked = GameObject.Find(BakedRootName); if (baked) baked.SetActive(false);             // tower_gun_fix: the edit-mode copy steps aside
            root = new GameObject("OZ_TowerMounts").transform;
            mesh = MountMesh();
            mat = CapConcrete ? CapMaterial(FortTemplate()) : GunPadsOnly ? PedestalMaterial(FortTemplate()) : MountMaterial(mountColor, "OZ_TowerMount (runtime)");
            matDark = MountMaterial(darkColor, "OZ_TowerMountDark (runtime)");                        // howitzer_towers_v1
            int n = 0;
            foreach (var o in game.World.Objects)
            {
                if (!o.IsFort || !game.StaticViews.TryGetValue(o.Id, out var view) || !view) continue;
                var ft = view.transform; var mounts = new GameObject[4];
                for (int i = 0; i < 4; i++)
                {
                    var go = new GameObject($"TowerMount_{o.Type}_{o.Id}_{i}");
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(PadWorld(ft, i), Quaternion.Euler(0f, ft.eulerAngles.y, 0f));
                    if (GunPadsOnly) go.transform.localScale = new Vector3(PedestalScale, 1f, PedestalScale);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { mat, matDark };     // howitzer_towers_v1: steel + dark
                    mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
                    if (GunPadsOnly) go.SetActive(false);                                                  // shown by LateUpdate only under a living gun
                    mounts[i] = go; n++;
                }
                forts.Add((o, view, mounts));
            }
            Debug.Log($"[OpenZ] TowerMounts: {n} plinths on {forts.Count} forts (pad top {PadTopY} m, plinth top +{MountHeight} m, drum r {MountRadius} m, foot r {PlinthFootRadius} m {PlinthFootDepth} m down, diagonal rest heading {SnapDiagonal}) [howitzer_towers_v1]");
        }

        readonly HashSet<(Transform, int)> gunPads = new HashSet<(Transform, int)>();
        /// <summary></summary>
        void GunPads()
        {
            gunPads.Clear();
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed) continue;
                if (Resolve(game, o, out var ft, out int pad) == 1 && ft) gunPads.Add((ft, pad));
            }
        }

        /// <summary>
        /// </summary>
        Material PedestalMaterial(Material fortMat)
        {
            if (fortMat)
            {
                var t = new Material(fortMat) { name = "OZ_TowerPedestal (runtime, fort TeamLit)" };
                t.SetTexture("_BaseMap", Texture2D.whiteTexture); t.SetColor("_BaseColor", pedestalColor);
                if (t.HasProperty("_BumpMap")) t.SetTexture("_BumpMap", null);
                t.SetFloat("_TeamMode", 0f); t.SetFloat("_UseNormalMap", 0f); t.SetFloat("_UseMaskMap", 0f);
                t.SetColor("_EmissionMapColor", Color.black); t.SetColor("_EmissionColor", Color.black);
                t.SetFloat("_Metallic", 0.35f); t.SetFloat("_Smoothness", 0.35f);
                return t;
            }
            return MountMaterial(pedestalColor, "OZ_TowerPedestal (runtime)");
        }

        Material MountMaterial(Color col, string name)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (!sh) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>
        /// The fort's own TeamLit base material (m01_fort_v2_base_mat, night class 2 = building), or null.</summary>
        Material FortTemplate()
        {
            foreach (var o in game.World.Objects)
            {
                if (!o.IsFort || !game.StaticViews.TryGetValue(o.Id, out var view) || !view) continue;
                foreach (var r in view.GetComponentsInChildren<Renderer>(true))
                    foreach (var sm in r.sharedMaterials)
                        if (sm && sm.shader && sm.shader.name == "OpenZ/TeamLit" && sm.name.Contains("fort_v2_base")) return sm;
            }
            return null;
        }

        /// <summary>
        /// fort's TeamLit base material when found (no team mask, no normal / mask / emission maps), so the cap gets the SAME night
        /// grade as the fort walls (night class 2, fill, rim, env fix); URP Lit missed it and read as a flat light grey disc at night
        /// (worst on L17, a night map). Fallback: URP Lit as before.</summary>
        Material CapMaterial(Material fortMat = null)
        {
            if (fortMat)
            {
                var t = new Material(fortMat) { name = "OZ_TowerCap (runtime, fort TeamLit)" };
                capTex = CapTexture(capColor);
                t.SetTexture("_BaseMap", capTex); t.SetTextureScale("_BaseMap", Vector2.one); t.SetTextureOffset("_BaseMap", Vector2.zero);
                t.SetColor("_BaseColor", Color.white);
                capNrm = CapNormal();                                                                   // joints + grain catch the lamps / moon at night
                t.SetTexture("_BumpMap", capNrm); t.SetFloat("_BumpScale", 1f);
                t.SetFloat("_TeamMode", 0f); t.SetFloat("_UseNormalMap", 1f); t.SetFloat("_UseMaskMap", 0f);
                t.SetColor("_EmissionMapColor", Color.black); t.SetColor("_EmissionColor", Color.black);
                t.SetFloat("_Metallic", 0f); t.SetFloat("_Smoothness", capSmoothness);
                return t;
            }
            var m = MountMaterial(Color.white, "OZ_TowerCap (runtime)");
            capTex = CapTexture(capColor);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", capTex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", capTex);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", capSmoothness);
            return m;
        }

        /// <summary>
        /// +-6 %, mid +-4.5 %, fine +-3 %, faint low stains -5 %, pores -8 %, cast-slab joints 3 cm wide at -15 % (rings r 0.30 /
        /// 0.80, 4 diagonal joints r 0.30-1.10, 4 axis joints r 0.80-1.10) plus CapNormal grooves; slight edge wear. Still clean
        /// painted concrete: no cracks, no dirt. sRGB, mipmapped, clamped.</summary>
        public static Texture2D CapTexture(Color baseSrgb, int size = 512)
        {
            var px = new Color32[size * size];
            const float span = 2.96f;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = ((i + 0.5f) / size - 0.5f) * span, z = ((j + 0.5f) / size - 0.5f) * span, r = Mathf.Sqrt(x * x + z * z);
                    float g = 1f + 0.06f * VNoise(x, z, 0.55f, 11) + 0.045f * VNoise(x, z, 0.18f, 23) + 0.03f * VNoise(x, z, 0.06f, 37);
                    float st = VNoise(x, z, 0.35f, 71); if (st > 0.35f) g -= 0.05f * (st - 0.35f) / 0.65f;         // faint stains
                    float h = Hash01(i, j, 53); if (h > 0.980f) g -= 0.08f * (h - 0.980f) / 0.020f;          // sparse pores
                    g *= 1f - 0.15f * CapSeam(x, z, r);
                    float e = Mathf.Clamp01((r - 1.10f) / 0.15f); g *= 1f - 0.06f * e * e * (3f - 2f * e);   // edge wear
                    px[j * size + i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(baseSrgb.r * g * 255f), 0, 255),
                                                   (byte)Mathf.Clamp(Mathf.RoundToInt(baseSrgb.g * g * 255f), 0, 255),
                                                   (byte)Mathf.Clamp(Mathf.RoundToInt(baseSrgb.b * g * 255f), 0, 255), 0);   // alpha 0: never a team mask
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "OZ_TowerCapConcrete (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        /// <summary></summary>
        static float CapSeam(float x, float z, float r)
        {
            const float hw = 0.015f; float d45 = Mathf.Sqrt(0.5f);
            float seam = Mathf.Max(1f - Mathf.Clamp01(Mathf.Abs(r - 0.80f) / hw), 1f - Mathf.Clamp01(Mathf.Abs(r - 0.30f) / hw));
            if (r > 0.30f && r < 1.10f) seam = Mathf.Max(seam, 1f - Mathf.Clamp01(Mathf.Min(Mathf.Abs(x - z), Mathf.Abs(x + z)) * d45 / hw));
            if (r > 0.80f && r < 1.10f) seam = Mathf.Max(seam, 1f - Mathf.Clamp01(Mathf.Min(Mathf.Abs(x), Mathf.Abs(z)) / hw));
            return seam * seam * (3f - 2f * seam);
        }

        /// <summary>
        /// grain, flat beyond r 1.25 m (the drum sides share the planar UV). Packed R = 1, G = y, B = z, A = x (Unity's
        /// UnpackNormalmapRGorAG reads x from A * R, y from G).</summary>
        public static Texture2D CapNormal(int size = 512)
        {
            const float span = 2.96f; float texel = span / size;
            var hgt = new float[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = ((i + 0.5f) / size - 0.5f) * span, z = ((j + 0.5f) / size - 0.5f) * span, r = Mathf.Sqrt(x * x + z * z);
                    hgt[j * size + i] = r > 1.25f ? 0f : -0.004f * CapSeam(x, z, r) + 0.0006f * VNoise(x, z, 0.05f, 91);   // 4 mm grooves, 0.6 mm grain
                }
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    int i0 = Mathf.Max(i - 1, 0), i1 = Mathf.Min(i + 1, size - 1), j0 = Mathf.Max(j - 1, 0), j1 = Mathf.Min(j + 1, size - 1);
                    float dx = (hgt[j * size + i1] - hgt[j * size + i0]) / ((i1 - i0) * texel), dz = (hgt[j1 * size + i] - hgt[j0 * size + i]) / ((j1 - j0) * texel);
                    var nn = new Vector3(-dx, -dz, 1f).normalized;
                    px[j * size + i] = new Color32(255, (byte)Mathf.RoundToInt((nn.y * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nn.z * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nn.x * 0.5f + 0.5f) * 255f));
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "OZ_TowerCapNormal (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }
        static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
        /// <summary>Smooth value noise in -1..1 at `scale` metres.</summary>
        static float VNoise(float x, float z, float scale, int seed)
        {
            float qx = x / scale + 1000f, qz = z / scale + 1000f; int ix = Mathf.FloorToInt(qx), iz = Mathf.FloorToInt(qz);
            float fx = qx - ix, fz = qz - iz; fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            float a = Hash01(ix, iz, seed), b = Hash01(ix + 1, iz, seed), c = Hash01(ix, iz + 1, seed), d = Hash01(ix + 1, iz + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz) * 2f - 1f;
        }

        // ------------------------------------------------------------------ plinth mesh (howitzer_towers_v1)
        /// <summary>Plinth profile, pad-local (r, y from the pad top: the plate 0, the ledge -0.24), one lathe band per segment (crisp
        /// edges between bands, smooth around the ring). Group 0 = steel, 1 = dark. Same numbers as the package preview
        /// </summary>
        static readonly (Vector2[] prof, int grp)[] Plinth =
        {
            (new[] { new Vector2(1.48f, -0.34f), new Vector2(1.48f, -0.19f), new Vector2(1.45f, -0.165f), new Vector2(1.42f, -0.165f) }, 1),   // foot flange, 10 cm into the ledge
            (new[] { new Vector2(1.42f, -0.165f), new Vector2(1.42f, -0.045f) }, 0),                                                     // drum
            (new[] { new Vector2(1.42f, -0.045f), new Vector2(1.40f, -0.035f), new Vector2(1.40f, -0.005f), new Vector2(1.42f, 0.005f) }, 1), // groove band
            (new[] { new Vector2(1.42f, 0.005f), new Vector2(1.42f, 0.06f), new Vector2(1.38f, 0.10f), new Vector2(1.30f, 0.10f) }, 0),      // drum top + chamfer
            (new[] { new Vector2(1.30f, 0.10f), new Vector2(1.25f, 0.10f) }, 1),                                                         // seam ring on the top
            (new[] { new Vector2(1.25f, 0.10f), new Vector2(0f, 0.10f) }, 0),                                                            // top the gun stands on
        };
        public const int BoltCount = 16;
        const float BoltY = -0.105f, BoltR = 0.045f, BoltL = 0.032f;

        /// <summary>The plinth as plain arrays (pad-local, Unity winding: cross(B-A, C-A) = the front normal). tris[0] steel, tris[1] dark.</summary>
        public static void PlinthArrays(int seg, List<Vector3> v, List<Vector3> n, List<int>[] tris)
        {
            void Tri(int grp, int a, int b, int c, Vector3 want)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) < 0f) { int x = b; b = c; c = x; }
                tris[grp].Add(a); tris[grp].Add(b); tris[grp].Add(c);
            }
            foreach (var (prof, grp) in Plinth)
            {
                for (int s = 0; s + 1 < prof.Length; s++)
                {
                    Vector2 a = prof[s], c = prof[s + 1];
                    var d = c - a; if (d.sqrMagnitude < 1e-10f) continue;
                    // the profile runs up the outside and then in along the top with the solid on its left: the right-hand
                    // perpendicular is the outward normal (drum out, top up, groove lips up-out / down-out)
                    var pn = new Vector2(d.y, -d.x).normalized;
                    int start = v.Count;
                    for (int i = 0; i <= seg; i++)
                    {
                        float ang = i * Mathf.PI * 2f / seg, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                        var nn = new Vector3(ca * pn.x, pn.y, sa * pn.x).normalized;
                        v.Add(new Vector3(ca * a.x, a.y, sa * a.x)); n.Add(nn);
                        v.Add(new Vector3(ca * c.x, c.y, sa * c.x)); n.Add(nn);
                    }
                    for (int i = 0; i < seg; i++)
                    {
                        int q = start + i * 2;
                        float am = (i + 0.5f) * Mathf.PI * 2f / seg;
                        var want = new Vector3(Mathf.Cos(am) * pn.x, pn.y, Mathf.Sin(am) * pn.x);
                        if (a.x > 1e-5f) Tri(grp, q, q + 1, q + 2, want);
                        if (c.x > 1e-5f) Tri(grp, q + 2, q + 1, q + 3, want);
                    }
                }
            }
            for (int k = 0; k < BoltCount; k++)
            {   // hex bolt heads on the drum, pointing out
                float a = (k + 0.5f) * Mathf.PI * 2f / BoltCount;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); var t = new Vector3(-d.z, 0f, d.x);
                var c0 = d * (MountRadius - 0.005f) + Vector3.up * BoltY;
                for (int i = 0; i < 6; i++)
                {
                    float b = i * Mathf.PI / 3f, b1 = (i + 1) * Mathf.PI / 3f;
                    Vector3 o0 = t * (Mathf.Cos(b) * BoltR) + Vector3.up * (Mathf.Sin(b) * BoltR), o1 = t * (Mathf.Cos(b1) * BoltR) + Vector3.up * (Mathf.Sin(b1) * BoltR);
                    var fn = (t * Mathf.Cos((b + b1) * 0.5f) + Vector3.up * Mathf.Sin((b + b1) * 0.5f)).normalized;
                    int q = v.Count;
                    v.Add(c0 + o0); v.Add(c0 + o1); v.Add(c0 + o1 + d * BoltL); v.Add(c0 + o0 + d * BoltL);
                    n.Add(fn); n.Add(fn); n.Add(fn); n.Add(fn);
                    Tri(1, q, q + 1, q + 2, fn); Tri(1, q, q + 2, q + 3, fn);
                }
                int cap = v.Count;
                v.Add(c0 + d * BoltL); n.Add(d);
                for (int i = 0; i < 6; i++) { float b = i * Mathf.PI / 3f; v.Add(c0 + d * BoltL + t * (Mathf.Cos(b) * BoltR) + Vector3.up * (Mathf.Sin(b) * BoltR)); n.Add(d); }
                for (int i = 0; i < 6; i++) Tri(1, cap, cap + 1 + i, cap + 1 + (i + 1) % 6, d);
            }
        }

        /// <summary>howitzer_towers_v1: the turret-ring plinth (see the class notes). Two submeshes: 0 steel, 1 dark.</summary>
        public static Mesh MountMesh(int seg = 48)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tris = new[] { new List<int>(), new List<int>() };
            PlinthArrays(seg, v, n, tris);
            var m = new Mesh { name = "OZ_TowerMount_v2" };
            m.SetVertices(v); m.SetNormals(n);
            var uv = new List<Vector2>(v.Count);
            foreach (var p in v) uv.Add(new Vector2(p.x / 2.96f + 0.5f, p.z / 2.96f + 0.5f));
            m.SetUVs(0, uv);
            var tg = new List<Vector4>(n.Count);
            foreach (var nv in n)
            {
                var ax = Mathf.Abs(nv.x) > 0.99f ? Vector3.forward : Vector3.right;
                var tt = (ax - nv * Vector3.Dot(nv, ax)).normalized;
                tg.Add(new Vector4(tt.x, tt.y, tt.z, Vector3.Dot(Vector3.Cross(nv, tt), ax == Vector3.right ? Vector3.forward : Vector3.right) >= 0f ? 1f : -1f));
            }
            m.SetTangents(tg);
            m.subMeshCount = 2; m.SetTriangles(tris[0], 0); m.SetTriangles(tris[1], 1);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>crews_v1: top surface of a readable mesh (the split cannon base plates), for the standing gunner's feet: the
    /// highest triangle at or below maxY under a local (x, z) point. Triangles are bucketed on a coarse xz grid once per mesh.</summary>
    public sealed class MeshTopSampler
    {
        static readonly Dictionary<Mesh, MeshTopSampler> cache = new Dictionary<Mesh, MeshTopSampler>();
        readonly Vector3[] v; readonly int[] t; readonly float cell, x0, z0; readonly int nx, nz; readonly List<int>[] cells;

        public static MeshTopSampler For(Mesh m)
        {
            if (!m) return null;
            if (cache.TryGetValue(m, out var s)) return s;
            s = m.isReadable ? new MeshTopSampler(m) : null;
            cache[m] = s;
            return s;
        }

        MeshTopSampler(Mesh m)
        {
            v = m.vertices; t = m.triangles; var b = m.bounds;
            cell = Mathf.Max(0.08f, Mathf.Max(b.size.x, b.size.z) / 24f);
            x0 = b.min.x; z0 = b.min.z;
            nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / cell) + 1); nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / cell) + 1);
            cells = new List<int>[nx * nz];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], c = v[t[i + 1]], d = v[t[i + 2]];
                int ix0 = Cx(Mathf.Min(a.x, Mathf.Min(c.x, d.x))), ix1 = Cx(Mathf.Max(a.x, Mathf.Max(c.x, d.x)));
                int iz0 = Cz(Mathf.Min(a.z, Mathf.Min(c.z, d.z))), iz1 = Cz(Mathf.Max(a.z, Mathf.Max(c.z, d.z)));
                for (int z = iz0; z <= iz1; z++)
                    for (int x = ix0; x <= ix1; x++)
                        (cells[z * nx + x] ??= new List<int>()).Add(i);
            }
        }
        int Cx(float x) => Mathf.Clamp(Mathf.FloorToInt((x - x0) / cell), 0, nx - 1);
        int Cz(float z) => Mathf.Clamp(Mathf.FloorToInt((z - z0) / cell), 0, nz - 1);

        public bool Top(float x, float z, float maxY, out float y)
        {
            y = float.NegativeInfinity;
            int ix = Mathf.FloorToInt((x - x0) / cell), iz = Mathf.FloorToInt((z - z0) / cell);
            if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return false;
            var list = cells[iz * nx + ix]; if (list == null) return false;
            foreach (int i in list)
            {
                Vector3 A = v[t[i]], B = v[t[i + 1]], C = v[t[i + 2]];
                float det = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                if (Mathf.Abs(det) < 1e-9f) continue;                                                      // vertical face
                float l1 = ((B.z - C.z) * (x - C.x) + (C.x - B.x) * (z - C.z)) / det;
                float l2 = ((C.z - A.z) * (x - C.x) + (A.x - C.x) * (z - C.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                float h = l1 * A.y + l2 * B.y + l3 * C.y;
                if (h <= maxY && h > y) y = h;
            }
            return !float.IsNegativeInfinity(y);
        }
    }
}

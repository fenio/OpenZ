using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// fort_defender_v1, the view of deviation DV41 (Sim/ZObject.FortDefender.cs): the four round roof hatches of every fort, one in
    /// the centre of each flower-shaped sandbag nest of the fort_v2 roof (fort-local ZWorld.FortDefenderRingLocal, measured bag by bag
    /// on the live FortBase mesh: free deck 0.50 m round each centre, deck 6.29-6.36 m), their lids and lift plates, the sounds, the
    /// defender's pop-up line, and the fort's roof as a physics surface for his ragdoll. View only: it reads the fort's
    /// FortDefenderState / PhaseAt / Ring / Man and never writes sim state.
    ///
    /// Timeline (sim seconds from the phase start, so it keeps pace with the sim at any game speed):
    ///  * Rising (ZWorld.FortDefenderPop = 1.2): the lid swings open (0.35 s, veh_lid_open), he climbs up out of the well on the
    ///    rising lift plate from 0.30 s (climb_ladder clip, brd_ladder), feet on the plate at the collar top at 1.2 s;
    ///  * Up: he stands in the ring centre, the lid open behind him on the fort-centre side (the nest's opening), the man is an
    ///    ordinary UnitView grunt: UnitAim turns him onto his targets, WeaponsV2 draws the grunt bolt, recoil, voice, deaths;
    ///  * Lowering (ZWorld.FortDefenderLower = 1.0): he sinks down the well by 0.70 s (brd_ladder), the lid closes (veh_lid_close);
    ///  * killed: a ragdoll thrown off by the hit (UnitView.Die: never the pop / knockdown on the roof); the lid closes 0.8 s later.
    /// Heights: collar 6.25..6.43 (7 cm over the highest deck point in the nest, never co-planar), lid 5 cm, well 3.2 m deep (inside
    /// the fort's hull); the man stands at full ground-grunt size (ManScale 1): his head at ~8.8 m stays under the fort gun's barrel
    /// (>= 9.30 m over the rings at its idle and lob elevations, tools/fd_gun.py) and the tower guns (10 m+).
    /// Roof collider: a MeshCollider of FortBase's own mesh on layer 2 (Ignore Raycast: invisible to every default / walk-layer ray
    /// and to GroundY) so ragdolls and debris land on the real roof, walls and bags; switched off the frame the fort is destroyed
    /// (before CombatView breaks it). RoofPoint / SurfaceY lift shells and corpses onto it.
    /// </summary>
    [DefaultExecutionOrder(56)]   // after ZGame's sim tick (-50), before UnitView (60) places the man and CombatView (50) is irrelevant here
    public class FortDefenderView : MonoBehaviour
    {
        public static FortDefenderView I { get; private set; }
        public static bool AutoAttach = true;
        /// <summary>The man's drawn size (1 = the ground grunts' size).</summary>
        public static float ManScale = 1f;
        /// <summary>Seconds between two pop-up lines (the player's own defender, on screen).</summary>
        public static float VoiceGap = 20f;

        // ------------------------------------------------------------------ hatch geometry (fort-local metres)
        public const float CollarBottom = 6.25f, CollarTop = 6.43f, CollarROut = 0.40f, CollarRIn = 0.33f, WellDepth = 3.2f;
        public const float LidR = 0.365f, LidT = 0.05f, LidOpenDeg = 100f, LidSwing = 0.35f;
        /// <summary>Where the man's soles are when he is up: the lift plate, 1.2 cm under the collar top (never co-planar).</summary>
        public const float PlateTop = CollarTop - 0.012f;
        /// <summary>
        /// between them, 5.95-6.25 m, are spanned by a boot).</summary>
        public const float DeckTop = 6.37f;
        /// <summary>Rising timeline: the climb starts at ClimbFrom s and ends with the phase (ZWorld.FortDefenderPop).</summary>
        public const float ClimbFrom = 0.30f, SinkUntil = 0.70f, LidCloseAt = 0.70f, DeadLidDelay = 0.8f;
        /// <summary>Fort-local point the lids hinge toward (the gun's yaw axis; the nests open toward it).</summary>
        static readonly Vector2 FortCentre = new Vector2(0f, -0.61f);

        class Hatch { public Transform hinge, plate; public Vector3 centreW, hingeW; public float angle, want; public Quaternion hingeRest; }
        class Rig
        {
            public ZObject fort; public Transform view; public GameObject root; public Hatch[] hatch = new Hatch[4];
            public MeshCollider roof; public MeshFilter baseMf; public float nextMeshCheck; public FortDefenderPhase seenPhase; public double seenAt = -1; public int seenRing = -1;
            public ZObject lastMan; public float deadAt = -1f; public bool ladderPlayed, closePlayed;
        }
        readonly List<Rig> rigs = new List<Rig>();
        ZGame game; bool built; Material matSteel, matLid, matWell, matDark; Mesh collarMesh, lidMesh, plateMesh;
        float lastVoice = -100f;
        /// <summary>Checks: pop-ups seen, lines said, lid sounds, ragdoll-surface colliders built.</summary>
        public static int PopsSeen, LinesSaid, LidSounds, RoofColliders;

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var g = ZGame.I ? ZGame.I : Object.FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<FortDefenderView>()) g.gameObject.AddComponent<FortDefenderView>();
        }

        void Awake() { I = this; game = GetComponent<ZGame>(); if (!game) game = ZGame.I; }
        void OnDestroy()
        {
            foreach (var r in rigs) if (r.root) Destroy(r.root);
            foreach (var m in new Object[] { matSteel, matLid, matWell, matDark, collarMesh, lidMesh, plateMesh }) if (m) Destroy(m);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ static API (UnitView, CombatView, PlayerInput)
        /// <summary>Ring `ring`'s centre on the deck of this fort view (world), and its outward heading (deg, from the fort centre).</summary>
        public static Vector3 RingWorld(Transform fortView, int ring, float y, out float outwardYaw)
        {
            float lx = (float)ZWorld.FortDefenderRingLocal[ring * 2], lz = (float)ZWorld.FortDefenderRingLocal[ring * 2 + 1];
            var c = fortView.TransformPoint(new Vector3(lx, y, lz));
            var o = fortView.TransformDirection(new Vector3(lx - FortCentre.x, 0f, lz - FortCentre.y)); o.y = 0f;
            outwardYaw = o.sqrMagnitude > 1e-6f ? Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg : fortView.eulerAngles.y;
            return c;
        }

        /// <summary>Where the man stands this frame: feet on the lift plate of his ring (world), his idle heading (outward over the
        /// bags), how far he is down the well (0 up .. 1 hidden) and whether he is climbing. False = not a defender / no fort view.</summary>
        public static bool Pose(ZGame g, ZObject man, out Vector3 feet, out float outwardYaw, out float sink, out bool climbing)
        {
            feet = default; outwardYaw = 0f; sink = 0f; climbing = false;
            if (!g || g.World == null || man == null || !man.IsFortDefender) return false;
            var fort = g.World.GetAny(man.DefenderFortId);
            if (fort == null || !g.StaticViews.TryGetValue(fort.Id, out var fv) || !fv) return false;
            int ring = fort.FortDefenderMan == man ? fort.FortDefenderRing : RingNearest(fv.transform, g, man);
            if (ring < 0) return false;
            feet = RingWorld(fv.transform, ring, PlateTop, out outwardYaw);
            if (fort.FortDefenderMan != man) return true;                                                    // his fort no longer lists him: stand
            if (ZWorld.FortDefenderWalkDeviation && fort.FortDefenderState == FortDefenderPhase.Up)
            {
                // the lift plate while he is in a nest's centre
                float a = Mathf.Clamp01(g.TickAlpha);
                float lx = Mathf.Lerp((float)man.FortDefenderPrevLocalX, (float)man.FortDefenderLocalX, a), lz = Mathf.Lerp((float)man.FortDefenderPrevLocalZ, (float)man.FortDefenderLocalZ, a);
                float dr = float.MaxValue;
                for (int i = 0; i < ZWorld.FortDefenderRings; i++) dr = Mathf.Min(dr, new Vector2(lx - (float)ZWorld.FortDefenderRingLocal[i * 2], lz - (float)ZWorld.FortDefenderRingLocal[i * 2 + 1]).magnitude);
                feet = fv.transform.TransformPoint(new Vector3(lx, Mathf.Lerp(PlateTop, DeckTop, Mathf.InverseLerp(0.3f, 0.55f, dr)), lz));
                var od = fv.transform.TransformDirection(new Vector3(lx - FortCentre.x, 0f, lz - FortCentre.y)); od.y = 0f;
                if (od.sqrMagnitude > 1e-6f) outwardYaw = Mathf.Atan2(od.x, od.z) * Mathf.Rad2Deg;
                return true;
            }
            float t = (float)(g.World.Time + g.TickAlpha * ZConst.TICK - fort.FortDefenderPhaseAt);
            switch (fort.FortDefenderState)
            {
                case FortDefenderPhase.Rising:
                {
                    float k = Mathf.Clamp01((t - ClimbFrom) / Mathf.Max(0.05f, (float)ZWorld.FortDefenderPop - ClimbFrom));
                    sink = 1f - Mathf.SmoothStep(0f, 1f, k); climbing = k < 1f; break;
                }
                case FortDefenderPhase.Lowering:
                {
                    float k = Mathf.Clamp01((t - 0.05f) / (SinkUntil - 0.05f));
                    sink = Mathf.SmoothStep(0f, 1f, k); climbing = k > 0f && k < 1f; break;
                }
            }
            return true;
        }

        /// <summary>The man's sink distance (m): his drawn height + 0.3 m, so at sink 1 his head is below the deck.</summary>
        public static float SinkDepth(Transform man) => 2.41f * Mathf.Max(0.3f, Mathf.Abs(man ? man.lossyScale.y : 1f)) + 0.3f;

        static int RingNearest(Transform fv, ZGame g, ZObject man)
        {
            var p = g.CenterWorld(man, false); int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < ZWorld.FortDefenderRings; i++)
            {
                var c = RingWorld(fv, i, 0f, out _); c.y = 0f; float d = (c - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>A point lifted onto a fort roof (the highest roof surface under it, + 2 cm) when it lies over one; else unchanged.
        /// CombatView: shells / grenades aimed at a defender burst on the roof where he stands, not on the terrain under the fort.</summary>
        public static Vector3 RoofPoint(Vector3 p)
        {
            if (!I) return p;
            var ray = new Ray(new Vector3(p.x, 60f, p.z), Vector3.down);
            float best = float.NegativeInfinity;
            foreach (var r in I.rigs) if (r.roof && r.roof.enabled && r.roof.Raycast(ray, out var hit, 120f)) best = Mathf.Max(best, hit.point.y);
            if (best > p.y) p.y = best + 0.02f;
            return p;
        }

        /// <summary>The surface under p: a fort roof / wall top (the roof collider, searched from 2.5 m above p) when it is higher than
        /// groundY, else groundY. UnitView: a corpse on the roof pops out there, not 6 m below.</summary>
        public static float SurfaceY(Vector3 p, float groundY)
        {
            if (!I) return groundY;
            var ray = new Ray(p + Vector3.up * 2.5f, Vector3.down);
            float best = groundY;
            foreach (var r in I.rigs) if (r.roof && r.roof.enabled && r.roof.Raycast(ray, out var hit, 40f)) best = Mathf.Max(best, hit.point.y);
            return best;
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            built = true;
            if (!ZWorld.FortDefenderDeviation) return;                                                       // DV41 off: the roof stays as it was
            MakeMaterials(); collarMesh = CollarMesh(); lidMesh = LidMesh(); plateMesh = PlateMesh();
            int debris = BuildingBreaker.DebrisLayer;
            Physics.IgnoreLayerCollision(2, debris, false);                                                  // ragdolls / debris (debris layer) land on the roof
            foreach (var o in game.World.Objects)
            {
                if (!o.IsFort || !game.StaticViews.TryGetValue(o.Id, out var fvGo) || !fvGo) continue;
                var fv = fvGo.transform; var rig = new Rig { fort = o, view = fv };
                rig.root = new GameObject($"OZ_FortDefender_{o.Type}_{o.Id}");
                for (int i = 0; i < ZWorld.FortDefenderRings; i++) rig.hatch[i] = BuildHatch(rig.root.transform, fv, i);
                rig.roof = RoofCollider(rig.root.transform, fv, out rig.baseMf);
                rigs.Add(rig);
                // check: the sim's ring centre (px) against the drawn one (the fort model's placement)
                o.FortDefenderRingCentre(0, out int sx, out int sy);
                var simW = ZGame.PxToWorld(sx, sy); var drawn = RingWorld(fv, 0, 0f, out _); simW.y = drawn.y = 0f;
                Debug.Log($"[OpenZ] FortDefenderView: {o.Owner} {o.Type} 4 roof hatches, roof collider {(rig.roof ? "on" : "none")}, sim vs drawn ring 0 {(simW - drawn).magnitude:0.00} m");
            }
        }

        void MakeMaterials()
        {
            var tm = TowerMounts.I;                                                                           // the fort's own tower-mount palette
            Color steel = tm ? tm.mountColor : new Color(0.52f, 0.53f, 0.55f), dark = tm ? tm.darkColor : new Color(0.21f, 0.22f, 0.24f);
            matSteel = Mat(steel, 0.4f, 0.45f, "OZ_FortHatchSteel (runtime)");
            matLid = Mat(new Color(0.60f, 0.59f, 0.56f), 0.3f, 0.38f, "OZ_FortHatchLid (runtime)");
            matWell = Mat(new Color(0.035f, 0.037f, 0.042f), 0f, 0.08f, "OZ_FortHatchWell (runtime)");
            matDark = Mat(dark, 0.4f, 0.4f, "OZ_FortHatchDark (runtime)");
        }

        static Material Mat(Color c, float metallic, float smooth, string name)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (!sh) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        Hatch BuildHatch(Transform root, Transform fv, int ring)
        {
            var h = new Hatch();
            var centre = RingWorld(fv, ring, CollarBottom, out _);
            float lx = (float)ZWorld.FortDefenderRingLocal[ring * 2], lz = (float)ZWorld.FortDefenderRingLocal[ring * 2 + 1];
            var inL = new Vector3(FortCentre.x - lx, 0f, FortCentre.y - lz).normalized;                      // toward the gun: the nest's opening
            var inW = fv.TransformDirection(inL); inW.y = 0f; inW.Normalize();
            var frame = new GameObject($"Hatch_{ring}").transform;
            frame.SetParent(root, false);
            frame.SetPositionAndRotation(centre, Quaternion.LookRotation(inW, Vector3.up));                  // +z toward the hinge
            Part(frame, "Collar", collarMesh, matSteel, matWell, matDark);
            // lift plate (moves with the man's feet; parked at the well floor)
            h.plate = Part(frame, "LiftPlate", plateMesh, matDark).transform;
            h.plate.localPosition = new Vector3(0f, -WellDepth + 0.06f, 0f);
            // lid on a hinge just past the collar rim on the opening side, at mid lid thickness over the collar top
            var hinge = new GameObject("LidHinge").transform;
            hinge.SetParent(frame, false);
            hinge.localPosition = new Vector3(0f, CollarTop - CollarBottom + 0.004f + LidT * 0.5f, CollarROut + 0.02f);
            hinge.localRotation = Quaternion.identity; h.hingeRest = hinge.localRotation;
            Part(hinge, "Lid", lidMesh, matLid, matDark);
            h.hinge = hinge; h.centreW = RingWorld(fv, ring, CollarTop, out _); h.hingeW = hinge.position;
            return h;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, params Material[] m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
            return go;
        }

        MeshCollider RoofCollider(Transform root, Transform fv, out MeshFilter fb)
        {
            fb = null;
            foreach (var mf in fv.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == "FortBase" && mf.sharedMesh) { fb = mf; break; }
            if (!fb || !fb.sharedMesh.isReadable) return null;
            var go = new GameObject("RoofCollider") { layer = 2 };                                              // Ignore Raycast: no default ray, no GroundY
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(fb.transform.position, fb.transform.rotation);
            go.transform.localScale = fb.transform.lossyScale;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = fb.sharedMesh;
            RoofColliders++;
            return mc;
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) return;
            if (!built) Build();
            if (rigs.Count == 0) return;
            float dt = Time.deltaTime * Mathf.Max(0.05f, game.gameSpeed);
            foreach (var r in rigs)
            {
                bool alive = !r.fort.Destroyed && r.view && r.view.gameObject.activeInHierarchy;
                if (r.root.activeSelf != alive) r.root.SetActive(alive);
                if (r.roof && r.roof.enabled != alive) r.roof.enabled = alive;                              // off before CombatView breaks the fort
                if (!alive) continue;
                if (r.roof && r.baseMf && Time.unscaledTime >= r.nextMeshCheck)
                {   // FlagView swaps FortBase for a copy without the baked flag: the collider follows (no invisible flag to bump into)
                    r.nextMeshCheck = Time.unscaledTime + 2f;
                    var cur = r.baseMf.sharedMesh;
                    if (cur && cur != r.roof.sharedMesh && cur.isReadable) r.roof.sharedMesh = cur;
                }
                Step(r, dt);
            }
        }

        void Step(Rig r, float dt)
        {
            var f = r.fort; var man = f.FortDefenderMan; var ph = f.FortDefenderState; int ring = f.FortDefenderRing;
            double tNow = game.World.Time; float t = (float)(tNow - f.FortDefenderPhaseAt);
            bool fresh = ph != r.seenPhase || f.FortDefenderPhaseAt != r.seenAt;
            if (fresh)
            {
                if (ph == FortDefenderPhase.Rising && ring >= 0) OnRise(r, ring, man);
                if (ph == FortDefenderPhase.Lowering && ring >= 0) { r.ladderPlayed = false; r.closePlayed = false; }
                if (ph == FortDefenderPhase.Down && r.seenPhase != FortDefenderPhase.Lowering && r.lastMan != null && r.lastMan.Destroyed) { r.deadAt = Time.time; r.closePlayed = false; }
                r.seenPhase = ph; r.seenAt = f.FortDefenderPhaseAt;
            }
            if (man != null)
            {
                if (r.seenRing >= 0 && ring >= 0 && ring != r.seenRing) Sfx("veh_lid_open", r.hatch[ring].centreW, r);
                r.lastMan = man; r.seenRing = ring;
            }
            for (int i = 0; i < ZWorld.FortDefenderRings; i++)
            {
                var h = r.hatch[i]; if (h == null) continue;
                bool mine = i == r.seenRing;
                bool open = mine && (ph == FortDefenderPhase.Rising || ph == FortDefenderPhase.Up || (ph == FortDefenderPhase.Lowering && t < LidCloseAt)
                                     || (ph == FortDefenderPhase.Down && r.deadAt >= 0f && Time.time - r.deadAt < DeadLidDelay));
                h.want = open ? LidOpenDeg : 0f;
                float was = h.angle;
                h.angle = Mathf.MoveTowards(h.angle, h.want, LidOpenDeg / LidSwing * dt);
                if (was > 0f && h.angle <= 0f && !r.closePlayed) { r.closePlayed = true; Sfx("veh_lid_close", h.centreW, r); }   // the clang when it seats
                h.hinge.localRotation = h.hingeRest * Quaternion.AngleAxis(h.angle, Vector3.right);           // + about x: the free edge (at -z) rises
                // lift plate: under the man's soles while he is on it, else parked on the well floor
                float plateY = -WellDepth + 0.06f;
                if (mine && man != null && Pose(game, man, out _, out _, out float sink, out _))
                    plateY = (PlateTop - CollarBottom) - SinkDepth(game.Views.TryGetValue(man.Id, out var mv) && mv ? mv.transform : null) * sink;
                h.plate.localPosition = new Vector3(0f, Mathf.Max(-WellDepth + 0.06f, plateY), 0f);
            }
            // ladder sounds on the climb up / down
            if (man != null && ring >= 0 && !r.ladderPlayed && ((ph == FortDefenderPhase.Rising && t >= ClimbFrom) || (ph == FortDefenderPhase.Lowering && t >= 0.05f)))
            { r.ladderPlayed = true; Sfx("brd_ladder", r.hatch[ring].centreW, r); if (ph == FortDefenderPhase.Lowering) Sfx("bld_fort_lift", r.hatch[ring].centreW, r); }   // sound_v3
        }

        void OnRise(Rig r, int ring, ZObject man)
        {
            PopsSeen++;
            r.ladderPlayed = false; r.closePlayed = false; r.deadAt = -1f; r.seenRing = ring;
            var p = r.hatch[ring].centreW;
            Sfx("veh_lid_open", p, r);
            Sfx("bld_fort_lift", p, r);                                                                  // sound_v3: the lift plate
            // his own line: the player's defender, on screen, not too often (Alan, existing lines only)
            if (man == null || man.Owner != game.player || Time.unscaledTime - lastVoice < VoiceGap || !OnScreen(p)) return;
            lastVoice = Time.unscaledTime; LinesSaid++;
            if (Random.value < 0.5f) AudioDirector.Say("vo_taunt");                                         // "Come on, tin cans!", "Is that all you've got?" ...
            else AudioDirector.Say("vo_order_attack", Random.value < 0.5f ? "=Open fire!" : Random.value < 0.5f ? "=Light 'em up!" : "=Eat this!");
        }

        void Sfx(string id, Vector3 p, Rig r) { LidSounds++; AudioDirector.Fire(id, p, -1000 - r.fort.Id); }

        static bool OnScreen(Vector3 p)
        {
            var cam = Camera.main; if (!cam) return false;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
        }

        // ------------------------------------------------------------------ meshes (hatch frame: y up from the collar bottom, +z = hinge side)
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<int>[] sub;
            public MB(int subs) { sub = new List<int>[subs]; for (int i = 0; i < subs; i++) sub[i] = new List<int>(); }
            int V(Vector3 p, Vector3 nn) { v.Add(p); n.Add(nn); return v.Count - 1; }
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                var want = na + nb + nc + nd;
                int ia = V(a, na), ib = V(b, nb), ic = V(c, nc), id = V(d, nd);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) >= 0f) sub[s].AddRange(new[] { ia, ib, ic, ia, ic, id });
                else sub[s].AddRange(new[] { ia, ic, ib, ia, id, ic });
            }
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn) => Quad(s, a, b, c, d, nn, nn, nn, nn);
            public void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                int ia = V(a, na), ib = V(b, nb), ic = V(c, nc);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) >= 0f) sub[s].AddRange(new[] { ia, ib, ic });
                else sub[s].AddRange(new[] { ia, ic, ib });
            }
            public void Box(int s, Vector3 c, Vector3 h)
            {
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        var nn = Vector3.zero; nn[ax] = sg;
                        int u = (ax + 1) % 3, w = (ax + 2) % 3;
                        Vector3 P(float a, float b) { var p = Vector3.zero; p[ax] = sg * h[ax]; p[u] = a * h[u]; p[w] = b * h[w]; return c + p; }
                        Quad(s, P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), nn);
                    }
            }
            public void CylinderX(int s, Vector3 c, float r, float halfLen, int seg)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(0, Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1));
                    Quad(s, c + d0 * r - Vector3.right * halfLen, c + d0 * r + Vector3.right * halfLen, c + d1 * r + Vector3.right * halfLen, c + d1 * r - Vector3.right * halfLen, d0, d0, d1, d1);
                    Tri(s, c + Vector3.right * halfLen, c + d0 * r + Vector3.right * halfLen, c + d1 * r + Vector3.right * halfLen, Vector3.right, Vector3.right, Vector3.right);
                    Tri(s, c - Vector3.right * halfLen, c + d1 * r - Vector3.right * halfLen, c + d0 * r - Vector3.right * halfLen, Vector3.left, Vector3.left, Vector3.left);
                }
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.subMeshCount = sub.Length;
                for (int i = 0; i < sub.Length; i++) m.SetTriangles(sub[i], i);
                m.RecalculateBounds();
                return m;
            }
        }

        const int Seg = 40;
        static Vector3 Dir(int i) { float a = i * Mathf.PI * 2f / Seg; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

        /// <summary>Collar (steel: outer wall from 11 cm under the deck, chamfer, flat top ring; 16 bolt heads), the well (near-black
        /// inward wall 3.2 m down to a floor, inside the fort's hull), a hinge bracket on the hinge side (dark).</summary>
        static Mesh CollarMesh()
        {
            var b = new MB(3);
            float ro = CollarROut, ri = CollarRIn, y1 = CollarTop - CollarBottom, ch = 0.025f, fl = -WellDepth;
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                var nc0 = (d0 + up).normalized; var nc1 = (d1 + up).normalized;
                b.Quad(0, d0 * ro, d1 * ro, d1 * ro + up * (y1 - ch), d0 * ro + up * (y1 - ch), d0, d1, d1, d0);                                // outer wall
                b.Quad(0, d0 * ro + up * (y1 - ch), d1 * ro + up * (y1 - ch), d1 * (ro - ch) + up * y1, d0 * (ro - ch) + up * y1, nc0, nc1, nc1, nc0);   // chamfer
                b.Quad(0, d0 * (ro - ch) + up * y1, d1 * (ro - ch) + up * y1, d1 * ri + up * y1, d0 * ri + up * y1, up);                            // top ring
                b.Quad(1, d0 * ri + up * y1, d1 * ri + up * y1, d1 * ri + up * fl, d0 * ri + up * fl, -d0, -d1, -d1, -d0);                         // well wall
                b.Tri(1, up * fl, d0 * ri + up * fl, d1 * ri + up * fl, up, up, up);                                                                // well floor
            }
            for (int k = 0; k < 16; k++)
            {   // bolt heads on the top ring (2 mm proud)
                float a = (k + 0.5f) * Mathf.PI * 2f / 16f; var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                b.Box(2, d * ((ro + ri) * 0.5f - 0.004f) + Vector3.up * (y1 + 0.006f), new Vector3(0.012f, 0.006f, 0.012f));
            }
            b.Box(2, new Vector3(0f, y1 - 0.035f, ro + 0.012f), new Vector3(0.15f, 0.03f, 0.03f));                                          // hinge bracket
            return b.Mesh("OZ_FortHatch_Collar");
        }

        /// <summary>Lid in hinge space (axis = x through the origin, the closed lid lies toward -z): a slightly domed steel disc with
        /// a chamfered rim and a bottom face, a knuckle on the axis, a strap and a grab bar (dark).</summary>
        static Mesh LidMesh()
        {
            var b = new MB(2);
            float R = LidR, T = LidT, ch = 0.016f, dome = 0.02f;
            var c = new Vector3(0f, 0f, -(CollarROut + 0.02f));
            float yb = -T * 0.5f, yt = T * 0.5f;
            var top = c + Vector3.up * (yt + dome);
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                var nd0 = (up + d0 * (dome / (R - ch))).normalized; var nd1 = (up + d1 * (dome / (R - ch))).normalized;
                b.Tri(0, top, c + d0 * (R - ch) + up * yt, c + d1 * (R - ch) + up * yt, up, nd0, nd1);
                var nc0 = (d0 + up).normalized; var nc1 = (d1 + up).normalized;
                b.Quad(0, c + d0 * (R - ch) + up * yt, c + d1 * (R - ch) + up * yt, c + d1 * R + up * (yt - ch), c + d0 * R + up * (yt - ch), nc0, nc1, nc1, nc0);
                b.Quad(0, c + d0 * R + up * (yt - ch), c + d1 * R + up * (yt - ch), c + d1 * R + up * yb, c + d0 * R + up * yb, d0, d1, d1, d0);
                b.Tri(0, c + up * yb, c + d1 * R + up * yb, c + d0 * R + up * yb, -up, -up, -up);
            }
            b.CylinderX(1, Vector3.zero, 0.032f, 0.12f, 10);                                                                      // knuckle
            b.Box(1, new Vector3(0f, 0f, -0.035f), new Vector3(0.11f, 0.012f, 0.035f));                                          // strap
            float hz = c.z - R * 0.5f, hy = yt + dome * 0.4f;
            b.Box(1, new Vector3(0f, hy + 0.05f, hz), new Vector3(0.11f, 0.014f, 0.016f));                                       // grab bar
            b.Box(1, new Vector3(-0.09f, hy + 0.02f, hz), new Vector3(0.014f, 0.03f, 0.014f));
            b.Box(1, new Vector3(0.09f, hy + 0.02f, hz), new Vector3(0.014f, 0.03f, 0.014f));
            return b.Mesh("OZ_FortHatch_Lid");
        }

        /// <summary>Lift plate: a dark tread disc (top at local 0, 4 cm thick) with a raised cross rib, 1.5 cm inside the well wall.</summary>
        static Mesh PlateMesh()
        {
            var b = new MB(1);
            float R = CollarRIn - 0.015f, T = 0.04f;
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                b.Tri(0, Vector3.zero, d0 * R, d1 * R, up, up, up);
                b.Quad(0, d0 * R, d1 * R, d1 * R - up * T, d0 * R - up * T, d0, d1, d1, d0);
                b.Tri(0, -up * T, d1 * R - up * T, d0 * R - up * T, -up, -up, -up);
            }
            b.Box(0, new Vector3(0f, 0.006f, 0f), new Vector3(R * 0.8f, 0.006f, 0.02f));
            b.Box(0, new Vector3(0f, 0.006f, 0f), new Vector3(0.02f, 0.006f, R * 0.8f));
            return b.Mesh("OZ_FortHatch_Plate");
        }
    }
}

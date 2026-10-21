using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16 "Jungle": the animals that are decoration (the crocodiles are a rule: CrocodileView.cs). View side only, like the eagles,
    /// gulls, hares and snakes of the other levels: no game object, no collider, never a target, the simulation knows nothing of them.
    ///   * GREEN PARROTS IN THE AIR take the eagle's place on this level (AmbientLife spawns no eagle here): three, each circling
    ///     its own places in turn, a few fast beats now and then, banking into the turn, a soft shadow on the ground, a squawk
    ///     every 15 to 20 s with one chance in three (the game's bird rule). Like the eagle a flying bird never fills the screen:
    ///     between 6 and 4 of its wingspans from the lens it shrinks away, nearer it is not drawn, nor its shadow (the birds of a
    ///     group on their ring keep the same rule; the flyover's own flock and the parrots lent to it do not: it wants them at the lens).
    ///   * PARROT GROUPS sit on the ground (5 groups) and in trees (5 groups), 3 to 6 birds each: they look round, preen and peck.
    ///     A group has bird objects only while its spot is inside the camera's view plus a rim of 15 m (at most 3 groups at once).
    ///     When any robot or vehicle comes within 12 m of a ground group or 8 m of a tree group (or a blast lands within 15 m) the whole
    ///     group takes off within 0.35 s: one wing burst and two squawks at the group's middle on that frame, a ring over a point away
    ///     from the threat, then off to the nearest free spot of its kind 30 m or more away, where it lands again when the camera
    ///     can see it. The spot it left stays empty for 60 s.
    ///   * MONKEYS: 6 troops of 3 sit in trees. A walking robot within 9 m, a moving vehicle within 13 m, a standing robot within
    ///     4.5 m or a blast within 14 m of the tree: one stands up (alarm), the troop jumps down one after another and runs on all fours
    ///     along a way that was laid out on walkable ground (never over water, never through a rock or a building) into dense trees
    ///     or off the map's edge. After 40 to 70 quiet seconds they run back and climb up again.
    ///   * SNAKES: 6 dens. A snake comes out, slithers along its bank and, where its den has water to cross, swims it (never more than
    ///     2 in the water at once); a unit within 6.5 m makes it rear up and hiss, then it goes home.
    /// Every action asks for its sound through L16AnimalKit (one line of the sound log each); a sound that was not prepared is a named
    /// event that stays silent (MISSING_SOUNDS.md of the kit). Places: JungleFaunaData.cs. Each animal has its own seeded random source,
    /// so the level looks the same every run. Boots itself on level 16.
    /// One hook for the mission-start flyover: <see cref="FlockAcross"/>.
    /// </summary>
    [DefaultExecutionOrder(75)]
    public sealed partial class JungleFauna : MonoBehaviour
    {
        public static JungleFauna I { get; private set; }
        /// <summary>A scene is staged at KeepOutAt (the lounger set while the briefing or an ending plays): no monkey, snake or perched
        /// parrot is drawn within KeepOutRadius m of it, nor inside KeepOutCam's picture within KeepOutCamReach m. View only: every
        /// animal goes on by its own rules and is drawn again the moment it is outside or the scene is over.</summary>
        public static bool KeepOut; public static Vector3 KeepOutAt; public static Camera KeepOutCam;
        public static float KeepOutRadius = 14f, KeepOutCamReach = 90f;
        public static int KeptOutNow, KeptOutTotal;
        readonly List<L16AnimalKit.Rig> keptOut = new List<L16AnimalKit.Rig>(); readonly HashSet<L16AnimalKit.Rig> keptBefore = new HashSet<L16AnimalKit.Rig>();

        void LateUpdate()
        {
            if (keptOut.Count == 0 && !KeepOut) return;
            keptBefore.Clear();
            foreach (var r in keptOut)
            {
                keptBefore.Add(r);
                if (r != null && r.Shown && r.rends != null) for (int i = 0; i < r.rends.Length; i++) if (r.rends[i]) r.rends[i].enabled = true;
            }
            keptOut.Clear();
            if (KeepOut)
            {
                foreach (var a in apes) Keep(a.rig);
                foreach (var a in tapes) Keep(a.rig);
                foreach (var s in snks) Keep(s.rig);
                foreach (var g in groups) if (g.alive) foreach (var b in g.birds) Keep(b.rig);
            }
            KeptOutNow = keptOut.Count;
        }

        void Keep(L16AnimalKit.Rig r)
        {
            if (r == null || !r.Shown || !r.root || r.rends == null) return;
            Vector3 p = r.root.position, d = p - KeepOutAt; d.y = 0f;
            bool inside = d.sqrMagnitude < KeepOutRadius * KeepOutRadius;
            if (!inside && KeepOutCam && KeepOutCam.isActiveAndEnabled)
            {
                var v = KeepOutCam.WorldToViewportPoint(p + Vector3.up * 0.4f);
                inside = v.z > 0.2f && v.z < KeepOutCamReach && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
            }
            if (!inside) return;
            for (int i = 0; i < r.rends.Length; i++) if (r.rends[i]) r.rends[i].enabled = false;
            keptOut.Add(r); if (!keptBefore.Contains(r)) KeptOutTotal++;
        }
        public static bool AutoBoot = true;

        public bool parrots = true, perched = true, monkeys = true, snakes = true, sounds = true;
        public int seed = 1996;
        [Tooltip("fright distances (m): ground group, tree group, blast")] public float groundFright = 12f, treeFright = 8f, blastFright = 15f;
        [Tooltip("a group has bird objects inside the camera view plus this rim (m), below this camera height (m); groups alive at once")] public float viewRim = 15f, maxCamHeight = 140f; public int maxGroupsAlive = 3;
        [Tooltip("monkey fright (m): walking robot, moving vehicle, blast; a standing robot counts half")] public float monkeyRobot = 9f, monkeyVehicle = 13f, monkeyBlast = 14f;
        [Tooltip("snake: rears up within (m); in the water at once")] public float snakeAlert = 6.5f; public int maxSwimming = 2;

        // ------------------------------------------------------------------ proof (Editor/OzL16AnimalsCheck.cs)
        public static int FlyingParrots, GroupsAlive, BirdObjects, TakeOffs, Landings, MonkeysTotal, MonkeysSitting, MonkeyRuns, MonkeyReturns, SnakesTotal, SnakesOut, SnakesSwimming, MaxSnakesSwimming, Hisses;
        /// <summary>Every take-off: tree group?, the nearest unit's distance from the group's middle on that frame (m), unscaled time.</summary>
        public static readonly List<(bool tree, float dist, float time, int group)> TakeOffLog = new List<(bool, float, float, int)>();
        /// <summary>Every return: the quiet seconds the monkey waited before it came back.</summary>
        public static readonly List<float> ReturnAfter = new List<float>();

        ZGame game; ZWorld W; Camera cam; RtsCamera rts; float clock; bool subscribed;
        Material parrotMat, monkeyMat, snakeMat, shadowMat; Mesh shadowMesh; MaterialPropertyBlock mpb;
        Vector3 lastBlast; float lastBlastT = -99f;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || ZGame.I.level != "L16" || FindFirstObjectByType<JungleFauna>()) return;
            new GameObject("JungleFauna").AddComponent<JungleFauna>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null || game.level != "L16") { enabled = false; return; }
            W = game.World; cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : FindFirstObjectByType<RtsCamera>(); mpb = new MaterialPropertyBlock();
            FlyingParrots = GroupsAlive = BirdObjects = TakeOffs = Landings = MonkeysTotal = MonkeysSitting = MonkeyRuns = MonkeyReturns = SnakesTotal = SnakesOut = SnakesSwimming = MaxSnakesSwimming = Hisses = 0;
            TakeOffLog.Clear(); ReturnAfter.Clear();
            parrotMat = L16AnimalKit.Mat("L16_Parrot", "parrot_perched_tex", 0.2f);
            monkeyMat = L16AnimalKit.Mat("L16_Monkey", "Monkey_tex", 0.12f);
            snakeMat = L16AnimalKit.Mat("L16_Snake", "JG_Snake_tex", 0.4f);
            var ov = Shader.Find("OpenZ/Overlay");
            if (ov) { shadowMat = DetailKit.Track(new Material(ov) { name = "L16_BirdShadow" }); shadowMesh = DetailKit.Track(ShadowMesh()); }
            if (parrots) MakeFlyers();
            if (perched) MakeGroups();
            if (monkeys) MakeTroops();
            if (monkeys) MakeTempleApes();                                                     // the monkeys on the pyramids (JungleFauna.Temple.cs)
            if (snakes) MakeSnakes();
            FxManager.Exploded += OnExplosion; subscribed = true;
            Debug.Log($"[OpenZ] JungleFauna L16: {flyers.Count} parrots in the air, {groups.Count} parrot groups ({groups.FindAll(g => g.tree).Count} in trees) on {JungleFaunaData.GroundSpots.Length} + {JungleFaunaData.TreeSpots.Length} spots, {apes.Count} monkeys in {JungleFaunaData.TroopTrees.Length} troops, {snks.Count} snakes");
        }

        void OnDestroy()
        {
            if (subscribed) FxManager.Exploded -= OnExplosion;
            foreach (var f in flyers) f.rig?.Dispose();
            foreach (var b in pool) b.rig?.Dispose();
            foreach (var b in showBirds) b.rig?.Dispose();
            foreach (var l in lent) l.rig?.Dispose();
            foreach (var a in apes) a.rig?.Dispose();
            foreach (var a in tapes) a.rig?.Dispose();
            foreach (var s in snks) s.rig?.Dispose();
            if (I == this) I = null;
        }

        void OnExplosion(Vector3 at, float strength)
        {
            lastBlast = at; lastBlastT = Time.time;
            foreach (var a in apes) if (a.st == MS.Sit && !ApeHidden(a) && Flat(a.pos, at) < 6f) { Snd("critter_monkey_yelp", a.pos, 0.7f, 8f, 80f, "sd_monkey_yelp_a"); break; }   // a blast right beside its tree: one yelp
        }

        // ------------------------------------------------------------------ shared
        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        float Ground(float x, float z) => game.GroundY(x, z);
        Vector3 OnGround(Vector2 p) => new Vector3(p.x, Ground(p.x, p.y), p.y);
        bool Night => DetailKit.Night > 0.6f;
        bool CamLow => !rts || rts.height < maxCamHeight;
        bool Seen(Vector3 p, float rim) => CamLow && L16AnimalKit.InView(cam, p, rim);
        void Snd(string evt, Vector3 at, float vol, float min, float max, params string[] files) { if (sounds && !(hush && OzJungleLife.IsLife(evt))) L16AnimalKit.Play(evt, at, vol, min, max, 1f, files); }   // night or rain: no bird or monkey call
        bool TreeStands(Vector2Int tile) => !W.Path.TilePassable(tile.x, tile.y, true);                  // a felled tree opens its tile

        /// <summary>The nearest robot or vehicle to a place (flat distance). kind: 0 any, 1 a walking robot, 2 a moving vehicle, 3 a standing robot.</summary>
        float NearestUnit(Vector3 c, int kind, out Vector3 at)
        {
            float best = float.MaxValue; at = c;
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Dying) continue;
                var o = v.Obj;
                bool robot = o.Kind == ZKind.Robot, veh = o.Kind == ZKind.Vehicle;
                if (!robot && !veh) continue;
                if (kind == 1 && !(robot && o.IsMoving)) continue;
                if (kind == 2 && !(veh && o.IsMoving)) continue;
                if (kind == 3 && !(robot && !o.IsMoving)) continue;
                float d = Flat(v.transform.position, c);
                if (d < best) { best = d; at = v.transform.position; }
            }
            return best;
        }

        // one long take at 24 frames a second, frame 1 = 0 s (a file that reads back one frame later keeps the same times)
        static float T(L16AnimalKit.Rig rig, int frame) => Mathf.Clamp((frame - 1) / 24f, 0f, rig.Length(0));
        static void PoseFrames(L16AnimalKit.Rig rig, float a, float b, float wb, bool force = false) => rig.Pose(0, a, 1, b, wb, force);
        static float Loop(L16AnimalKit.Rig rig, int first, int last, float t, float rate = 1f) => T(rig, first) + Mathf.Repeat(t * rate, (last - first) / 24f);
        static float Once(L16AnimalKit.Rig rig, int first, int last, float t) => T(rig, first) + Mathf.Clamp(t, 0f, (last - first) / 24f);

        // ================================================================== parrots in the air
        const int FlapA = 1, FlapB = 15, GlideF = 18;
        /// <summary>Wingspan of the parrot model at size 1 (m): the eagle's.</summary>
        public const float FlyerSpan = 2.36f;

        /// <summary>Never fill the screen (the eagle's rule, AmbientLife.PoseBird): how large a flying bird is drawn at this place: full
        /// size from 6 of its wingspans from the camera on, shrinking away smoothly down to 4, not drawn nearer (0).</summary>
        float LensK(Vector3 at, float span) => cam ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f * span, 6f * span, Vector3.Distance(cam.transform.position, at))) : 1f;
        static void Lens(L16AnimalKit.Rig rig, float k) { rig.Show(k > 0.001f); if (rig.Shown) rig.root.localScale = Vector3.one * k; }

        sealed class Flyer
        {
            public L16AnimalKit.Rig rig; public System.Random rng; public Vector3[] route; public int wp; public Vector2 centre; public float radius, alt, ang, dir = 1f, hold, speed;
            public float flapW, burstT, burstLeft, callT, flapT; public Vector3 pos; public float yaw, roll;
        }
        readonly List<Flyer> flyers = new List<Flyer>();

        void MakeFlyers()
        {
            for (int i = 0; i < JungleFaunaData.ParrotRoutes.Length; i++)
            {
                var rig = L16AnimalKit.Rig.Make("Parrot_perched", "ParrotAir" + i, transform, new[] { parrotMat }, 1f, false);
                if (rig == null) return;
                rig.FaceAhead("head", "tail_2");
                var f = new Flyer { rig = rig, rng = new System.Random(seed * 31 + i * 7919 + 2), route = JungleFaunaData.ParrotRoutes[i], alt = JungleFaunaData.ParrotAlt[i], dir = i % 2 == 0 ? 1f : -1f };
                f.centre = new Vector2(f.route[0].x, f.route[0].y); f.radius = f.route[0].z; f.ang = R(f.rng, 0f, Mathf.PI * 2f); f.hold = R(f.rng, 25f, 60f);
                f.speed = 7.4f + R(f.rng, -0.6f, 0.6f); f.burstT = R(f.rng, 1f, 5f); f.callT = 3f + R(f.rng, 0f, 5f);
                flyers.Add(f);
            }
            FlyingParrots = flyers.Count;
        }

        void StepFlyers(float dt)
        {
            foreach (var f in flyers)
            {
                // on to the next circle now and then: the middle of the circle glides over, the bird keeps turning
                if ((f.hold -= dt) <= 0f) { f.wp = (f.wp + 1) % f.route.Length; f.hold = R(f.rng, 25f, 60f); }
                var want = new Vector2(f.route[f.wp].x, f.route[f.wp].y);
                f.centre = Vector2.MoveTowards(f.centre, want, 5.5f * dt); f.radius = Mathf.MoveTowards(f.radius, f.route[f.wp].z, 1.5f * dt);
                f.ang += f.dir * f.speed / Mathf.Max(4f, f.radius) * dt;
                var p = new Vector3(f.centre.x + Mathf.Cos(f.ang) * f.radius, 0f, f.centre.y + Mathf.Sin(f.ang) * f.radius);
                p.y = Mathf.Max(Ground(p.x, p.z), 0f) + f.alt + Mathf.Sin(clock * 0.31f + f.alt) * 0.8f;
                p = HushFly(f, p, dt);                                                              // night or rain: off over the map's side, and back after
                p = BlastFly(f, p, dt);                                                             // the fort blast: out of its keep-out circle (JungleFauna.Blast.cs)
                p = ShipGuard(ShipFly(f, p, dt), dt);                                               // DV118: out of the flying shuttle's way (JungleFauna.Ship.cs), never into its keep-out (JungleFauna.ShipGuard.cs)
                var vel = p - f.pos; f.pos = p;
                if (vel.sqrMagnitude > 1e-6f) f.yaw = Mathf.LerpAngle(f.yaw, Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 8f));
                f.roll = Mathf.Lerp(f.roll, -f.dir * 24f, 1f - Mathf.Exp(-dt * 2f));
                // a few fast beats now and then (a parrot beats faster and shallower than the eagle glides)
                if (f.burstLeft > 0f) { f.burstLeft -= dt; f.flapW = Mathf.MoveTowards(f.flapW, 1f, dt * 4f); }
                else { f.flapW = Mathf.MoveTowards(f.flapW, 0f, dt * 1.5f); if ((f.burstT -= dt) <= 0f) { f.burstT = R(f.rng, 2.5f, 7f); f.burstLeft = R(f.rng, 1.2f, 2.6f); } }
                f.flapT += dt * 2.2f;
                f.rig.root.SetPositionAndRotation(f.pos, Quaternion.Euler(0f, f.yaw, f.roll));
                Lens(f.rig, LensK(f.pos, FlyerSpan) * HushShow(f));                                                    // never fills the screen
                PoseFrames(f.rig, Loop(f.rig, FlapA, FlapB, f.flapT), T(f.rig, GlideF), 1f - f.flapW);
                if (f.rig.Shown) Shadow(f.pos, f.yaw, FlyerSpan);                                        // like the eagle's: no shadow of a bird that is not drawn
                // the game's bird rule: a call every 15 + up to 5 s with one chance in three; none at night
                if ((f.callT -= dt) <= 0f)
                {
                    f.callT = 15f + R(f.rng, 0f, 5f);
                    if (!Night && f.rng.Next(3) == 0) Snd("amb_l16_parrot_squawk", f.pos, 0.5f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
                }
            }
        }

        static Mesh ShadowMesh()
        {
            // a soft bird shape lying on the ground: body and two wings, the rim fades out
            var half = new[] { new Vector2(0f, 0.3f), new Vector2(0.06f, 0.2f), new Vector2(0.2f, 0.12f), new Vector2(0.5f, 0.05f), new Vector2(0.36f, -0.03f), new Vector2(0.08f, -0.12f), new Vector2(0.1f, -0.28f), new Vector2(0f, -0.3f) };
            var ring = new List<Vector2>(half);
            for (int i = half.Length - 2; i >= 1; i--) ring.Add(new Vector2(-half[i].x, half[i].y));
            int n = ring.Count;
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { Color.white }; var t = new List<int>();
            for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, 0f, ring[i].y)); c.Add(Color.white); }
            for (int i = 0; i < n; i++) { var o = ring[i] * 1.2f + ring[i].normalized * 0.02f; v.Add(new Vector3(o.x, 0f, o.y)); c.Add(new Color(1f, 1f, 1f, 0f)); }
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n, oa = 1 + n + i, ob = 1 + n + (i + 1) % n;
                t.Add(0); t.Add(b); t.Add(a); t.Add(a); t.Add(b); t.Add(ob); t.Add(a); t.Add(ob); t.Add(oa);
            }
            var m = new Mesh { name = "L16_BirdShadow" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        void Shadow(Vector3 bird, float yaw, float span)
        {
            if (!shadowMat || Night) return;
            float gy = Ground(bird.x, bird.z), h = bird.y - gy;
            if (h < 0.3f || h > 40f || !Seen(new Vector3(bird.x, gy, bird.z), 3f)) return;
            float a = Mathf.Lerp(0.3f, 0.1f, Mathf.Clamp01(h / 30f));
            mpb.SetColor(ColorId, new Color(0.05f, 0.04f, 0.03f, a));
            Graphics.DrawMesh(shadowMesh, Matrix4x4.TRS(new Vector3(bird.x, gy + 0.06f, bird.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * span), shadowMat, 0, null, 0, mpb, ShadowCastingMode.Off, false);
        }

        // ================================================================== parrot groups that take fright
        const int BranchA = 24, BranchB = 48, GroundA = 52, GroundB = 76, LookA = 80, LookB = 116, PreenA = 120, PreenB = 168, PeckA = 188, PeckB = 204, TakeA = 208, TakeB = 244, LandA = 248, LandB = 280;
        const float PerchScale = 0.6f, PerchSpan = FlyerSpan * PerchScale;
        enum GS { Sitting, Fright, Ring, Travel, Landing, Away }
        sealed class PBird
        {
            public L16AnimalKit.Rig rig; public bool used; public Vector3 seat, pos, from; public float yaw, t, delay, phase, moveT, moveLeft; public int move; public float ringAng, ringR, ringH;
        }
        sealed class Group
        {
            public int id, spot, size, toSpot = -1; public bool tree, alive; public GS st; public float t, ringLen, awayT; public Vector3 centre, threat, ringC, target; public System.Random rng;
            public readonly List<PBird> birds = new List<PBird>(); public float callT, checkT; public bool viaData;
        }
        readonly List<Group> groups = new List<Group>();
        readonly List<PBird> pool = new List<PBird>();
        float[] groundFreeAt, treeFreeAt;                                                             // a spot that was left stays empty until this time

        void MakeGroups()
        {
            var d = JungleFaunaData.GroupSize;
            groundFreeAt = new float[JungleFaunaData.GroundSpots.Length]; treeFreeAt = new float[JungleFaunaData.TreeSpots.Length];
            for (int i = 0; i < 10; i++)
            {
                bool tree = i >= 5; int spot = tree ? i - 5 : i;
                if (spot >= (tree ? JungleFaunaData.TreeSpots.Length : JungleFaunaData.GroundSpots.Length)) continue;
                var g = new Group { id = i, tree = tree, spot = spot, size = d[i % d.Length], rng = new System.Random(seed * 57 + i * 104729 + 9), st = GS.Sitting };
                if (tree) g.size = Mathf.Min(g.size, JungleFaunaData.TreeSeats[spot].Length);
                g.centre = SpotCentre(tree, spot); g.callT = R(g.rng, 6f, 20f);
                groups.Add(g);
            }
        }

        Vector3 SpotCentre(bool tree, int spot)
        {
            if (!tree) return OnGround(JungleFaunaData.GroundSpots[spot]);
            var s = JungleFaunaData.TreeSeats[spot]; var c = Vector3.zero; foreach (var p in s) c += p;
            return c / s.Length;
        }

        Vector3 SeatOf(Group g, int k)
        {
            if (g.tree) return JungleFaunaData.TreeSeats[g.spot][k % JungleFaunaData.TreeSeats[g.spot].Length];
            // on the ground: a loose ring of 1.5 m round the spot
            float a = (k + 0.37f * g.id) * Mathf.PI * 2f / Mathf.Max(3, g.size), r = k == 0 ? 0.35f : 1.0f + 0.5f * ((k * 7 + g.id * 3) % 5) / 4f;
            var p = new Vector3(g.centre.x + Mathf.Cos(a) * r, 0f, g.centre.z + Mathf.Sin(a) * r); p.y = Ground(p.x, p.z);
            return p;
        }

        PBird TakeBird()
        {
            foreach (var b in pool) if (!b.used) { b.used = true; b.rig.Show(true); b.rig.root.localScale = Vector3.one; return b; }
            if (pool.Count >= 18) return null;
            var rig = L16AnimalKit.Rig.Make("Parrot_perched", "ParrotPerched" + pool.Count, transform, new[] { parrotMat }, PerchScale, true);
            if (rig == null) return null;
            rig.FaceAhead("head", "tail_2");
            var nb = new PBird { rig = rig, used = true }; pool.Add(nb);
            return nb;
        }

        void Wake(Group g)
        {
            if (g.alive) return;
            // units already stand at the spot as it comes into view: the group is not shown there, it is somewhere else (as data)
            if (g.st == GS.Sitting && NearestUnit(g.centre, 0, out _) < (g.tree ? treeFright : groundFright) + 2f) { Relocate(g, false); return; }
            for (int k = 0; k < g.size; k++)
            {
                var b = TakeBird(); if (b == null) break;
                b.seat = SeatOf(g, k); b.pos = b.seat; b.yaw = R(g.rng, 0f, 360f); b.phase = R(g.rng, 0f, 3f); b.move = 0; b.moveT = R(g.rng, 2f, 9f); b.t = 0f;
                g.birds.Add(b);
            }
            g.alive = g.birds.Count > 0;
        }

        void Sleep(Group g)
        {
            foreach (var b in g.birds) { b.used = false; b.rig.Show(false); }
            g.birds.Clear(); g.alive = false;
        }

        /// <summary>The nearest free spot of the group's kind 30 m or more away and 20 m or more from every unit.</summary>
        int NextSpot(Group g)
        {
            if (hush) return -1;                                                                // night or rain: off the screen, no landing
            if (BlastHolds(g)) return -1;                                                       // fled the fort blast: off the screen first
            int n = g.tree ? JungleFaunaData.TreeSpots.Length : JungleFaunaData.GroundSpots.Length; int best = -1; float bd = float.MaxValue;
            for (int s = 0; s < n; s++)
            {
                if (s == g.spot || (g.tree ? treeFreeAt[s] : groundFreeAt[s]) > clock) continue;
                if (g.tree && !TreeStands(JungleFaunaData.TreeSpotTiles[s])) continue;
                bool taken = false; foreach (var o in groups) if (o != g && o.tree == g.tree && (o.spot == s || o.toSpot == s)) { taken = true; break; }
                if (taken) continue;
                var c = SpotCentre(g.tree, s); float d = Flat(c, g.centre);
                if (BlastKeeps(c) || BlastCrosses(g.centre, c)) continue;                           // never near the fort blast, nor over it
                if (ShipKeeps(c) || ShipCrosses(g.centre, c)) continue;                             // DV118: nor by the flying shuttle or across its way
                if (d < 30f || NearestUnit(c, 0, out _) < 20f) continue;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        void Relocate(Group g, bool leftNow)
        {
            int s = NextSpot(g);
            if (leftNow) { if (g.tree) treeFreeAt[g.spot] = clock + 60f; else groundFreeAt[g.spot] = clock + 60f; }
            if (s >= 0) { g.spot = s; g.centre = SpotCentre(g.tree, s); }
            g.toSpot = -1; g.st = GS.Sitting; g.viaData = true;
        }

        void Scare(Group g, Vector3 threat, float dist)
        {
            if (g.st != GS.Sitting || !g.alive) return;
            g.st = GS.Fright; g.t = 0f; g.threat = threat; TakeOffs++;
            TakeOffLog.Add((g.tree, dist, Time.unscaledTime, g.id));
            // the nearest to the threat goes first; all within 0.35 s
            g.birds.Sort((a, b) => Flat(a.seat, threat).CompareTo(Flat(b.seat, threat)));
            for (int k = 0; k < g.birds.Count; k++) { var b = g.birds[k]; b.delay = 0.35f * k / Mathf.Max(1, g.birds.Count - 1); b.t = 0f; b.from = b.seat; }
            var away = g.centre - threat; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away = Quaternion.Euler(0f, R(g.rng, -35f, 35f), 0f) * away.normalized;
            g.ringC = g.centre + away * 8f; g.ringC.y = Ground(g.ringC.x, g.ringC.z) + R(g.rng, 7f, 9f);
            g.ringLen = R(g.rng, 6f, 9f);
            foreach (var b in g.birds) { b.ringR = R(g.rng, 6f, 8f); b.ringAng = Mathf.Atan2(b.seat.z - g.ringC.z, b.seat.x - g.ringC.x); b.ringH = R(g.rng, -0.6f, 0.6f); }
            // one wing burst and two squawks for the GROUP, on this frame, at its middle; leaves for a tree group
            Snd("critter_parrot_flutter", g.centre, 0.75f, 8f, 90f, "sd_parrot_flock_flutter_a", "sd_parrot_flock_flutter_b");   // the whole group lifts off: the two flock takes (1.6 s for a 1.5 s take-off)
            Snd("amb_l16_parrot_squawk", g.centre, 0.8f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
            Snd("amb_l16_parrot_squawk", g.centre + Vector3.up, 0.65f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
            if (g.tree) Snd("dtl_leaves_rustle", g.centre, 0.5f, 6f, 60f, "sd_leaves_rustle_a", "sd_branch_shake_a", "sd_branch_shake_b");
            // the fright spreads to groups that are alive within 14 m, 0.3 to 0.5 s later
            foreach (var o in groups) if (o != g && o.alive && o.st == GS.Sitting && Flat(o.centre, g.centre) < 14f) { o.checkT = -R(g.rng, 0.3f, 0.5f); o.threat = threat; }
        }

        void StepGroups(float dt)
        {
            int alive = 0, objects = 0;
            foreach (var g in groups) if (g.alive) alive++;
            foreach (var g in groups)
            {
                bool seen = Seen(g.centre, viewRim) || (g.alive && g.st != GS.Sitting && g.birds.Count > 0 && Seen(g.birds[0].pos, viewRim));
                if (!g.alive)
                {
                    if (g.st == GS.Away) { if ((g.awayT -= dt) <= 0f) { if (backAfterHush.Contains(g)) { g.st = GS.Sitting; g.toSpot = -1; } else Relocate(g, false); } continue; }   // a group away for the night / rain keeps its spot: it flies back in there
                    if (BlastKeeps(g.centre) || BlastHolds(g)) { if (!BlastHolds(g)) Relocate(g, false); continue; }   // its spot is by the fort blast: a spot outside first
                    if (seen && !hush && alive < maxGroupsAlive && (!g.tree || TreeStands(JungleFaunaData.TreeSpotTiles[g.spot]))) { ComeBack(g); if (g.alive) alive++; }
                    else if (g.tree && !TreeStands(JungleFaunaData.TreeSpotTiles[g.spot])) Relocate(g, true);     // its tree was felled while nobody looked
                    continue;
                }
                if (!seen)
                {   // out of the camera's view and rim: no bird objects; a group in the air goes on as data
                    if (g.st != GS.Sitting) { if (g.toSpot >= 0) { g.spot = g.toSpot; g.centre = SpotCentre(g.tree, g.spot); g.toSpot = -1; } g.st = GS.Sitting; }
                    Sleep(g); alive--; continue;
                }
                switch (g.st)
                {
                    case GS.Sitting: if (hush) { Leave(g); break; } Sitting(g, dt); break;
                    case GS.Fright: g.t += dt; bool up = true; foreach (var b in g.birds) up &= TakeOff(g, b, dt); if (up) { g.st = GS.Ring; g.t = 0f; } break;
                    case GS.Ring:
                        g.t += dt; foreach (var b in g.birds) Ring(g, b, dt);
                        if (g.t >= g.ringLen)
                        {
                            g.toSpot = NextSpot(g); if (g.tree) treeFreeAt[g.spot] = clock + 60f; else groundFreeAt[g.spot] = clock + 60f;
                            g.target = g.toSpot >= 0 ? SpotCentre(g.tree, g.toSpot) : g.ringC + (g.ringC - g.threat).normalized * 120f;
                            g.st = GS.Travel; g.t = 0f;
                        }
                        break;
                    case GS.Travel:
                    {
                        g.t += dt; bool there = true;
                        foreach (var b in g.birds) there &= Travel(g, b, dt);
                        if (g.toSpot < 0 && g.t > 14f) { Sleep(g); alive--; g.st = GS.Away; g.awayT = 20f; break; }      // no free spot: off the screen, back later
                        if (there && g.toSpot >= 0)
                        {
                            g.spot = g.toSpot; g.toSpot = -1; g.centre = SpotCentre(g.tree, g.spot); g.st = GS.Landing; g.t = 0f;
                            for (int k = 0; k < g.birds.Count; k++) { var b = g.birds[k]; b.seat = SeatOf(g, k); b.from = b.pos; b.t = 0f; b.delay = k * R(g.rng, 0.2f, 0.5f); }
                            Snd("critter_parrot_land", g.centre, 0.5f, 6f, 60f);                           // no file yet: a named silent event (MISSING_SOUNDS.md)
                            if (g.tree) Snd("dtl_leaves_rustle", g.centre, 0.4f, 6f, 60f, "sd_leaves_rustle_a", "sd_branch_shake_a", "sd_branch_shake_b");
                        }
                        break;
                    }
                    case GS.Landing:
                    {
                        bool down = true; foreach (var b in g.birds) down &= Land(g, b, dt);
                        if (down) { g.st = GS.Sitting; Landings++; foreach (var b in g.birds) { b.pos = b.seat; b.move = 0; b.moveT = R(g.rng, 2f, 8f); } }
                        break;
                    }
                }
                // on its ring and on its way a group's bird keeps the flying birds' rule near the lens; sitting, taking off and landing it is full size
                bool air = g.st == GS.Ring || g.st == GS.Travel;
                foreach (var b in g.birds) Lens(b.rig, air ? LensK(b.pos, PerchSpan) : 1f);
                objects += g.birds.Count;
            }
            GroupsAlive = alive; BirdObjects = objects;
        }

        void Sitting(Group g, float dt)
        {
            // fright: every frame for a group that has bird objects (a fast vehicle must not get 3 m closer between two looks)
            float lim = g.tree ? treeFright : groundFright;
            if (g.checkT < 0f) { if ((g.checkT += dt) >= 0f) { Scare(g, g.threat, Flat(g.threat, g.centre)); return; } }
            else
            {
                float d = NearestUnit(g.centre, 0, out var at);
                if (d <= lim) { Scare(g, at, d); return; }
                if (ShipScares(g, out var sat, out var sd)) { Scare(g, sat, sd); return; }       // DV118: the loud low shuttle frightens from farther
                if (Time.time - lastBlastT < 0.3f && Flat(lastBlast, g.centre) < blastFright) { Scare(g, lastBlast, Flat(lastBlast, g.centre)); return; }
                if (g.tree && !TreeStands(JungleFaunaData.TreeSpotTiles[g.spot])) { Scare(g, g.centre + Vector3.right, 0f); return; }
            }
            int busy = 0; foreach (var b in g.birds) if (b.move != 0) busy++;
            foreach (var b in g.birds)
            {
                b.t += dt;
                float idle = g.tree ? Loop(b.rig, BranchA, BranchB, b.t + b.phase) : Loop(b.rig, GroundA, GroundB, b.t + b.phase);
                if (b.move == 0)
                {
                    if ((b.moveT -= dt) <= 0f && busy < 2)
                    {   // look round, preen, peck (ground only)
                        int pick = g.rng.Next(g.tree ? 2 : 3); b.move = pick == 0 ? 1 : pick == 1 ? 2 : 3; b.moveLeft = 0f; busy++;
                        b.moveT = R(g.rng, 5f, 14f);
                        // the small sound of the move, heard only close by. No file yet for either: named events that stay silent (MISSING_SOUNDS.md of the kit)
                        if (b.move == 2) Snd("critter_parrot_preen", b.seat, 0.25f, 3f, 30f); else if (b.move == 3) Snd("critter_parrot_peck", b.seat, 0.3f, 3f, 30f);
                    }
                    PoseFrames(b.rig, idle, idle, 0f);
                }
                else
                {
                    int fa = b.move == 1 ? LookA : b.move == 2 ? PreenA : PeckA, fb = b.move == 1 ? LookB : b.move == 2 ? PreenB : PeckB;
                    b.moveLeft += dt; float len = (fb - fa) / 24f;
                    float w = Mathf.Clamp01(Mathf.Min(b.moveLeft, len - b.moveLeft) / 0.2f);                // in and out of the idle pose over 0.2 s
                    PoseFrames(b.rig, idle, Once(b.rig, fa, fb, b.moveLeft), w);
                    if (b.moveLeft >= len) b.move = 0;
                }
                b.rig.root.SetPositionAndRotation(b.seat, Quaternion.Euler(0f, b.yaw, 0f));
            }
            // a call from a sitting group every 15 to 20 s with one chance in three, only where the camera looks, none at night
            if ((g.callT -= dt) <= 0f) { g.callT = 15f + R(g.rng, 0f, 5f); if (!Night && g.rng.Next(3) == 0) Snd("critter_parrot_sit_call", g.centre, 0.4f, 8f, 70f, "sd_parrot_squawk_c", "sd_parrot_chatter_a", "sd_parrot_chatter_b", "sd_parrot_chatter_c"); }
        }

        /// <summary>The fright take-off take (1.5 s): the bird holds its place for 0.375 s, then leaps: ground birds climb at 3.2 m/s, tree
        /// birds drop off the branch first, both away from the threat at up to 6.5 m/s. True when it is in the air and flying.</summary>
        bool TakeOff(Group g, PBird b, float dt)
        {
            if (g.t < b.delay) { PoseFrames(b.rig, T(b.rig, g.tree ? BranchA : GroundA), 0f, 0f); return false; }
            b.t += dt;
            float len = (TakeB - TakeA) / 24f;
            var away = b.seat - g.threat; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            b.yaw = Mathf.LerpAngle(b.yaw, Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f));
            if (b.t > 0.375f)
            {
                float k = Mathf.Clamp01((b.t - 0.375f) / (BlastHolds(g) ? 0.3f : 1.2f));                 // K271: off the fort blast: full speed in 0.3 s
                b.pos += away * (BlastPace(g, 6.5f) * k) * dt + Vector3.up * ((g.tree ? Mathf.Lerp(-1.6f, 1.2f, k) : 3.2f) * dt);
                b.pos.y = Mathf.Max(b.pos.y, Ground(b.pos.x, b.pos.z) + 0.4f);
            }
            b.rig.root.SetPositionAndRotation(b.pos, Quaternion.Euler(0f, b.yaw, 0f));
            PoseFrames(b.rig, Once(b.rig, TakeA, TakeB, b.t), 0f, 0f);
            return b.t >= len;
        }

        void Fly(PBird b, Vector3 to, float speed, float dt, float flapRate)
        {
            var d = to - b.pos; float l = d.magnitude;
            if (l > 1e-4f) { var step = d / l * Mathf.Min(l, speed * dt); b.pos += step; b.yaw = Mathf.LerpAngle(b.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 6f)); }
            b.pos = ShipGuard(b.pos, dt);                                                                // DV118: never into the shuttle's keep-out on the way (JungleFauna.ShipGuard.cs)
            b.t += dt;
            b.rig.root.SetPositionAndRotation(b.pos, Quaternion.Euler(0f, b.yaw, 0f));
            PoseFrames(b.rig, Loop(b.rig, FlapA, FlapB, b.t + b.phase, flapRate), T(b.rig, GlideF), 0.25f);
            if (b.rig.Shown) Shadow(b.pos, b.yaw, PerchSpan);
        }

        /// <summary>One ring over a point 8 m farther from the threat: radius 6 to 8 m, 7 to 9 m over the ground (low on purpose: the
        /// close zoom cuts off anything higher than about 10 m).</summary>
        void Ring(Group g, PBird b, float dt)
        {
            b.ringAng += 6.5f / b.ringR * dt;
            var p = new Vector3(g.ringC.x + Mathf.Cos(b.ringAng) * b.ringR, g.ringC.y + b.ringH, g.ringC.z + Mathf.Sin(b.ringAng) * b.ringR);
            Fly(b, ShipAvoid(p), 9f, dt, 2.4f);
        }

        bool Travel(Group g, PBird b, float dt)
        {
            var p = g.target + new Vector3(Mathf.Cos(b.phase * 2.1f) * 2f, 0f, Mathf.Sin(b.phase * 2.1f) * 2f);
            p.y = Mathf.Max(Ground(p.x, p.z), 0f) + (g.toSpot >= 0 && Flat(b.pos, g.target) < 14f ? (g.tree ? g.target.y - Ground(p.x, p.z) + 2f : 2.5f) : 8f + b.ringH);
            Fly(b, ShipAvoid(p), BlastPace(g, 6.5f), dt, 2.2f);                                              // K271: fast off the fort blast first
            return Flat(b.pos, g.target) < 3.5f;
        }

        /// <summary>The landing take (1.33 s): flare, two braking beats, feet down on the seat.</summary>
        bool Land(Group g, PBird b, float dt)
        {
            if ((b.delay -= dt) > 0f) { Fly(b, b.pos + Quaternion.Euler(0, b.yaw, 0) * Vector3.forward, 1.5f, dt, 2.2f); b.from = b.pos; b.t = 0f; return false; }
            b.t += dt; float len = (LandB - LandA) / 24f, k = Mathf.Clamp01(b.t / len);
            b.pos = Vector3.Lerp(b.from, b.seat, Mathf.SmoothStep(0f, 1f, k)) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.6f);
            b.rig.root.SetPositionAndRotation(b.pos, Quaternion.Euler(0f, b.yaw, 0f));
            PoseFrames(b.rig, Once(b.rig, LandA, LandB, b.t), 0f, 0f);
            return k >= 1f;
        }

        // ================================================================== the flyover's parrot flock
        sealed class ShowBird { public L16AnimalKit.Rig rig; public bool used; public Vector3 off; public float phase; }
        readonly List<ShowBird> showBirds = new List<ShowBird>();
        sealed class Flock { public Vector3 from, to; public float t, len; public readonly List<ShowBird> birds = new List<ShowBird>(); public bool sound, called; }
        readonly List<Flock> flocks = new List<Flock>();

        /// <summary>
        /// FLYOVER HOOK 2. A flock of `count` parrots (at most 8 per flock, 16 on screen) flies the straight line from `from` to `to`
        /// (world points: the flock's middle) in `seconds`, starting `startIn` seconds from now; each bird keeps its own place inside
        /// the box `spread` (metres right, up, along; default 2.4, 0.9, 2.4) from the seed, beating its wings out of step with the
        /// others. The birds exist only for this crossing and are gone at its end; the level's own parrots and groups are untouched.
        /// With `sound` the flock's wing burst and two squawks are asked for at its middle as it starts (off by default: the flyover
        /// plays its own sounds). False = the parrot model is missing or the flock is too large.
        /// </summary>
        public static bool FlockAcross(Vector3 from, Vector3 to, float seconds, int count = 6, Vector3? spread = null, int seed = 1, float startIn = 0f, bool sound = false)
        {
            if (!I || seconds <= 0f || count < 1 || count > 8) return false;
            if (BlastCrosses(from, to)) return false;                                           // never over the fort blast
            var sp = spread ?? new Vector3(2.4f, 0.9f, 2.4f);
            var rng = new System.Random(seed * 7919 + 13);
            var f = new Flock { from = from, to = to, t = -Mathf.Max(0f, startIn), len = seconds, sound = sound };
            var fwd = (to - from).normalized; var right = Vector3.Cross(Vector3.up, fwd); if (right.sqrMagnitude < 1e-4f) right = Vector3.right; right.Normalize();
            for (int k = 0; k < count; k++)
            {
                ShowBird b = null;
                foreach (var s in I.showBirds) if (!s.used) { b = s; break; }
                if (b == null)
                {
                    if (I.showBirds.Count >= 16) break;
                    var rig = L16AnimalKit.Rig.Make("Parrot_perched", "ParrotShow" + I.showBirds.Count, I.transform, new[] { I.parrotMat }, 0.85f, false);
                    if (rig == null) return false;
                    rig.FaceAhead("head", "tail_2");
                    b = new ShowBird { rig = rig }; I.showBirds.Add(b);
                }
                b.used = true; b.rig.Show(false);
                b.off = right * R(rng, -sp.x, sp.x) + Vector3.up * R(rng, -sp.y, sp.y) + fwd * R(rng, -sp.z, sp.z); b.phase = R(rng, 0f, 1f);
                f.birds.Add(b);
            }
            I.flocks.Add(f);
            return f.birds.Count > 0;
        }

        void StepFlocks(float dt)
        {
            for (int i = flocks.Count - 1; i >= 0; i--)
            {
                var f = flocks[i]; f.t += dt;
                if (f.t < 0f) continue;
                float k = f.t / f.len;
                if (k >= 1f) { foreach (var b in f.birds) { b.used = false; b.rig.Show(false); } flocks.RemoveAt(i); continue; }
                var mid = Vector3.Lerp(f.from, f.to, k); var d = f.to - f.from; float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                if (f.sound && !f.called)
                {
                    f.called = true;
                    Snd("critter_parrot_flutter", mid, 0.75f, 8f, 90f, "sd_parrot_flutter_a", "sd_parrot_flutter_b", "sd_parrot_flutter_c");
                    Snd("amb_l16_parrot_squawk", mid, 0.8f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
                }
                foreach (var b in f.birds)
                {
                    b.rig.Show(true);
                    var p = mid + b.off + Vector3.up * (Mathf.Sin((f.t + b.phase) * 3.1f) * 0.25f);
                    b.rig.root.SetPositionAndRotation(p, Quaternion.Euler(-4f, yaw, Mathf.Sin((f.t + b.phase) * 1.7f) * 8f));
                    PoseFrames(b.rig, Loop(b.rig, FlapA, FlapB, f.t + b.phase, 2.3f), T(b.rig, GlideF), 0.15f, true);
                }
            }
        }

        // ---- parrots lent to the flyover stage (it places and turns them itself)
        sealed class Lent { public Transform holder; public L16AnimalKit.Rig rig; public bool used; }
        readonly List<Lent> lent = new List<Lent>();

        /// <summary>
        /// FLYOVER HOOK 2b (the lending the flyover stage asks for: its own interface is served by OzL16AnimalsForBriefing.cs). One
        /// flying parrot of the level's model that the level does not steer and does not shrink: the returned root has +Z = the beak,
        /// +Y = up and the body's middle at its origin, at the size of the level's sitting parrots (0.6 of the file: wingspan 1.42 m;
        /// whoever borrows it sets place and turn, and may scale the root). LendParrotPose shows the wing-beat loop at that second (it
        /// wraps). LendParrotReturn takes it back. At most 8.
        /// </summary>
        public static Transform LendParrot()
        {
            if (!I) return null;
            foreach (var l in I.lent) if (!l.used) { l.used = true; l.rig.Show(true); l.holder.localScale = Vector3.one; return l.holder; }
            if (I.lent.Count >= 8) return null;
            var holder = new GameObject("ParrotLent" + I.lent.Count).transform; holder.SetParent(I.transform, false);
            var rig = L16AnimalKit.Rig.Make("Parrot_perched", "Model", holder, new[] { I.parrotMat }, PerchScale, false);
            if (rig == null) { Destroy(holder.gameObject); return null; }
            rig.FaceAhead("head", "tail_2");
            rig.root.localPosition = new Vector3(0f, -0.33f * PerchScale, 0f);                       // the file's origin is the feet point: the body's middle is 0.33 m above it
            var n = new Lent { holder = holder, rig = rig, used = true }; I.lent.Add(n);
            return holder;
        }

        public static void LendParrotPose(Transform parrot, float flapSeconds)
        {
            if (!I) return;
            foreach (var l in I.lent) if (l.holder == parrot && l.used) { PoseFrames(l.rig, Loop(l.rig, FlapA, FlapB, flapSeconds), 0f, 0f, true); return; }
        }

        public static void LendParrotReturn(Transform parrot)
        {
            if (!I) return;
            foreach (var l in I.lent) if (l.holder == parrot) { l.used = false; l.rig.Show(false); }
        }

        /// <summary>Checks: every flying parrot of the level right now (the three in the air and the birds of a group on its ring or on
        /// its way; not the flyover's): where it is, its wingspan at full size (m), whether it is drawn.</summary>
        public static void Flying(List<Vector3> pos, List<float> span, List<bool> drawn)
        {
            pos.Clear(); span.Clear(); drawn.Clear();
            if (!I) return;
            foreach (var f in I.flyers) { pos.Add(f.pos); span.Add(FlyerSpan); drawn.Add(f.rig.Shown); }
            foreach (var g in I.groups)
                if (g.alive && (g.st == GS.Ring || g.st == GS.Travel)) foreach (var b in g.birds) { pos.Add(b.pos); span.Add(PerchSpan); drawn.Add(b.rig.Shown); }
        }

        /// <summary>Parrots of a flyover flock in the air right now.</summary>
        public static int FlockBirds { get { int n = 0; if (I) foreach (var f in I.flocks) if (f.t >= 0f) n += f.birds.Count; return n; } }

        // ================================================================== monkeys
        const int SitA = 1, SitB = 48, LookMA = 50, LookMB = 97, AlarmA = 100, AlarmB = 124, JumpA = 126, JumpB = 150, RunA = 152, RunB = 164, ClimbA = 166, ClimbB = 190;
        enum MS { Sit, Alarm, Jump, Run, Gone, Back, Climb }
        sealed class Ape
        {
            public float chatterAt;
            public int troop, k; public L16AnimalKit.Rig rig; public System.Random rng; public MS st; public float t, delay, idleT, quiet, awayT, yaw; public bool look;
            public Vector3 seat, pos, from; public Vector2[] way; public int leg; public float legT; public Vector2 side;
        }
        readonly List<Ape> apes = new List<Ape>();
        float[] troopCallT, troopCheckT; bool[] troopGone;

        void MakeTroops()
        {
            int n = JungleFaunaData.TroopTrees.Length;
            troopCallT = new float[n]; troopCheckT = new float[n]; troopGone = new bool[n];
            for (int i = 0; i < n; i++)
            {
                troopCallT[i] = 8f + i * 5f;
                for (int k = 0; k < JungleFaunaData.TroopSeats[i].Length; k++)
                {
                    var rig = L16AnimalKit.Rig.Make("Monkey", "Monkey" + i + "_" + k, transform, new[] { monkeyMat }, 1f, true);
                    if (rig == null) return;
                    rig.FaceAhead("head", "tail_2");
                    var a = new Ape { troop = i, k = k, rig = rig, rng = new System.Random(seed * 131 + i * 7919 + k * 613 + 4), st = MS.Sit, seat = JungleFaunaData.TroopSeats[i][k] };
                    a.pos = a.seat; a.yaw = R(a.rng, 150f, 210f); a.idleT = R(a.rng, 1f, 7f); a.t = R(a.rng, 0f, 2f);          // they face the camera's side (south)
                    rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                    apes.Add(a);
                }
            }
            MonkeysTotal = apes.Count;
        }

        /// <summary>What frightens the troop of this tree right now: a walking robot within 9 m, a moving vehicle within 13 m, a standing
        /// robot within half the robot distance, a blast within 14 m in the last 1.5 s. Measured flat from the foot of the tree.</summary>
        bool TroopThreat(int troop, out Vector3 at)
        {
            var foot = OnGround(JungleFaunaData.TroopTrees[troop]);
            if (NearestUnit(foot, 1, out at) <= monkeyRobot) return true;
            if (NearestUnit(foot, 2, out at) <= monkeyVehicle) return true;
            if (ShipNear(foot, 16f, out var sAt)) { at = sAt; return true; }                     // DV118: the shuttle in the air, hovering too
            if (NearestUnit(foot, 3, out at) <= monkeyRobot * 0.5f) return true;
            if (Time.time - lastBlastT < 1.5f && Flat(lastBlast, foot) < monkeyBlast) { at = lastBlast; return true; }
            return false;
        }

        /// <summary>The way of the troop that leads most directly away from the threat and ends 14 m or more from it (else the one that
        /// leads away most).</summary>
        Vector2[] PickWay(int troop, Vector3 threat)
        {
            var ways = JungleFaunaData.TroopWays[troop]; if (ways.Length == 0) return null;
            var foot = JungleFaunaData.TroopTrees[troop]; var th = new Vector2(threat.x - foot.x, threat.z - foot.y);
            int best = 0; float bs = float.MinValue;
            for (int i = 0; i < ways.Length; i++)
            {
                var w = ways[i]; var dir = (w[0] - foot).normalized; var end = w[w.Length - 1];
                float score = -Vector2.Dot(dir, th.sqrMagnitude > 0.01f ? th.normalized : Vector2.zero) + (Vector2.Distance(end, new Vector2(threat.x, threat.z)) >= 14f ? 1f : 0f);
                if (score > bs) { bs = score; best = i; }
            }
            return ways[best];
        }

        void StepTroops(float dt)
        {
            int sitting = 0;
            for (int i = 0; i < troopGone.Length; i++)
            {
                if (troopGone[i]) continue;
                if (!TreeStands(JungleFaunaData.TroopTiles[i]))
                {   // their tree was felled: they jump and do not come back
                    troopGone[i] = true;
                    foreach (var a in apes) if (a.troop == i && (a.st == MS.Sit || a.st == MS.Climb)) Flee(a, a.seat + Vector3.right * 3f, 0f, true);
                    continue;
                }
                if ((troopCheckT[i] -= dt) > 0f) continue;
                troopCheckT[i] = 0.2f;                                                               // five looks a second, like the game's other critters
                if (!TroopThreat(i, out var at)) continue;
                float delay = 0f; bool any = false;
                foreach (var a in apes)
                {
                    if (a.troop != i) continue;
                    if ((a.st == MS.Sit || a.st == MS.Climb || a.st == MS.Back) && !ApeHidden(a)) { Flee(a, at, delay, false); delay += R(a.rng, 0.2f, 0.6f); any = true; }
                    else if (a.st == MS.Gone) a.quiet = 0f;                                         // not quiet yet
                }
                if (any) MonkeyRuns++;
            }
            foreach (var a in apes)
            {
                StepApe(a, dt);
                if (a.st == MS.Sit) sitting++;
            }
            MonkeysSitting = sitting;
            // a far call from a sitting troop every 20 to 45 s, one in two heard, by day
            for (int i = 0; i < troopCallT.Length; i++)
            {
                if ((troopCallT[i] -= dt) > 0f) continue;
                troopCallT[i] = 20f + ((i * 37 + (int)clock) % 26);
                var foot = OnGround(JungleFaunaData.TroopTrees[i]);
                // the idle troop from close by coos softly (the camera near its tree); from farther away its far call is heard
                if (!troopGone[i] && !Night && Seen(foot, 4f) && cam && (cam.transform.position - foot).sqrMagnitude < 55f * 55f) Snd("critter_monkey_coo", foot + Vector3.up * 4f, 0.4f, 6f, 60f, "sd_monkey_coo_a", "sd_monkey_coo_b");
                else if (!troopGone[i] && !Night && ((int)clock & 1) == 0 && Seen(foot, 30f)) Snd("amb_l16_monkey_far", foot + Vector3.up * 4f, 0.45f, 20f, 150f, "sd_monkey_far_a", "sd_monkey_far_b", "sd_monkey_far_c");
            }
        }

        void Flee(Ape a, Vector3 threat, float delay, bool forGood)
        {
            a.way = PickWay(a.troop, threat);
            if (a.way == null) return;
            bool fromGround = a.st == MS.Back || (a.st == MS.Climb && a.pos.y - Ground(a.pos.x, a.pos.z) < 1.2f);
            a.side = new Vector2(R(a.rng, -0.25f, 0.25f), R(a.rng, -0.25f, 0.25f));
            if (fromGround) { a.st = MS.Run; a.leg = 0; a.legT = 0f; a.from = a.pos; }
            else { a.st = MS.Alarm; a.t = 0f; a.delay = delay; a.from = a.pos; }
            a.awayT = forGood ? float.PositiveInfinity : R(a.rng, 40f, 70f); a.quiet = 0f;
        }

        void StepApe(Ape a, float dt)
        {
            bool vis = Seen(a.pos, 4f);
            switch (a.st)
            {
                case MS.Sit:
                {
                    if (HideApe(a, dt)) break;                                                      // night or rain: up into the crown, down after
                    a.t += dt; bool fresh = false;
                    if (!Night && (a.idleT -= dt) <= 0f) { a.idleT = R(a.rng, 4f, 9f); a.look = a.rng.Next(2) == 0; a.t = 0f; fresh = true; }
                    // a unit within twice the fright distance: it stops scratching and watches it (the tell before it runs)
                    float dWatch = NearestUnit(a.seat, 0, out var at);
                    if (dWatch < monkeyRobot * 2f) { a.look = true; var d = at - a.pos; a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 3f)); }
                    if (fresh && !a.look && vis) Snd("critter_monkey_scratch", a.seat, 0.25f, 3f, 30f);        // it starts to scratch; heard only close by. No file yet: a named silent event (MISSING_SOUNDS.md)
                    a.rig.root.SetPositionAndRotation(a.seat, Quaternion.Euler(0f, a.yaw, 0f));
                    PoseFrames(a.rig, a.look ? Loop(a.rig, LookMA, LookMB, a.t) : Loop(a.rig, SitA, SitB, a.t), 0f, 0f);
                    break;
                }
                case MS.Alarm:
                    if ((a.delay -= dt) > 0f) { PoseFrames(a.rig, Loop(a.rig, LookMA, LookMB, clock), 0f, 0f); break; }
                    if (a.t == 0f) { Snd("critter_monkey_alarm", a.pos, 0.8f, 10f, 90f); a.chatterAt = Random.value < 0.34f ? Random.Range(0.2f, 0.6f) : 0f; }
                    a.t += dt;
                    if (a.chatterAt > 0f && a.t >= a.chatterAt) { a.chatterAt = 0f; Snd("critter_monkey_chatter", a.pos, 0.6f, 8f, 80f, "sd_monkey_chatter_a", "sd_monkey_chatter_b"); }   // one in three chatters after the alarm call
                    PoseFrames(a.rig, Once(a.rig, AlarmA, AlarmB, a.t), 0f, 0f);
                    if (a.t >= 1f)
                    {
                        a.st = MS.Jump; a.t = 0f; a.from = a.pos;
                        Snd("critter_monkey_jump", a.pos, 0.5f, 6f, 60f, "sd_monkey_branch_spring_a", "sd_monkey_branch_spring_b");   // the limb springs back, leaves shake
                    }
                    break;
                case MS.Jump:
                {
                    // an arc from the seat to the landing place of its way, 1 s
                    a.t += dt; float k = Mathf.Clamp01(a.t);
                    var land = OnGround(a.way[0] + a.side);
                    var p = Vector3.Lerp(a.from, land, k); p.y += Mathf.Sin(k * Mathf.PI) * 1.1f;
                    a.pos = p; var d = land - a.from; a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 8f));
                    a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                    PoseFrames(a.rig, Once(a.rig, JumpA, JumpB, a.t), 0f, 0f);
                    if (k >= 1f)
                    {
                        a.st = MS.Run; a.leg = 0; a.legT = 0f; a.from = a.pos;
                        Snd("critter_monkey_land", a.pos, 0.5f, 5f, 50f);                                       // no file yet: a named silent event
                        Snd("critter_monkey_run", a.pos, 0.35f, 5f, 45f);                                       // no file yet: a named silent event (the patter of its run)
                        if (vis && OpenZFxPlayer.I) OpenZFxPlayer.I.EmitChannel("m12_dust", a.pos + Vector3.up * 0.1f, Vector3.up * 0.3f, 0.6f, 0.5f, new Color(0.5f, 0.45f, 0.32f), 0.35f);
                    }
                    break;
                }
                case MS.Run:
                case MS.Back:
                {
                    // along its way, point to point (the way was laid out on walkable ground and checked: no water, no rock, no building)
                    bool back = a.st == MS.Back; float speed = back ? 4f : 6.5f;
                    int n = a.way.Length, next = back ? a.leg - 1 : a.leg + 1;
                    if (next < 0 || next >= n)
                    {
                        if (!back) { a.st = MS.Gone; a.rig.Show(false); a.quiet = 0f; a.t = 0f; }                 // under the crowns, or off the map
                        else { a.st = MS.Climb; a.t = 0f; a.from = a.pos; }
                        break;
                    }
                    var to = OnGround(a.way[next] + (next == n - 1 ? Vector2.zero : a.side));
                    var d = to - a.pos; d.y = 0f; float l = d.magnitude;
                    if (l < speed * dt) { a.pos = to; a.leg = next; }
                    else { a.pos += d / l * speed * dt; a.pos.y = Ground(a.pos.x, a.pos.z); a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 10f)); }
                    a.t += dt;
                    a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                    PoseFrames(a.rig, Loop(a.rig, RunA, RunB, a.t, back ? 0.8f : 1.2f), 0f, 0f);
                    break;
                }
                case MS.Gone:
                {
                    // back after 40 to 70 quiet seconds: no unit and no blast within 14 m of its tree
                    var foot = OnGround(JungleFaunaData.TroopTrees[a.troop]);
                    bool calm = NearestUnit(foot, 0, out _) > 14f && !(Time.time - lastBlastT < 1.5f && Flat(lastBlast, foot) < monkeyBlast);
                    a.quiet = calm ? a.quiet + dt : 0f; a.t += dt;
                    if (a.quiet >= a.awayT && !troopGone[a.troop] && !hush)
                    {
                        ReturnAfter.Add(a.quiet); MonkeyReturns++;                                // the quiet seconds it waited
                        a.st = MS.Back; a.leg = a.way.Length - 1; a.pos = OnGround(a.way[a.leg]); a.rig.Show(true); a.t = 0f;
                    }
                    break;
                }
                case MS.Climb:
                {
                    // to the foot of the trunk, then up to its seat at 1.5 m/s
                    a.t += dt;
                    var foot = OnGround(JungleFaunaData.TroopTrees[a.troop]); var under = foot;
                    float walk = Flat(a.from, under) / 4f, up = Mathf.Max(0.1f, (a.seat.y - foot.y) / 1.5f);
                    if (a.t < walk) { a.pos = Vector3.Lerp(a.from, under, a.t / Mathf.Max(0.01f, walk)); PoseFrames(a.rig, Loop(a.rig, RunA, RunB, a.t, 0.8f), 0f, 0f); }
                    else { a.pos = Vector3.Lerp(under, a.seat, Mathf.Clamp01((a.t - walk) / up)); PoseFrames(a.rig, Loop(a.rig, ClimbA, ClimbB, a.t), 0f, 0f); if (a.t - walk < dt * 1.5f) Snd("critter_monkey_climb", a.pos, 0.4f, 5f, 40f); }
                    a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                    if (a.t >= walk + up) { a.st = MS.Sit; a.pos = a.seat; a.t = 0f; a.idleT = R(a.rng, 2f, 6f); }
                    break;
                }
            }
        }

        /// <summary>Checks: where every monkey that is drawn is right now, and what it does (0 sits, 1 alarm, 2 jump, 3 runs, 4 gone, 5 runs back, 6 climbs).</summary>
        public static void Monkeys(List<Vector3> pos, List<int> state)
        {
            pos.Clear(); state.Clear();
            if (!I) return;
            foreach (var a in I.apes) { pos.Add(a.pos); state.Add((int)a.st); }
        }

        // ================================================================== snakes
        const int SlitherA = 1, SlitherB = 25, SwimA = 31, SwimB = 63, RearA = 71, RearB = 131;
        enum SS { Den, Out, Rest, Rear, Home, Swim, SwimBack, Far }
        sealed class Snake
        {
            public int id; public L16AnimalKit.Rig rig; public System.Random rng; public SS st; public float t, timer, yaw, ringT; public Vector3 pos; public int leg, dir = 1; public Vector3 threat; public bool hissed;
        }
        readonly List<Snake> snks = new List<Snake>();
        float flatWater;

        void MakeSnakes()
        {
            flatWater = DetailKit.WaterY(game.level);
            for (int i = 0; i < JungleFaunaData.Dens.Length; i++)
            {
                var rig = L16AnimalKit.Rig.Make("JG_Snake", "Snake" + i, transform, new[] { snakeMat }, 1f, true);
                if (rig == null) return;
                rig.FaceAhead("head", "body_01");
                var s = new Snake { id = i, rig = rig, rng = new System.Random(seed * 17 + i * 15485863 + 6), st = SS.Den, timer = 3f + i * 7f };
                s.pos = OnGround(JungleFaunaData.Dens[i]); rig.Show(false);
                snks.Add(s);
            }
            SnakesTotal = snks.Count;
        }

        float WaterAt(float x, float z) => OzWaterLevel.At(game.level, x, z, flatWater);
        static bool HasSwim(int den) => !float.IsNaN(JungleFaunaData.DenSwims[den].x);

        void MoveSnake(Snake s, Vector3 to, float speed, float dt, bool water)
        {
            var d = to - s.pos; d.y = 0f; float l = d.magnitude;
            if (l > 1e-4f) { s.pos += d / l * Mathf.Min(l, speed * dt); s.yaw = Mathf.LerpAngle(s.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 5f)); }
            s.pos.y = water && OpenZFxPlayer.IsWaterTile(s.pos) ? WaterAt(s.pos.x, s.pos.z) - 0.03f : Ground(s.pos.x, s.pos.z);
            s.t += dt;
            s.rig.root.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
        }

        void StepSnakes(float dt)
        {
            int outN = 0, swimN = 0;
            foreach (var s in snks) if (s.st == SS.Swim || s.st == SS.SwimBack) swimN++;
            foreach (var s in snks)
            {
                var den = OnGround(JungleFaunaData.Dens[s.id]); var walk = JungleFaunaData.DenWalks[s.id];
                bool vis = Seen(s.pos, 3f);
                if (s.st != SS.Den && s.st != SS.Swim && s.st != SS.SwimBack && s.st != SS.Rear && s.st != SS.Home)
                {   // a unit comes near: it rears up and hisses
                    float d = NearestUnit(s.pos, 0, out var at);
                    if (d <= snakeAlert) { s.st = SS.Rear; s.t = 0f; s.threat = at; s.hissed = false; }
                }
                switch (s.st)
                {
                    case SS.Den:
                        if ((s.timer -= dt) <= 0f)
                        {
                            if (NearestUnit(den, 0, out _) < 14f) { s.timer = R(s.rng, 5f, 12f); break; }     // out only when it is calm
                            s.st = SS.Out; s.leg = 0; s.dir = 1; s.pos = den; s.t = 0f; s.rig.Show(true); s.yaw = R(s.rng, 0f, 360f);
                            if (vis) Snd("amb_l16_snake_slither", s.pos, 0.35f, 4f, 40f, "sd_snake_slither_a", "sd_snake_slither_b");
                        }
                        break;
                    case SS.Out:
                    {
                        int next = s.leg + s.dir;
                        if (next < 0 || next >= walk.Length)
                        {   // an end of its walk: rest; at the den end it goes in, or into the water if it has water to cross and two are not in already
                            if (s.leg == 0 && (s.dir < 0 || walk.Length <= 1))
                            {
                                if (HasSwim(s.id) && swimN < maxSwimming && s.rng.Next(2) == 0)
                                {
                                    s.st = SS.Swim; s.t = 0f; swimN++;
                                    Snd("critter_snake_water_in", s.pos, 0.4f, 4f, 40f);                      // no file yet: a named silent event
                                    Snd("critter_snake_swim", s.pos, 0.3f, 4f, 40f);                          // no file yet: a named silent event
                                }
                                else { s.st = SS.Den; s.rig.Show(false); s.timer = R(s.rng, 15f, 40f); }
                            }
                            else { s.st = SS.Rest; s.timer = R(s.rng, 2f, 6f); s.dir = -s.dir; }
                            break;
                        }
                        var to = OnGround(walk[next]);
                        MoveSnake(s, to, 0.7f, dt, false);
                        PoseFrames(s.rig, Loop(s.rig, SlitherA, SlitherB, s.t), 0f, 0f);
                        if (Flat(s.pos, to) < 0.05f) s.leg = next;
                        break;
                    }
                    case SS.Rest:
                        PoseFrames(s.rig, T(s.rig, SlitherA), 0f, 0f);
                        if ((s.timer -= dt) <= 0f) { s.st = walk.Length > 1 ? SS.Out : SS.Home; if (walk.Length <= 1) s.dir = -1; s.t = 0f; }
                        break;
                    case SS.Rear:
                    {
                        s.t += dt;
                        var d = s.threat - s.pos; s.yaw = Mathf.LerpAngle(s.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 6f));
                        s.rig.root.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
                        PoseFrames(s.rig, Once(s.rig, RearA, RearB, s.t), 0f, 0f);
                        if (!s.hissed && s.t > 0.3f) { s.hissed = true; Hisses++; Snd("amb_l16_snake_hiss", s.pos, 0.6f, 4f, 60f, "sd_snake_hiss_jungle_a", "sd_snake_hiss_jungle_b"); }
                        if (s.t >= (RearB - RearA) / 24f) { s.st = SS.Home; s.t = 0f; }
                        break;
                    }
                    case SS.Home:
                    {
                        // back along its walk to the den, quickly
                        if (s.leg > 0)
                        {
                            var to = OnGround(walk[s.leg - 1]);
                            MoveSnake(s, to, 1.6f, dt, false);
                            if (Flat(s.pos, to) < 0.05f) s.leg--;
                        }
                        else
                        {
                            MoveSnake(s, den, 1.6f, dt, false);
                            if (Flat(s.pos, den) < 0.08f) { s.st = SS.Den; s.rig.Show(false); s.timer = R(s.rng, 20f, 40f); break; }
                        }
                        PoseFrames(s.rig, Loop(s.rig, SlitherA, SlitherB, s.t, 2f), 0f, 0f);
                        break;
                    }
                    case SS.Swim:
                    case SS.SwimBack:
                    {
                        // low in the water, head up, 0.6 m/s, a small ring behind it now and then
                        var far = OnGround(JungleFaunaData.DenSwims[s.id]);
                        var to = s.st == SS.Swim ? far : den;
                        MoveSnake(s, to, 0.6f, dt, true);
                        PoseFrames(s.rig, Loop(s.rig, SwimA, SwimB, s.t), 0f, 0f);
                        if ((s.ringT -= dt) <= 0f) { s.ringT = 0.7f; if (vis && OpenZFxPlayer.I && OpenZFxPlayer.IsWaterTile(s.pos)) OpenZFxPlayer.I.Ripple(new Vector3(s.pos.x, WaterAt(s.pos.x, s.pos.z) + 0.03f, s.pos.z), 0.22f, 1.1f); }
                        if (Flat(s.pos, to) < 0.08f)
                        {
                            if (s.st == SS.Swim) { s.st = OpenZFxPlayer.IsWaterTile(far) ? SS.SwimBack : SS.Far; s.timer = R(s.rng, 5f, 10f); if (s.st == SS.Far) swimN--; }
                            else { s.st = SS.Den; s.rig.Show(false); s.timer = R(s.rng, 20f, 45f); swimN--; }
                        }
                        break;
                    }
                    case SS.Far:
                        // on the far bank: a rest, then back through the water when two are not in already
                        PoseFrames(s.rig, T(s.rig, SlitherA), 0f, 0f);
                        if ((s.timer -= dt) <= 0f) { if (swimN < maxSwimming) { s.st = SS.SwimBack; s.t = 0f; swimN++; } else s.timer = 2f; }
                        break;
                }
                if (s.st != SS.Den) outN++;
            }
            int sw = 0; foreach (var s in snks) if (s.st == SS.Swim || s.st == SS.SwimBack) sw++;
            SnakesOut = outN; SnakesSwimming = sw; if (sw > MaxSnakesSwimming) MaxSnakesSwimming = sw;
        }

        /// <summary>Checks: where every snake that is out is right now, and whether it swims.</summary>
        public static void Snakes(List<Vector3> pos, List<bool> swimming)
        {
            pos.Clear(); swimming.Clear();
            if (!I) return;
            foreach (var s in I.snks) if (s.st != SS.Den) { pos.Add(s.pos); swimming.Add(s.st == SS.Swim || s.st == SS.SwimBack); }
        }

        // ================================================================== frame
        void Update()
        {
            if (W == null) return;
            if (!cam) cam = Camera.main;
            float dt = Time.deltaTime; clock += dt;
            if (dt <= 0f) return;
            StepHush(dt);                                                                       // night or rain: the birds and monkeys keep away (JungleFauna.Hush.cs)
            StepBlast();                                                                        // the fort blast: the parrots flee and keep away (JungleFauna.Blast.cs)
            StepFlyers(dt);
            if (groups.Count > 0) StepGroups(dt);
            if (apes.Count > 0) StepTroops(dt);
            if (tapes.Count > 0) StepTempleApes(dt);                                            // the monkeys on the pyramids (JungleFauna.Temple.cs)
            if (snks.Count > 0) StepSnakes(dt);
            if (flocks.Count > 0) StepFlocks(dt);
        }

        /// <summary>One line per kind (for the checks and for whoever looks).</summary>
        public static string Status()
        {
            if (!I) return "JungleFauna not running (not level 16)";
            return $"parrots in the air {FlyingParrots}; parrot groups {I.groups.Count} ({GroupsAlive} with bird objects, {BirdObjects} birds drawn), take-offs {TakeOffs}, landings {Landings}; " +
                   $"monkeys {MonkeysTotal} ({MonkeysSitting} sitting), runs {MonkeyRuns}, climbs back {MonkeyReturns}; snakes {SnakesTotal} ({SnakesOut} out, {SnakesSwimming} swimming, most at once {MaxSnakesSwimming}), hisses {Hisses}";
        }

        /// <summary>Checks: the middle of every parrot group, whether it sits in a tree, whether it has bird objects, how many.</summary>
        public static void Groups(List<Vector3> centre, List<bool> tree, List<int> birds)
        {
            centre.Clear(); tree.Clear(); birds.Clear();
            if (!I) return;
            foreach (var g in I.groups) { centre.Add(g.centre); tree.Add(g.tree); birds.Add(g.alive ? g.birds.Count : 0); }
        }
    }
}

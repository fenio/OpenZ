using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the two robots' landing shuttle beside the red fort (rule: Sim/ZWorld.L16Shuttle.cs). It does nothing in the game;
    /// this component is the DECORATION's own part: it sits beside the DetailProp of the same object (the detail path binds it to
    /// its map item by the object's name, knocks it on hits and calls the break on the death event) and owns the team colour,
    /// the damaged stage, the destruction and the wreck.
    /// What a ship that flies would keep is no longer in here. The ship is ONE prefab
    /// (Assets/OpenZ/Models/L16/Shuttle/L16_Shuttle.prefab, its root on the ground under the ship's middle, the dome looking
    /// along the root's forward, no place baked in), and on that root sit, each a component of its own:
    ///   OzShuttleLights   the lamps, their ground glow, the window band, the engine mouths, the neon strips (emission only)
    ///   OzShuttleExhaust  the thin exhaust wisp
    ///   TeamPaint         the team colour (the game's own)
    /// This component only tells them the stage (intact, damaged, dark) and the team. The army searchlight beside the ship is an
    /// object of its own (OzFieldSearchlight); the ship takes it along when it is destroyed.
    /// <list type="bullet">
    /// <item>TEAM COLOUR: the panels his reference shows in red carry the team colour of the fort the ship stands beside, through
    /// the game's own TeamPaint (so the player's "your team colour" option swaps the ship with its fort); the neon strips take
    /// their two colours from the same team.</item>
    /// <item>DAMAGED at half the hit points: dark smoke from the hull, the right side's lamp set (as it stands: the east side,
    /// with its ground glow), the blinking neon and the right engine's exhaust dead, the window band dim and stuttering, the
    /// running neon stuttering, the paint scorched, a groan, glass and a small fire catching.</item>
    /// <item>DESTROYED (the rules' death): every light, both ground glows, the neon and the exhaust off, the game's explosion,
    /// the searchlight beside it broken with it, the burnt wreck: both dome halves, both engines and the two front legs fly (the
    /// model's own pieces), the rear body sinks onto the ground on its two rear legs and burns; a scorch mark; after
    /// <see cref="wreckSeconds"/> the wreck goes as all debris of the game goes (its tiles are open again by the rules, nothing
    /// tall may stay on them). The scorch stays.
    /// The six flying pieces are moved here with the numbers of the game's own debris mover (DetailKit.Chunks: gravity, drag,
    /// bounce, half the spin at a bounce, settle flat, the same landing sound), but held on the ground by its OUTLINE (its
    /// outermost points against the ground under them): the pieces are large and their pivot is at their middle, so the game's
    /// mover (which lands a piece when its pivot reaches the ground) would let a dome half sink 2 m into the ground before it settles.</item>
    /// </list>
    /// 'Zod exact' (the rule off): the hut of the map stands there again, the ship and its searchlight are not drawn.
    /// View only. Play check: Stage, LastEvent, PiecesThrown, ShownTeam, lights, exhaust, searchlight.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public sealed class L16Shuttle : MonoBehaviour
    {
        public static L16Shuttle I { get; private set; }
        /// <summary>The outline of a wreck piece: its outermost points in the piece's own space (made by the menu from the mesh).</summary>
        [System.Serializable] public struct PieceShape { public Vector3[] points; }

        [Header("set by the menu OpenZ/L16 Jungle/14")]
        public Renderer hull;
        [Tooltip("the lights and the exhaust wisp: components of their own on this same root (a unit version keeps them)")] public OzShuttleLights lights;
        public OzShuttleExhaust exhaust;
        [Tooltip("the army searchlight beside the ship (an object of its own): broken with the ship, hidden in 'Zod exact'")] public OzFieldSearchlight searchlight;
        [Tooltip("the wreck that stays for a while: rear body and rear legs (child RUIN_Wreck, shown by the detail's break)")] public Transform wreck;
        [Tooltip("the wreck pieces that fly: dome halves, engines, front legs (renderers off until the ship is destroyed)")] public Transform[] pieces = new Transform[0];
        [Tooltip("the outermost points of each flying piece, same order as pieces (the piece lands on its lowest point)")] public PieceShape[] pieceShapes = new PieceShape[0];
        public Transform smokeAt, topAt;
        [Tooltip("the hut of the map this ship replaces (kept in the scene, inactive; shown again in 'Zod exact')")] public GameObject replacedHut;
        [Tooltip("plants the menu switched off under the ship (switched on again when the menu runs again)")] public GameObject[] hiddenPlants = new GameObject[0];
        [Header("damage")]
        public float wreckSeconds = 16f;
        public float scorchRadius = 4.5f;
        [Tooltip("the game's explosion at the destruction (FxManager.Explosion), and the demolition dust's width (m)")] public float blast = 2.2f;
        public float dustWidth = 6.3f;

        /// <summary>0 intact, 1 damaged, 2 destroyed.</summary>
        public int Stage { get; private set; }
        public Team ShownTeam { get; private set; }
        public int PiecesThrown { get; private set; }
        /// <summary>Flying pieces that have not come to rest yet.</summary>
        public int PiecesInFlight { get { int n = 0; for (int i = 0; i < flyN; i++) if (!fly[i].done && !fly[i].settled) n++; return n; } }
        public bool RuleOff { get; private set; }
        public string LastEvent { get; private set; } = "";
        public readonly List<string> Sounds = new List<string>();

        static readonly int EmMapColorId = Shader.PropertyToID("_EmissionMapColor");
        DetailProp dp; TeamPaint paint; MaterialPropertyBlock mpb;
        float nextPoll, nextSmoke, nextTeam, burnUntil; bool ruleKnown;

        struct Fly { public Transform t; public Vector3 v, w; public Vector3[] pts; public float age, life, rest, reach; public bool landed, settled, done; public Quaternion r0, r1; }
        Fly[] fly = new Fly[0]; int flyN;

        void OnEnable() { I = this; }
        void OnDisable() { if (I == this) I = null; }

        void Awake()
        {
            dp = GetComponent<DetailProp>(); paint = GetComponent<TeamPaint>();
            if (!lights) lights = GetComponent<OzShuttleLights>(); if (!exhaust) exhaust = GetComponent<OzShuttleExhaust>();
            mpb = new MaterialPropertyBlock();
            dp.HpOwned = false;
            dp.Breaking += OnBreaking; dp.BrokenBy += OnBroken;
            foreach (var p in pieces) if (p) foreach (var r in p.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        void OnDestroy()
        {
            if (dp) { dp.Breaking -= OnBreaking; dp.BrokenBy -= OnBroken; }
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            KnowRule();
            if (!RuleOff) ApplyTeam(true);
        }

        /// <summary>'Zod exact': the rule is off, the hut of the map is in the rules again: show it and do not draw the ship. Asked
        /// once, as soon as the game has its rules (at the start, else in the first frames).</summary>
        void KnowRule()
        {
            var g = ZGame.I; if (ruleKnown || !g || g.World == null) return;
            ruleKnown = true;
            if (OzPlayableShuttle.TakeOver(this, g)) return;                              // DV118: the playable shuttle (or the option SHUTTLE off) takes the scene's ship over (View/OzPlayableShuttle.cs)
            if (g.World.L16Shuttle != null)
            {   // item 174: the hut beside the start camp is out of the rules: its scene object is not drawn (it stands again in 'Zod exact')
                if (g.World.L16CampHutRemoved)
                {
                    var zod = GameObject.Find("ZodObjects"); var ch = zod ? zod.transform.Find("hut_" + ZWorld.L16CampHutTile[0] + "_" + ZWorld.L16CampHutTile[1]) : null;
                    if (ch) { ch.gameObject.SetActive(false); Debug.Log("[OpenZ] L16 shuttle: the hut " + ch.name + " beside the start camp is not drawn (out of the rules)"); }
                }
                return;
            }
            RuleOff = true;
            if (replacedHut) replacedHut.SetActive(true);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (lights) { lights.SetMode(2); lights.enabled = false; }
            if (exhaust) { exhaust.SetMode(2); exhaust.enabled = false; }
            if (searchlight) searchlight.gameObject.SetActive(false);
            dp.intact = true; dp.loopEvent = ""; dp.loopRes = ""; dp.StopLoop();           // the detail starts its idle loop in its own Start (after this one): with no loop named it starts none
            LastEvent = "the rule is off: the hut stands, the ship and its searchlight are not drawn";
            Debug.Log("[OpenZ] L16 shuttle: " + LastEvent);
        }

        /// <summary>The team of the fort nearest to the ship (the rules' owner; TeamPaint draws it in the colour the player chose).</summary>
        public Team FortTeam()
        {
            var g = ZGame.I; if (!g || g.World == null) return paint ? paint.Team : Team.Red;
            Team best = paint ? paint.Team : Team.Red; double bd = double.MaxValue;
            double px = transform.position.x * 8.0, py = -transform.position.z * 8.0;                // 1 tile = 2 m = 16 px
            foreach (var o in g.World.Objects)
            {
                if (o.Kind != ZKind.Building || !o.IsFort) continue;
                double dx = o.CenterX - px, dy = o.CenterY - py, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = o.Owner; }
            }
            return best;
        }

        void ApplyTeam(bool force)
        {
            if (!paint) return;
            var t = FortTeam();
            if (force || t != paint.Team) paint.Team = t;
            ShownTeam = TeamColors.Shown(t);
            if (lights) lights.SetTeam(ShownTeam);
        }

        /// <summary>The game clock in seconds (OzShuttleLights.GameClock: the rules' time).</summary>
        public static double GameClock() => OzShuttleLights.GameClock();

        void Update()
        {
            if (!Application.isPlaying) return;
            if (!ruleKnown) { KnowRule(); if (ruleKnown && !RuleOff) ApplyTeam(true); }
            if (RuleOff) return;
            var g = ZGame.I;
            if (Stage < 2)
            {
                if (dp) dp.SetLoopLevel(Stage == 1 ? 0.35f : 0.6f + 0.4f * (lights ? lights.Engine : 0.5f));      // (an idle sound, when one is set: none today)
            }
            else if (flyN > 0) StepPieces(g && g.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * (g ? Mathf.Clamp(g.gameSpeed, 0.25f, 2f) : 1f));   // the time step of the game's debris mover
            if (Time.time >= nextTeam) { nextTeam = Time.time + 2f; if (Stage < 2) ApplyTeam(false); }
            if (Stage == 0 && Time.time >= nextPoll && dp && dp.SimBacked && g && g.World != null)
            {
                nextPoll = Time.time + 0.2f;
                var o = g.World.Get(dp.SimId);
                if (o != null && o.MaxHealth > 0 && o.Health > 0 && o.Health <= o.MaxHealth / 2) Damage();
            }
            bool paused = g && g.paused;
            if (!paused && Time.time >= nextSmoke && (Stage == 1 || (Stage == 2 && Time.time < burnUntil)))
            {
                nextSmoke = Time.time + 0.25f;
                var fx = OpenZFxPlayer.I;
                if (fx)
                {
                    var at = Stage == 1 ? (smokeAt ? smokeAt.position : transform.position + Vector3.up * 4.5f) : (wreck ? wreck.position + Vector3.up * 2f : transform.position + Vector3.up * 2f);
                    fx.WreckSmoke(at, Stage == 1 ? 2.8f : 3.5f, Stage == 2);
                }
            }
        }

        void Fire(string ev, Vector3 at, float vol, float pitch = 1f)
        {
            if (AudioDirector.Has(ev)) { AudioDirector.Fire(ev, at, dp ? dp.SimId : -1, vol, pitch); Sounds.Add(ev); }
            else OzSoundLog.Add("L16", ev, null, "DROP no event", 0f, at);
        }

        /// <summary>Stage 1 (also callable by a check): half the hit points are gone.</summary>
        public void Damage()
        {
            if (Stage >= 1 || RuleOff) return;
            Stage = 1;
            if (lights) lights.SetMode(1); if (exhaust) exhaust.SetMode(1);
            var top = smokeAt ? smokeAt.position : transform.position + Vector3.up * 4.5f;
            if (paint) paint.Charred = 0.38f;                                             // scorched paint and hull (the game's own char)
            if (FxManager.I) { FxManager.I.Pop(top, 1.4f); FxManager.I.Dust(top, 0.9f); }
            ScorchDecals.Add(transform.position + transform.rotation * new Vector3(1.8f, 0f, 2f), 1.9f, ScorchKind.Contact);
            Fire("dst_structure_groan", top, 0.8f, 1.15f); Fire("dst_glass", top, 0.7f); Fire("dst_fire_ignite", top, 0.9f);
            LastEvent = name + ": damaged stage at " + Time.time.ToString("0.0") + " s";
            Debug.Log("[OpenZ] L16 shuttle " + LastEvent);
        }

        /// <summary>The detail's break starts (the rules destroyed the ship): lights off, the game's explosion.</summary>
        void OnBreaking(DetailProp d, Vector3 from)
        {
            if (Stage >= 2) return;
            Stage = 2;
            if (lights) lights.SetMode(2);                                                // every light part and the neon dark, both ground glows off
            if (exhaust) exhaust.SetMode(2);                                              // no exhaust from a wreck (it has its own smoke)
            if (paint) paint.Charred = 0.85f;                                             // burnt: hull, team paint and every wreck piece
            var c = transform.position + Vector3.up * 2.5f;
            if (FxManager.I)
            {
                FxManager.I.Explosion(c, blast);
                FxManager.I.DemolitionDust(transform.position, dustWidth, dustWidth, 0.6f);
            }
            if (searchlight) searchlight.BreakWith(transform.position);                   // the blast takes the searchlight beside it along
            ScorchDecals.Add(transform.position, scorchRadius, ScorchKind.Wreck);
            Fire("dst_collapse_medium", c, 0.9f); Fire("dst_glass", c, 0.8f, 0.9f);
            burnUntil = Time.time + wreckSeconds;
        }

        /// <summary>The detail's break is done (the ship's own parts are hidden, the wreck is shown): the pieces fly, the body settles.</summary>
        void OnBroken(DetailProp d, Vector3 from)
        {
            var c = transform.position + Vector3.up * 1.5f;
            fly = new Fly[pieces.Length]; flyN = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i]; if (!p) continue;
                foreach (var r in p.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; Dark(r); }
                var away = p.position - c; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = p.position - from; away.y = 0f;
                away = away.sqrMagnitude < 0.01f ? Vector3.forward : away.normalized;
                bool small = p.name.Contains("Leg");
                var v = away * Random.Range(small ? 3.5f : 2.2f, small ? 6f : 3.8f) + Vector3.up * Random.Range(small ? 4.5f : 3.2f, small ? 7f : 5f);
                var w = Random.onUnitSphere * Random.Range(small ? 160f : 60f, small ? 420f : 150f);            // heavy parts turn slowly
                var pts = Outline(i, p); float reach = 0f; foreach (var q in pts) reach = Mathf.Max(reach, q.magnitude);
                p.SetParent(transform.parent, true);
                fly[flyN++] = new Fly { t = p, v = Shorten(p.position, v, reach), w = w, pts = pts, reach = reach, life = wreckSeconds + Random.Range(-1.5f, 1.5f) };
                PiecesThrown++;
            }
            if (wreck)
            {
                foreach (var r in wreck.GetComponentsInChildren<Renderer>(true)) Dark(r);
                StartCoroutine(Settle());
            }
            Fire("dst_debris_metal", c, 1f, 0.85f);
            LastEvent = name + ": destroyed at " + Time.time.ToString("0.0") + " s, " + PiecesThrown + " pieces thrown";
            Debug.Log("[OpenZ] L16 shuttle " + LastEvent);
        }

        /// <summary>A wreck part never glows.</summary>
        void Dark(Renderer r)
        {
            r.GetPropertyBlock(mpb, 0); mpb.SetVector(EmMapColorId, new Vector4(0f, 0f, 0f, 1f)); r.SetPropertyBlock(mpb, 0);
        }

        // ------------------------------------------------------------------------------------------------ the six flying pieces
        /// <summary>A piece's outermost points in its own space: the menu's list, else the eight corners of its mesh box.</summary>
        Vector3[] Outline(int i, Transform p)
        {
            if (pieceShapes != null && i < pieceShapes.Length && pieceShapes[i].points != null && pieceShapes[i].points.Length >= 4) return pieceShapes[i].points;
            var mf = p.GetComponent<MeshFilter>(); var b = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            var c = new Vector3[8];
            for (int k = 0; k < 8; k++) c[k] = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
            return c;
        }

        /// <summary>How far the piece, as it is drawn now, is clear of the ground: the smallest height of its outline points over
        /// the ground under EACH of them (under 0 = in the ground). Round 2: the terrain is read under every outline point (the
        /// first version took a plane through the ground under the piece's middle; the ship's ground has a shelf with a bend,
        /// and a dome half of the larger ship, 4.4 m tall, lay up to 8 cm in it).</summary>
        static float Clear(Transform t, Vector3[] pts)
        {
            var g = ZGame.I; float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = t.TransformPoint(pts[i]); float d = p.y - (g ? g.TerrainY(p.x, p.z) : 0f);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>DetailKit.Chunks.Flat: the rotation that lays the piece on the face nearest the ground (its yaw kept).</summary>
        static Quaternion Flat(Quaternion r)
        {
            Vector3 best = r * Vector3.up; float bd = Mathf.Abs(best.y);
            var ax = r * Vector3.right; if (Mathf.Abs(ax.y) > bd) { best = ax; bd = Mathf.Abs(ax.y); }
            var az = r * Vector3.forward; if (Mathf.Abs(az.y) > bd) { best = az; }
            if (best.y < 0f) best = -best;
            return Quaternion.FromToRotation(best, Vector3.up) * r;
        }

        /// <summary>True when the point lies inside a standing building's rectangle of the rules, widened by `margin` metres.</summary>
        static bool InBuilding(Vector3 p, float margin)
        {
            var g = ZGame.I; if (!g || g.World == null) return false;
            float px = p.x * 8f, py = -p.z * 8f, m = margin * 8f;                                              // 1 m = 8 px of the rules
            foreach (var o in g.World.Objects)
            {
                if (o.Kind != ZKind.Building || o.Destroyed || o.IsBridge) continue;
                if (px > o.LocX - m && px < o.LocX + o.WidthPix + m && py > o.LocY - m && py < o.LocY + o.HeightPix + m) return true;
            }
            return false;
        }

        /// <summary>A throw towards a building is shortened until the piece comes down in front of it (the fort's wall stands 4 m
        /// from the ship's square): its flight is worked out (up, down from where it starts, a third more for the bounces).</summary>
        static Vector3 Shorten(Vector3 from, Vector3 v, float reach)
        {
            var g = ZGame.I; if (!g) return v;
            var h = new Vector3(v.x, 0f, v.z); if (h.sqrMagnitude < 1e-4f) return v;
            float up = Mathf.Max(0f, v.y), drop = Mathf.Max(0f, from.y - g.TerrainY(from.x, from.z));
            float air = (up + Mathf.Sqrt(up * up + 2f * 9.81f * drop)) / 9.81f;
            for (int k = 0; k < 8; k++)
            {
                if (!InBuilding(from + h * (air * 1.35f), reach + 0.5f)) break;
                h *= 0.7f;
            }
            return new Vector3(h.x, v.y, h.z);
        }

        void StepPieces(float dt)
        {
            if (dt <= 0f) return;
            var g = ZGame.I; var hub = DetailPropHub.I;
            for (int i = 0; i < flyN; i++)
            {
                ref var f = ref fly[i];
                if (f.done || !f.t) continue;
                f.age += dt;
                var p = f.t.position;
                if (!f.landed)
                {
                    f.v.y -= 9.81f * dt; f.v *= 1f - 0.08f * dt;
                    var next = p + f.v * dt;
                    if (InBuilding(next, f.reach * 0.5f) && !InBuilding(p, f.reach * 0.5f)) { f.v.x *= -0.3f; f.v.z *= -0.3f; next = new Vector3(p.x, next.y, p.z); }   // a wall: it drops in front of it
                    p = next; f.t.position = p;
                    f.t.Rotate(f.w * dt, Space.World);
                    if (g && hub && (OpenZFxPlayer.IsWaterTile(p) || OzWaterLanding.OpenWaterAt(p, g.TerrainY(p.x, p.z), out _)))
                    {   // over water the game's own debris mover takes the piece: its splash, its sinking
                        hub.Chunks.Throw(f.t, f.v, f.w, Mathf.Max(2f, f.life - f.age)); f.done = true; continue;
                    }
                    float clear = Clear(f.t, f.pts);
                    if (clear <= 0.02f)
                    {
                        p.y += 0.02f - clear; f.t.position = p;
                        if (f.v.y < -2.2f)
                        {
                            f.v = new Vector3(f.v.x * 0.45f, -f.v.y * 0.3f, f.v.z * 0.45f); f.w *= 0.5f;
                            DetailKit.Sound("dtl_debris_land", p, 0.35f, "debris", 0.12f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
                        }
                        else if (f.v.y <= 0f) { f.landed = true; f.rest = 0f; f.r0 = f.t.rotation; f.r1 = Flat(f.r0); }        // (still rising: a leg starts on the ground, a turning piece may brush it on its way up)
                    }
                }
                else if (!f.settled)
                {   // ease flat, resting on its lowest point all the way
                    f.rest += dt; float k = Mathf.Clamp01(f.rest / DetailKit.Chunks.SettleTime);
                    f.t.rotation = Quaternion.Slerp(f.r0, f.r1, k * k * (3f - 2f * k));
                    p.y += 0.005f - Clear(f.t, f.pts); f.t.position = p;
                    if (k >= 1f) f.settled = true;
                }
                if (f.age >= f.life)
                {   // it goes as the game's debris goes
                    f.done = true; var gone = f.t;
                    PopOut.Begin(gone, PopOut.Style.Debris, () => { if (gone) gone.gameObject.SetActive(false); }, false, true);
                }
            }
        }

        /// <summary>The rear body loses its front legs: it tips nose down onto the ground about its rear feet, a thud, it burns, then it goes.</summary>
        IEnumerator Settle()
        {
            // nose down about the ship's cross axis through the rear feet (the two rear legs are part of this wreck and stay standing)
            Transform pivotT = null; float far = float.NegativeInfinity;
            var fwd = topAt && smokeAt ? Vector3.ProjectOnPlane(topAt.position - smokeAt.position, Vector3.up) : -transform.forward;      // top marker lies in front of the smoke marker
            if (fwd.sqrMagnitude < 1e-4f) fwd = -transform.forward; fwd.Normalize();
            foreach (Transform t in wreck) { float d = Vector3.Dot(t.position - transform.position, -fwd); if (t.name.Contains("Leg") && d > far) { far = d; pivotT = t; } }
            var pivot = pivotT ? new Vector3(pivotT.position.x, transform.position.y, pivotT.position.z) : transform.position - fwd * 2.5f;
            var axis = Vector3.Cross(Vector3.up, fwd).normalized;
            float low = float.PositiveInfinity, reach = 0.1f;
            foreach (var r in wreck.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Leg")) continue;
                var b = r.bounds; float front = Vector3.Dot(b.center - pivot, fwd) + Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), 0f, Mathf.Abs(fwd.z))));
                low = Mathf.Min(low, b.min.y - transform.position.y); reach = Mathf.Max(reach, front);
            }
            float angle = float.IsInfinity(low) ? 0f : Mathf.Clamp(Mathf.Atan2(Mathf.Max(0f, low), reach) * Mathf.Rad2Deg, 0f, 14f);
            float k = 0f, done = 0f;
            while (k < 1f && wreck)
            {
                k = Mathf.Min(1f, k + Time.deltaTime / 0.45f);
                float want = angle * k * k, step = want - done; done = want;
                wreck.RotateAround(pivot, axis, step);
                yield return null;
            }
            if (!wreck) yield break;
            var at = wreck.position + fwd * 1.9f;
            Fire("dst_debris_metal", at, 0.9f, 0.7f);
            if (FxManager.I) FxManager.I.Dust(at, 1.2f);
            yield return new WaitForSeconds(Mathf.Max(2f, wreckSeconds * 0.5f));
            if (wreck) Fire("dst_wreck_creak", wreck.position + Vector3.up, 0.8f);
            yield return new WaitForSeconds(Mathf.Max(2f, wreckSeconds * 0.5f));
            if (wreck) PopOut.Begin(wreck, PopOut.Style.Debris, null, false, true);
        }
    }
}

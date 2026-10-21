using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The rocket fuel cans in the shuttle's hull (a little joke, view only: the rules never see them). On every shuttle death
    /// (OzShuttleBreakup.FuelCans.cs: in the air, on the ground, the dropped ship hitting the ground) 5 to 10 of them fly out of the
    /// broken hull with the pieces: the game's own rocket fuel can (the model Alan and Brad drink from, AnticKit.can, a few of them
    /// the crushed empties, AnticKit.canCrushed) at its real size (<see cref="CanHeightM"/>: tiny next to the ship), each a light
    /// physics body on the debris layer (it touches the world, never units, other debris or the wreck pieces: it passes them like the
    /// chips do). A full can is a capsule along its height, so it lies on its side and rolls a little; a crushed one is a box.
    /// Sounds, per can and spread out: the first ground contact = the can land event (the can clatter and clink takes), a later hard
    /// bounce = the same, softer, a roll on its side = the can roll event (a bounce or a roll only while no first landing waits); never two can sounds within <see cref="SoundSpacing"/> s
    /// (a first landing that cannot sound at once waits up to <see cref="FirstWait"/> s: every can's landing is heard), the loudness
    /// by the contact's speed and lower while many cans sound together. Into open water: the shared splash of a small piece, then it
    /// sinks out of sight. At rest it lies on the ground: a can left floating over the ground with nothing under it, or sunk into it,
    /// glides onto the ground (counted). It goes with the wreck: when the wreck sinks away (its own time after the break) the can sinks
    /// into the ground over the same 3 s, <see cref="FadeLag"/> s after the pieces start. Pooled: a can that is gone is kept hidden
    /// and thrown again by a later death; at most <see cref="MaxLive"/> at once (the oldest goes early).
    /// </summary>
    public sealed class OzFuelCan : MonoBehaviour
    {
        // ------------------------------------------------------------------ numbers (test aids, never saved)
        /// <summary>Switch: false = no cans fly out (the break-up is unchanged).</summary>
        public static bool Enabled = true;
        /// <summary>How many fly out of one death (both inclusive; a seeded pick per death).</summary>
        public static int MinCount = 5, MaxCount = 10;
        /// <summary>The can's height in the world (m): the rocket fuel can model's own size, the can in a robot's hand.</summary>
        public static float CanHeightM = 0.32f;
        /// <summary>The can is drawn this many times its real size, so it reads from the game camera (70 m up: about 10 px long on a
        /// 1080 px screen instead of 7); its body follows the drawn size.</summary>
        public static float ShowScale = 1.5f;
        /// <summary>The full can's drawn height (m).</summary>
        public static float DrawnHeightM => CanHeightM * ShowScale;
        /// <summary>Share of crushed empties among them.</summary>
        public static float CrushedShare = 0.3f;
        /// <summary>Seconds between any two can sounds; a first landing waits up to FirstWait s for its turn.</summary>
        public static float SoundSpacing = 0.06f, FirstWait = 0.9f;
        /// <summary>The wreck sinks away: the cans start their own 3 s sink this long after the pieces start theirs.</summary>
        public static float FadeLag = 0.5f, SinkSeconds = 3f, SinkDepthM = 0.5f;
        public const int MaxLive = 40;
        public const string LandEvent = "dst_fuel_can_land", RollEvent = "dst_fuel_can_roll", FallbackEvent = "rob_antic_can_clatter";

        // ------------------------------------------------------------------ the record (the Play check reads it)
        /// <summary>One death's cans.</summary>
        public sealed class Batch
        {
            public int ship, count, seed, crushed, dryFirst, landedGround, landedOther, inWater, rested, floatFixed, sunkFixed, landHeard, firstHeard, bounceHeard, rollHeard, faded, maxIn100ms;
            public bool air, reported; public string how = "-"; public float bornT, bornU, minGap = 9f, maxGap = -9f, farthest;
            public Vector3 shipVel; public float relOutMax, relUpMin = 99f, relUpMax = -99f, dropH; public bool ownAirThrow;   // K424: how they left the ship (OzShuttleBreakup.FuelCansAir.cs)
            public Vector3 centre; public readonly List<OzFuelCan> cans = new List<OzFuelCan>(); public readonly HashSet<Collider> pieces = new HashSet<Collider>(); public readonly List<float> soundT = new List<float>();
            public bool AllDone { get { foreach (var c in cans) if (c && c.batch == this && c.state < State.Rest) return false; return true; } }
            public int Landed => landedGround + landedOther + inWater;
        }
        public static readonly List<Batch> Batches = new List<Batch>();
        public static Batch LastBatch => Batches.Count > 0 ? Batches[Batches.Count - 1] : null;
        public static int Thrown, Deaths, Landings, Heard, Dropped, Splashes, Faded, Reused, Made, Capped;
        static readonly List<OzFuelCan> pool = new List<OzFuelCan>();
        static float lastSoundT = -9f;
        static Mesh fullMesh; static float unitScale = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pool.Clear(); Batches.Clear(); Thrown = Deaths = Landings = Heard = Dropped = Splashes = Faded = Reused = Made = Capped = 0; lastSoundT = -9f; unitScale = -1f; fullMesh = null; waitingFirst = 0; }

        public enum State { Off = 0, Flying = 1, Landed = 2, Rest = 3, Water = 4, Sinking = 5 }

        // ------------------------------------------------------------------ one can
        Rigidbody rb; Collider col; Transform model; bool crushed; Batch batch; int id;
        public State state;
        float age, life, landedAt = -1f, stillT, lastSound = -9f, firstAt = -1f, firstVol, lastRoll = -9f, sinkT, glideT = -1f;
        int sounds, rolls; bool firstOnGround; Vector3 firstPos, sinkFrom, glideFrom, glideTo, baseScale, startPos; string restOn = "-";
        public bool Crushed => crushed;
        public string RestOn => restOn;
        /// <summary>Its collider (the Play check measures the rest and the drawn size).</summary>
        public Collider Body => col;
        public Batch Of => batch;

        static PhysicsMaterial tin;
        static PhysicsMaterial Tin => tin ? tin : (tin = new PhysicsMaterial("OZ_FuelCan") { bounciness = 0.3f, dynamicFriction = 0.5f, staticFriction = 0.65f, bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average });

        /// <summary>The scale that makes the full can CanHeightM tall (its longest side; the model at scale 1 measured once).</summary>
        static float UnitScale(AnticKit kit)
        {
            if (unitScale > 0f) return unitScale;
            unitScale = 1f;
            var mf = kit && kit.can ? kit.can.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf && mf.sharedMesh)
            {
                fullMesh = mf.sharedMesh;
                var s = Vector3.Scale(mf.sharedMesh.bounds.size, Abs(mf.transform.lossyScale));
                float tall = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (tall > 1e-4f) unitScale = CanHeightM / tall;
            }
            return unitScale;
        }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>A hidden can from the pool (or a new one) of the kind asked; null when the game's can model is not there.</summary>
        static OzFuelCan Take(bool wantCrushed)
        {
            var kit = AnticKit.Get();
            var model = kit ? (wantCrushed && kit.canCrushed ? kit.canCrushed : kit.can) : null;
            if (!model) return null;
            bool isCrushed = model == kit.canCrushed;
            int live = 0; OzFuelCan oldest = null, free = null;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var c = pool[i];
                if (!c) { pool.RemoveAt(i); continue; }                                                   // the level was left: gone with its scene
                if (c.state == State.Off) { if (c.crushed == isCrushed && !free) free = c; continue; }
                live++;
                if (c.state != State.Sinking && (!oldest || c.startOrder < oldest.startOrder)) oldest = c;
            }
            if (live >= MaxLive && oldest) { oldest.StartSink(); Capped++; }
            if (free) { Reused++; return free; }
            return Make(model, isCrushed, kit);
        }

        static int order; int startOrder;

        static OzFuelCan Make(GameObject model, bool isCrushed, AnticKit kit)
        {
            var h = AnticFx.Make(model, isCrushed ? "OZ_FuelCanCrushed" : "OZ_FuelCan", kit.canMat);
            if (!h) return null;
            int layer = BuildingBreaker.DebrisLayer;
            foreach (var t in h.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            float s = UnitScale(kit);
            h.localScale = Vector3.one * (s * ShowScale);                                              // K424: drawn ShowScale x the real can (it reads from the game camera)
            // the collider follows the drawn mesh (in the holder's own space): a capsule along the full can's height (it lies on its
            // side and rolls), a box round the crushed one
            var b = new Bounds(); bool any = false;
            foreach (var mf in h.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = h.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, new Vector3(0.11f, 0.32f, 0.11f) / Mathf.Max(1e-3f, s));
            Collider c;
            var sz = b.size;
            if (!isCrushed)
            {
                var cap = h.gameObject.AddComponent<CapsuleCollider>();
                int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
                float tall = ax == 0 ? sz.x : ax == 1 ? sz.y : sz.z;
                float across = ax == 0 ? Mathf.Max(sz.y, sz.z) : ax == 1 ? Mathf.Max(sz.x, sz.z) : Mathf.Max(sz.x, sz.y);
                cap.direction = ax; cap.center = b.center; cap.radius = across * 0.5f * 0.97f; cap.height = Mathf.Max(tall * 0.98f, cap.radius * 2.05f);
                c = cap;
            }
            else { var box = h.gameObject.AddComponent<BoxCollider>(); box.center = b.center; box.size = sz * 0.94f; c = box; }
            c.sharedMaterial = Tin;
            var body = h.gameObject.AddComponent<Rigidbody>();
            body.mass = isCrushed ? 0.15f : 0.4f; body.linearDamping = 0.05f; body.angularDamping = 0.3f; body.maxAngularVelocity = 30f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var can = h.gameObject.AddComponent<OzFuelCan>();
            can.rb = body; can.col = c; can.model = h; can.crushed = isCrushed; can.baseScale = h.localScale; can.id = h.gameObject.GetInstanceID();
            h.gameObject.SetActive(false); can.state = State.Off;
            pool.Add(can); Made++;
            return can;
        }

        /// <summary>OzShuttleBreakup.FuelCans: one death's cans, each from its place in the hull with its own throw and spin.
        /// dieIn = seconds until the wreck sinks away (its pieces' own wreck time).</summary>
        public static Batch Throw(int ship, string how, bool air, Vector3 centre, IList<Collider> pieceCols, IList<Vector3> at, IList<Quaternion> rot, IList<Vector3> vel, IList<Vector3> spin, IList<bool> crushedPick, float dieIn, int seed)
        {
            var bt = new Batch { ship = ship, how = how, air = air, centre = centre, seed = seed, bornT = Time.time, bornU = Time.unscaledTime };
            Batches.Add(bt); if (Batches.Count > 12) Batches.RemoveAt(0);
            Deaths++;
            if (pieceCols != null) foreach (var pc in pieceCols) if (pc) bt.pieces.Add(pc);
            for (int i = 0; i < at.Count; i++)
            {
                var c = Take(crushedPick[i]);
                if (!c) continue;
                c.Begin(bt, at[i], rot[i], vel[i], spin[i], dieIn + FadeLag + i * 0.03f);                   // active first: a pair ignored while a collider is inactive does not hold
                foreach (var pc in bt.pieces) if (pc && c.col) Physics.IgnoreCollision(c.col, pc, true);     // it passes through this wreck's pieces (the chips' rule, whatever the layer table says)
                bt.cans.Add(c); bt.count++; if (c.crushed) bt.crushed++;
            }
            Thrown += bt.count;
            if (bt.count == 0) Debug.LogWarning("[OpenZ] shuttle #" + ship + ": no rocket fuel cans (the game's can model is not in the antics kit)");
            else Debug.Log($"[OpenZ] shuttle #{ship} ({how}): {bt.count} rocket fuel can(s) fly out of the hull ({bt.crushed} crushed empties; seed {seed})");
            return bt;
        }

        void Begin(Batch b, Vector3 pos, Quaternion rot, Vector3 v, Vector3 w, float dieIn)
        {
            batch = b; age = 0f; life = dieIn; landedAt = -1f; stillT = 0f; lastSound = -9f; ClearFirst(); lastRoll = -9f; sinkT = 0f; glideT = -1f; sounds = 0; rolls = 0; restOn = "-";
            startOrder = ++order;
            transform.SetPositionAndRotation(pos, rot); transform.localScale = baseScale; startPos = pos;
            gameObject.SetActive(true);
            col.enabled = true;
            rb.isKinematic = false; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.angularDamping = 0.3f;
            rb.linearVelocity = v; rb.angularVelocity = w; rb.WakeUp();
            state = State.Flying;
        }

        void Update()
        {
            if (state == State.Off) return;
            var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (firstAt >= 0f) TryFirstSound();
            if (state == State.Sinking) { Sinking(dt); return; }
            if (age >= life) { StartSink(); return; }
            if (state == State.Water) { WaterSink(dt); return; }
            if (glideT >= 0f)
            {
                glideT += dt; transform.position = Vector3.Lerp(glideFrom, glideTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glideT / 0.15f)));
                if (glideT >= 0.15f) glideT = -1f;
            }
            if (state == State.Flying || state == State.Landed)
            {
                var p = transform.position;
                if (OzWaterLanding.OpenWaterNear(p, 0.4f, out float wy, out _) && p.y - DrawnHeightM * 0.5f <= wy) { IntoWater(p, wy); return; }
                if (batch != null) batch.farthest = Mathf.Max(batch.farthest, new Vector2(p.x - batch.centre.x, p.z - batch.centre.z).magnitude);
            }
            if (state == State.Landed && !rb.isKinematic)
            {
                bool still = rb.IsSleeping() || (rb.linearVelocity.sqrMagnitude < 0.015f && rb.angularVelocity.sqrMagnitude < 0.36f);
                stillT = still ? stillT + dt : 0f;
                if (stillT > 0.35f || age - landedAt > 6f) Rest();
            }
            if (state == State.Flying && age > 12f) Rest();                                                  // never seen to land (caught on something): put it on the ground
        }

        void OnCollisionEnter(Collision c)
        {
            if (state != State.Flying && state != State.Landed) return;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (OzWaterLanding.UnderSurface(at)) return;                                                     // its splash is made at the surface
            float n = c.contactCount > 0 ? Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal)) : c.relativeVelocity.magnitude;
            if (state == State.Flying)
            {
                state = State.Landed; landedAt = age; Landings++;
                rb.angularDamping = 1.1f;                                                                     // it rolls a little, then lies
                bool ground = IsGround(c.collider); firstOnGround = ground;
                if (batch != null) { batch.dryFirst++; if (ground) batch.landedGround++; else batch.landedOther++; }
                SetFirst(age); firstPos = at; firstVol = Mathf.Clamp(0.4f + n * 0.08f, 0.4f, 1f);
                TryFirstSound();
                if (n > 3f && WreckFx.OnScreen(at)) { var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, 0.1f); }
                return;
            }
            // a later hard bounce: the same take, softer, skipped when it is not its turn
            if (n < 1.4f || sounds >= 5 || age - lastSound < 0.12f || waitingFirst > 0) return;
            if (PlaySound(LandEvent, at, Mathf.Clamp(0.25f + n * 0.06f, 0.25f, 0.6f))) { if (batch != null) batch.bounceHeard++; }
        }

        void OnCollisionStay(Collision c)
        {
            if (state != State.Landed || rb.isKinematic || rolls >= 2 || age - lastRoll < 0.35f || age - landedAt < 0.15f || waitingFirst > 0) return;
            var hv = rb.linearVelocity; hv.y = 0f;
            if (crushed || rb.angularVelocity.magnitude < 3f || hv.magnitude < 0.35f) return;
            lastRoll = age;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (PlaySound(RollEvent, at, Mathf.Clamp(0.45f + hv.magnitude * 0.1f, 0.45f, 0.8f))) { rolls++; if (batch != null) batch.rollHeard++; }
        }

        /// <summary>A first landing waiting for its turn: while any waits, no bounce or roll takes a turn (every landing is heard first).</summary>
        static int waitingFirst;
        void SetFirst(float at) { if (firstAt < 0f) waitingFirst++; firstAt = at; }
        void ClearFirst() { if (firstAt >= 0f) waitingFirst = Mathf.Max(0, waitingFirst - 1); firstAt = -1f; }

        void TryFirstSound()
        {
            if (age - firstAt > FirstWait) { ClearFirst(); Dropped++; return; }                             // waited its turn too long
            float vol = firstVol * (1f - 0.3f * Mathf.Clamp01((age - firstAt) / FirstWait));
            if (PlaySound(LandEvent, firstPos, vol)) { ClearFirst(); if (batch != null) { batch.landHeard++; batch.firstHeard++; } }
        }

        /// <summary>One can sound, its turn kept (SoundSpacing) and its loudness lower while several cans sound together.</summary>
        bool PlaySound(string ev, Vector3 at, float vol)
        {
            float now = Time.unscaledTime;
            if (now - lastSoundT < SoundSpacing) return false;
            var ad = AudioDirector.I; if (!ad) return false;
            string use = AudioDirector.Has(ev) ? ev : FallbackEvent;
            int crowd = 0; if (batch != null) for (int i = batch.soundT.Count - 1; i >= 0 && now - batch.soundT[i] < 0.3f; i--) crowd++;
            if (crowd >= 3) vol *= 0.7f;
            if (!ad.Play(use, at, id, vol, crushed ? 1.08f : 1f)) return false;
            lastSoundT = now; lastSound = age; sounds++; Heard++;
            if (batch != null)
            {
                batch.soundT.Add(now);
                int inWin = 0; for (int i = batch.soundT.Count - 1; i >= 0 && now - batch.soundT[i] < 0.1f; i--) inWin++;
                batch.maxIn100ms = Mathf.Max(batch.maxIn100ms, inWin);
            }
            return true;
        }

        static bool IsGround(Collider c) => c && (c.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(c));

        /// <summary>At rest: it lies on the ground (its lowest point within 6 cm of it) or on something under it; floating with nothing
        /// under it, or sunk into the ground, it glides onto the ground.</summary>
        void Rest()
        {
            state = State.Rest; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            var g = ZGame.I; var bnd = col.bounds; var p = transform.position;
            float gy = g ? g.GroundY(bnd.center.x, bnd.center.z) : bnd.min.y;
            float gap = bnd.min.y - gy;
            string on = null;
            foreach (var h in Physics.RaycastAll(bnd.center + Vector3.up * 0.3f, Vector3.down, 0.3f + bnd.extents.y + 0.12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!h.collider || h.collider == col) continue;
                if (IsGround(h.collider)) { on = on ?? "ground"; continue; }
                if (batch != null && batch.pieces.Contains(h.collider)) continue;                            // it passes through its own wreck's pieces: never held up by them
                if (h.collider.GetComponentInParent<OzFuelCan>()) continue;                                  // nor by another can (the debris layer ignores itself)
                var piece = h.collider.GetComponentInParent<OzShuttleBreakup>();
                on = piece ? piece.PieceName : h.collider.transform.root.name;
                break;
            }
            if (batch != null) batch.rested++;
            if (gap > 0.06f && (on == null || on == "ground"))
            {   // nothing under it: down onto the ground
                glideFrom = p; glideTo = p + Vector3.down * (gap - 0.005f); glideT = 0f; restOn = "ground (was " + (gap * 100f).ToString("F0") + " cm up)";
                if (batch != null) batch.floatFixed++;
                gap = 0.005f;
            }
            else if (gap < -0.03f)
            {   // into the ground (a crater's rim, a fast landing): up onto it
                glideFrom = p; glideTo = p + Vector3.up * (-gap - 0.005f); glideT = 0f; restOn = "ground (was " + (-gap * 100f).ToString("F0") + " cm in)";
                if (batch != null) batch.sunkFixed++;
                gap = -0.005f;
            }
            else restOn = gap > 0.06f ? "on " + on : "ground";
            if (batch != null) { batch.minGap = Mathf.Min(batch.minGap, gap); if (gap <= 0.06f) batch.maxGap = Mathf.Max(batch.maxGap, gap); }
            Report();
        }

        void IntoWater(Vector3 p, float wy)
        {
            var v = rb.linearVelocity; bool wasLanded = state == State.Landed;
            state = State.Water; sinkT = 0f; sinkFrom = new Vector3(p.x, Mathf.Min(p.y, wy), p.z);
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; col.enabled = false;
            OzWaterLanding.Splash(new Vector3(p.x, wy, p.z), OzWaterSize.Small, "fuel can", v.magnitude, false, id);
            Splashes++; restOn = "water";
            if (batch != null) { batch.inWater++; if (wasLanded) { if (firstOnGround) batch.landedGround--; else batch.landedOther--; } }   // it rolled in after landing: counted once, as in the water
            ClearFirst();
            Report();
        }

        void WaterSink(float dt)
        {
            sinkT += dt; float k = Mathf.Clamp01(sinkT / 1.1f);
            transform.position = sinkFrom + Vector3.down * 0.6f * Mathf.SmoothStep(0f, 1f, k);
            if (k > 0.65f) transform.localScale = baseScale * Mathf.Max(0.001f, 1f - (k - 0.65f) / 0.35f);
            if (k >= 1f) Off();
        }

        /// <summary>It goes with the wreck: into the ground over SinkSeconds, smaller in the last third.</summary>
        void StartSink()
        {
            if (state == State.Sinking || state == State.Off) return;
            state = State.Sinking; sinkT = 0f; sinkFrom = transform.position; glideT = -1f; ClearFirst();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; col.enabled = false;
            Report();
        }

        void Sinking(float dt)
        {
            sinkT += dt; float k = Mathf.Clamp01(sinkT / Mathf.Max(0.1f, SinkSeconds));
            transform.position = sinkFrom + Vector3.down * SinkDepthM * k;
            if (k > 0.66f) transform.localScale = baseScale * Mathf.Max(0.001f, 1f - (k - 0.66f) / 0.34f);
            if (k >= 1f) { Faded++; if (batch != null) batch.faded++; Off(); }
        }

        void Off()
        {
            state = State.Off; transform.localScale = baseScale;
            if (rb) { rb.isKinematic = true; }
            gameObject.SetActive(false);
        }

        /// <summary>Once every can of its death lies (or is gone): one line in the log.</summary>
        void Report()
        {
            var b = batch; if (b == null || b.reported || !b.AllDone) return;
            b.reported = true;
            Debug.Log("[OpenZ] " + Line(b));
        }

        /// <summary>The Play check's line for one death.</summary>
        public static string Line(Batch b)
        {
            if (b == null) return "no shuttle death with rocket fuel cans yet";
            return $"rocket fuel cans of shuttle #{b.ship} ({b.how}, {(b.air ? "in the air" : "on the ground")}): {b.count} thrown ({b.crushed} crushed, seed {b.seed}), " +
                   $"{b.Landed} landed (ground {b.landedGround}, on something {b.landedOther}, water {b.inWater}), {b.rested} at rest, " +
                   $"glided down {b.floatFixed} / up {b.sunkFixed}, rest gap {(b.rested > 0 ? (b.minGap * 100f).ToString("F1") + ".." + (b.maxGap * 100f).ToString("F1") + " cm" : "-")}, " +
                   $"farthest {b.farthest:F1} m (ship {new Vector2(b.shipVel.x, b.shipVel.z).magnitude:F1} m/s, thrown out at most {b.relOutMax:F1} m/s, up {b.relUpMin:F1}..{b.relUpMax:F1} m/s{(b.ownAirThrow ? ", own air tumble" : "")}); sounds: landings heard {b.firstHeard}/{b.dryFirst}, bounces {b.bounceHeard}, rolls {b.rollHeard}, most in 0.1 s {b.maxIn100ms}";
        }

        /// <summary>Play check: return OpenZ.OzFuelCan.Status();</summary>
        public static string Status()
        {
            var sb = new StringBuilder();
            sb.Append($"fuel cans: on {Enabled}, drawn {DrawnHeightM:F2} m (x{ShowScale:F1} the real can), deaths {Deaths}, thrown {Thrown}, landings {Landings}, sounds {Heard} (first landings not heard {Dropped}), splashes {Splashes}, faded {Faded}, pool {pool.Count} (made {Made}, reused {Reused}, capped {Capped})");
            foreach (var b in Batches) sb.Append("\n  ").Append(Line(b));
            return sb.ToString();
        }

        /// <summary>The cans still drawn (proof pictures).</summary>
        public static int Drawn(Batch b) { int n = 0; if (b != null) foreach (var c in b.cans) if (c && c.batch == b && c.state != State.Off && c.gameObject.activeInHierarchy) n++; return n; }
        public static Vector3 Middle(Batch b)
        {
            if (b == null) return Vector3.zero;
            Vector3 s = Vector3.zero; int n = 0;
            foreach (var c in b.cans) if (c && c.batch == b && c.state != State.Off) { s += c.transform.position; n++; }
            return n > 0 ? s / n : b.centre;
        }

        void OnDestroy() { ClearFirst(); pool.Remove(this); }
    }
}

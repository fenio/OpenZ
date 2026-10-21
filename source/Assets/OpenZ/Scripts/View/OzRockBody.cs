using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: one big falling body of physics against the rocks (OzRockSolid.All.cs). Put on a body when it is
    /// made (a vehicle wreck piece or gun base piece, the flipped hull, a robot's ragdoll, a building's heavy chunk, a shuttle piece, the
    /// radar dish), only when its longest side is at least <see cref="BigM"/> and a rock lies within its reach (else nothing is added, no
    /// cost). It:
    /// <list type="bullet">
    /// <item>lets the body's colliders meet the rock colliders (OzRockSolid.Include: they include the rock layer and win the pair) and
    /// keeps the push out of a rock gentle (<see cref="MaxPushOut"/> m/s);</item>
    /// <item>switches the rocks round the body on while it moves (every 0.2 s, as far as it can go before the next look), so they are off
    /// again soon after it rests;</item>
    /// <item>a collider born deep inside a rock (more than <see cref="DeepStart"/>: a wreck against a rock's overhang) ignores that rock
    /// until the two are apart, so it is never thrown out of it; a shallow one is eased out at that gentle speed;</item>
    /// <item>when the body rests (the code that made it makes it kinematic) it notes the rocks it touches and whether it lies ON a rock (then that code
    /// pops it away with a puff at the end instead of sinking it through the rock), and its colliders stop including the rock layer;</item>
    /// <item>when a rock it rests on or leans against goes (the stump replaces the pillar, or the rules remove the rock), it wakes and
    /// falls again (a robot's frozen body drops onto what is under it now), then rests again.</item>
    /// </list>
    /// View only.
    /// </summary>
    [DefaultExecutionOrder(-35)]
    public sealed class OzRockBody : MonoBehaviour
    {
        public enum Kind { WreckPiece = 0, FlipHull = 1, RobotBody = 2, BuildingChunk = 3, ShuttlePiece = 4, Dish = 5 }
        public const int Kinds = 6;
        public static readonly string[] KindNames = { "wreck piece", "flipped hull", "robot body", "building chunk", "shuttle piece", "radar dish" };
        /// <summary>A body counts as big (meets the rocks) from this longest side (m); smaller chips stay as they are.</summary>
        public static float BigM = 1f;
        /// <summary>A body is only made solid against rocks whose middle lies within this distance (m) of where it is made.</summary>
        public static readonly float[] ReachM = { 22f, 14f, 30f, 26f, 30f, 26f };
        /// <summary>On: every big body gets its rock watch, also one made far from any rock (it may slide or roll a long way down a
        /// slope; away from rocks the watch only looks up the rock grid every 0.2 s while it moves and switches nothing on). Off: only
        /// bodies with a rock within <see cref="ReachM"/> (or their throw's range) of where they are made.</summary>
        public static bool EveryBigBody = true;
        /// <summary>The fastest push out of a rock (m/s), a start deeper than this (m) ignores the rock until apart, the touch skin at rest (m).</summary>
        public static float MaxPushOut = 3f, DeepStart = 0.6f, TouchSkin = 0.06f;
        /// <summary>A start deeper than <see cref="DeepStart"/> but no deeper than this (m) is set out of the rock before its first step
        /// (along the shortest way out, never downward); only a deeper one ignores the rock until apart.</summary>
        public static float DeepMoveM = 1.5f;

        // ------------------------------------------------------------------ statistics (Play check), per kind
        public static readonly int[] Added = new int[Kinds], NoRockNear = new int[Kinds], Small = new int[Kinds], RestedN = new int[Kinds], Touching = new int[Kinds],
                                     OnRockN = new int[Kinds], Popped = new int[Kinds], WokenN = new int[Kinds], Dropped = new int[Kinds], StartedIn = new int[Kinds],
                                     StartedDeep = new int[Kinds], DeepAtRest = new int[Kinds], StartedMoved = new int[Kinds];
        static readonly List<OzRockBody> all = new List<OzRockBody>();
        static readonly List<OzRockBody> pendingStart = new List<OzRockBody>();
        static readonly Collider[] hits = new Collider[32];
        public static int Live => all.Count;
        public static IReadOnlyList<OzRockBody> All => all;

        public Kind kind;
        public float Size { get; private set; }
        public bool Rested => rested;
        /// <summary>At rest it lies on a rock (its middle over a rock top, or held up off the ground by one).</summary>
        public bool OnRock { get; private set; }
        public int TouchCount => touch.Count;
        public int HeldCount => held.Count;
        public float Age => age;

        Rigidbody rb; Rigidbody[] rbs = new Rigidbody[0];
        readonly List<Collider> cols = new List<Collider>(4);
        readonly List<int> origInclude = new List<int>(4), origPriority = new List<int>(4);
        readonly List<bool> restOn = new List<bool>(4);
        readonly List<Collider> touch = new List<Collider>(4);
        sealed class Held { public Collider a, b; }
        readonly List<Held> held = new List<Held>(2);
        float age, nextArm, nextHeld, wakeUntil, wokeAt = -1f, dropV, dropLeft;
        bool rested, included, popped, dropping, marker, counted;
        Vector3 restSupport; bool hasSupport;
        System.Func<bool> leaving;

        static int HeavyLayer { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : BuildingBreaker.HeavyLayer; } }

        // ------------------------------------------------------------------ making one
        /// <summary>The body `rb` on `go` (its colliders: every non-trigger collider under `go` on a debris layer) meets the rocks from
        /// now on, when its longest side (`size`, m) is big and a rock lies within its reach. `wakeSeconds` = after this long it is not woken
        /// any more (its maker may be taking it away by then); `leaving` = its maker is taking it away now. Null = nothing added.</summary>
        public static OzRockBody Add(GameObject go, Rigidbody rb, Kind kind, Vector3 size, float wakeSeconds, System.Func<bool> leaving = null)
        {
            if (!go || !rb || !OzRockSolid.AllOn) return null;
            int k = (int)kind;
            float big = Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
            if (big < BigM) { Small[k]++; return null; }
            float v0 = rb.isKinematic ? 0f : rb.linearVelocity.magnitude;                                         // thrown hard: as far as it can fly (v^2 / g) + 8 m
            if (!EveryBigBody && !OzRockSolid.RockWithin(go.transform.position, Mathf.Max(ReachM[k], v0 * v0 / 9.81f + 8f))) { NoRockNear[k]++; return null; }
            var b = go.GetComponent<OzRockBody>(); if (!b) b = go.AddComponent<OzRockBody>();
            b.Init(rb, kind, big, wakeSeconds, leaving);
            return b;
        }

        /// <summary>RobotRagdoll.Launch, after the throw: a robot's ragdoll meets the rocks (when a rock is within reach).</summary>
        public static OzRockBody Ragdoll(GameObject robot, RobotRagdoll rd)
        {
            if (!robot || !rd || !OzRockSolid.AllOn) return null;
            var first = robot.GetComponentInChildren<Rigidbody>(); if (!first) return null;
            float h = 3f; var sk = robot.GetComponentInChildren<SkinnedMeshRenderer>(); if (sk) h = Mathf.Max(1f, sk.bounds.size.y, Mathf.Max(sk.bounds.size.x, sk.bounds.size.z));
            return Add(robot, first, Kind.RobotBody, new Vector3(h, h, h), 12.5f);
        }

        /// <summary>The size of a body made inactive (no renderer bounds yet): its colliders' own shapes, scaled.</summary>
        public static Vector3 ShapeSize(GameObject go)
        {
            var s = Vector3.zero;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                Vector3 z;
                if (c is BoxCollider bc) z = bc.size;
                else if (c is MeshCollider mc && mc.sharedMesh) z = mc.sharedMesh.bounds.size;
                else if (c is CapsuleCollider cc) z = new Vector3(cc.radius * 2f, cc.height, cc.radius * 2f);
                else if (c is SphereCollider sc) z = Vector3.one * sc.radius * 2f;
                else continue;
                var ls = c.transform.lossyScale; z = new Vector3(Mathf.Abs(z.x * ls.x), Mathf.Abs(z.y * ls.y), Mathf.Abs(z.z * ls.z));
                s = Vector3.Max(s, z);
            }
            return s;
        }

        void Init(Rigidbody body, Kind k, float big, float wakeSeconds, System.Func<bool> leave)
        {
            IncludeAll(false);                                                                                   // made again: its colliders as they were first
            kind = k; Size = big; rb = body; leaving = leave; wakeUntil = wakeSeconds; age = 0f; rested = false; popped = false; dropping = false; marker = false;
            rbs = kind == Kind.RobotBody ? GetComponentsInChildren<Rigidbody>(true) : new[] { body };
            cols.Clear(); origInclude.Clear(); origPriority.Clear(); restOn.Clear();
            int light = BuildingBreaker.DebrisLayer, heavy = HeavyLayer;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (!c || c.isTrigger || (c.gameObject.layer != light && c.gameObject.layer != heavy)) continue;
                if (OzRockSolid.IsRock(c)) continue;
                cols.Add(c); origInclude.Add(c.includeLayers.value); origPriority.Add(c.layerOverridePriority); restOn.Add(c.enabled);
            }
            IncludeAll(true);
            foreach (var r in rbs) if (r) r.maxDepenetrationVelocity = Mathf.Min(r.maxDepenetrationVelocity, MaxPushOut);
            if (!all.Contains(this)) all.Add(this);
            if (!counted) { counted = true; Added[(int)kind]++; }
            ArmNow(true);
            if (!pendingStart.Contains(this)) pendingStart.Add(this);
        }

        void IncludeAll(bool on)
        {
            if (included == on) return;
            included = on;
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i]; if (!c) continue;
                if (on) OzRockSolid.Include(c);
                else if ((origInclude[i] & OzRockSolid.Mask) == 0) c.includeLayers = c.includeLayers.value & ~OzRockSolid.Mask;   // only the rock layer goes (another rule's include stays; its raised priority too)
            }
        }

        Vector3 Middle() => rb && rb.gameObject.activeInHierarchy ? rb.worldCenterOfMass : transform.position;

        void ArmNow(bool first)
        {
            float sp = rb && !rb.isKinematic ? rb.linearVelocity.magnitude : 0f;
            if (first) sp = Mathf.Max(sp, 8f);                                                                   // made this frame: its throw is set after this
            OzRockSolid.ArmAround(Middle(), 3f + Size + sp * 0.8f, 1.6f, 1.5f + Size + sp * 0.35f);
        }

        // ------------------------------------------------------------------ OzRockDebris.FixedUpdate: the start (before the first step)
        internal static void StartChecks()
        {
            if (pendingStart.Count == 0) return;
            Physics.SyncTransforms();
            for (int i = 0; i < pendingStart.Count; i++) { var b = pendingStart[i]; if (b) b.StartCheck(); }
            pendingStart.Clear();
        }

        void StartCheck()
        {
            if (OzRockSolid.Layer < 0) return;
            bool any = false;
            foreach (var c in cols)
            {
                if (!c || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                var bb = c.bounds;
                int n = Physics.OverlapBoxNonAlloc(bb.center, bb.extents + Vector3.one * 0.02f, hits, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rc = hits[i]; if (!OzRockSolid.IsRock(rc)) continue;
                    if (!Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out Vector3 outDir, out float d) || d <= 0.01f) continue;
                    any = true;
                    if (d <= DeepStart) continue;                                                                    // eased out at MaxPushOut
                    if (d <= DeepMoveM && outDir.y > -0.3f)
                    {   // born deeper (a wreck against a rock's side): set out of the rock before its first step (never down into the ground)
                        transform.position += outDir * (d + 0.03f); Physics.SyncTransforms(); StartedMoved[(int)kind]++;
                        continue;
                    }
                    Physics.IgnoreCollision(c, rc, true); held.Add(new Held { a = c, b = rc }); StartedDeep[(int)kind]++;
                }
            }
            if (any) StartedIn[(int)kind]++;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (marker) return;
            float dt = Time.deltaTime; age += dt;
            if (dropping) { DropStep(dt); return; }
            if (held.Count > 0 && age >= nextHeld) { nextHeld = age + 0.1f; HeldStep(); }
            bool moving = rb && !rb.isKinematic && rb.gameObject.activeInHierarchy;
            if (moving)
            {
                if (rested)
                {   // woken: by a rock that went (Wake), or by its maker / another rule (it falls and rests again)
                    if (wokeAt < 0f) { rested = false; IncludeAll(true); }
                    else if ((rb.IsSleeping() && Time.time - wokeAt > 0.4f) || Time.time - wokeAt > 4f) { ReFreeze(); return; }
                }
                if (age >= nextArm) { nextArm = age + 0.2f; ArmNow(false); }                              // also while it sleeps: a rock switched off under a sleeping body would drop it
                return;
            }
            if (rb && rb.isKinematic && (!rested || wokeAt >= 0f)) Rest();                                     // its maker froze it (again)
        }

        void HeldStep()
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                var h = held[i];
                bool apart = !h.a || !h.b || !h.a.enabled || !h.b.enabled ||
                             !(Physics.ComputePenetration(h.a, h.a.transform.position, h.a.transform.rotation, h.b, h.b.transform.position, h.b.transform.rotation, out _, out float d) && d > 0.02f);
                if (!apart) continue;
                if (h.a && h.b) Physics.IgnoreCollision(h.a, h.b, false);
                held.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ resting
        /// <summary>Its maker made it kinematic (it rests): the rocks it touches (its colliders' own shapes, a skin larger), whether it
        /// lies ON a rock, and its colliders stop including the rock layer (resting bodies are left to their makers).</summary>
        void Rest()
        {
            rested = true; wokeAt = -1f; touch.Clear(); OnRock = false; hasSupport = false;
            for (int i = 0; i < cols.Count; i++) restOn[i] = cols[i] && cols[i].enabled;
            if (OzRockSolid.Layer >= 0)
            {
                foreach (var c in cols) Touches(c, touch);
                if (touch.Count > 0)
                {
                    var b = BodyBounds();
                    float gy = ZGame.I ? ZGame.I.GroundY(b.center.x, b.center.z) : b.min.y;
                    float best = float.NegativeInfinity;
                    for (int s = 0; s < 5; s++)
                    {   // its middle and four points half way to its sides
                        var o = s == 0 ? b.center : b.center + new Vector3(((s & 1) == 1 ? 0.5f : -0.5f) * b.extents.x, 0f, ((s & 2) == 2 ? 0.5f : -0.5f) * b.extents.z);
                        if (Physics.Raycast(new Vector3(o.x, b.center.y, o.z), Vector3.down, out var hit, b.extents.y + 0.6f, OzRockSolid.Mask, QueryTriggerInteraction.Ignore) && OzRockSolid.IsRock(hit.collider) && hit.point.y > best)
                            best = hit.point.y;
                    }
                    if (best > float.NegativeInfinity && best > gy + 0.05f) { OnRock = true; hasSupport = true; restSupport = new Vector3(b.center.x, best, b.center.z); }
                    else if (b.min.y > gy + 0.15f) { OnRock = true; hasSupport = true; restSupport = new Vector3(b.center.x, b.min.y, b.center.z); }   // held up off the ground by a rock it leans on
                    Touching[(int)kind]++; if (OnRock) OnRockN[(int)kind]++;
                }
                if (held.Count > 0) DeepAtRest[(int)kind]++;
            }
            RestedN[(int)kind]++;
            IncludeAll(false);
        }

        /// <summary>The rock colliders `c`'s own shape touches (a skin larger), added to `into`.</summary>
        static void Touches(Collider c, List<Collider> into)
        {
            if (!c) return;
            int n = 0; var t = c.transform; var ls = t.lossyScale; ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            int m = OzRockSolid.Mask; var q = QueryTriggerInteraction.Ignore;
            if (c is BoxCollider bc) n = Physics.OverlapBoxNonAlloc(t.TransformPoint(bc.center), Vector3.Scale(bc.size * 0.5f, ls) + Vector3.one * TouchSkin, hits, t.rotation, m, q);
            else if (c is MeshCollider mc && mc.sharedMesh) { var mb = mc.sharedMesh.bounds; n = Physics.OverlapBoxNonAlloc(t.TransformPoint(mb.center), Vector3.Scale(mb.extents, ls) + Vector3.one * TouchSkin, hits, t.rotation, m, q); }
            else if (c is SphereCollider sc) n = Physics.OverlapSphereNonAlloc(t.TransformPoint(sc.center), sc.radius * Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z)) + TouchSkin, hits, m, q);
            else if (c is CapsuleCollider cc)
            {
                var ax = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                float sa = cc.direction == 0 ? ls.x : cc.direction == 1 ? ls.y : ls.z, so = cc.direction == 0 ? Mathf.Max(ls.y, ls.z) : cc.direction == 1 ? Mathf.Max(ls.x, ls.z) : Mathf.Max(ls.x, ls.y);
                float r = cc.radius * so, half = Mathf.Max(0f, cc.height * 0.5f * sa - r);
                var mid = t.TransformPoint(cc.center); var w = t.TransformDirection(ax) * half;
                n = Physics.OverlapCapsuleNonAlloc(mid - w, mid + w, r + TouchSkin, hits, m, q);
            }
            for (int i = 0; i < n; i++) if (OzRockSolid.IsRock(hits[i]) && !into.Contains(hits[i])) into.Add(hits[i]);
        }

        Bounds BodyBounds()
        {
            bool any = false, skin = kind == Kind.RobotBody; var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (skin && !(r is SkinnedMeshRenderer)) continue;                                                // a robot: its body only (never a mark left where it stood)
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return b;
        }

        /// <summary>Back at rest after a wake: kinematic again, its colliders as its maker had them at rest, its rocks noted again.</summary>
        void ReFreeze()
        {
            foreach (var r in rbs) if (r) { r.isKinematic = true; r.interpolation = RigidbodyInterpolation.None; }
            for (int i = 0; i < cols.Count; i++) if (cols[i]) cols[i].enabled = restOn[i];
            rested = false; Rest();
        }

        // ------------------------------------------------------------------ a rock goes (OzRockSolid.SupportGone)
        /// <summary>OzRockSolid.SupportGone: these rock colliders go; the bodies resting on or leaning against them wake and fall (a robot's
        /// frozen body drops onto what is under it now). How many.</summary>
        public static int SupportGone(List<Collider> gone)
        {
            if (gone == null || gone.Count == 0) return 0;
            int n = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var b = all[i];
                if (!b) { all.RemoveAt(i); continue; }
                if (!b.rested || b.popped || b.dropping || b.marker || b.age > b.wakeUntil || PopOut.IsPopping(b.transform) || (b.leaving != null && b.leaving())) continue;
                bool hit = false; foreach (var c in b.touch) if (c && gone.Contains(c)) { hit = true; break; }
                if (!hit) continue;
                b.Wake(gone); n++;
            }
            return n;
        }

        void Wake(List<Collider> gone)
        {
            WokenN[(int)kind]++;
            if (kind == Kind.RobotBody) { StartDrop(gone); return; }
            rested = false; wokeAt = Time.time; OnRock = false; touch.Clear();
            IncludeAll(true);
            for (int i = 0; i < cols.Count; i++) if (cols[i]) cols[i].enabled = true;
            foreach (var r in rbs) if (r) { r.isKinematic = false; r.interpolation = RigidbodyInterpolation.Interpolate; r.WakeUp(); }
            rested = true;                                                                                       // counted as woken until it rests again (Update)
            ArmNow(false);
        }

        /// <summary>A robot's frozen body cannot move its limbs any more: the whole body drops onto what is under it now (the ground, or the
        /// stump that replaced the pillar), with gravity.</summary>
        void StartDrop(List<Collider> gone)
        {
            var b = BodyBounds();
            float gy = ZGame.I ? ZGame.I.GroundY(b.center.x, b.center.z) : b.min.y, top = gy;
            for (int s = 0; s < 5; s++)
            {
                var o = b.center + (s == 0 ? Vector3.zero : new Vector3(((s & 1) == 1 ? 1f : -1f) * b.extents.x * 0.5f, 0f, ((s & 2) == 2 ? 1f : -1f) * b.extents.z * 0.5f));
                int n = Physics.RaycastNonAlloc(new Vector3(o.x, b.max.y + 0.3f, o.z), Vector3.down, rayHits, b.size.y + 1f, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) { var h = rayHits[i]; if (!OzRockSolid.IsRock(h.collider) || gone.Contains(h.collider)) continue; top = Mathf.Max(top, h.point.y); }
                if (ZGame.I) top = Mathf.Max(top, ZGame.I.GroundY(o.x, o.z));
            }
            dropLeft = b.min.y - (top - 0.02f);
            touch.Clear(); OnRock = top > gy + 0.05f; hasSupport = true; restSupport = new Vector3(b.center.x, top, b.center.z);
            if (dropLeft <= 0.03f) return;
            dropping = true; dropV = 0f; Dropped[(int)kind]++;
        }
        static readonly RaycastHit[] rayHits = new RaycastHit[16];
        /// <summary>Scripted crew bodies stopped at a rock's side.</summary>
        public static int SideStops;

        void DropStep(float dt)
        {
            dropV += 9.81f * dt; float step = Mathf.Min(dropLeft, dropV * dt);
            transform.position += Vector3.down * step; dropLeft -= step;
            if (dropLeft > 1e-4f) return;
            dropping = false;
            if (WreckFx.OnScreen(transform.position)) FxManager.I?.Dust(restSupport, 0.5f);
            OzBodySounds.Landed(GetComponent<OzBodyLanding>(), restSupport, dropV);                           // the body lands on the stump or the ground: its metal thud
        }

        // ------------------------------------------------------------------ the makers' end of the wreck time
        /// <summary>Its maker takes it away now (the end of its wreck time): a body lying ON a rock pops away with a puff (it cannot sink
        /// through the rock); true = popped (the maker leaves its sink out). `done` / `destroy` as PopOut.</summary>
        public static bool PopIfOnRock(GameObject go, System.Action done = null, bool destroy = false)
        {
            var b = go ? go.GetComponent<OzRockBody>() : null;
            if (!b || !b.rested || !b.OnRock || b.popped || b.dropping) return false;
            b.popped = true; Popped[(int)b.kind]++;
            var at = PopOut.BottomCentre(go.transform);
            if (WreckFx.OnScreen(at)) FxManager.I?.Dust(at + Vector3.up * 0.2f, Mathf.Clamp(b.Size * 0.3f, 0.3f, 1f));
            PopOut.Begin(go.transform, PopOut.Style.Debris, done, destroy, false);
            return true;
        }

        /// <summary>OzShuttleCrewFall.Retire: a crew body lying on a rock pops away (the corpse pop with its puff, about its own spot on the
        /// rock) instead of sinking through it. True = popped.</summary>
        public static bool PopCorpseIfOnRock(GameObject go, Vector3 bodyPos)
        {
            var b = go ? go.GetComponent<OzRockBody>() : null;
            if (!b || !b.rested || !b.OnRock || b.popped || b.dropping) return false;
            b.popped = true; Popped[(int)b.kind]++;
            var pivot = b.hasSupport ? new Vector3(bodyPos.x, b.restSupport.y, bodyPos.z) : PopOut.BottomCentre(go.transform);
            PopOut.Begin(go.transform, PopOut.Style.Corpse, null, true, true, pivot);
            return true;
        }

        /// <summary>UnitView.PopAway: the ground point a robot's corpse pops about: the rock top it lies on (else the ground as before).</summary>
        public static float CorpseFloor(GameObject robot, Vector3 hips, float groundY)
        {
            var b = robot ? robot.GetComponent<OzRockBody>() : null;
            if (!b || !b.rested || !b.OnRock || !b.hasSupport) return groundY;
            var d = hips - b.restSupport; d.y = 0f;
            return d.magnitude < 2.5f ? Mathf.Max(groundY, b.restSupport.y) : groundY;
        }

        /// <summary>UnitView.BeginFade: the flipped hull rests on a rock now.</summary>
        public static bool OnRockNow(GameObject go)
        {
            var b = go ? go.GetComponent<OzRockBody>() : null;
            return b && b.rested && b.OnRock && !b.popped && !b.dropping;
        }

        internal static void CountPop(GameObject go) { var b = go ? go.GetComponent<OzRockBody>() : null; if (b && !b.popped) { b.popped = true; Popped[(int)b.kind]++; } }

        /// <summary>DebrisAudit: this resting body is held by a rock (on it or against it): not "frozen in the air", leave it.</summary>
        public static bool RestsOnRock(Rigidbody body)
        {
            var b = body ? body.GetComponentInParent<OzRockBody>() : null;
            if (!b || b.popped || b.marker) return false;
            if (b.rested) return b.touch.Count > 0 || b.OnRock;
            if (!b.rb || !b.rb.IsSleeping()) return false;
            touchNow.Clear(); foreach (var c in b.cols) Touches(c, touchNow);                                       // asleep, never kinematic (the radar dish): on or against a rock now
            return touchNow.Count > 0;
        }
        static readonly List<Collider> touchNow = new List<Collider>(4);

        /// <summary>OzShuttleBreakup.DepthIfMoved: how deep (m) `col` would lie in a switched-on rock if it moved by `delta` (its step onto
        /// the ground never goes into a rock).</summary>
        public static float RockDepthIfMoved(Collider col, Vector3 delta)
        {
            if (!col || !col.enabled || OzRockSolid.Layer < 0 || !OzRockSolid.AllOn) return 0f;
            var b = col.bounds; float deep = 0f;
            int n = Physics.OverlapBoxNonAlloc(b.center + delta, b.extents + Vector3.one * 0.05f, hits, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rc = hits[i];
                if (OzRockSolid.IsRock(rc) && Physics.ComputePenetration(col, col.transform.position + delta, col.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out _, out float d))
                    deep = Mathf.Max(deep, d);
            }
            return deep;
        }

        /// <summary>OzShuttleBreakup.FallGap, per hull point `w` of the dropping ship (`gap` = its height over the ground or the water): a
        /// switched-on rock between the hull's middle `mid` and the point means the point went into or past the rock this frame (the drop
        /// breaks now). Met on a rock's top (a face turned up): the gap to that face (below 0: the ship is lifted onto it, as on the ground);
        /// met on a side: 0 (it breaks where it is; OzShuttleBreakup.Rocks.cs pushes the pieces out of the rock).</summary>
        public static float FallGapRock(Vector3 mid, Vector3 w, float gap)
        {
            if (!OzRockSolid.AllOn) return gap;
            var d = w - mid; float len = d.magnitude; if (len < 1e-3f) return gap;
            if (!Physics.Raycast(mid, d / len, out var hit, len, OzRockSolid.Mask, QueryTriggerInteraction.Ignore) || !OzRockSolid.IsRock(hit.collider)) return gap;
            OzRockSolid.DropTouches++;
            return Mathf.Min(gap, hit.normal.y > 0.6f ? w.y - hit.point.y : 0f);
        }

        /// <summary>OzShuttleCrewFall's scripted fall: what the falling body lands on: a rock top under it (switched on round it), else the
        /// ground. A body that flew into a rock's side this step (`vel` x `dt`) stops at the side (0.3 m off it) and loses its speed into
        /// it, so it slides down the side. A body landing on a rock is noted (it pops away on the rock at the end instead of sinking).</summary>
        public static float FloorAt(GameObject go, ref Vector3 pos, ref Vector3 vel, float dt, float groundY)
        {
            if (!OzRockSolid.AllOn || !go) return groundY;
            OzRockSolid.ArmAround(pos, 8f, 2f, 5f);
            var step = vel * dt; float sl = step.magnitude;
            if (sl > 1e-4f)
            {
                var prev = pos - step;
                if (Physics.Raycast(prev, step / sl, out var side, sl + 0.3f, OzRockSolid.Mask, QueryTriggerInteraction.Ignore) && OzRockSolid.IsRock(side.collider) && side.normal.y <= 0.6f)
                {
                    var n = side.normal; n.y = 0f; n = n.sqrMagnitude > 1e-4f ? n.normalized : -new Vector3(step.x, 0f, step.z).normalized;
                    var q = side.point + n * 0.3f; pos = new Vector3(q.x, pos.y, q.z);
                    float into = Vector3.Dot(vel, n); if (into < 0f) vel -= n * into;
                    SideStops++;
                }
            }
            if (!Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, Mathf.Max(0.5f, pos.y + 0.3f - groundY), OzRockSolid.Mask, QueryTriggerInteraction.Ignore)) return groundY;
            if (!OzRockSolid.IsRock(hit.collider) || hit.point.y <= groundY + 0.05f) return groundY;
            if (pos.y <= hit.point.y + 0.06f)
            {   // it lands on the rock now: noted
                var b = go.GetComponent<OzRockBody>(); if (!b) b = go.AddComponent<OzRockBody>();
                if (!b.counted) { b.counted = true; b.kind = Kind.RobotBody; Added[(int)Kind.RobotBody]++; }
                b.marker = true; b.rested = true; b.OnRock = true; b.hasSupport = true; b.restSupport = new Vector3(pos.x, hit.point.y, pos.z); b.wakeUntil = 0f;
                if (!all.Contains(b)) all.Add(b);
            }
            return hit.point.y;
        }

        void OnDestroy() { all.Remove(this); pendingStart.Remove(this); }

        // ------------------------------------------------------------------ checks
        /// <summary>Play check: how deep (m) this resting body lies in a rock's drawn shape at its deepest sampled point (its colliders' own
        /// shapes, 5 cm inside their surface, tested with OzRockSolid.InsideRock against the switched-on rocks), and its middle inside one.</summary>
        public bool InRock(out bool middle, out string which)
        {
            middle = false; which = "-";
            var b = BodyBounds();
            if (OzRockSolid.InsideRock(b.center, out var wm)) { middle = true; which = wm; }
            bool deep = false;
            foreach (var c in cols)
            {
                if (!c) continue;
                foreach (var p in Samples(c, 0.05f))
                    if (OzRockSolid.InsideRock(p, out var w)) { deep = true; which = w; break; }
                if (deep) break;
            }
            return deep || middle;
        }

        static readonly List<Vector3> samples = new List<Vector3>(27);
        static List<Vector3> Samples(Collider c, float inset)
        {
            samples.Clear();
            var t = c.transform; Vector3 ctr, half; var ls = t.lossyScale; ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (c is BoxCollider bc) { ctr = bc.center; half = bc.size * 0.5f; }
            else if (c is MeshCollider mc && mc.sharedMesh) { ctr = mc.sharedMesh.bounds.center; half = mc.sharedMesh.bounds.extents * 0.8f; }
            else if (c is CapsuleCollider cc) { ctr = cc.center; float r = cc.radius * 0.7f; half = new Vector3(r, r, r); half[cc.direction] = Mathf.Max(r, cc.height * 0.5f - cc.radius * 0.3f); }
            else if (c is SphereCollider sc) { ctr = sc.center; half = Vector3.one * sc.radius * 0.7f; }
            else return samples;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        var l = ctr + new Vector3(x * half.x, y * half.y, z * half.z);
                        var w = t.TransformPoint(l);
                        var toC = t.TransformPoint(ctr) - w; float m = toC.magnitude;
                        samples.Add(m > inset ? w + toC / m * inset : w);
                    }
            return samples;
        }

        /// <summary>One line per kind for the Play check and the log.</summary>
        public static string State
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < Kinds; k++)
                    sb.Append(KindNames[k]).Append(": added ").Append(Added[k]).Append(" (small ").Append(Small[k]).Append(", no rock near ").Append(NoRockNear[k]).Append("), rested ").Append(RestedN[k])
                      .Append(", touching a rock ").Append(Touching[k]).Append(", on a rock ").Append(OnRockN[k]).Append(", popped on a rock ").Append(Popped[k]).Append(", woken ").Append(WokenN[k])
                      .Append(k == (int)Kind.RobotBody ? ", dropped " + Dropped[k] : "").Append(", started in a rock ").Append(StartedIn[k]).Append(" (set out ").Append(StartedMoved[k]).Append(", deep ").Append(StartedDeep[k]).Append(", still at rest ").Append(DeepAtRest[k]).Append("); ");
                return sb.ToString();
            }
        }
    }
}

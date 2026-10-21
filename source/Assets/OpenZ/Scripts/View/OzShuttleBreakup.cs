using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle falls apart when it is destroyed (rule DV118, view only: the pieces hurt nobody). At the DRAWN kill moment
    /// (CombatView.Death and VehicleDeathSync's held death call <see cref="TakesDeath"/>; false for everything but the playable shuttle)
    /// the ship bursts into its MODELLED pieces, each a physics body flying on from where it is drawn (banked and 12 m up, or standing):
    /// both dome halves, both engine pods, the four legs, the rear body (the kit's wreck body with the same doorway, burnt) broken into
    /// four chunks (a cut down its middle and a roof cut, each cut closed by a burnt cap), a shard off each dome half's rim and the ramp
    /// leaf itself (15 pieces). The cuts are made one per frame from the level's start (one plan for every shuttle: the same model), so
    /// the kill frame only builds the bodies. The pieces follow the fort cannon's break-up rules (OzFortGunBreakup, copied, not called): a convex
    /// collider of the piece's own mesh, mass by size, heavy metal, layer DebrisHeavy (they hit each other and the ground); pieces that
    /// start deep inside each other ignore each other until they are apart; at rest a piece lies on the ground by its real lowest
    /// vertex (3 cm in) and never more than 3 cm inside a neighbour; after <see cref="WreckSeconds"/> a creak, then they sink away
    /// together over 3 s. Launch: the ship's drawn velocity plus a scatter out of the hull's middle (light parts fast and spinning, the
    /// body chunks slow), and the push of the killing hit (OzShuttleAim.KillHit): the burst's middle moves toward where it was hit and
    /// every piece is thrown on along the round's way, harder for a heavy shell than for rifle fire (so a rocket salvo from one side, a
    /// shell from above and rifle fire from below break it apart differently); destroyed on the ground the same break with a lower throw. In the air the three heaviest trail fire and smoke
    /// and the two biggest whoosh as they tumble. Hits: a heavy piece's first slam = the heavy metal crash with dust; the body's first
    /// slam adds the wreck crash, metal sparks, a scorch, a dust burst, a small camera kick and the debris rain once; other hits clank
    /// (a clank the sound table drops is tried once more 0.07 s later); rolling and sliding pieces knock. Over the river every piece is
    /// handed to the shared water rule (OzWaterLanding: splash, sink or bob). The squad aboard is lost with it (rule): their bodies tumble
    /// out with the pieces (BoardingView.ShuttleCrewLost). Switch: <see cref="Enabled"/> (off = the old whole fall, an error in the log).
    /// The third round (OzShuttleBreakup.Look.cs, OzShuttleBreakup.Kills.cs): the pieces keep the hull's paint lightly burnt, the rear body
    /// breaks into eight curved hull panels (open cuts, grey ribbed metal inside, ember rims along the cuts that cool in 5 s), the three
    /// biggest pieces burn with a dark smoke column for 12-15 s and the rest smoulder, an air break leaves a dark smoke cloud with an
    /// orange core, and the kill decides the break: rifle-class fire = the whole ship drops tilted and smoking and breaks on the ground,
    /// a heavy shell (howitzer, the fort's gun) = a second blast splits the body in the air and throws the pieces wider.
    /// </summary>
    public sealed partial class OzShuttleBreakup : MonoBehaviour
    {
        public static bool Enabled = true;
        public const float Sink = 0.03f, HoldDepth = 0.03f, PushAt = 9f, WreckSeconds = 16f;
        public const int HeavyLayerFallback = 28;
        public static int Breaks, Pieces, Crashes, Clanks, Knocks, RetriedClanks, BodySlams, Tosses, RestedCount, OnGround, OnOther, InWaterCount, Floating, Sunk, Faded, HeldPairs, ReleasedPairs, ForcedPairs, PushedOut, Prepared, PrepareSteps, Creaks;
        public static float LastBreakAt = -1f; public static bool LastInAir; public static string LastPlan = "-"; public static Vector3 LastCentre;
        /// <summary>The last break: its wreck time (s after the break: the creak, then all sink), the killing hit (kind, flat direction, power).</summary>
        public static float LastWreckAt = -1f, LastKillPower = 1f; public static string LastKillKind = "-"; public static Vector3 LastKillDir;
        public static readonly List<OzShuttleBreakup> LastPieces = new List<OzShuttleBreakup>();
        static readonly List<OzShuttleBreakup> live = new List<OzShuttleBreakup>();

        // ------------------------------------------------------------------ the plan (meshes in the ship's ROOT space)
        sealed class Def { public string name, kind; public Mesh mesh; public Material mat; public bool capped; public int tris; }
        static readonly List<Def> plan = new List<Def>();
        static IEnumerator<bool> job; static int planFor = -1; static bool planDone; static Transform jobRoot;
        /// <summary>Proof: the ship the shared plan is (being) cut from, and whether it is done.</summary>
        public static int PlanFrom => planFor;
        public static bool PlanDone => planDone;
        static readonly StringBuilder planText = new StringBuilder();

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = Find(c, name); if (r) return r; }
            return null;
        }

        /// <summary>OzPlayableShuttle.Bind (a ship drawn on a new level, or one more from the testing panel): a plan still being cut from a
        /// ship that is gone (the level was restarted) starts again from this one; a finished plan is kept (every shuttle is the same model,
        /// the pieces are in the ship's own space).</summary>
        public static void BindShip(Transform shipRoot)
        {
            if (planDone || !shipRoot) return;
            if (job == null || !jobRoot) { plan.Clear(); job = null; jobRoot = null; planFor = -1; }
        }

        /// <summary>OzPlayableShuttle, once a frame from the level's start: one cut of the shared plan (true = more to do). One ship cuts
        /// it; the others wait.</summary>
        public static bool PrepareStep(Transform shipRoot, int shipId)
        {
            if (!Enabled || !shipRoot || planDone) return false;
            if (job == null || !jobRoot) { plan.Clear(); jobRoot = shipRoot; planFor = shipId; job = Steps(shipRoot); }
            else if (jobRoot != shipRoot) return false;
            return Cut();
        }

        static bool Cut()
        {
            bool more;
            try { more = job.MoveNext(); } catch (System.Exception e) { Debug.LogError("[OpenZ] shuttle break-up plan: " + e.Message); more = false; }
            PrepareSteps++;
            if (!more) { planDone = true; Prepared++; LastPlan = planText.ToString(); job = null; }
            return more;
        }

        /// <summary>The kill frame of a ship whose plan is not done yet: the rest of it now (from whichever ship is cutting it).</summary>
        static void FinishPlan(Transform shipRoot, int shipId)
        {
            if (planDone || !Enabled) return;
            if (job == null || !jobRoot) { plan.Clear(); jobRoot = shipRoot; planFor = shipId; job = Steps(shipRoot); }
            int guard = 0; while (!planDone && guard++ < 400 && Cut()) { }
        }

        static OzMeshSlice.Part RootPart(Mesh m, Matrix4x4 rootFromMesh)
        {
            var p = OzMeshSlice.FromMesh(m); if (p == null) return null;
            var A = rootFromMesh; var Ninv = A.inverse.transpose;
            for (int i = 0; i < p.v.Count; i++)
            {
                var q = p.v[i]; q.p = A.MultiplyPoint3x4(q.p); q.n = Ninv.MultiplyVector(q.n).normalized;
                var t3 = A.MultiplyVector(new Vector3(q.t.x, q.t.y, q.t.z)).normalized; q.t = new Vector4(t3.x, t3.y, t3.z, q.t.w);
                p.v[i] = q;
            }
            return p;
        }

        static void Add(string name, string kind, OzMeshSlice.Part part, Material mat)
        {
            if (part == null || part.Tris < 4) return;
            var m = OzMeshSlice.ToMesh(part, "OZ_ShuttlePiece_" + name);
            plan.Add(new Def { name = name, kind = kind, mesh = m, mat = mat, capped = part.CapTris > 0, tris = part.Tris });
            planText.Append(name).Append(' ').Append(part.Tris).Append(part.CapTris > 0 ? " (cut)" : "").Append(", ");
        }

        static IEnumerator<bool> Steps(Transform root)
        {
            planText.Clear();
            var model = root.Find("Model"); if (!model) yield break;
            var hull = Find(model, "SH_Hull"); var hullMr = hull ? hull.GetComponent<MeshRenderer>() : null;
            Material mat = hullMr ? hullMr.sharedMaterial : null;
            Matrix4x4 RootFrom(Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;
            // the ship's own wreck pieces (hidden parts of the model)
            foreach (var (n, kind) in new[] { ("WR_Dome_L", "dome"), ("WR_Dome_R", "dome"), ("WR_Engine_L", "engine"), ("WR_Engine_R", "engine"),
                                             ("WR_Leg_FL", "leg"), ("WR_Leg_FR", "leg"), ("WR_Leg_BL", "leg"), ("WR_Leg_BR", "leg") })
            {
                var t = Find(model, n) ?? Find(root, n); var mf = t ? t.GetComponent<MeshFilter>() : null;   // the back legs stand under the ship's RUIN_Wreck
                if (!mf || !mf.sharedMesh) continue;
                var part = RootPart(mf.sharedMesh, RootFrom(t));
                yield return true;
                if (kind == "dome" && part != null)
                {   // a shard off the dome half's lower outer rim (a plane through two legs from its box corner)
                    var b = part.Box(); float sx = b.center.x >= 0f ? 1f : -1f;
                    var corner = new Vector3(sx > 0 ? b.max.x : b.min.x, b.min.y, b.max.z);
                    var pa = corner - new Vector3(sx * 0.75f, 0f, 0f); var pb = corner + new Vector3(0f, 0.85f, 0f); var pc = corner - new Vector3(0f, 0f, 0.6f);
                    var nrm = Vector3.Cross(pb - pa, pc - pa).normalized; if (Vector3.Dot(nrm, corner - pa) < 0f) nrm = -nrm;
                    if (DomeCut(part, nrm, Vector3.Dot(nrm, pa), out var shard, out var rest) && shard != null && rest != null && shard.Tris >= 6)   // K256k: the cannon's relief cut (OzShuttleBreakup.Shell.cs)
                    {
                        var ss = shard.Box().size; float big = Mathf.Max(ss.x, Mathf.Max(ss.y, ss.z));
                        if (big >= 0.25f && big <= 1.2f) { Add(n + "_Shard", "shard", shard, mat); part = rest; }
                    }
                    yield return true;
                }
                Add(n, kind, part, mat);
                yield return true;
            }
            // the rear body: the kit's wreck body with the doorway (else the ship's own), in four chunks
            Mesh body = null; Matrix4x4 bodyFrom = Matrix4x4.identity;
            var kit = Resources.Load<GameObject>(ShuttleRearDoor.KitPath);
            var kb = kit ? Find(kit.transform, "WR_Body_Door") : null; var kbm = kb ? kb.GetComponent<MeshFilter>() : null;
            if (kbm && kbm.sharedMesh) { body = kbm.sharedMesh; bodyFrom = root.worldToLocalMatrix * model.localToWorldMatrix * (kit.transform.worldToLocalMatrix * kb.localToWorldMatrix); }
            else
            {
                var wb = Find(model, "WR_Body"); var wm = wb ? wb.GetComponent<MeshFilter>() : null;
                if (wm && wm.sharedMesh) { body = wm.sharedMesh; bodyFrom = RootFrom(wb); }
                Debug.LogError("[OpenZ] shuttle break-up: the kit's wreck body is missing (the plain wreck body, no doorway)");
            }
            if (body)
            {
                var bp = RootPart(body, bodyFrom);
                yield return true;
                // the third round: eight curved hull panels (open cuts down the middle, at the roof line and across the length), each
                // with its grey ribbed inside and an ember rim along its cuts (OzShuttleBreakup.Look.cs), never a capped block
                var steps = ShellSteps(bp, mat);
                while (steps.MoveNext()) yield return true;
            }
            // the colliders cooked one per step (MeshCollider reuses them on the kill frame)
            foreach (var d in plan)
            {
                yield return true;
                try { Physics.BakeMesh(d.mesh.GetInstanceID(), true); } catch (System.Exception) { }
            }
        }

        // ------------------------------------------------------------------ the break (the drawn kill moment)
        sealed class Pair { public Collider a, b; public OzShuttleBreakup pa, pb; public bool forced; }
        sealed class Group
        {
            public float wreckAt, sinkDepth = 2f, nextCheck = 0.2f, age; public Vector3 centre; public bool slammed, creaked; public int emitter;
            public readonly List<OzShuttleBreakup> pieces = new List<OzShuttleBreakup>();
            public readonly List<Pair> held = new List<Pair>();
            public bool Holds(OzShuttleBreakup p) { foreach (var q in held) if (q.pa == p || q.pb == p) return true; return false; }
            // the third round (OzShuttleBreakup.Kills.cs / .Look.cs): how it broke, the second blast, the air cloud, the fire's sound, the clank queue
            public string how = "-"; public ZObject obj; public Vector3 vel; public float secondAt = -1f, cloudUntil = -1f, cloudT, power = 1f;
            public bool second, fireLoopOn, scorched; public string fireLoopKey; public int deck = -1;
            public readonly List<Clank> clanks = new List<Clank>(); public float lastClankAt = -1f;
        }
        static int HeavyLayer { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : HeavyLayerFallback; } }
        static PhysicsMaterial metal;
        static PhysicsMaterial Metal() => metal ? metal : (metal = new PhysicsMaterial("OZ_ShuttleMetal") { bounciness = 0.08f, dynamicFriction = 0.7f, staticFriction = 0.85f, bounceCombine = PhysicsMaterialCombine.Minimum, frictionCombine = PhysicsMaterialCombine.Maximum });
        static float Depth(Collider a, Collider b) => Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d) ? d : 0f;
        static bool PlayAd(string id, Vector3 at, int emitter, float vol = 1f, float pitch = 1f) => AudioDirector.Has(id) && AudioDirector.I.Play(id, at, emitter, vol, pitch);
        static bool PlayCrash(Vector3 at, float vol, int emitter) { var ca = CombatAudio.I; if (!ca) return false; int p0 = ca.Played; ca.Play("turret", at, vol, 0, emitter, OzSoundV3.BankMetalHeavy); return ca.Played > p0; }

        /// <summary>CombatView.Death / VehicleDeathSync hook: the playable shuttle breaks up here (true; the caller plays nothing else for it);
        /// false for everything else. Rifle-class fire in the air: the whole ship drops first (OzShuttleBreakup.Kills.cs, StartFall) and
        /// breaks where it hits the ground.</summary>
        public static bool TakesDeath(ZObject o, UnitView v, Vector3 p, FxManager fx, CombatAudio au)
        {
            var ps = OzPlayableShuttle.Of(o);
            if (o == null || !o.IsShuttle || !ps) return false;
            if (ps.BrokenUp) return true;
            if (!Enabled) { Debug.LogError("[OpenZ] shuttle break-up switched off: the whole ship falls"); return false; }
            var root = ps.transform;
            FinishPlan(root, o.Id);                                                                  // not cut yet (destroyed in the first moments of the level): now
            if (plan.Count < 2) { Debug.LogError("[OpenZ] shuttle break-up: no pieces (" + LastPlan + "): the whole ship falls"); return false; }
            var g = ZGame.I; var w = g ? g.World : null;
            bool air = ps.FeetHeight > 0.5f;
            var vel = ps.Velocity; var centre = root.TransformPoint(new Vector3(0f, 2.6f, 0f));
            // the killing hit: where it struck, the round's way, how hard (none drawn in the last 1.5 s: a plain burst)
            bool hitKnown = OzShuttleAim.KillHit(o.Id, out var hitDir, out var hitAt, out float power, out string hitKind);
            if (!hitKnown) { hitDir = Vector3.zero; power = 1f; hitKind = "-"; hitAt = centre; }
            var burst = centre;
            if (hitKnown) { var off = hitAt - centre; burst = centre + Vector3.ClampMagnitude(off, 1.5f); }
            LastKillKind = hitKind; LastKillPower = power; LastKillDir = hitDir;
            int aboard = 0; string type = string.IsNullOrEmpty(o.PassengerType) ? "grunt" : o.PassengerType;
            if (w != null) for (int i = w.ShuttleNotes.Count - 1; i >= 0; i--) { var n = w.ShuttleNotes[i]; if (n.id == o.Id && (n.kind == ZShuttleNoteKind.Crashed || n.kind == ZShuttleNoteKind.Wrecked)) { aboard = Mathf.Max(0, n.aboard); break; } }
            // the third round: rifle-class fire drops the whole ship, tilted and smoking; it breaks where it hits the ground
            if (air && DropWhole && StartFall(o, v, ps, fx, au, hitKnown, hitDir, hitAt, power, hitKind, aboard, type)) { HoldDrawn(v); return true; }   // K256h: the whole-ship drop only with its switch (OzShuttleBreakup.Altitude.cs), drawn all the way down
            string how = AirHow(ps, hitKnown, power);                                                // K256h: every air kill breaks here, where the ship is drawn (rifle-class fire: the crack)
            Break(o, v, p, fx, au, ps, air, vel, centre, burst, hitDir, power, hitKind, aboard, type, !air ? "ground" : how);
            BreakEnd(how, ps);                                                                       // K256h: where it all started (the proof), the crack's trails and its cloud
            return true;
        }

        /// <summary>The break itself: the drawn ship goes, its pieces fly. how = "burst" (in the air), "split" (in the air, a second blast
        /// splits the body: OzShuttleBreakup.Kills.cs), "ground" (destroyed standing), "drop" (the dropped ship hitting the ground).</summary>
        static void Break(ZObject o, UnitView v, Vector3 p, FxManager fx, CombatAudio au, OzPlayableShuttle ps, bool air, Vector3 vel, Vector3 centre, Vector3 burst,
                          Vector3 hitDir, float power, string hitKind, int aboard, string type, string how)
        {
            var root = ps.transform;
            // the ship as drawn goes: the hull's own paint, lightly burnt, for the pieces (PaintCharred), every part hidden, the ramp leaf
            // taken off as its own piece
            var tp = root.GetComponent<TeamPaint>(); if (tp) tp.Charred = PaintCharred;
            var door = ShuttleRearDoor.Of(v);
            var leafR = door ? door.BreakOff() : null;
            MaterialPropertyBlock paint = null;
            var hull = Find(root, "SH_Hull"); var hullR = hull ? hull.GetComponent<MeshRenderer>() : null;
            if (hullR) { paint = new MaterialPropertyBlock(); hullR.GetPropertyBlock(paint, 0); }
            var grp = new Group { centre = centre, emitter = o.Id, how = how, obj = o, vel = vel, power = power, deck = ps.DeckStation };
            var holder = new GameObject("OZ_ShuttleBreakup_" + o.Id).transform;
            var keeper = holder.gameObject.AddComponent<OzShuttleBreakup>(); keeper.keeper = true; keeper.grp = grp; keeper.ship = root.gameObject;
            int layer = HeavyLayer;
            foreach (var d in plan)
            {
                var pc = Build(d.name, d.kind, d.mesh, d.mat, d.capped, root.localToWorldMatrix * ps.LegsBreakFrom(d.name), paint, layer, holder);   // K388: a wreck leg from the pose its leg is in
                if (pc) { pc.grp = grp; grp.pieces.Add(pc); }
            }
            if (leafR && leafR.TryGetComponent<MeshFilter>(out var lmf) && lmf.sharedMesh)
            {
                var pc = Build("RampLeaf", "leaf", lmf.sharedMesh, leafR.sharedMaterial, false, leafR.transform.localToWorldMatrix, paint, layer, holder);
                if (pc) { pc.grp = grp; grp.pieces.Add(pc); }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
            if (!ps.BrokenUp) ps.OnBrokenUp(air);                                                   // a dropped ship stopped its own pose when it was hit
            foreach (var pc in grp.pieces) pc.CollectPoints();
            // nothing starts in the ground (a piece standing on its pads): all lifted together
            float lift = 0f; foreach (var pc in grp.pieces) lift = Mathf.Max(lift, pc.LiftNeed());
            if (lift > 0f) foreach (var pc in grp.pieces) pc.transform.position += Vector3.up * lift;
            Physics.SyncTransforms();
            RocksAtBreak(grp);                                                                                     // items 331 + 332: none starts in a rock (OzShuttleBreakup.Rocks.cs)
            for (int i = 0; i < grp.pieces.Count; i++)
                for (int j = i + 1; j < grp.pieces.Count; j++)
                {
                    var a = grp.pieces[i].col; var b = grp.pieces[j].col; if (!a || !b) continue;
                    if (Depth(a, b) <= HoldDepth) continue;
                    Physics.IgnoreCollision(a, b, true); grp.held.Add(new Pair { a = a, b = b, pa = grp.pieces[i], pb = grp.pieces[j] }); HeldPairs++;
                }
            // the launch: the ship's drawn velocity + a scatter out of the hull's middle
            var rnd = new System.Random(o.Id * 7919 + Breaks * 31 + 118);
            float R(float x0, float x1) => x0 + (float)rnd.NextDouble() * (x1 - x0);
            float outK = (air ? 1f : 0.5f) * (0.6f + 0.4f * power), upK = (air ? 1f : 0.4f) * (0.7f + 0.3f * power);
            var shove = new Vector3(hitDir.x, Mathf.Max(0f, hitDir.y) * 0.6f, hitDir.z) * (power * (air ? 1f : 0.5f));
            foreach (var pc in grp.pieces)
            {
                var c = pc.Centre(); var dir = c - burst; dir.y = 0f; if (dir.sqrMagnitude < 1e-3f) dir = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); dir.Normalize();
                dir = Quaternion.Euler(0f, R(-25f, 25f), 0f) * dir;
                float vo, vu, sp;
                switch (pc.Kind)
                {
                    case "leg": case "shard": vo = R(4f, 6f); vu = R(3.5f, 6f); sp = R(6f, 10f); break;
                    case "dome": case "engine": case "leaf": vo = R(2.5f, 4f); vu = R(2f, 4f); sp = R(3f, 5f); break;
                    default: vo = R(1f, 2.5f); vu = R(0.5f, 1.5f); sp = R(1f, 2f); break;
                }
                if (how == "drop") { vo *= DropThrowK; vu *= DropThrowK; sp *= 1.3f; }                  // it hit the ground whole: a low, rolling break
                CrackThrow(how, ref vo, ref vu, ref sp);                                               // K256h: the crack's low, slow, tumbling throw (OzShuttleBreakup.Altitude.cs)
                BigThrow(how, air, pc.Kind, ref vo, ref vu, ref sp);                                   // K396: an air break throws the pieces out of the big blast, harder and spinning (OzShuttleBreakup.BigBlast.cs)
                var v0 = vel + dir * (vo * outK) + Vector3.up * (vu * upK) + shove * R(1.2f, 2.4f);   // + the killing round's push
                if (!ThrowVary(o, grp, holder, pc, how, vel, dir, vo * outK, vu * upK, v0, sp)) pc.Launch(v0, Random.onUnitSphere * sp);   // K399: each piece its own throw, spin and moment (OzShuttleBreakup.ThrowVary.cs)
            }
            OzTreeCrush.ShuttleBurst(o, grp.pieces, centre, air, how);                               // item 360: the hull comes down where the rules fell the trees, a big piece into each (OzTreeCrush.cs)
            AssignFires(grp, R);                                                                     // the three biggest burn 12-15 s, the rest smoulder (OzShuttleBreakup.Look.cs)
            if (air) { var byMass = new List<OzShuttleBreakup>(grp.pieces); byMass.Sort((a, b) => b.MassKg.CompareTo(a.MassKg)); for (int i = 0; i < byMass.Count && i < 2; i++) if (PlayAd("dst_turret_toss", byMass[i].Centre(), o.Id + 11 + i)) Tosses++; }
            grp.wreckAt = WreckSeconds + R(0f, 1f); LastWreckAt = grp.wreckAt;
            float tall = 0f; foreach (var pc in grp.pieces) tall = Mathf.Max(tall, pc.Size); grp.sinkDepth = Mathf.Clamp(tall + 0.3f, 1.2f, 6.5f);
            // the blast: in the air the table blast where it is drawn (no wreck fire on the ground under it; its smoke dark: OpenZFxPlayer's
            // air-burst gate) and the dark cloud with its orange core; on the ground as every vehicle
            if (air) { if (!AirBigBlast(o, grp, centre, fx, au, how, holder)) LegacyMatch.DoBlast(centre, LegacyMatch.ForDeath(o), o.Id, fx); grp.cloudUntil = AirCloudSec; AirClouds++; }   // K396: the big blast (OzShuttleBreakup.BigBlast.cs): the big var2 explosion up there
            else { if (!GroundBigBlast(o, p, fx, au)) LegacyMatch.UnitDeath(o, p, fx, au); ScorchDecals.Add(p, 2.2f, ScorchKind.Wreck); }   // K396: the big blast (OzShuttleBreakup.BigBlast.cs): the big var2 explosion on the ground every time
            if (how == "split") grp.secondAt = SecondBlastAt;
            OpenZHitSparks.Burst(centre, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 2.2f, false);
            if (BoardingView.I) BoardingView.I.ShuttleCrewLost(o, aboard, type, o.Owner, vel, burst, air, hitDir, power);
            Breaks++; Pieces += grp.pieces.Count; LastBreakAt = Time.time; LastInAir = air; LastCentre = centre; LastHow = how;
            LastBreakGround = new Vector3(centre.x, ZGame.I ? ZGame.I.GroundY(centre.x, centre.z) : centre.y - 2.6f, centre.z);
            if (how == "split") Splits++; else if (how == "drop") DropBreaks++; else if (how == "burst") Bursts++;
            LastPieces.Clear(); LastPieces.AddRange(grp.pieces);
            FuelCans(grp, root, air, burst, hitDir);                                                // K397: the rocket fuel cans in the hull fly out with the pieces (OzShuttleBreakup.FuelCans.cs)
            Debug.Log($"[OpenZ] shuttle #{o.Id} broke apart {(air ? "in the air" : "on the ground")} ({how}) at {centre}: {grp.pieces.Count} pieces ({LastPlan}), {grp.held.Count} pairs held apart at the start, {aboard} lost aboard; the killing hit {hitKind} power {power:F2} along {hitDir}");
        }

        static OzShuttleBreakup Build(string name, string kind, Mesh mesh, Material mat, bool capped, Matrix4x4 meshToWorld, MaterialPropertyBlock paint, int layer, Transform holder)
        {
            if (!mesh) return null;
            var vis = new GameObject(name) { layer = layer };
            vis.transform.SetPositionAndRotation(meshToWorld.GetColumn(3), meshToWorld.rotation); vis.transform.localScale = meshToWorld.lossyScale;
            vis.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = vis.AddComponent<MeshRenderer>();
            int nSub = Mathf.Max(1, mesh.subMeshCount); var mats = new Material[nSub];
            for (int i = 0; i < nSub; i++) mats[i] = mat;
            if (capped && nSub > 1 && mat) mats[nSub - 1] = ShellInnerMat(mat);                         // the inside / cut faces: grey ribbed metal (OzShuttleBreakup.Look.cs)
            r.sharedMaterials = mats;
            if (paint != null) for (int i = 0; i < nSub; i++) { if (capped && i == nSub - 1) continue; ShellPaint(r, paint, i, mesh); }   // K256k: a cut piece's soot shows (OzShuttleBreakup.Shell.cs)
            if (paint != null) PieceBand(r, nSub, capped);   // K419a: the paint keeps the team colour over the piece's whole height (OzShuttleBreakup.PieceBand.cs)
            r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            var rim = AddRim(vis.transform, mesh, mat, layer);                                    // the ember rim along its cuts (none on a whole part)
            Collider col;
            if (mesh.isReadable) { var mc = vis.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true; col = mc; }
            else { var bc = vis.AddComponent<BoxCollider>(); var mb = mesh.bounds; bc.center = mb.center; bc.size = mb.size; col = bc; }
            col.sharedMaterial = Metal();
            var rootGo = new GameObject("OZ_ShuttlePiece_" + name) { layer = layer };
            rootGo.transform.SetParent(holder, false); rootGo.transform.position = r.bounds.center;
            vis.transform.SetParent(rootGo.transform, true);
            var rb = rootGo.AddComponent<Rigidbody>();
            var bs = r.bounds.size; float vol = Mathf.Abs(bs.x * bs.y * bs.z) * 0.45f;                       // the piece's own bulk (a box over a round shell)
            float mass = Mathf.Clamp(vol * 5f, 0.8f, 400f) * 10f;
            rb.mass = mass; rb.linearDamping = 0.15f; rb.angularDamping = 0.9f; rb.maxAngularVelocity = 14f; rb.maxDepenetrationVelocity = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var pc = rootGo.AddComponent<OzShuttleBreakup>();
            pc.rb = rb; pc.PieceName = name; pc.Kind = kind; pc.MassKg = mass; pc.col = col; pc.vis = vis.transform; pc.rimR = rim;
            live.Add(pc);
            pc.RockPiece();                                                                                        // items 331 + 332: a big piece meets the rocks (OzShuttleBreakup.Rocks.cs)
            return pc;
        }

        // ------------------------------------------------------------------ one piece
        public string PieceName { get; private set; } = "-";
        /// <summary>Its collider (the squad's bodies hit the pieces: OzShuttleCrewFall).</summary>
        public Collider PieceCollider => col;
        public string Kind { get; private set; } = "-";
        public float MassKg { get; private set; }
        public float Size { get; private set; }
        public string RestOn { get; private set; } = "-";
        public float RestGap { get; private set; }
        public bool Rested { get; private set; }
        public int Hits => hits;

        Rigidbody rb; Group grp; Transform vis; Collider col; GameObject ship; bool keeper;
        Vector3[] pts = new Vector3[0];
        static readonly Dictionary<Mesh, Vector3[]> pointCache = new Dictionary<Mesh, Vector3[]>();
        float age, lastHit = -1f, lastKnock = -1f, stayAt = -1f, otherAt = -1f, burnUntil, burnT, snapT = -1f, sinkT = -1f, sinkSpeed, retryAt = -1f, retryVol, retryPitch;
        Vector3 retryPos, snapFrom, snapTo, touchAt; int hits, knockTries; bool frozen, crashed, bigFire, touching, retiring, fired; string otherName;

        void Launch(Vector3 v, Vector3 w) { rb.linearVelocity = v; rb.angularVelocity = w; }
        Vector3 Centre() => rb ? rb.worldCenterOfMass : transform.position;
        bool WantsRest => !frozen && rb && age > 0.6f && (rb.IsSleeping() || age > 7f || (rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.1f));

        void CollectPoints()
        {
            var mf = vis ? vis.GetComponent<MeshFilter>() : null; var m = mf ? mf.sharedMesh : null;
            if (m && !pointCache.TryGetValue(m, out pts))
            {
                if (m.isReadable) { var v = m.vertices; var seen = new HashSet<Vector3>(); var l = new List<Vector3>(); foreach (var q in v) if (seen.Add(q)) l.Add(q); pts = l.ToArray(); }
                else { var b = m.bounds; pts = new Vector3[8]; for (int i = 0; i < 8; i++) pts[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z); }
                pointCache[m] = pts;
            }
            var rr = vis ? vis.GetComponent<Renderer>() : null; var bs = rr ? rr.bounds.size : Vector3.one;
            Size = Mathf.Max(bs.x, Mathf.Max(bs.y, bs.z));
        }

        static readonly List<Vector3> wpts = new List<Vector3>(4096);
        void World() { wpts.Clear(); if (!vis) return; var m = vis.localToWorldMatrix; foreach (var q in pts) wpts.Add(m.MultiplyPoint3x4(q)); }

        /// <summary>Its real lowest vertex against the exact ground under it (m, + = above), from its 48 lowest vertices.</summary>
        float Gap(out Vector3 low)
        {
            var g = ZGame.I; World(); low = transform.position;
            if (!g || wpts.Count == 0) return 0f;
            wpts.Sort((a, b) => a.y.CompareTo(b.y));
            float best = float.MaxValue;
            for (int i = 0; i < wpts.Count && i < 48; i++) { var q = wpts[i]; float e = q.y - g.GroundY(q.x, q.z); if (e < best) { best = e; low = q; } }
            return best;
        }
        float LiftNeed() => Mathf.Max(0f, 0.02f - Gap(out _));

        void Update()
        {
            var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
            if (keeper) { KeeperUpdate(dt); return; }
            if (dt <= 0f) return;
            age += dt;
            LookStep(dt);                                                                                       // the ember rim cooling, the smoulder (OzShuttleBreakup.Look.cs)
            if (retiring) { Sinking(dt); return; }
            if (grp != null && grp.age >= grp.wreckAt) { Retire(); return; }
            if (retryAt >= 0f && age >= retryAt) { retryAt = -1f; if (PlayAd("dst_debris_metal", retryPos, GetInstanceID(), retryVol, retryPitch)) { Clanks++; RetriedClanks++; } }
            if (!frozen && rb)
            {
                if (touching && age - stayAt < 0.1f && knockTries < 8 && age - lastKnock > 0.3f && age > 0.3f)
                {
                    var hv = rb.linearVelocity; hv.y = 0f;
                    if (rb.angularVelocity.magnitude > 1.6f || hv.magnitude > 1.3f)
                    { lastKnock = age; knockTries++; if (PlayAd("dst_debris_metal", touchAt, GetInstanceID(), Mathf.Clamp(0.22f + hv.magnitude * 0.05f, 0.22f, 0.45f), MassKg > 1500f ? 0.78f : 0.88f)) Knocks++; }
                }
                if (age > 0.6f && (rb.IsSleeping() || age > 7f))
                {
                    bool held = grp != null && grp.Holds(this);
                    if (!held) Freeze(); else if (age >= PushAt) { PushOut(); Freeze(); }
                }
            }
            touching = false;
            if (snapT >= 0f)
            {
                snapT += dt; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(snapT / 0.15f));
                transform.position = Vector3.Lerp(snapFrom, snapTo, k);
                if (snapT >= 0.15f) { snapT = -1f; RestGap = Gap(out _); Settle(); }
            }
            if (burnUntil > 0f && age < burnUntil + 3f && (burnT -= dt) <= 0f) Burn();
        }

        void KeeperUpdate(float dt)
        {
            if (fall != null) { FallStep(dt); return; }                                                       // a dropped ship falling whole (OzShuttleBreakup.Kills.cs)
            if (ship) { ship.SetActive(false); ship = null; }                                                  // the drawn ship goes (its view, frame and bar with it)
            if (grp == null) return;
            grp.age += dt;
            KeeperFx(dt);                                                                                       // the second blast, the air cloud, the fire's sound, the clank queue
            if (!grp.creaked && grp.age >= grp.wreckAt - 0.8f && grp.pieces.Count > 0) { grp.creaked = true; Creaks++; PlayAd("dst_wreck_creak", grp.centre, grp.emitter); }
            if (grp.held.Count > 0 && grp.age >= grp.nextCheck)
            {
                grp.nextCheck = grp.age + 0.2f;
                for (int i = grp.held.Count - 1; i >= 0; i--)
                {
                    var q = grp.held[i];
                    if (!q.a || !q.b || !q.pa || !q.pb || q.pa.retiring || q.pb.retiring) { grp.held.RemoveAt(i); continue; }
                    if (Depth(q.a, q.b) <= HoldDepth) { Physics.IgnoreCollision(q.a, q.b, false); grp.held.RemoveAt(i); ReleasedPairs++; continue; }
                    if (!q.forced && (q.pa.WantsRest || q.pb.WantsRest)) { q.forced = true; ForcedPairs++; Physics.IgnoreCollision(q.a, q.b, false); if (q.pa.rb && !q.pa.rb.isKinematic) q.pa.rb.WakeUp(); if (q.pb.rb && !q.pb.rb.isKinematic) q.pb.rb.WakeUp(); }
                }
            }
            RestClear();                                                                                        // K256h2: resting pieces lie on each other, never inside (OzShuttleBreakup.RestClear.cs)
            if (grp.pieces.Count == 0 && grp.age > 2f) Destroy(gameObject);
        }

        void PushOut()
        {
            if (grp == null) return;
            for (int i = grp.held.Count - 1; i >= 0; i--)
            {
                var q = grp.held[i]; if (q.pa != this && q.pb != this) continue;
                var mine = q.pa == this ? q.a : q.b; var other = q.pa == this ? q.b : q.a;
                if (mine && other && Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out var dir, out var dist) && dist > 0f)
                { transform.position += dir * (dist + 0.01f); Physics.SyncTransforms(); PushedOut++; }
                if (q.a && q.b) Physics.IgnoreCollision(q.a, q.b, false);
                grp.held.RemoveAt(i);
            }
        }

        void Freeze()
        {
            frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            DeckRest(); OutOfStatics();                                                                       // K256k: never left inside a building, gun or rock (OzShuttleBreakup.Shell.cs)
                                                                                                  // lying on a repair station's deck: it clears away (OzShuttleBreakup.Look.cs)
            if (OzWaterLanding.InWater(transform)) { RestOn = "water"; InWaterCount++; Settle(); return; }
            float gap = Gap(out var low);
            var on = Under(low, out float support);
            bool ground = on == "ground";
            if (!ground && on != null) { RestOn = "on " + on; RestGap = support; OnOther++; Settle(); return; }
            if (!ground && gap > 0.06f && otherName != null && age - otherAt < 0.6f) { RestOn = "on " + otherName; RestGap = 0f; OnOther++; Settle(); return; }
            if (ground || (gap < 0.35f && gap > -0.35f))
            {
                float step = -Sink - gap;
                float lim = grp != null ? Mathf.Min(Mathf.Max(DepthIfMoved(Vector3.zero), 0.01f) + 0.015f, HoldDepth - 0.005f) : 0f;
                if (step < 0f && grp != null && DepthIfMoved(Vector3.up * step) > lim) step = Mathf.Min(0f, -gap - 0.005f);
                if (step < 0f && grp != null && DepthIfMoved(Vector3.up * step) > lim)
                { float s0 = 0f, s1 = step; for (int i = 0; i < 6; i++) { float m = (s0 + s1) * 0.5f; if (DepthIfMoved(Vector3.up * m) > lim) s1 = m; else s0 = m; } step = s0; }
                RestOn = "ground"; snapFrom = transform.position; snapTo = transform.position + Vector3.up * step; snapT = 0f; OnGround++;
                if (bigFire && !fired) { fired = true; FireAtRestBeat(low); }                                  // the wreck fire under it (+ the main wreck's scorch)
                return;
            }
            RestOn = gap > 0f ? "floating" : "sunk"; RestGap = gap; if (gap > 0f) Floating++; else Sunk++;
            Settle();
        }

        void Settle() { Rested = true; RestedCount++; }

        float DepthIfMoved(Vector3 delta)
        {
            float deep = 0f;
            if (grp == null || !col) return 0f;
            foreach (var o in grp.pieces)
            {
                if (!o || o == this || !o.col || !o.col.enabled || !col.enabled) continue;
                if (Physics.ComputePenetration(col, col.transform.position + delta, col.transform.rotation, o.col, o.col.transform.position, o.col.transform.rotation, out _, out float d)) deep = Mathf.Max(deep, d);
            }
            return Mathf.Max(deep, OzRockBody.RockDepthIfMoved(col, delta));                                      // items 331 + 332: nor into a rock
        }

        string Under(Vector3 low, out float support)
        {
            support = 0f;
            var hs = Physics.RaycastAll(low + Vector3.up * 0.4f, Vector3.down, 0.48f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider c = null;
            foreach (var h in hs) { if (h.collider == col || h.collider.transform.IsChildOf(transform)) continue; if (h.distance < best) { best = h.distance; c = h.collider; } }
            if (!c) return null;
            support = best - 0.4f;
            if (c.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(c)) return "ground";
            var host = c.GetComponentInParent<OzShuttleBreakup>();
            return host ? host.PieceName : c.transform.root.name;
        }

        void Burn() => BurnBeat();      /* item 400: each fire its own beat (OzShuttleBreakup.FireBeat.cs) */                                                                    // the fire and its dark smoke column, sized for the RTS camera (OzShuttleBreakup.Look.cs)

        void Retire()
        {
            if (retiring) return;
            retiring = true; snapT = -1f; retryAt = -1f;
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            if (col) col.enabled = false;
            sinkSpeed = (grp != null ? grp.sinkDepth : 2f) / 3f; sinkT = 0f;
            if (OzRockBody.PopIfOnRock(gameObject, () => Faded++, true)) sinkSpeed = 0f;                          // items 331 + 332: on a rock it pops away, never sinks through it
        }

        void Sinking(float dt) { sinkT += dt; transform.position += Vector3.down * sinkSpeed * dt; if (sinkT >= 3f) { Faded++; Destroy(gameObject); } }

        void OnCollisionEnter(Collision c)
        {
            if (keeper || retiring || frozen) return;
            float sp = c.relativeVelocity.magnitude;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position; touchAt = at;
            if (age < 0.06f || hits >= 6 || age - lastHit < 0.25f || sp < 1.2f) return;
            if (OzWaterLanding.UnderSurface(at)) return;                                                      // its splash is made at the surface
            if (!LandingContact(c, at, sp)) return;                                                // K256h: no landing crash, scorch, dust, kick or landing sound up in the air (OzShuttleBreakup.Altitude.cs)
            lastHit = age; hits++;
            bool firstContact = hits == 1; if (firstContact) FirstContacts++;
            bool onScreen = WreckFx.OnScreen(at);
            if (Kind == "body" && grp != null && !grp.slammed && sp > 2.5f)
            {   // the body's first slam: the wreck crash, sparks, a scorch, dust, the camera kick and the debris rain once
                grp.slammed = true; BodySlams++;
                var au = CombatAudio.I; if (au) au.PlayPitched("wreck_crash", at, 1f, 0f, grp.emitter, Random.Range(0.85f, 0.95f), LegacyMatch.BankTankImpact);
                OpenZHitSparks.Burst(at + Vector3.up * 0.2f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.6f, false);
                ScorchDecals.Add(at, 2.6f, ScorchKind.Wreck);
                var fxp = OpenZFxPlayer.I; if (onScreen && fxp && fxp.isActiveAndEnabled) fxp.DemolitionDust(at, 2.6f, 2.6f, 0.5f);
                if (onScreen) { var rc = FindFirstObjectByType<RtsCamera>(); if (rc) rc.Shake(0.3f, at, "wreck_slam"); }
                PlayAd("exp_debris_rain", at, grp.emitter + 1);
            }
            if (!crashed && MassKg >= 250f && sp > 2.5f && onScreen)
            {
                crashed = true;
                var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, Mathf.Min(1.4f, 0.5f + sp * 0.06f));
                if (PlayCrash(at, Mathf.Clamp01(0.55f + sp * 0.05f), GetInstanceID())) { Crashes++; if (firstContact) FirstContactsHeard++; return; }
            }
            float vol = Mathf.Clamp01(0.3f + sp * 0.07f), pitch = MassKg > 1500f ? 0.8f : MassKg > 200f ? 0.9f : 1.05f;
            if (firstContact ? FirstClank(at, GetInstanceID(), vol, pitch) : PlayAd("dst_debris_metal", at, GetInstanceID(), vol, pitch)) { Clanks++; if (firstContact) FirstContactsHeard++; }
            else if (firstContact && grp != null) QueueClank(at, vol, pitch, GetInstanceID());           // every piece's first contact is heard: queued past the sound's gap
            else if (retryAt < 0f) { retryAt = age + 0.07f; retryVol = vol; retryPitch = pitch; retryPos = at; }
        }

        void OnCollisionStay(Collision c)
        {
            if (keeper || frozen) return;
            touching = true; stayAt = age;
            if (c.contactCount > 0) touchAt = c.GetContact(0).point;
            if (!(c.collider.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(c.collider))) { var host = c.collider.GetComponentInParent<OzShuttleBreakup>(); otherName = host ? host.PieceName : c.collider.transform.root.name; otherAt = age; }
        }

        void Start() { if (!keeper && rb) OzWaterLanding.Watch(transform, "shuttle piece", rb); }

        void OnDestroy()
        {
            live.Remove(this);
            if (grp != null) grp.pieces.Remove(this);
            if (fall != null) FallGone();                                                                     // a drop cut short (the level left): its count and its fire sound end
        }

        // ------------------------------------------------------------------ checks
        public static int Shown() { int n = 0; foreach (var p in LastPieces) if (p && p.isActiveAndEnabled && p.vis && p.vis.GetComponent<Renderer>().enabled) n++; return n; }
        public static bool LastAllRested() { if (LastPieces.Count == 0) return false; foreach (var p in LastPieces) if (p && !p.Rested) return false; return true; }
        public static int ThroughEachOther(out float worst, out string which)
        {
            worst = 0f; which = "-"; int n = 0;
            for (int i = 0; i < LastPieces.Count; i++)
                for (int j = i + 1; j < LastPieces.Count; j++)
                {
                    var a = LastPieces[i]; var b = LastPieces[j]; if (!a || !b || !a.col || !b.col || !a.col.enabled || !b.col.enabled) continue;
                    float d = Depth(a.col, b.col);
                    if (d > worst) { worst = d; which = a.PieceName + " / " + b.PieceName; }
                    if (d > HoldDepth) n++;
                }
            return n;
        }
        /// <summary>Pieces floating more than 6 cm over the ground or sunk into it after they rested (not those lying on another piece or in water).</summary>
        public static int BadRests(out string which)
        {
            int n = 0; which = "-";
            foreach (var p in LastPieces) { if (!p || !p.Rested) continue; if (p.RestOn == "floating" || p.RestOn == "sunk") { n++; which = p.PieceName + " " + p.RestOn + " " + p.RestGap.ToString("F2"); } }
            return n;
        }
    }
}

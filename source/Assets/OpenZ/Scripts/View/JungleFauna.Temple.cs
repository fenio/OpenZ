using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: a few monkeys sit on the two stepped pyramids (4 on the big one, 3 on the small one), on the lower and middle steps,
    /// never on the platform or the temple on top. Scenery like the level's other monkeys: no game object of the rules, no collider,
    /// never a target. Places and ways: JungleTempleMonkeysData.cs (checked on the pyramid's own mesh).
    /// <list type="bullet">
    /// <item>SITTING: the tree monkeys' own sit / scratch and look-around takes, a new choice every 4 to 9 s; a unit within twice the
    /// fright distance: it stops scratching and watches. On stone the tail does not hang down as from a branch: it is laid down
    /// behind the rump and round its left side on the stone (<see cref="LayTail"/>).</item>
    /// <item>FRIGHT (the tree monkeys' rule, measured from each seat): a walking robot within 9 m, a moving vehicle within 13 m, a
    /// standing robot within 4.5 m, the shuttle near, a blast within 14 m. The whole pyramid's troop stands up (alarm call), one after
    /// the other, and runs along its tread, hopping over the corner stones and plants on the way, to a place behind the pyramid the
    /// camera cannot see; there it is not drawn. After 40 to 70 quiet seconds (no unit and no blast within 14 m of any seat) they come
    /// back the same way, one by one.</item>
    /// <item>NIGHT AND RAIN (OzJungleLife.Hushed): they leave the same way, quietly and slower, and come back a few seconds after the
    /// day or the dry weather returns. A level that starts at night or in rain has them away from the first frame.</item>
    /// <item>A pyramid that is damaged or destroyed: its monkeys run off and do not come back.</item>
    /// </list>
    /// Their sounds are the tree monkeys' events (alarm, chatter, scratch, landing); every one is a line of the sound log. Proof:
    /// <see cref="TempleStatus"/>, <see cref="TempleMonkeyList"/>, the Play check Editor/OzL16TempleMonkeysCheck.cs.
    /// </summary>
    public sealed partial class JungleFauna
    {
        /// <summary>Run speeds (m/s): away on fright, back, away for the night or the rain.</summary>
        public static float TempleRunAway = 6.5f, TempleRunBack = 3.6f, TempleRunQuiet = 3.2f;
        /// <summary>Quiet seconds before a frightened troop comes back (from .. to); seconds after the night or the rain.</summary>
        public static float TempleBackMin = 40f, TempleBackMax = 70f, TempleBackAfterHushMin = 3f, TempleBackAfterHushMax = 8f;
        public static int TempleTotal, TempleSitting, TempleHidden, TempleFlights, TempleQuietLeaves, TempleReturns, TempleHops;

        enum TS { Sit, Alarm, Away, Gone, Back }
        sealed class TApe
        {
            public int id, temple; public L16AnimalKit.Rig rig; public System.Random rng; public TS st;
            public Vector3 seat; public Quaternion seatRot; public float seatYaw;
            public Vector3[] way; public float[] apex; public float wayLen;
            public int target, dir; public bool hopping, quiet, look; public Vector3 from, pos; public float hopT, hopLen, hopApex, yaw;
            public float t, delay, idleT, chatterAt;
            public Transform[] tail; public Vector3[] tailAxis;
        }
        readonly List<TApe> tapes = new List<TApe>();
        readonly L16Pyramid[] templeOf = new L16Pyramid[2];
        readonly float[] templeCheckT = new float[2], templeQuiet = new float[2], templeWait = new float[2], templeNextBack = new float[2];
        readonly bool[] templeLost = new bool[2], templeHushed = new bool[2];

        static L16Pyramid FindTemple(string uid)
        {
            foreach (var p in L16Pyramid.All) if (p && p.name == uid) return p;
            return null;
        }

        void MakeTempleApes()
        {
            TempleTotal = TempleSitting = TempleHidden = TempleFlights = TempleQuietLeaves = TempleReturns = TempleHops = 0;
            for (int k = 0; k < 2; k++) templeOf[k] = FindTemple(JungleTempleMonkeysData.Uids[k]);
            for (int i = 0; i < JungleTempleMonkeysData.Names.Length; i++)
            {
                int tk = JungleTempleMonkeysData.Temple[i]; var pyr = templeOf[tk];
                if (!pyr) continue;                                                                  // no pyramid on this level copy: no monkeys for it
                var rig = L16AnimalKit.Rig.Make("Monkey", "TempleMonkey_" + JungleTempleMonkeysData.Names[i], transform, new[] { monkeyMat }, 1f, true);
                if (rig == null) return;
                rig.FaceAhead("head", "tail_2");
                var a = new TApe { id = i, temple = tk, rig = rig, rng = new System.Random(seed * 211 + i * 7867 + 11), st = TS.Sit };
                var tr = pyr.transform;
                a.seat = tr.TransformPoint(JungleTempleMonkeysData.Seat[i] + Vector3.up * 0.005f);
                a.seatYaw = tr.eulerAngles.y + JungleTempleMonkeysData.Yaw[i];
                var turn = Quaternion.Euler(0f, a.seatYaw, 0f); var lean = JungleTempleMonkeysData.Lean[i];
                var n = turn * new Vector3(-lean.x, 1f, -lean.y).normalized;
                a.seatRot = Quaternion.FromToRotation(Vector3.up, n) * turn;                          // it leans with the stone (under 2.2 deg on every seat)
                var w = JungleTempleMonkeysData.Way[i];
                a.way = new Vector3[w.Length]; a.apex = new float[w.Length];
                for (int k = 0; k < w.Length; k++) { a.way[k] = tr.TransformPoint(new Vector3(w[k].x, w[k].y, w[k].z)); a.apex[k] = w[k].w; if (k > 0) a.wayLen += Flat(a.way[k], a.way[k - 1]); }
                a.way[0] = new Vector3(a.seat.x, a.way[0].y, a.seat.z);
                a.pos = a.seat; a.yaw = a.seatYaw; a.idleT = R(a.rng, 1f, 7f); a.t = R(a.rng, 0f, 2f);
                // the tail bones and the way each one points in its own frame (its child's place; the last one as the one before)
                a.tail = new Transform[5]; a.tailAxis = new Vector3[5];
                for (int k = 0; k < 5; k++) a.tail[k] = L16AnimalKit.Find(rig.model, "tail_" + (k + 1));
                bool ok = true; for (int k = 0; k < 5; k++) ok &= a.tail[k];
                if (ok)
                {
                    for (int k = 0; k < 4; k++) a.tailAxis[k] = a.tail[k + 1].localPosition.normalized;
                    a.tailAxis[4] = a.tailAxis[3];
                }
                else a.tail = null;
                rig.root.SetPositionAndRotation(a.seat, a.seatRot);
                tapes.Add(a);
            }
            TempleTotal = tapes.Count;
            if (tapes.Count > 0) Debug.Log($"[OpenZ] JungleFauna L16: {tapes.Count} monkeys on the pyramids ({TempleCount(0)} on the big one, {TempleCount(1)} on the small one)");
        }

        int TempleCount(int k) { int c = 0; foreach (var a in tapes) if (a.temple == k) c++; return c; }

        /// <summary>On stone the tail lies down: each tail bone is turned so it points the laid way (after the take's pose).</summary>
        void LayTail(TApe a)
        {
            if (a.tail == null || !a.rig.Shown || !a.rig.Visible()) return;
            var rot = a.rig.root.rotation;
            for (int k = 0; k < 5; k++)
            {
                var b = a.tail[k]; var cur = b.rotation * a.tailAxis[k]; var want = rot * JungleTempleMonkeysData.TailDirs[k];
                if (cur.sqrMagnitude < 1e-8f) continue;
                b.rotation = Quaternion.FromToRotation(cur, want) * b.rotation;
            }
        }

        /// <summary>What frightens a pyramid's troop now (the tree troops' distances, from each seat of a monkey that is there).</summary>
        bool TempleThreat(int tk, out Vector3 at)
        {
            at = Vector3.zero;
            foreach (var a in tapes)
            {
                if (a.temple != tk || a.st == TS.Gone) continue;
                var p = a.st == TS.Sit ? a.seat : a.pos;
                if (NearestUnit(p, 1, out at) <= monkeyRobot) return true;
                if (NearestUnit(p, 2, out at) <= monkeyVehicle) return true;
                if (ShipNear(p, 16f, out var sAt)) { at = sAt; return true; }
                if (NearestUnit(p, 3, out at) <= monkeyRobot * 0.5f) return true;
                if (Time.time - lastBlastT < 1.5f && Flat(lastBlast, p) < monkeyBlast) { at = lastBlast; return true; }
            }
            return false;
        }

        bool TempleCalm(int tk)
        {
            foreach (var a in tapes)
            {
                if (a.temple != tk) continue;
                if (NearestUnit(a.seat, 0, out _) <= 14f) return false;
                if (Time.time - lastBlastT < 1.5f && Flat(lastBlast, a.seat) < monkeyBlast) return false;
            }
            return true;
        }

        /// <summary>Off along its way: the shortest way first, the others 0.2 to 0.6 s later each (fright) or 0.6 s (quiet).</summary>
        void TempleLeave(int tk, bool quiet)
        {
            var list = new List<TApe>(); foreach (var a in tapes) if (a.temple == tk && (a.st == TS.Sit || a.st == TS.Back)) list.Add(a);
            list.Sort((x, y) => x.wayLen.CompareTo(y.wayLen));
            float delay = 0f;
            foreach (var a in list)
            {
                a.quiet = quiet;
                if (a.st == TS.Back) { if (!a.hopping) { a.dir = 1; a.target = Mathf.Min(a.target + 1, a.way.Length - 1); } else a.dir = 1; a.st = TS.Away; continue; }
                a.st = quiet ? TS.Away : TS.Alarm; a.t = 0f; a.delay = delay; a.dir = 1; a.target = 1; a.hopping = false; a.pos = a.seat;
                a.rig.root.localScale = Vector3.one;
                delay += quiet ? 0.6f : R(a.rng, 0.2f, 0.6f);
                if (quiet) { a.t = -delay; TempleQuietLeaves++; }
            }
            if (!quiet && list.Count > 0) TempleFlights++;
            templeQuiet[tk] = 0f;
        }

        void StepTempleApes(float dt)
        {
            for (int tk = 0; tk < 2; tk++)
            {
                if (TempleCount(tk) == 0) continue;
                var pyr = templeOf[tk];
                if (!templeLost[tk] && (!pyr || pyr.Stage != 0))
                {   // damaged or gone: off for good
                    templeLost[tk] = true;
                    foreach (var a in tapes) if (a.temple == tk && (a.st == TS.Sit || a.st == TS.Back) && !Seen(a.pos, 4f)) { a.st = TS.Gone; a.rig.Show(false); }
                    TempleLeave(tk, false);
                }
                // the night or the rain: away quietly; at the level's start they are away from the first frame
                if (hush && !templeHushed[tk])
                {
                    templeHushed[tk] = true;
                    if (clock < 0.5f) { foreach (var a in tapes) if (a.temple == tk) { a.st = TS.Gone; a.rig.Show(false); } }
                    else TempleLeave(tk, true);
                }
                else if (!hush && templeHushed[tk]) { templeHushed[tk] = false; templeQuiet[tk] = 0f; templeWait[tk] = R(tapes[0].rng, TempleBackAfterHushMin, TempleBackAfterHushMax); }
                // fright, five looks a second
                if (!templeLost[tk] && (templeCheckT[tk] -= dt) <= 0f)
                {
                    templeCheckT[tk] = 0.2f;
                    if (TempleThreat(tk, out _))
                    {
                        bool any = false; foreach (var a in tapes) if (a.temple == tk && (a.st == TS.Sit || a.st == TS.Back)) any = true;
                        if (any) TempleLeave(tk, false);
                        if (templeWait[tk] < TempleBackMin) templeWait[tk] = R(tapes[0].rng, TempleBackMin, TempleBackMax);
                        templeQuiet[tk] = 0f;
                    }
                }
                // back, one by one, the longest way first, after the quiet seconds
                bool anyGone = false; foreach (var a in tapes) if (a.temple == tk && a.st == TS.Gone) anyGone = true;
                if (anyGone && !templeLost[tk] && !hush)
                {
                    templeQuiet[tk] = TempleCalm(tk) ? templeQuiet[tk] + dt : 0f;
                    if (templeWait[tk] <= 0f) templeWait[tk] = R(tapes[0].rng, TempleBackMin, TempleBackMax);
                    if (templeQuiet[tk] >= templeWait[tk] && (templeNextBack[tk] -= dt) <= 0f)
                    {
                        TApe next = null; bool moving = false;
                        foreach (var a in tapes) if (a.temple == tk) { if (a.st == TS.Away || a.st == TS.Alarm) moving = true; if (a.st == TS.Gone && (next == null || a.wayLen > next.wayLen)) next = a; }
                        if (next != null && !moving)
                        {
                            next.st = TS.Back; next.dir = -1; next.target = next.way.Length - 2; next.hopping = false; next.pos = next.way[next.way.Length - 1];
                            next.t = 0f; next.rig.Show(true); TempleReturns++; templeNextBack[tk] = R(next.rng, 1.5f, 3f);
                            if (next.target < 0) next.target = 0;
                        }
                    }
                }
                else if (!anyGone) { templeWait[tk] = 0f; templeNextBack[tk] = 0f; }
            }
            int sitting = 0, hidden = 0;
            foreach (var a in tapes)
            {
                StepTempleApe(a, dt);
                if (a.st == TS.Sit) sitting++; else if (a.st == TS.Gone) hidden++;
            }
            TempleSitting = sitting; TempleHidden = hidden;
        }

        void StepTempleApe(TApe a, float dt)
        {
            bool vis = Seen(a.pos, 4f);
            switch (a.st)
            {
                case TS.Sit:
                {
                    a.t += dt; bool fresh = false;
                    if (!Night && (a.idleT -= dt) <= 0f) { a.idleT = R(a.rng, 4f, 9f); a.look = a.rng.Next(2) == 0; a.t = 0f; fresh = true; }
                    if (NearestUnit(a.seat, 0, out _) < monkeyRobot * 2f) a.look = true;             // the tell before it runs: it watches
                    if (fresh && !a.look && vis) Snd("critter_monkey_scratch", a.seat, 0.25f, 3f, 30f);
                    a.pos = a.seat; a.yaw = a.seatYaw;
                    a.rig.root.SetPositionAndRotation(a.seat, a.seatRot);
                    PoseFrames(a.rig, a.look ? Loop(a.rig, LookMA, LookMB, a.t) : Loop(a.rig, SitA, SitB, a.t), 0f, 0f);
                    LayTail(a);
                    break;
                }
                case TS.Alarm:
                    if ((a.delay -= dt) > 0f) { PoseFrames(a.rig, Loop(a.rig, LookMA, LookMB, clock), 0f, 0f); LayTail(a); break; }
                    if (a.t == 0f) { Snd("critter_monkey_alarm", a.pos, 0.8f, 10f, 90f); a.chatterAt = a.rng.Next(3) == 0 ? R(a.rng, 0.2f, 0.6f) : 0f; }
                    a.t += dt;
                    if (a.chatterAt > 0f && a.t >= a.chatterAt) { a.chatterAt = 0f; Snd("critter_monkey_chatter", a.pos, 0.6f, 8f, 80f, "sd_monkey_chatter_a", "sd_monkey_chatter_b"); }
                    a.rig.root.SetPositionAndRotation(a.seat, a.seatRot);
                    PoseFrames(a.rig, Once(a.rig, AlarmA, AlarmB, a.t), 0f, 0f);
                    if (a.t >= 1f) { a.st = TS.Away; a.t = 0f; a.target = 1; a.dir = 1; a.hopping = false; a.pos = a.seat; }
                    break;
                case TS.Away:
                case TS.Back:
                    if (a.t < 0f) { a.t += dt; PoseFrames(a.rig, Loop(a.rig, SitA, SitB, clock), 0f, 0f); LayTail(a); break; }   // its turn to leave quietly has not come yet
                    MoveOnWay(a, dt, vis);
                    break;
                case TS.Gone:
                    break;
            }
        }

        /// <summary>Along its way point to point: running on the tread (the stone's height), hopping where the way says.</summary>
        void MoveOnWay(TApe a, float dt, bool vis)
        {
            bool back = a.st == TS.Back;
            float speed = back ? TempleRunBack : a.quiet ? TempleRunQuiet : TempleRunAway;
            a.t += dt;
            if (!a.hopping)
            {
                if (a.target < 0 || a.target >= a.way.Length)
                {
                    if (!back) { a.st = TS.Gone; a.rig.Show(false); }
                    else
                    {   // home: turn to its seat's way and sit
                        a.st = TS.Sit; a.pos = a.seat; a.yaw = a.seatYaw; a.t = 0f; a.idleT = R(a.rng, 2f, 6f); a.look = false;
                        a.rig.root.SetPositionAndRotation(a.seat, a.seatRot);
                    }
                    return;
                }
                // the leg from here to the target: a hop when the way marks the far end of it (going away) or the near end (coming back)
                int hopEnd = a.dir > 0 ? a.target : a.target + 1;
                bool hopLeg = hopEnd >= 0 && hopEnd < a.apex.Length && a.apex[hopEnd] > 0f;
                if (hopLeg)
                {
                    a.hopping = true; a.from = a.pos; a.hopT = 0f; a.hopApex = a.apex[hopEnd];
                    a.hopLen = 0.45f + 0.22f * Flat(a.from, a.way[a.target]); TempleHops++;
                }
            }
            var to = a.way[Mathf.Clamp(a.target, 0, a.way.Length - 1)];
            if (a.hopping)
            {
                a.hopT += dt; float k = Mathf.Clamp01(a.hopT / a.hopLen);
                var p = Vector3.Lerp(a.from, to, k); p.y += Mathf.Sin(k * Mathf.PI) * a.hopApex; a.pos = p;
                var d = to - a.from; if (d.sqrMagnitude > 1e-4f) a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 12f));
                a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                PoseFrames(a.rig, Once(a.rig, JumpA, JumpB, k * (JumpB - JumpA) / 24f), 0f, 0f);
                if (k >= 1f)
                {
                    a.hopping = false; a.pos = to; a.target += a.dir;
                    if (vis) Snd("critter_monkey_land", a.pos, 0.4f, 5f, 50f);                          // no file yet: a named silent event (the tree monkeys' landing)
                }
                return;
            }
            var dd = to - a.pos; dd.y = 0f; float l = dd.magnitude;
            if (l <= speed * dt) { a.pos = to; a.target += a.dir; }
            else
            {
                var prev = a.way[Mathf.Clamp(a.target - a.dir, 0, a.way.Length - 1)];
                a.pos += dd / l * speed * dt;
                float seg = Flat(prev, to); float u = seg > 1e-4f ? Mathf.Clamp01(1f - Flat(a.pos, to) / seg) : 1f;
                a.pos.y = Mathf.Lerp(prev.y, to.y, u);
                a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 12f));
            }
            a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
            PoseFrames(a.rig, Loop(a.rig, RunA, RunB, a.t, back || a.quiet ? 0.8f : 1.2f), 0f, 0f);
        }

        // ------------------------------------------------------------------ proof
        /// <summary>Checks: every pyramid monkey: where it is, what it does (0 sits, 1 alarm, 2 away, 3 gone, 4 back), its pyramid,
        /// whether it is drawn, its seat.</summary>
        public static void TempleMonkeyList(List<Vector3> pos, List<int> state, List<int> temple, List<bool> drawn, List<Vector3> seat)
        {
            pos.Clear(); state.Clear(); temple.Clear(); drawn.Clear(); seat?.Clear();
            if (!I) return;
            foreach (var a in I.tapes) { pos.Add(a.pos); state.Add((int)a.st); temple.Add(a.temple); drawn.Add(a.rig.Shown); seat?.Add(a.seat); }
        }

        /// <summary>Checks: the model root of pyramid monkey i (its tail bones hang under it), or null.</summary>
        public static Transform TempleMonkeyRoot(int i) => I && i >= 0 && i < I.tapes.Count ? I.tapes[i].rig.root : null;

        /// <summary>Checks: a blast at a place as the game's own explosions report it (every animal of the level reacts as to a real one).</summary>
        public static void TestBlast(Vector3 at) { if (I) { I.lastBlast = at; I.lastBlastT = Time.time; } }

        public static string TempleStatus()
        {
            if (!I) return "JungleFauna not running (not level 16)";
            return $"pyramid monkeys {TempleTotal} ({I.TempleCount(0)} big, {I.TempleCount(1)} small): {TempleSitting} sitting, {TempleHidden} away and not drawn; frights {TempleFlights}, quiet leaves {TempleQuietLeaves}, back {TempleReturns}, hops {TempleHops}; lost pyramids {(I.templeLost[0] ? 1 : 0) + (I.templeLost[1] ? 1 : 0)}";
        }
    }
}

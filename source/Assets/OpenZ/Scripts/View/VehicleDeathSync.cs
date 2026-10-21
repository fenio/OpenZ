using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 item 202: a vehicle or a gun explodes on the frame the shot that killed it is DRAWN hitting it.
    ///
    /// Found (counted trace, kit KC5b): the sim kills a vehicle in the tick of the hit (0 ms for every weapon) and CombatView drew its
    /// death explosion on that frame. Two ways of dying were still out of step with what is drawn:
    /// (1) an MLRS shot is ONE sim missile (rocket 1) with a 10 m splash, drawn as an 8-rocket ripple (MlrsSalvo): a vehicle 5-10 m from
    ///     rocket 1 (often the very target, which dodges out of rocket 1's point during the flight) blew up on rocket 1's frame although no
    ///     drawn rocket was near it, and the ripple rocket that came down beside it landed 0.1-1.7 s LATER. MlrsSalvo now sends a ripple
    ///     rocket onto every vehicle / gun the shot's splash can reach (MlrsSalvo.Marked.cs): it follows its vehicle and lands on it on
    ///     the sim's blast frame, so the death explosion comes with a rocket on the hull;
    /// (2) a vehicle killed by a ROUND (bolt, tracer, laser bolt, sniper crack, flame; also the round of a tank's hatch driver) blew up
    ///     0.1-0.45 s before the drawn round reached it (robots were already held for their killing round, BlastWave.BulletDelay): the
    ///     vehicle now waits for the drawn round.
    /// This class answers how long a vehicle death waits (0 = on the sim's frame, as before), cached per object and frame so CombatView,
    /// AudioDirector and BoardingView agree. While it waits the dead vehicle's view is held (UnitView.HoldForDeath: no sim sync); one that
    /// was driving rolls on along its last heading and slows to a stop by its explosion (within GlideStopS), so it never stops dead.
    /// Then CombatView's death plays exactly as before (wreck, scorch, explosion, sounds, crew). View only: the sim, its damage and its
    /// death tick are untouched.
    /// </summary>
    public static class VehicleDeathSync
    {
        /// <summary>Off = every vehicle / gun death on the sim's frame (the old way; for the before / after check). The marked MLRS rockets
        /// have their own switch, MlrsSalvo.MarkVehicles.</summary>
        public static bool Enabled = true;
        /// <summary>Rocket 1's drawn blast reaches a hull within this many metres of its point (a robot's core in BlastWave is 5 m to its centre).</summary>
        public const float CoreHullM = 3f;
        /// <summary>A ripple rocket's smaller drawn blast reaches a hull within this many metres of its point.</summary>
        public const float RippleReachM = 1.8f;
        /// <summary>Longest wait (s), as BlastWave.MaxDelay.</summary>
        public const float MaxDelay = 1.6f;
        /// <summary>A held vehicle that was driving slows from its speed to a stop within this many seconds (or by its explosion, if sooner).</summary>
        public const float GlideStopS = 0.4f;

        // statistics (Play proof)
        public static int OnFrame { get; private set; }
        public static int HeldForRound { get; private set; }
        public static int HeldForRipple { get; private set; }
        public static int MarkedHits { get; private set; }
        public static float MaxHeld { get; private set; }
        /// <summary>Play proof: the farthest a held vehicle rolled on before it exploded (m).</summary>
        public static float MaxGlide { get; private set; }

        /// <summary>One vehicle / gun death: the sim's frame, the wait (switched off: what it would have waited), why, (filled in when
        /// it played) the frame it exploded, and for a held round kill whether the drawing code noted its killing round and when that
        /// round arrived (BlastWave.DrawnArrival). The last 64, for the Play checks.</summary>
        public struct Record { public int id, frame, playedFrame; public string type, why; public float time, wait, playedAt, drawnAt, glide; public bool noted; }
        public static readonly List<Record> Last = new List<Record>();

        struct Entry { public float delay; public string why; }
        static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        static int cacheFrame = -1;

        /// <summary>Real seconds from now until vehicle / gun o (its Death event e) explodes; 0 = now.</summary>
        public static float Delay(ZGame g, ZEvent e, ZObject o) => Delay(g, e, o, out _);

        public static float Delay(ZGame g, ZEvent e, ZObject o, out string why)
        {
            why = "";
            if (g == null || g.World == null || o == null || (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) || e.otherId < 0) return 0f;
            if (cacheFrame != Time.frameCount) { cache.Clear(); cacheFrame = Time.frameCount; }
            if (cache.TryGetValue(o.Id, out var c)) { why = c.why; return Enabled ? c.delay : 0f; }
            float d = Compute(g, e, o, out why);                                                        // switched off: computed all the same (the BEFORE
            cache[o.Id] = new Entry { delay = d, why = why };                                           // report shows what it would wait), played at once
            if (Enabled)
            {
                if (d > 0.015f) { if (why.StartsWith("round")) HeldForRound++; else HeldForRipple++; if (d > MaxHeld) MaxHeld = d; }
                else { OnFrame++; if (why == "mlrs marked rocket") MarkedHits++; }
            }
            Last.Add(new Record { id = o.Id, frame = Time.frameCount, type = o.Type, why = why, time = Time.time, wait = d, playedFrame = -1 });
            if (Last.Count > 64) Last.RemoveAt(0);
            return Enabled ? d : 0f;
        }

        static float HullM(ZObject o, int px, int py)
        {
            float cx = o.LocX + o.WidthPix * 0.5f, cy = o.LocY + o.HeightPix * 0.5f;
            float dx = Mathf.Max(0f, Mathf.Abs(px - cx) - o.WidthPix * 0.5f), dy = Mathf.Max(0f, Mathf.Abs(py - cy) - o.HeightPix * 0.5f);
            return Mathf.Sqrt(dx * dx + dy * dy) / 8f;
        }

        static float Compute(ZGame g, ZEvent e, ZObject o, out string why)
        {
            why = "on the frame";
            var ev = g.World.Events; var killer = g.World.GetAny(e.otherId);
            // the killer's blast of this frame that reached it (the sim emits the MissileExploded, then its victims' deaths)
            bool blast = false; ZEvent best = default; float bd = float.MaxValue;
            for (int i = 0; i < ev.Count; i++)
            {
                var b = ev[i];
                if (b.type != ZEventType.MissileExploded || b.objId != e.otherId || b.radius <= 0) continue;
                float dx = b.x - o.CenterX, dy = b.y - o.CenterY, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= b.radius + 2f || d >= bd) continue;
                bd = d; best = b; blast = true;
            }
            if (blast)
            {
                if (killer == null || killer.Kind != ZKind.Vehicle || killer.Type != "missile_launcher" || best.flag || best.flag2) { why = "blast"; return 0f; }
                if (HullM(o, best.x, best.y) <= CoreHullM) { why = "mlrs rocket 1"; return 0f; }
                var p = g.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : g.CenterWorld(o);
                float half = Mathf.Max(o.WidthPix, o.HeightPix) / 16f;
                if (MlrsSalvo.HitOn(o.Id, killer.Id) || MlrsSalvo.LandedNear(killer.Id, p, half + RippleReachM)) { why = "mlrs marked rocket"; return 0f; }
                // not marked (it drove into the splash after the shot, or more than 7 vehicles in it): the ripple rocket that lands on it
                if (MlrsSalvo.NearestLanding(g, killer.Id, p, half + RippleReachM, 2.5f, out float landIn, out _) && landIn > 0.015f)
                { why = "mlrs ripple rocket"; return Mathf.Min(MaxDelay, landIn); }
                why = "mlrs, no rocket on it"; return 0f;
            }
            // a round of its killer that hit it this frame: when the drawn round arrives (Later refines it by the drawing code's note).
            // Its gun's round (Hitscan), or the round of the hatch driver of a tank (DriverShot, a direct hit: no rocket, no tesla bolt)
            bool shot = false, driver = false;
            for (int i = 0; i < ev.Count && !shot; i++)
            {
                var b = ev[i];
                if (b.objId != e.otherId || b.otherId != o.Id || !b.flag) continue;
                if (b.type == ZEventType.Hitscan) shot = true;
                else if (b.type == ZEventType.DriverShot && b.radius <= 0) shot = driver = true;
            }
            if (!shot || killer == null) return 0f;
            string type = driver ? killer.DriverGunType : BlastWave.RoundType(killer);
            if (driver && (type == ZElectro.Type || type == "tough")) return 0f;                           // his tesla bolt is drawn at once; a rocket is a blast
            var aim = g.Views.TryGetValue(o.Id, out var tv) && tv ? tv.AimPos() : g.CenterWorld(o) + Vector3.up;
            var muzzle = driver && BoardingView.DriverMuzzleNow(killer.Id, out var hatch) ? hatch : WeaponsV2Runner.DrawnMuzzle(g, killer);
            float dist = Vector3.Distance(muzzle, aim);
            why = (driver ? "round hatch " : "round ") + type;
            return Mathf.Min(MaxDelay, BlastWave.RoundFlight(type, dist, g.gameSpeed));
        }

        /// <summary>CombatView.Death, vehicle / gun: true = it waits (the death plays later from `host`, as CombatView would play it);
        /// false = play it now.</summary>
        public static bool Hold(MonoBehaviour host, ZGame g, ZEvent e, ZObject o, UnitView v, CombatAudio au)
        {
            float wait = Delay(g, e, o, out var why);
            if (!host || !v || wait <= 0.015f) { Played(o); return false; }
            v.HoldForDeath(wait + 0.5f);
            host.StartCoroutine(Later(e, o, v, wait, why, au, Drift(g, o)));
            return true;
        }

        /// <summary>The dead vehicle's last ground speed (m per real second) from the sim's heading (as ZGame.CenterWorldSmooth draws it);
        /// zero for one that stood or waited in a queue.</summary>
        static Vector3 Drift(ZGame g, ZObject o)
        {
            if (g == null || o == null || o.Kind != ZKind.Vehicle || !o.IsMoving || o.PathHeld) return Vector3.zero;
            return new Vector3(o.Dx / 8f, 0f, -o.Dy / 8f) * Mathf.Max(0.05f, g.gameSpeed);
        }

        /// <summary>One frame of the held vehicle rolling on: its speed falls off linearly to 0 at `stop`; it keeps to the ground.</summary>
        static float Glide(UnitView v, Vector3 vel, float t0, float stop)
        {
            if (!v || vel.sqrMagnitude < 1e-4f || Time.time >= stop) return 0f;
            var g = ZGame.I; float k = Mathf.Clamp01((stop - Time.time) / Mathf.Max(0.02f, stop - t0));
            var p = v.transform.position; var q = p + vel * (k * Time.deltaTime);
            if (g) q.y = p.y + g.GroundY(q.x, q.z) - g.GroundY(p.x, p.z);
            v.transform.position = q;
            return Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(q.x, 0f, q.z));
        }

        static IEnumerator Later(ZEvent e, ZObject o, UnitView v, float wait, string why, CombatAudio au, Vector3 vel)
        {
            float t0 = Time.time, due = t0 + wait, rolled = 0f; int f0 = Time.frameCount;
            rolled += Glide(v, vel, t0, t0 + Mathf.Min(GlideStopS, wait));                                // this frame: no stop dead (its own sync is held)
            yield return null;
            if (why.StartsWith("round") && BlastWave.DrawnArrival(e.otherId, o.Id, f0, out float at)) { due = at; Noted(o, at); }   // the drawing code noted its killing round: exactly then
            while (Time.time < due) { rolled += Glide(v, vel, t0, t0 + Mathf.Min(GlideStopS, due - t0)); yield return null; }
            if (!v) yield break;
            var p = v.transform.position;
            if (OzShuttleBreakup.TakesDeath(o, v, p, FxManager.I, au ? au : CombatAudio.I)) { Played(o, rolled); yield break; }   // DV118: the shuttle falls apart (View/OzShuttleBreakup.cs)
            v.Die(false, true, p);
            ScorchDecals.Add(p, 2.2f, ScorchKind.Wreck);                                                // as CombatView.Death
            LegacyMatch.UnitDeath(o, p, FxManager.I, au ? au : CombatAudio.I);
            if (rolled > MaxGlide) MaxGlide = rolled;
            Played(o, rolled);
        }

        static void Played(ZObject o, float glide = 0f)
        {
            for (int i = Last.Count - 1; i >= 0; i--)
                if (Last[i].id == o.Id && Last[i].playedFrame < 0) { var r = Last[i]; r.playedFrame = Time.frameCount; r.playedAt = Time.time; r.glide = glide; Last[i] = r; return; }
        }

        static void Noted(ZObject o, float at)
        {
            for (int i = Last.Count - 1; i >= 0; i--)
                if (Last[i].id == o.Id && Last[i].playedFrame < 0) { var r = Last[i]; r.noted = true; r.drawnAt = at; Last[i] = r; return; }
        }

        /// <summary>AudioDirector / BoardingView: run `then` after `wait` s on `host` (scaled time: a paused game waits).</summary>
        public static IEnumerator After(float wait, System.Action then)
        {
            float due = Time.time + wait;
            while (Time.time < due) yield return null;
            then?.Invoke();
        }
    }
}

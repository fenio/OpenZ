using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle as a target while it flies (rule DV118, view only). The rules aim every weapon at the ship's place on the
    /// map; the view draws the ship up to 12 m over that place. Here the drawn shots are taken up to the drawn ship:
    ///  * the aim point: the hull's middle (<see cref="MidM"/> over the pad soles) at the ship's drawn height, for shells and rockets
    ///    the height it will have when they arrive (its climb or sink over the flight, at most 6 m);
    ///  * a hit ends on the drawn hull (CombatView's surface test from the muzzle toward the hull's middle);
    ///  * a miss passes beside the hull (4.2 - 5.5 m from its middle) and flies on into the sky: no ground puff, no impact in mid-air
    ///    (<see cref="SkyMiss"/>, the impact layers ask <see cref="IsSkyMiss"/>);
    ///  * a blast at it bursts at the hull; the extra rockets of a salvo burst round it in the air; nothing under it on the ground
    ///    (no crater, no burning ground, no ground strikes, no robots knocked down below).
    /// The same calls keep the counts for the Play proof's weapons gallery (<see cref="Recording"/>: per weapon the shots, the hits,
    /// where each drawn shot started and ended, its height over the ground) and the last hit on the ship (where it came from and how
    /// hard), which the break-up uses for the direction its pieces are thrown.
    /// </summary>
    public static class OzShuttleAim
    {
        /// <summary>The hull's middle over the pad soles (m), the least miss distance from it (m), the miss line's run past the ship (m), a
        /// miss end that high over the ground is in the sky (m).</summary>
        public const float MidM = 2.2f, MissNear = 4.2f, MissFar = 5.5f, MissRun = 16f, SkyM = 4f;
        /// <summary>The drawn hull's box in the ship's root space (x across, y up from the pad soles, z along the nose).</summary>
        public static readonly Vector3 HullMin = new Vector3(-3.3f, 0.3f, -3.5f), HullMax = new Vector3(3.3f, 5.3f, 3.5f);

        /// <summary>The ship the last <see cref="Air"/> asked about (every entry asks first; several shuttles may fly).</summary>
        static OzPlayableShuttle last;
        static OzPlayableShuttle Ship => last ? last : OzPlayableShuttle.I;

        /// <summary>t is a playable shuttle, drawn, in the air.</summary>
        public static bool Air(ZObject t)
        {
            if (t == null || !t.IsShuttle) return false;
            var s = OzPlayableShuttle.Of(t); if (s) last = s;
            return t.ShuttleAirborne && s && !s.BrokenUp && s.isActiveAndEnabled;
        }
        public static bool AirId(int id) { var s = OzPlayableShuttle.OfId(id); return s && s.Obj != null && Air(s.Obj); }
        /// <summary>Up high enough that a shot at it goes up (the pads more than 2.5 m over the ground).</summary>
        public static bool High(ZObject t) => Air(t) && Ship.ShownHeight >= 2.5f;

        static float Gs { get { var g = ZGame.I; return g ? Mathf.Max(0.05f, g.gameSpeed) : 1f; } }

        /// <summary>The drawn root's height `simDelay` sim seconds from now (its present climb or sink, at most 6 m either way).</summary>
        static float RootYAt(float simDelay)
        {
            var s = Ship; float y = s.transform.position.y;
            if (simDelay <= 0f) return y;
            float dy = Mathf.Clamp(s.Velocity.y * simDelay / Gs, -6f, 6f);
            var g = ZGame.I; float gy = g ? g.GroundY(s.transform.position.x, s.transform.position.z) : y - s.ShownHeight;
            return Mathf.Max(gy + 0.5f, y + dy);
        }

        /// <summary>CombatView / UnitAim / FortGunView: the point to aim at for t. For the flying shuttle: `ground`'s place (the rules'
        /// aim, with their lead) at the hull's middle height it will have after simDelay; anything else: `ground` unchanged.</summary>
        public static Vector3 AirAim(ZObject t, Vector3 ground, float simDelay)
        {
            if (!Air(t)) return OzAirShots.LostAim(t, ground);                                         // K337: a round in flight when the ship broke up bursts up there (View/OzAirShots.cs)
            return OzAirShots.AimUp(t, new Vector3(ground.x, RootYAt(simDelay) + MidM, ground.z));      // K337: inside the map and under the ceiling; the ship's height remembered
        }

        /// <summary>CombatView.DrawShot, a hit: aim the surface test at the hull's middle (the root is the pad soles: the old aim point
        /// lay under the belly between the legs).</summary>
        public static Vector3 HitAim(ZObject t, Vector3 to) => Air(t) ? Ship.transform.TransformPoint(new Vector3(0f, MidM, 0f)) : to;

        /// <summary>The distance (m) from a world point to the drawn hull's box (0 inside).</summary>
        public static float HullDist(Vector3 p)
        {
            var s = Ship; if (!s) return 999f;
            var l = s.transform.InverseTransformPoint(p);
            var c = new Vector3(Mathf.Clamp(l.x, HullMin.x, HullMax.x), Mathf.Clamp(l.y, HullMin.y, HullMax.y), Mathf.Clamp(l.z, HullMin.z, HullMax.z));
            return (l - c).magnitude;
        }

        // ------------------------------------------------------------------ misses into the sky
        struct SkyEnd { public Vector3 p; public float t; }
        static readonly List<SkyEnd> sky = new List<SkyEnd>(64);
        public static int SkyMisses;

        /// <summary>A miss at the flying ship from `from`: a line past the hull (4.2 - 5.5 m from its middle, round the line of fire) that
        /// flies on 16 m into the sky. A shooter above the ship (a cliff) misses into the ground below as before.</summary>
        public static Vector3 SkyMiss(Vector3 from, ZObject t)
        {
            if (!Air(t)) return from;
            var s = Ship;
            var c = s.transform.TransformPoint(new Vector3(0f, MidM, 0f));
            var d = c - from; float len = d.magnitude; if (len < 0.5f) return c + Vector3.up * MissRun;
            var n = d / len;
            var a = Vector3.Cross(n, Mathf.Abs(n.y) > 0.95f ? Vector3.right : Vector3.up).normalized; var b = Vector3.Cross(n, a);
            float ang = Random.value * Mathf.PI * 2f, r = Random.Range(MissNear, MissFar);
            var aim = c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r;
            var dir = (aim - from).normalized;
            if (dir.y < -0.05f) return HitSurface.MissGround(from, aim);
            var end = OzAirShots.MissLine(from, from + dir * ((aim - from).magnitude + MissRun));   // K337: it ends inside the map and under the ceiling
            Remember(end);
            return end;
        }

        /// <summary>OpenZPyroFire, a miss at the flying ship: the stream goes up past the hull's side and ends 1.5 m beyond it, never past
        /// the flame's reach (no burning ground under the ship).</summary>
        public static Vector3 FlameMiss(Vector3 from, ZObject t, float reach)
        {
            if (!Air(t)) return from;
            var s = Ship;
            var c = s.transform.TransformPoint(new Vector3(0f, MidM, 0f));
            var d = c - from; float len = d.magnitude; if (len < 0.5f) return c;
            var n = d / len;
            var a = Vector3.Cross(n, Mathf.Abs(n.y) > 0.95f ? Vector3.right : Vector3.up).normalized; var b = Vector3.Cross(n, a);
            float ang = Random.value * Mathf.PI * 2f;
            var aim = c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * Random.Range(MissNear, MissFar);
            var dir = (aim - from).normalized;
            return from + dir * Mathf.Min((aim - from).magnitude + 1.5f, Mathf.Max(reach, len));
        }

        static void Remember(Vector3 p)
        {
            float now = Time.time;
            for (int i = sky.Count - 1; i >= 0; i--) if (now - sky[i].t > 4f) sky.RemoveAt(i);
            if (sky.Count >= 64) sky.RemoveAt(0);
            sky.Add(new SkyEnd { p = p, t = now }); SkyMisses++;
        }

        /// <summary>The impact layers (OpenZFxPlayer.Shot, WeaponsV2Runner): this end point is a miss that flew on into the sky (no
        /// impact, no ground puff, no sound of a hit); the second laser bolt's end (0.25 m round it) too.</summary>
        public static bool IsSkyMiss(Vector3 p)
        {
            float now = Time.time;
            foreach (var e in sky) if (now - e.t <= 4f && (e.p - p).sqrMagnitude <= 0.49f) return true;
            return false;
        }

        // ------------------------------------------------------------------ salvo extras, blasts, fire
        /// <summary>MlrsSalvo / MechRockets / MissileCannonSalvo: the height of an extra rocket's end. When the salvo's own rocket ends at
        /// the flying ship (within 10 m of its hull), the extras burst round it in the air; else on the ground as before.</summary>
        public static float SalvoEndY(Vector3 to, Vector3 p, float groundY)
        {
            OzPlayableShuttle s = null; float best = 10f;                                               // the flying ship nearest the salvo's own end
            for (int i = 0; i < OzPlayableShuttle.Count; i++)
            {
                var q = OzPlayableShuttle.At(i); if (!q || q.Obj == null || !Air(q.Obj) || q.ShownHeight < 2.5f) continue;
                last = q; float d = HullDist(to); if (d <= best) { best = d; s = q; }
            }
            if (!s && !OzAirShots.AimedAloft(to, true)) return groundY;                                // K337: round its round's end up there also when the ship has flown on (was: on the ground)
            last = s;
            float y = to.y + Random.Range(-0.8f, 0.8f);
            NoteCosmetic(p, y, groundY);
            return Mathf.Max(groundY, y);
        }

        /// <summary>A blast at the flying ship: nothing of it reaches the ground under it (UnitView.Deaths HitReactions: robots below are
        /// not knocked; LegacyMatch: no crater under a heavy shell's air burst).</summary>
        public static bool AirBurst(ZObject target) => High(target) || OzAirShots.LostAloft(target);   // K337: also when it broke up while the rules still have it up there
        public static bool AirBurstSkips(ZEvent e, UnitView v)
        {
            var s = OzPlayableShuttle.OfId(e.otherId);
            if (!s || s.Obj == null || !(High(s.Obj) || OzAirShots.LostAloft(s.Obj))) return false;   // K337: also when it broke up up there
            return !v || v.Obj != s.Obj;
        }

        // ------------------------------------------------------------------ the last hit (the break-up's throw)
        /// <summary>The last drawn hit on the ship: when (Time.time), where, the shot's direction, how hard (rifle 0.55 .. fort gun 1.6) and what.</summary>
        public static float LastHitAt = -99f, LastHitPower = 1f; public static Vector3 LastHitPoint, LastHitDir; public static string LastHitKind = "-";
        public static float Power(string kind)
        {
            switch (kind)
            {
                case "fort gun": return 1.6f;
                case "howitzer": return 1.4f;
                case "heavy": case "gun": return 1.2f;
                case "light": case "medium": case "missile_launcher": case "missile_cannon": case "tough": return 1.0f;
                case "grenade": return 0.9f;
                case "pyro": case "electrobot": return 0.6f;
                default: return 0.55f;
            }
        }
        /// <summary>The ship the last drawn hit was on (several may fly).</summary>
        public static int LastHitShip = -1;
        static void Hit(string kind, Vector3 from, Vector3 at)
        {
            LastHitAt = Time.time; LastHitPoint = at; LastHitKind = kind; LastHitPower = Power(kind); LastHitShip = Ship && Ship.Obj != null ? Ship.Obj.Id : -1;
            var d = at - from; LastHitDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.down;
        }
        /// <summary>OzShuttleBreakup: the killing hit, when one was drawn in the last 1.5 s.</summary>
        public static bool KillHit(out Vector3 dir, out Vector3 at, out float power, out string kind) => KillHit(-1, out dir, out at, out power, out kind);
        /// <summary>The same for one ship (shipId -1 = any).</summary>
        public static bool KillHit(int shipId, out Vector3 dir, out Vector3 at, out float power, out string kind)
        {
            dir = LastHitDir; at = LastHitPoint; power = LastHitPower; kind = LastHitKind;
            return Time.time - LastHitAt <= 1.5f && (shipId < 0 || LastHitShip < 0 || LastHitShip == shipId);
        }

        // ------------------------------------------------------------------ the gallery's counts
        public sealed class Probe
        {
            public string key; public int shots, hits, misses, ends, onHull, inSky, low, startsFar, blasts, blastsOnHull, blastsLow, extras, extrasLow;
            public float minH = 999f, maxH = -999f, sumH, worstStart, worstHull, minBlastH = 999f;
            public void End(float h) { ends++; sumH += h; minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h); }
        }
        /// <summary>Proof switch: count every drawn shot at the flying ship (test only).</summary>
        public static bool Recording;
        public static readonly Dictionary<string, Probe> Probes = new Dictionary<string, Probe>();
        public static readonly StringBuilder Lines = new StringBuilder();
        static int lineCount;
        public static void ProbeReset() { Probes.Clear(); Lines.Clear(); lineCount = 0; }

        static string KeyOf(ZObject a, bool grenade)
        {
            if (a == null) return "?";
            string k = a.IsFort ? "fort gun" : a.Kind == ZKind.Vehicle && a.Type == "apc" ? "apc crew " + a.CrewWeaponType : a.Type;
            return grenade ? "grenade " + k : k;
        }
        static Probe P(string key) { if (!Probes.TryGetValue(key, out var p)) Probes[key] = p = new Probe { key = key }; return p; }
        static float GroundAt(Vector3 p) { var g = ZGame.I; return g ? g.GroundY(p.x, p.z) : 0f; }
        static float StartGap(ZObject a, Vector3 from)
        {
            var g = ZGame.I; if (!g || a == null) return 0f;
            Transform tr = g.Views.TryGetValue(a.Id, out var v) && v ? v.transform : g.StaticViews.TryGetValue(a.Id, out var go) && go ? go.transform : null;
            if (!tr) return 0f;
            var d = from - tr.position; d.y = 0f; return d.magnitude;
        }
        static float StartLimit(ZObject a) => a == null ? 6f : a.IsFort ? 16f : a.Kind == ZKind.Robot ? 2.5f : 6.5f;
        static void Line(string s) { if (lineCount++ < 3000) Lines.AppendLine(s); }

        /// <summary>CombatView.DrawShot: a drawn rifle / gun / laser / flame round at t (start = the muzzle, end = where it is drawn ending).</summary>
        public static void NoteShot(ZEvent e, ZObject a, ZObject t, Vector3 from, Vector3 to)
        {
            if (t == null || !t.IsShuttle || !Air(t)) return;
            string key = KeyOf(a, false);
            if (e.flag) Hit(a != null && a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a != null ? a.Type : "?", from, to);
            if (!Recording) return;
            var p = P(key); p.shots++; if (e.flag) p.hits++; else p.misses++;
            float h = to.y - GroundAt(to), sg = StartGap(a, from), hd = HullDist(to);
            p.End(h); if (sg > StartLimit(a)) p.startsFar++; p.worstStart = Mathf.Max(p.worstStart, sg);
            bool sk = IsSkyMiss(to) || (!e.flag && h >= SkyM && hd > 1.5f);
            if (e.flag) { if (hd <= 0.75f) p.onHull++; p.worstHull = Mathf.Max(p.worstHull, hd); }
            else if (sk) p.inSky++;
            if (Ship.ShownHeight >= 6f && h < 2.5f) p.low++;
            Line($"{Time.time:F2} {key}#{(a != null ? a.Id : -1)} {(e.flag ? "HIT " : "MISS")} start {sg:F1} m from it, {from.y - GroundAt(from):F1} m up; end {h:F1} m over the ground, {hd:F2} m from the hull{(sk ? ", in the sky" : "")}; ship {Ship.ShownHeight:F1} m up");
        }

        /// <summary>CombatView.MissileFired / FortGunView (the fort's own shell): a drawn rocket, shell or grenade leaving for t.</summary>
        public static void NoteFired(ZEvent e, ZObject a, ZObject t, Vector3 from, Vector3 to)
        {
            OzAirShots.Fired(e, a, t, from, to);                                                        // K337: the round's record (View/OzAirShots.cs)
            if (!Recording || t == null || !t.IsShuttle || !Air(t)) return;
            string key = KeyOf(a, e.flag);
            var p = P(key); p.shots++;
            float h = to.y - GroundAt(to), sg = StartGap(a, from);
            p.End(h); if (sg > StartLimit(a)) p.startsFar++; p.worstStart = Mathf.Max(p.worstStart, sg);
            if (Ship.ShownHeight >= 6f && h < 2.5f) p.low++;
            Line($"{Time.time:F2} {key}#{(a != null ? a.Id : -1)} FIRED start {sg:F1} m from it, {from.y - GroundAt(from):F1} m up; aimed {h:F1} m over the ground, {HullDist(to):F2} m from the hull now; flight {e.value:F2} s; ship {Ship.ShownHeight:F1} m up");
        }

        /// <summary>CombatView.MissileExploded: the drawn blast of a rocket, shell or grenade the rules aimed at t.</summary>
        public static void NoteBlast(ZEvent e, ZObject a, ZObject t, Vector3 at)
        {
            if (t == null || !t.IsShuttle || !Air(t)) return;
            float hd = HullDist(at);
            if (hd <= 1.5f) Hit(e.flag ? "grenade" : a != null && a.IsFort ? "fort gun" : a != null ? (a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type) : "?",
                                a != null && ZGame.I ? ZGame.I.CenterWorld(a) + Vector3.up : at + Vector3.down, at);
            if (!Recording) return;
            var p = P(KeyOf(a, e.flag)); p.blasts++;
            float h = at.y - GroundAt(at);
            if (hd <= 1.0f) p.blastsOnHull++;
            if (Ship.ShownHeight >= 6f && h < 2.5f) p.blastsLow++;
            p.minBlastH = Mathf.Min(p.minBlastH, h);
            Line($"{Time.time:F2} {KeyOf(a, e.flag)}#{(a != null ? a.Id : -1)} BLAST {h:F1} m over the ground, {hd:F2} m from the hull; ship {Ship.ShownHeight:F1} m up");
        }

        /// <summary>OpenZElectroFx: a drawn tesla bolt at t (it always ends on the target's body).</summary>
        public static void NoteBolt(ZObject a, ZObject t, Vector3 from, Vector3 end)
        {
            if (t == null || !t.IsShuttle || !Air(t)) return;
            Hit("electrobot", from, end);
            if (!Recording) return;
            var p = P(KeyOf(a, false)); p.shots++; p.hits++;
            float h = end.y - GroundAt(end), sg = StartGap(a, from), hd = HullDist(end);
            p.End(h); if (sg > StartLimit(a)) p.startsFar++; p.worstStart = Mathf.Max(p.worstStart, sg);
            if (hd <= 0.75f) p.onHull++; p.worstHull = Mathf.Max(p.worstHull, hd);
            if (Ship.ShownHeight >= 6f && h < 2.5f) p.low++;
            Line($"{Time.time:F2} {KeyOf(a, false)}#{(a != null ? a.Id : -1)} BOLT start {sg:F1} m from it; end {h:F1} m over the ground, {hd:F2} m from the hull; ship {Ship.ShownHeight:F1} m up");
        }

        static void NoteCosmetic(Vector3 p, float y, float groundY)
        {
            if (!Recording) return;
            var pr = P("salvo extras"); pr.extras++;
            if (y - groundY < 2.5f) pr.extrasLow++;
        }

        /// <summary>The gallery's table: one line per weapon.</summary>
        public static string ProbeText()
        {
            var sb = new StringBuilder();
            foreach (var p in Probes.Values)
                sb.AppendLine($"  {p.key,-24} shots {p.shots,3} hits {p.hits,3} misses {p.misses,3} | ends over the ground {(p.ends > 0 ? p.minH : 0f),5:F1} .. {(p.ends > 0 ? p.maxH : 0f),5:F1} m (mean {(p.ends > 0 ? p.sumH / p.ends : 0f):F1}) | hits on the hull {p.onHull}/{p.hits} (worst {p.worstHull:F2} m) | misses into the sky {p.inSky}/{p.misses} | " +
                              $"blasts {p.blasts} on the hull {p.blastsOnHull} (lowest {(p.blasts > 0 ? p.minBlastH : 0f):F1} m) | salvo extras {p.extras} | ended low {p.low + p.blastsLow + p.extrasLow} | starts off the shooter {p.startsFar} (worst {p.worstStart:F1} m)");
            return sb.ToString();
        }
    }
}

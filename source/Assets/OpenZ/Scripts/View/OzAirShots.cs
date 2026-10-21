using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K337, the game's side of OzAirShots.Core.cs (the numbers and the decisions): what the drawers do with a round fired at the flying
    /// shuttle that misses it. Called from small hooks: OzShuttleAim (the round's record, the aim point in the map box, the miss line in the
    /// box, a round in flight when the ship broke up, the salvo extras kept in the air), LegacyMatch.MissileBlast (the miss's small air burst,
    /// a round already ended up there not drawn again), OpenZFxPlayer (the projectiles' hard cap, the arc under the ceiling, the sure miss's
    /// air burst at its cap, the pool's oldest-first end), MlrsSalvo / MissileCannonSalvo / MechRockets (an extra rocket that burst with its
    /// round lands no more), MlrsSalvo.Marked (no marked rocket for a shot at the flying ship), FortGunView (the fort shell's landing boom
    /// left out for a miss up there), GrenadeFx (no landing clink up in the air). View only.
    /// </summary>
    public static partial class OzAirShots
    {
        static ZWorld W => ZGame.I ? ZGame.I.World : null;
        static float Gs => ZGame.I ? Mathf.Max(0.05f, ZGame.I.gameSpeed) : 1f;
        static int frameSeen = -1, quietFrame = -1;
        static ZWorld worldSeen;
        /// <summary>Proof: the last air burst drawn (where, the frame, at a projectile's cap or at the rules' end point).</summary>
        public static Vector3 LastPopAt; public static int LastPopFrame = -1; public static bool LastPopEarly;

        /// <summary>Once a frame: records whose blast never came are let go; a new match (a level start) begins with empty tables.</summary>
        static void Frame()
        {
            if (Time.frameCount == frameSeen) return;
            frameSeen = Time.frameCount;
            var w = W;
            if (w != worldSeen) { worldSeen = w; Clear(); }
            if (w != null) Expire(w.Time, Time.time);
        }

        // ------------------------------------------------------------------ OzShuttleAim
        /// <summary>OzShuttleAim.NoteFired (CombatView.MissileFired, FortGunView's own shell): a drawn rocket, shell or grenade leaves for t.</summary>
        public static void Fired(ZEvent e, ZObject a, ZObject t, Vector3 from, Vector3 to)
        {
            if (!On || t == null || !t.IsShuttle || !OzShuttleAim.Air(t)) return;
            Frame();
            var s = OzPlayableShuttle.Of(t);
            var hull = s ? s.transform.TransformPoint(new Vector3(0f, OzShuttleAim.MidM, 0f)) : to;
            Record(e, a, t, from, to, hull, Gs, Time.time);
        }

        /// <summary>OzShuttleAim.AirAim, the ship up there: its hull height remembered (for a round still in flight when it breaks up), the
        /// point kept inside the map and under the ceiling.</summary>
        public static Vector3 AimUp(ZObject t, Vector3 p)
        {
            if (!On || t == null) return p;
            SeenAloft(t.Id, p.y, Time.time);
            var g = ZGame.I; if (!g || g.World == null || g.World.Map == null) return p;
            return InMap(p, g.World.Map.width * 2f, -g.World.Map.height * 2f, g.GroundY(p.x, p.z));
        }

        /// <summary>OzShuttleAim.AirAim, the ship not drawn flying: a round aimed at it while it flew, still in the air when it broke up, bursts
        /// up there where it was going (the rules still have the ship up there: nobody below is hurt), not on the ground under it.</summary>
        public static Vector3 LostAim(ZObject t, Vector3 ground)
        {
            if (!On || t == null || !t.IsShuttle || !t.ShuttleAirborne) return ground;
            Frame();
            return LostPoint(t, ground, Time.time, out var p) ? p : ground;
        }

        /// <summary>OzShuttleAim.AirBurst / AirBurstSkips: the ship broke up, the rules still have it up in the air (a blast aimed at it reaches
        /// nothing on the ground: no crater, no exp2 ground rings, no robot knocked down below).</summary>
        public static bool LostAloft(ZObject t) => On && t != null && t.IsShuttle && t.ShuttleAirborne && !OzShuttleAim.Air(t) && LastAloft(t.Id, Time.time, out _);

        /// <summary>OzShuttleAim.SalvoEndY (count = true) and GrenadeFx: this end point is a round's drawn end at the flying ship.</summary>
        public static bool AimedAloft(Vector3 to, bool salvo = false)
        {
            if (!On) return false;
            bool up = RecAt(to) >= 0;
            if (up && salvo) SalvoAloft++;
            return up;
        }

        /// <summary>OzShuttleAim.SkyMiss: the miss line past the hull ends where it would leave the map or rise past the ceiling.</summary>
        public static Vector3 MissLine(Vector3 from, Vector3 end)
        {
            var g = ZGame.I; if (!On || !g || g.World == null || g.World.Map == null) return end;
            float x1 = g.World.Map.width * 2f, z0 = -g.World.Map.height * 2f;
            return InBox(from, end, EdgeM, x1 - EdgeM, z0 + EdgeM, -EdgeM, g.GroundY(end.x, end.z) + CeilingM);
        }

        // ------------------------------------------------------------------ LegacyMatch.MissileBlast
        /// <summary>A blast aimed at the ship while it is (or was, broken up) up in the air. True = drawn here (a miss: its small air burst; a
        /// round that already ended up there: nothing), the caller draws nothing more. False = a hit (the full blast, as before) or not up there.</summary>
        public static bool AirBlast(ZEvent e, ZObject attacker, ZObject target, Vector3 at, LegacyMatch.BlastSpec d)
        {
            if (!On || target == null || !target.IsShuttle || e.flag2) return false;
            var z = OpenZFxPlayer.I; if (!z || !z.isActiveAndEnabled) return false;
            bool lost = LostAloft(target);
            Frame();
            double span = Mathf.Max(1, ZGame.RuleTicksLastFrame) * ZConst.TICK;
            if (!lost && !OzShuttleAim.High(target)) return EndedAlready(e, attacker, target, W, span);   // low (K256e's rule, as before), unless it burst at its cap up there already
            var k = Classify(e, attacker, target, W, true, span);
            if (lost) LostBursts++;
            if (k == Blast.Hit || k == Blast.NotAloft) return false;
            if (k == Blast.AlreadyEnded) return true;                                               // it burst up there at its cap already
            Pop(z, at + Vector3.up * 0.2f, d, e.flag ? 1f : PopScale, e.objId * 7919 + e.x * 31 + e.y, OzSoundV3.BlastEmitter(e), e.flag ? 1f : PopGain, e.flag ? d.pitch : PopPitch, !e.flag);
            LastPopEarly = false;
            return true;
        }

        /// <summary>The air burst of a miss: the blast's own look at `scale` (grenades: their own size), up there, nothing of it on the ground
        /// (QuietBlast: no prop, lamp or sandbag broken, no crater; FxManager's explosion listeners are not told), and its sound.</summary>
        static void Pop(OpenZFxPlayer z, Vector3 at, LegacyMatch.BlastSpec d, float scale, int id, int emitter, float gain, float pitch, bool popBank)
        {
            LastPopAt = at; LastPopFrame = Time.frameCount; LastPopEarly = true;
            bool q = OpenZFxPlayer.QuietBlast;
            OpenZFxPlayer.QuietBlast = true;
            try { z.BlastSized(at, d.s * scale, d.legRadius * scale, d.kind, id & 0x3fffffff); }
            finally { OpenZFxPlayer.QuietBlast = q; }
            var au = CombatAudio.I;
            if (!au || d.clips == null || d.clips.Length == 0) return;
            au.PlayPitched(string.IsNullOrEmpty(d.key) ? "explosion" : d.key, at, d.gain * gain, 0.05f, emitter, pitch, popBank ? BankPop : d.clips);
        }

        // ------------------------------------------------------------------ OpenZFxPlayer (its partial OpenZFxPlayer.AirShots.cs)
        /// <summary>A bound projectile ends in its air burst at pos (a sure miss at its cap, or a salvo's extra with its round, or the hard cap).</summary>
        internal static void PopProjectile(OpenZFxPlayer z, FxWeapon wpn, int serial, Vector3 pos)
        {
            Frame();
            if (!z || !BindOf(serial, out var b)) return;
            var r = Recs[b.rec];
            LegacyMatch.BlastSpec d;
            if (!b.follower)
            {
                var w = W; var a = w != null ? w.GetAny(r.attacker) : null;
                d = LegacyMatch.ForMissile(a, wpn == FxWeapon.Grenade, false, r.radius);                // the pool's grenade (GrenadeFx off): its own blast
            }
            else d = wpn == FxWeapon.Mlrs ? LegacyMatch.MlrsRocket : wpn == FxWeapon.Rocket ? LegacyMatch.MissileCannonSmall : LegacyMatch.MechRocket;
            float k = b.follower ? PopScale * 0.8f : PopScale;
            Pop(z, pos, d, k, r.attacker * 7919 + serial * 131, b.follower ? (r.attacker * 7919 + serial) & 0x3fffffff : (r.attacker * 7919 + r.ex * 31 + r.ey) & 0x3fffffff,
                b.follower ? PopGain * 0.7f : PopGain, PopPitch * (b.follower ? Random.Range(1.0f, 1.08f) : 1f), true);
        }

        // ------------------------------------------------------------------ the salvo helpers, the fort's gun, the grenade
        /// <summary>MlrsSalvo / MissileCannonSalvo / MechRockets: this extra landing burst with its round up there already.</summary>
        public static bool SkipLanding(int shooter, Vector3 pos) => SkipLanding(shooter, pos, Time.time);

        /// <summary>OpenZHeavyShells.Launch (the howitzer's heavy shell): a shell at the flying ship flies its lob with its top at most CeilingM
        /// over the ground (ArcGravity; the same end point and flight time). A long lead at a receding ship (the howitzer's shell is only a
        /// little faster than the ship at full strafe) would lob it far up into the sky. Shells at anything else: g as before.</summary>
        public static float LobGravity(Vector3 from, Vector3 to, float T, float g)
        {
            if (!On || RecAt(to) < 0) return g;
            var gm = ZGame.I; if (!gm) return g;
            var mid = (from + to) * 0.5f;
            float top = Mathf.Min(gm.GroundY(from.x, from.z), Mathf.Min(gm.GroundY(mid.x, mid.z), gm.GroundY(to.x, to.z))) + CeilingM;
            float g2 = ArcGravity(g, T, top - from.y, top - to.y);
            if (g2 < g - 1e-4f) ArcsFlattened++;
            return g2;
        }

        /// <summary>MlrsSalvo.QueueMarked (item 202's "marked" rockets that follow a vehicle in the splash and land on it): not for a shot at a
        /// shuttle the rules have in the air. Its blast hurts only the ship (ZWorld.ShuttleAirBlast), so a marked rocket on a vehicle under it
        /// drew a blast on the ground that hurts nobody, and the one that followed the ship itself landed on its hull also when the rules
        /// missed (a miss drawn as a hit). Rocket 1 (the sim's) and the 7 ripple rockets round its end (up there) stay.</summary>
        public static bool NoMarks(ZWorld w, ZMissile m)
        {
            if (!On || w == null || m == null || m.targetId < 0) return false;
            var t = w.GetAny(m.targetId);
            if (t == null || !t.IsShuttle || !t.ShuttleAirborne) return false;
            MarksLeftOut++;
            return true;
        }

        /// <summary>FortGunView.ReadEvents: the fort's shell at the ship up there missed: its landing boom is left out (the small air burst
        /// plays its own pop, LegacyMatch.MissileBlast -> AirBlast).</summary>
        public static void FortLanding(ZEvent e, ZObject fort, ZObject t)
        {
            if (!On || t == null || !t.IsShuttle) return;
            if (!(OzShuttleAim.High(t) || LostAloft(t))) return;
            if (RulesHit(e, fort, t, W, Mathf.Max(1, ZGame.RuleTicksLastFrame) * ZConst.TICK)) return;
            quietFrame = Time.frameCount; QuietLandings++;
        }
        /// <summary>FortGunView.ShellLanded: true (once) when FortLanding said so this frame.</summary>
        public static bool TakeQuietLanding() { bool q = quietFrame == Time.frameCount; quietFrame = -1; return q; }

        /// <summary>GrenadeFx: a grenade lobbed at the ship up there lands on nothing: no landing clink in the air (true = left out, done).</summary>
        public static bool NoClinkUpThere(Vector3 to, ref bool clink)
        {
            if (!On || RecAt(to, Time.time, 0.05f) < 0) return false;
            clink = true; NoClink++;
            return true;
        }
    }
}

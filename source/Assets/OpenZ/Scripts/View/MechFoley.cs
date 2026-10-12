using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// a stride counter of its own; nothing mechanical in between. Now, on the footfalls of the mech's OWN walk cycle (MechWalk.Travel:
    /// the left foot plants at travel x 3.2 = (2k+1) pi, the right at 2k pi; LegacyEngineAudio follows it too), only while the mech is
    /// on screen and walking:
    /// <list type="bullet">
    /// <item>
    /// mech()) as the other leg lifts, on about one footfall in four (seeded by the mech id and step), at least ServoGapMech s apart
    /// per mech and ServoGapAll s between any two mechs;</item>
    /// <item>
    /// with a dust puff at the planted foot.</item>
    /// </list>
    /// 3D at the mech (CombatAudio keys mech_servo / mech_stomp: vehicle bus, field profile, 85 / 95 m), deterministic per mech id
    /// and step index; view only.
    /// </summary>
    public static class MechFoley
    {
        public static bool Enabled = true;
        public const string ServoKey = "mech_servo", StompKey = "mech_stomp";
        public static readonly string[] BankServo = { "mech-servo-1", "mech-servo-2" };
        public static float ServoGain = 0.84f * 0.58f, StompGain = LegacyMatch.GainMechStep * 1.45f;
        public static float ServoGapMech = 1.8f, ServoGapAll = 0.35f, ScreenMargin = 0.08f;
        public static int ServoEvery = 4, StompEvery = 7;

        static readonly Dictionary<int, float> lastServo = new Dictionary<int, float>();
        static readonly Dictionary<UnitView, MechWalk> walks = new Dictionary<UnitView, MechWalk>();
        static float lastAnyServo = -10f;
        /// <summary>Play proof counters.</summary>
        public static int Servos { get; private set; }
        public static int Stomps { get; private set; }

        /// <summary>The mech's walk cycle (MechWalk on the model), cached per view.</summary>
        public static MechWalk Walk(UnitView v)
        {
            if (!v) return null;
            if (!walks.TryGetValue(v, out var w) || !w) { w = v.GetComponentInChildren<MechWalk>(); walks[v] = w; if (walks.Count > 64) Prune(); }
            return w;
        }
        static void Prune() { var dead = new List<UnitView>(); foreach (var kv in walks) if (!kv.Key) dead.Add(kv.Key); foreach (var d in dead) walks.Remove(d); }

        static uint Hash(int a, int b) { unchecked { uint x = (uint)a * 747796405u ^ (uint)b * 2891336453u; x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }

        static bool OnScreen(Vector3 p)
        {
            var cam = Camera.main; if (!cam) return true;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.x > -ScreenMargin && vp.x < 1f + ScreenMargin && vp.y > -ScreenMargin && vp.y < 1f + ScreenMargin;
        }

        /// <summary>
        /// Footfall, at the real planted foot, also for steps in place (turning); the stomp's dust is surface-aware (TrackMarks.MechDust:
        /// none on roads / concrete / water, the deck's own on a bridge) instead of sand everywhere.</summary>
        public static float StompDust = 1.6f;
        public static void Plant(UnitView v, int index, Vector3 foot, CombatAudio au)
        {
            if (!Enabled || !au || !v || v.Obj == null || v.Obj.Type != "medium" || v.Dying) return;
            if (!OnScreen(foot + Vector3.up * 2f)) return;
            int id = v.Obj.Id; uint h = Hash(id, index); float now = Time.time;
            if (h % (uint)Mathf.Max(1, ServoEvery) == 0u && now - lastAnyServo >= ServoGapAll && (!lastServo.TryGetValue(id, out var ls) || now - ls >= ServoGapMech))
            {   // the other leg lifts: a hydraulic / servo whir from the hip
                lastServo[id] = now; lastAnyServo = now; Servos++;
                au.PlayPitched(ServoKey, v.transform.position + Vector3.up * 2.4f, ServoGain, 0.3f, id, 0.93f + 0.1f * ((h >> 8) & 255u) / 255f, BankServo);
            }
            if ((h >> 12) % (uint)Mathf.Max(1, StompEvery) == 0u)
            {   // a heavy plant: the metal land pitched down, louder, with a bigger surface-aware puff at the foot that landed
                Stomps++;
                au.PlayPitched(StompKey, foot, StompGain * Mathf.Pow(10f, OzFoleyV1.StompExtraDb / 20f), 0.3f, id,
                    Mathf.Lerp(OzFoleyV1.StompPitchMin, OzFoleyV1.StompPitchMax, ((h >> 20) & 255u) / 255f), LegacyEngineAudio.PlantBank(foot));   // sfx_batch_v1 surface plants
                if (TrackMarks.I) TrackMarks.I.MechDust(foot, StompDust);
            }
        }

        /// <summary>LegacyEngineAudio.MechSteps, on every footfall (phase = the footfall index) of a walking mech.</summary>
        public static void Footfall(UnitView v, int phase, Vector3 p, CombatAudio au)
        {
            if (!Enabled || !au || !v || v.Obj == null || v.Obj.Type != "medium" || v.Dying || !v.Obj.IsMoving) return;
            if (!OnScreen(p + Vector3.up * 2f)) return;
            int id = v.Obj.Id; uint h = Hash(id, phase); float now = Time.time;
            if (h % (uint)Mathf.Max(1, ServoEvery) == 0u && now - lastAnyServo >= ServoGapAll && (!lastServo.TryGetValue(id, out var ls) || now - ls >= ServoGapMech))
            {   // the other leg lifts: a hydraulic / servo whir from the hip
                lastServo[id] = now; lastAnyServo = now; Servos++;
                au.PlayPitched(ServoKey, p + Vector3.up * 2.4f, ServoGain, 0.3f, id, 0.93f + 0.1f * ((h >> 8) & 255u) / 255f, BankServo);
            }
            if ((h >> 12) % (uint)Mathf.Max(1, StompEvery) == 0u)
            {   // a heavy plant: the metal land pitched down, louder, dust under the foot that landed (left on odd footfalls)
                Stomps++;
                au.PlayPitched(StompKey, p, StompGain, 0.3f, id, 0.62f + 0.1f * ((h >> 20) & 255u) / 255f, LegacyMatch.BankMechStep);
                var right = v.transform.right; float side = (phase & 1) != 0 ? -1f : 1f;
                var foot = p + right * (0.9f * side);
                if (ZGame.I) foot.y = ZGame.I.GroundY(foot.x, foot.z);
                FxManager.I?.Dust(foot + Vector3.up * 0.15f, 0.45f);
            }
        }
    }
}

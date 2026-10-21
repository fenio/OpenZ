using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Rocket 1 is the sim's own medium-tank missile (CombatView.MissileFired launches it from the LEFT pod; its MissileExploded is
    /// the one sim hit, full Zod damage). Rocket 2 is queued here: it leaves the RIGHT pod GapMin-GapMax s later and lands beside
    /// the real impact. Both land with the same half-size blast (LegacyMatch.MechRocket), so on screen the pair reads as one shot
    /// split in two; the twin is cosmetic (OpenZFxPlayer.QuietBlast: no damage, no prop breaking). Pod tubes alternate per shot
    /// (L0/R2, then L1/R3). Pure view: no sim object, event or damage.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class MechRockets : MonoBehaviour
    {
        public const float GapMin = 0.15f, GapMax = 0.25f, TwinSpread = 0.9f, KickM = 0.12f, KickTime = 0.16f;
        public static readonly string[] MuzzlesL = { "MechRocketMuzzle_L0", "MechRocketMuzzle_L1" }, MuzzlesR = { "MechRocketMuzzle_R2", "MechRocketMuzzle_R3" };

        struct Pending { public float at, flight; public int shooter, tube, id; public Vector3 to; }
        struct Landing { public float at; public Vector3 pos; public int shooter, id; }
        readonly List<Pending> launches = new List<Pending>();
        readonly List<Landing> landings = new List<Landing>();
        static readonly Dictionary<int, int> shotIndex = new Dictionary<int, int>();
        static readonly Dictionary<Transform, Vector3> podRest = new Dictionary<Transform, Vector3>();
        ZGame game;

        /// <summary>Checks: cosmetic twins launched / landed since load.</summary>
        public static int Twins { get; private set; }
        public static int TwinsLanded { get; private set; }

        static MechRockets For(ZGame g)
        {
            var s = g.GetComponent<MechRockets>(); if (!s) s = g.gameObject.AddComponent<MechRockets>();
            s.game = g;
            return s;
        }

        static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(false)) if (t.name == name) return t;
            return null;
        }

        /// <summary>CombatView.MissileFired (medium): the launch point of rocket 1 = this shot's LEFT pod tube; kicks the pod.</summary>
        public static Vector3 LeftMuzzle(ZGame g, UnitView av, int unitId, Vector3 fallback)
        {
            if (!av) return fallback;
            int k = shotIndex.TryGetValue(unitId, out var n) ? n : 0;
            var t = Find(av.transform, MuzzlesL[k % 2]);
            if (!t) return fallback;
            if (g) For(g).Kick(Find(av.transform, "MechRocketPod_L"));
            return t.position;
        }

        /// <summary>CombatView.MissileFired (medium), after rocket 1 left: queues rocket 2 from the right pod.</summary>
        public static void Follow(ZGame g, ZObject a, UnitView av, Vector3 to, float delay)
        {
            if (!g || a == null || !av || av.Dying) return;
            int k = shotIndex.TryGetValue(a.Id, out var n) ? n : 0; shotIndex[a.Id] = k + 1;
            float speed = Mathf.Max(0.25f, g.gameSpeed);
            var off = Random.insideUnitCircle * TwinSpread;
            var p = to + new Vector3(off.x, 0f, off.y); p.y = OzShuttleAim.SalvoEndY(to, p, g.GroundY(p.x, p.z) + 0.25f);   // DV118: beside the flying shuttle
            For(g).launches.Add(new Pending { at = Time.time + Random.Range(GapMin, GapMax) / speed, flight = Mathf.Max(0.05f, delay), shooter = a.Id,
                                              tube = k % 2, to = p, id = (a.Id * 7919 + k * 131 + 17) & 0x3fffffff });
        }

        void OnDisable() { launches.Clear(); landings.Clear(); }

        void Update()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) { launches.Clear(); landings.Clear(); return; }
            float now = Time.time;
            for (int i = 0; i < launches.Count; i++) { var l = launches[i]; if (now < l.at) continue; launches.RemoveAt(i--); Fire(l); }
            for (int i = 0; i < landings.Count; i++) { var l = landings[i]; if (now < l.at) continue; landings.RemoveAt(i--); Land(l); }
        }

        void Fire(Pending l)
        {
            if (!game.Views.TryGetValue(l.shooter, out var v) || !v || v.Dying || !v.isActiveAndEnabled) return;
            var o = v.Obj;
            if (o == null || o.Destroyed || o.Owner == Team.Neutral) return;                          // killed / driver sniped between the two
            var m = Find(v.transform, MuzzlesR[l.tube]);
            var from = m ? m.position : v.MuzzlePos();
            Kick(Find(v.transform, "MechRocketPod_R"));
            float flight = l.flight;
            LegacyMatch.Launch(o, "medium", from, l.to, flight, FxManager.I);
            LegacyMatch.LaunchSound("medium", from, l.shooter, CombatAudio.I);
            OzSoundV3.Flight("medium", from, l.to, flight, l.shooter, CombatAudio.I);                 // sound_v3: the twin flies audibly too
            landings.Add(new Landing { at = Time.time + OpenZFxPlayer.RealFlight(flight, game.gameSpeed), pos = l.to, shooter = l.shooter, id = l.id });
            Twins++;
        }

        void Land(Landing l)
        {
            if (OzAirShots.SkipLanding(l.shooter, l.pos)) { TwinsLanded++; return; }                 // K337: it burst up there with its round already (View/OzAirShots.cs)
            var d = LegacyMatch.MechRocket;
            var at = l.pos + Vector3.up * 0.2f;
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
            {
                OpenZFxPlayer.QuietBlast = true;                                                       // cosmetic: no Exploded listeners (props, lamps)
                try { z.BlastSized(at, d.s, d.legRadius, d.kind, l.id); }
                finally { OpenZFxPlayer.QuietBlast = false; }
            }
            else FxManager.I?.Pop(at, 0.8f);
            var au = CombatAudio.I;
            if (au && d.clips != null) au.PlayPitched(d.key, l.pos, d.gain, 0.05f, l.shooter, d.pitch, d.clips);
            TwinsLanded++;
        }

        /// <summary>The pod jolts back along its tubes (+z: the sockets fire along -z) and returns.</summary>
        void Kick(Transform pod)
        {
            if (!pod) return;
            if (!podRest.ContainsKey(pod)) podRest[pod] = pod.localPosition;
            StartCoroutine(KickRoutine(pod, podRest[pod]));
        }

        IEnumerator KickRoutine(Transform pod, Vector3 rest)
        {
            float t = 0;
            while (t < KickTime && pod)
            {
                t += Time.deltaTime; float k = t / KickTime, e = k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f;   // fast back, eased return
                pod.localPosition = rest + Vector3.forward * (KickM * Mathf.Clamp01(e));
                yield return null;
            }
            if (pod) pod.localPosition = rest;
        }
    }
}

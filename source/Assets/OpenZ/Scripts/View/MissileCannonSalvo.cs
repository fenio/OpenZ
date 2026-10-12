using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Zod: one missile per shot (damage, radius, speed kept).
    /// View only, like MechRockets:
    ///  - the sim's missile leaves the LEFT pod (BarrelPitch Muzzle_01 / Muzzle_03, rows alternate per salvo); its twin leaves the
    ///    RIGHT pod's tube beside it (Muzzle_02 / Muzzle_04) TwinGap s later, so the pair reads as two rockets side by side;
    ///  - each launch jolts the pod block back on the firing side (a small yaw about the turret axis away from that pod + a push back
    ///    along the tubes), eased back in 0.22 s;
    ///    steal it);
    ///  - the impact: the sim's blast (made small: MissileCannonSmall) + the twin's small blast beside it + a third small one between,
    ///    staggered 0.08-0.22 s: 3 small mushroom blasts per salvo (cosmetic ones are QuietBlast: no damage, no props).
    /// </summary>
    [DefaultExecutionOrder(950)]   // LateUpdate after UnitAim (62): the kick sits on top of the aimed pitch
    public class MissileCannonSalvo : MonoBehaviour
    {
        public const float TwinGap = 0.07f, TwinSpread = 1.1f, KickM = 0.16f, KickDeg = 4.5f, KickTime = 0.22f; public const float RoarDelay = 0.06f, RoarGainK = 1.4f;   // SOUND-bundle: the rocket roar 0.06 s after the launch (level unchanged, x1.4)
        static readonly string[] Left = { "Muzzle_01", "Muzzle_03" }, Right = { "Muzzle_02", "Muzzle_04" };

        struct Pending { public float at, flight; public int shooter, row, id; public Vector3 to; }
        struct Landing { public float at; public Vector3 pos; public int shooter, id; public float scale; }
        readonly List<Pending> launches = new List<Pending>();
        readonly List<Landing> landings = new List<Landing>();
        static readonly Dictionary<int, int> salvo = new Dictionary<int, int>();
        ZGame game;
        public static int Twins { get; private set; }
        public static int Blasts { get; private set; }
        public static int Salvos { get; private set; }
        /// <summary>Checks: salvos whose shooter had no live view (twin flies unseen), and the last shooter id.</summary>
        public static int NoView { get; private set; }
        public static int LastShooter { get; private set; } = -1;

        static MissileCannonSalvo For(ZGame g) { var s = g.GetComponent<MissileCannonSalvo>(); if (!s) s = g.gameObject.AddComponent<MissileCannonSalvo>(); s.game = g; return s; }

        static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(false)) if (t.name == name) return t;
            return null;
        }

        /// <summary>CombatView.MissileFired (missile_cannon): rocket 1 (the sim's) leaves this salvo's LEFT tube; the pod kicks left.</summary>
        public static Vector3 LeftMuzzle(ZGame g, UnitView av, int unitId, Vector3 fallback)
        {
            if (!av) return fallback;
            int k = salvo.TryGetValue(unitId, out var n) ? n : 0;
            var t = Find(av.transform, Left[k % 2]);
            if (g) For(g).Kick(Find(av.transform, "BarrelPitch"), -1f);
            return t ? t.position : fallback;
        }

        /// <summary>CombatView.MissileFired (missile_cannon), after rocket 1: launch sound, the twin from the right tube, the 3 blasts.</summary>
        public static void Follow(ZGame g, ZObject a, UnitView av, Vector3 from, Vector3 to, float delay)
        {
            if (!g || a == null) return;
            var s = For(g);
            int k = salvo.TryGetValue(a.Id, out var n) ? n : 0; salvo[a.Id] = k + 1; Salvos++;
            LastShooter = a.Id; if (!av || av.Dying) NoView++;
            PlayLaunch(from, a.Id);
            float speed = Mathf.Max(0.25f, g.gameSpeed), flight = Mathf.Max(0.05f, delay);
            // the twin lands beside the sim's impact, across the line of fire (side by side)
            var fwd = to - from; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-3f ? fwd.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, fwd);
            var p2 = to + side * TwinSpread + fwd * Random.Range(-0.3f, 0.3f); p2.y = g.GroundY(p2.x, p2.z) + 0.25f;
            int id = (a.Id * 7919 + k * 131 + 23) & 0x3fffffff;
            if (av && !av.Dying) s.launches.Add(new Pending { at = Time.time + TwinGap / speed, flight = flight, shooter = a.Id, row = k % 2, to = p2, id = id });
            else s.landings.Add(new Landing { at = Time.time + OpenZFxPlayer.RealFlight(flight, g.gameSpeed) + TwinGap / speed, pos = p2, shooter = a.Id, id = id, scale = 1f });
            // the third, smaller burst between the two impacts, a beat after both
            var p3 = to + side * (TwinSpread * 0.5f) + fwd * Random.Range(0.4f, 0.9f); p3.y = g.GroundY(p3.x, p3.z) + 0.25f;
            s.landings.Add(new Landing { at = Time.time + OpenZFxPlayer.RealFlight(flight, g.gameSpeed) + Random.Range(0.14f, 0.22f) / speed, pos = p3, shooter = a.Id, id = id ^ 0x5bd1, scale = 0.8f });
        }

        static void PlayLaunch(Vector3 from, int emitter)
        {
            var au = CombatAudio.I; if (!au) return;
            au.PlayPitched("launch", from, LegacyMatch.GainDefault * 2.2f, 0.02f, emitter + 7100000, Random.Range(0.96f, 1.06f), LegacyMatch.BankMlrsLaunch);
            au.StartCoroutine(Roar(au, from, emitter + 7200000));
        }
        static System.Collections.IEnumerator Roar(CombatAudio au, Vector3 from, int emitter)
        {
            yield return new WaitForSeconds(RoarDelay);
            if (au) au.PlayPitched("mc_rocket", from, LegacyMatch.GainDefault * RoarGainK, 0.02f, emitter, Random.Range(0.95f, 1.08f), OzSoundV3.BankMcRoar);   // sound_v3: own key + 4 takes
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
            var o = v.Obj; if (o == null || o.Destroyed) return;
            var m = Find(v.transform, Right[l.row]);
            var from = m ? m.position : v.MuzzlePos();
            Kick(Find(v.transform, "BarrelPitch"), +1f);
            LegacyMatch.Launch(o, "missile_cannon", from, l.to, l.flight, FxManager.I);
            PlayLaunch(from, l.shooter + 1);
            landings.Add(new Landing { at = Time.time + OpenZFxPlayer.RealFlight(l.flight, game.gameSpeed), pos = l.to, shooter = l.shooter, id = l.id, scale = 1f });
            Twins++;
        }

        void Land(Landing l)
        {
            var d = LegacyMatch.MissileCannonSmall;
            var at = l.pos + Vector3.up * 0.2f;
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
            {
                OpenZFxPlayer.QuietBlast = true;
                try { z.BlastSized(at, d.s * l.scale, d.legRadius * l.scale, d.kind, l.id); }
                finally { OpenZFxPlayer.QuietBlast = false; }
            }
            else FxManager.I?.Pop(at, 0.7f * l.scale);
            var au = CombatAudio.I;
            if (au && d.clips != null) au.PlayPitched(d.key, l.pos, d.gain * l.scale, 0.03f, l.shooter, d.pitch * Random.Range(0.95f, 1.1f), d.clips);
            Blasts++;
        }

        /// <summary>Side recoil: the pod block turns away from the firing pod (side -1 = left / Muzzle_01-03 at -z, +1 = right) and pushes
        /// back along the tubes (they fire along +x in BarrelPitch space), then eases home. Applied in LateUpdate on top of UnitAim's
        /// pitch pose (order 950), undoing last frame's offset only if nothing re-posed the node since.</summary>
        sealed class Kicker { public Transform t; public float t0, side; public Quaternion setRot, delta = Quaternion.identity; public Vector3 setPos, dPos; }
        readonly List<Kicker> kicks = new List<Kicker>();

        void Kick(Transform pod, float side)
        {
            if (!pod) return;
            var k = kicks.Find(x => x.t == pod);
            if (k == null) { k = new Kicker { t = pod }; kicks.Add(k); k.setRot = pod.localRotation; k.setPos = pod.localPosition; }
            k.t0 = Time.time; k.side = side;
        }

        void LateUpdate()
        {
            for (int i = kicks.Count - 1; i >= 0; i--)
            {
                var k = kicks[i];
                if (!k.t) { kicks.RemoveAt(i); continue; }
                // base pose: what UnitAim set this frame, or (untouched since our last write) our write minus our offset
                var baseRot = k.t.localRotation == k.setRot ? k.setRot * Quaternion.Inverse(k.delta) : k.t.localRotation;
                var basePos = k.t.localPosition == k.setPos ? k.setPos - k.dPos : k.t.localPosition;
                float x = (Time.time - k.t0) / KickTime, e = x < 0.2f ? x / 0.2f : 1f - (x - 0.2f) / 0.8f; e = Mathf.Clamp01(e);
                k.delta = Quaternion.AngleAxis(-k.side * KickDeg * e, Vector3.up); k.dPos = Vector3.left * (KickM * e);
                k.t.localRotation = baseRot * k.delta; k.t.localPosition = basePos + k.dPos;
                k.setRot = k.t.localRotation; k.setPos = k.t.localPosition;
                if (x >= 1f) { k.t.localRotation = baseRot; k.t.localPosition = basePos; kicks.RemoveAt(i); }
            }
        }
    }
}

using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// volleySpacingMs 190; spawnSpecialRocket: one MUZZLE flash + mlrsLaunch per rocket). Rocket 1 is the sim's
    /// own missile (CombatView -> LegacyMatch.Launch / LaunchSound, lands on the sim's MissileExploded blast: the only one that does
    /// damage); rockets 2-8 are cosmetic and queued here: they leave the next rack tubes (MLRSMuzzle_01..08, UnitAim round robin)
    /// Spacing s apart (/ gameSpeed) with the MLRS launch flash, exhaust, smoke trail and halo (OpenZFxPlayer.Launch FxWeapon.Mlrs),
    /// the rack kick and hull jolt (UnitView.OnFire -> UnitAim.OnFire), a launch sound on their own capped key, land spread around the
    /// real impact inside Zod's splash, and burst smaller and quieter (OpenZFxPlayer.QuietBlast: no prop / lamp / sandbag breaking).
    /// Then UnitAim reloads the rack tube by tube (ReloadAfter, slide-in + servo). Pure view: no sim object, event or damage.
    /// Timing (plan/research/zod_units_combat.md 1.1): missile_launcher attack_speed 4.454 s, missile_speed 70 px/s, attack_radius
    /// 160 px -> at most 2.29 s flight; ripple 7 x 0.15 = 1.05 s, so the last cosmetic blast lands by 3.34 s, inside one reload.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public partial class MlrsSalvo : MonoBehaviour                                                  // version 1.1 item 202: + MlrsSalvo.Marked.cs
    {
        // ------------------------------------------------------------------ numbers (README_IMPORT.md)
        public const int Rockets = 8;
        public const float Spacing = 0.15f;
        public const float SpreadK = 0.45f, SpreadMin = 2.5f, SpreadMax = 4.5f, RangeStretch = 1.3f;   // landing disc: .45 x Zod splash (80 px = 10 m -> 4.5 m), x1.3 along the line of fire
        public const float FlightMin = 0.75f, FlightMax = 1.35f;             // cosmetic flight = sim delay x (its distance / the real rocket's), clamped
        public const float BlastS = 1.0f, BlastLegRadius = 3.6f;             // real MLRS rocket 1.4 / 5.6 (LegacyMatch.MlrsRocket); < LegLargeRadius 3.8: no outer / hero rings
        // 24 m, under the reload servo (0.15) and every blast: inaudible. Each ripple rocket now launches at the heavy-shot level and
        public const float LaunchGain = LegacyMatch.GainDefault * 2.2f, WhooshGain = 0f, WhooshDelay = 0.06f;
        public static readonly string[] BankWhoosh = { "rocket", "rocket-2", "mech-rocket-2" };
        public const float BlastGain = LegacyMatch.GainHeavy * 0.85f, BlastThrottle = 0.06f;
        public const float ReloadAfter = 2.4f, ReloadSlide = 0.5f, ReloadSlideM = 0.42f;
        public const float ReloadGain = 0.18f, ReloadThrottle = 0.45f;
        public static readonly string[] ReloadClips = { "mech-servo-1", "mech-servo-2" };

        struct Pending { public float at, flight, d0; public int shooter, index; public Vector3 to; }
        struct Landing { public float at; public Vector3 pos; public int id, shooter; }
        readonly List<Pending> launches = new List<Pending>();
        readonly List<Landing> landings = new List<Landing>();
        readonly HashSet<int> firedThisFrame = new HashSet<int>();
        ZGame game;

        /// <summary>Stats for checks: cosmetic rockets launched / landed since load.</summary>
        public static int Launched { get; private set; }
        public static int Landed { get; private set; }
        public int Queued => launches.Count + landings.Count;

        /// <summary>combat_nb_v1 NB3 (BlastWave): the ripple rocket of `shooter` (queued or in flight) that lands nearest to p, within
        /// `radius` m of it and `within` s from now: seconds until it lands and where. Queued rockets: launch time + their flight
        /// (Fire() scales it by 0.75-1.35 with the distance ratio, ~1 inside the splash disc).</summary>
        public static bool NearestLanding(ZGame g, int shooter, Vector3 p, float radius, float within, out float inSeconds, out Vector3 at)
        {
            inSeconds = 0f; at = default;
            var s = g ? g.GetComponent<MlrsSalvo>() : null; if (!s) return false;
            float now = Time.time, speed = Mathf.Max(0.25f, g.gameSpeed), best = radius; bool found = false; p.y = 0f;
            foreach (var l in s.launches)
            {
                if (l.shooter != shooter) continue;
                float t = l.at + OpenZFxPlayer.RealFlight(Mathf.Max(0.2f, l.flight), speed) - now; var q = l.to; q.y = 0f; float d = Vector3.Distance(p, q);
                if (t < 0f || t > within || d >= best) continue;
                best = d; inSeconds = t; at = l.to; found = true;
            }
            foreach (var l in s.landings)
            {
                if (l.shooter != shooter) continue;
                float t = l.at - now; var q = l.pos; q.y = 0f; float d = Vector3.Distance(p, q);
                if (t < 0f || t > within || d >= best) continue;
                best = d; inSeconds = t; at = l.pos; found = true;
            }
            if (s.NearestMarked(shooter, p, within, speed, ref best, ref inSeconds, ref at)) found = true;   // version 1.1 item 202: the marked rockets land on the sim's blast frame
            return found;
        }

        static MlrsSalvo For(ZGame g)
        {
            var s = g.GetComponent<MlrsSalvo>(); if (!s) s = g.gameObject.AddComponent<MlrsSalvo>();
            s.game = g;
            return s;
        }

        /// <summary>CombatView.MissileFired, right after the real rocket left `from` for `to` (delay = the sim's flight time in sim s,
        /// radiusPx = the missile's Zod splash radius): queues rockets 2-8 of the ripple.</summary>
        public static void Follow(ZGame g, ZObject a, UnitView av, Vector3 from, Vector3 to, float delay, int radiusPx)
        {
            if (!g || a == null || !av || av.Dying) return;
            For(g).Queue(a, av, from, to, delay, radiusPx);
        }

        void Queue(ZObject a, UnitView av, Vector3 from, Vector3 to, float delay, int radiusPx)
        {
            float speed = Mathf.Max(0.25f, game.gameSpeed);
            var line = to - av.transform.position; line.y = 0f;
            var fwd = line.sqrMagnitude > 0.01f ? line.normalized : Vector3.forward; var right = new Vector3(fwd.z, 0f, -fwd.x);
            float spread = Mathf.Clamp(SpreadK * Mathf.Max(0, radiusPx) / 8f, SpreadMin, SpreadMax);
            float d0 = Mathf.Max(1f, Vector3.Distance(from, to)), turn = Random.value * Mathf.PI * 2f;
            for (int i = 1; i < Rockets; i++)
            {   // golden-angle fill of the landing disc (never two rockets on one spot), stretched along the line of fire like a ripple
                float r = spread * Mathf.Sqrt((i - 0.5f) / (Rockets - 1)), ang = turn + i * 2.39996f;
                var p = to + right * (Mathf.Cos(ang) * r) + fwd * (Mathf.Sin(ang) * r * RangeStretch);
                p.y = OzShuttleAim.SalvoEndY(to, p, game.GroundY(p.x, p.z) + 0.25f);                 // DV118: round the flying shuttle in the air
                launches.Add(new Pending { at = Time.time + i * Spacing / speed, flight = Mathf.Max(0.05f, delay), d0 = d0, shooter = a.Id, index = i, to = p });
            }
            QueueMarked(a, launches.Count - (Rockets - 1));                                          // version 1.1 item 202: a rocket onto every vehicle / gun the splash reaches (MlrsSalvo.Marked.cs)
            MlrsSideKick.Kick(av, from, to);                                                          // rocket 1 (the sim's own) kicks its side too
            var aim = av.GetComponent<UnitAim>();
            if (aim) aim.KeepAim((Rockets - 1) * Spacing / speed + 0.25f);                          // the rack stays on the ripple's aim
        }

        /// <summary>UnitAim: a rack tube reloaded (its rocket slides in): a quiet servo, throttled per launcher.</summary>
        public static void ReloadCue(int unitId, Vector3 at)
        {
            var au = CombatAudio.I;
            if (au) au.PlayPitched("mlrs_reload", at, ReloadGain, ReloadThrottle, unitId, 1.1f, ReloadClips);
        }

        void OnDisable() { launches.Clear(); landings.Clear(); marked.Clear(); }

        void Update()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) { launches.Clear(); landings.Clear(); return; }
            float now = Time.time;                                                                    // paused game = frozen ripple
            firedThisFrame.Clear();
            for (int i = 0; i < launches.Count; i++)
            {
                var l = launches[i];
                if (now < l.at || firedThisFrame.Contains(l.shooter)) continue;                      // one tube per launcher and frame
                launches.RemoveAt(i--);
                firedThisFrame.Add(l.shooter);
                Fire(l);
            }
            for (int i = 0; i < landings.Count; i++)
            {
                var l = landings[i];
                if (now < l.at) continue;
                landings.RemoveAt(i--);
                Land(l);
            }
            StepMarked();                                                                             // version 1.1 item 202: launch, steer and land the marked rockets
        }

        void Fire(Pending l)
        {
            if (!game.Views.TryGetValue(l.shooter, out var v) || !v || v.Dying || !v.isActiveAndEnabled) return;   // the rack is gone: no more rockets
            var o = v.Obj;
            if (o == null || o.Destroyed || o.Owner == Team.Neutral) return;                          // killed / driver sniped mid-ripple
            var from = v.MuzzlePos();                                                                 // this tube (UnitAim advances after LateUpdate)
            v.OnFire();                                                                               // tube empties, rack kick, hull jolt, next tube
            MlrsSideKick.Kick(v, from, l.to);
            float flight = Mathf.Max(0.2f, l.flight * Mathf.Clamp(Vector3.Distance(from, l.to) / l.d0, FlightMin, FlightMax));
            LegacyMatch.Launch(o, "missile_launcher", from, l.to, flight, FxManager.I);
            var au = CombatAudio.I;
            if (au) au.PlayPitched("mlrs_salvo", from, LaunchGain, 0f, l.shooter, 0f, LegacyMatch.BankMlrsLaunch);
            if (au && WhooshGain > 0f) StartCoroutine(Whoosh(au, from, l.shooter));
            landings.Add(new Landing { at = Time.time + OpenZFxPlayer.RealFlight(flight, game.gameSpeed), pos = l.to, shooter = l.shooter,
                                       id = (l.shooter * 7919 + l.index * 131 + Time.frameCount) & 0x3fffffff });
            Launched++;
        }

        void Land(Landing l)
        {
            if (OzAirShots.SkipLanding(l.shooter, l.pos)) { Landed++; return; }                      // K337: it burst up there with its round already (View/OzAirShots.cs)
            var at = l.pos + Vector3.up * 0.2f;
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
            {
                OpenZFxPlayer.QuietBlast = true;                                                       // cosmetic: no Exploded listeners (props, lamps, bloom flare)
                try
                {
                    if (OpenZFxPlayer.VolleyExp2(l.shooter, at, l.id) && !OzShuttleBreakup.IsAirBurst(at, z.GroundAt(at), FxBlastKind.Rocket)) z.Exp2(at, 1.9f, Exp2Kind.Salvo, l.id, BlastS, FxBlastKind.Rocket);   // K256e: up in the air (at the flying shuttle) the gated blast
                    else z.BlastSized(at, BlastS, BlastLegRadius, FxBlastKind.Rocket, l.id);
                }
                finally { OpenZFxPlayer.QuietBlast = false; }
            }
            else FxManager.I?.Pop(at, 0.9f);                                                           // legacy pool: a flash, never FxManager.Explosion (breaks props)
            var au = CombatAudio.I;
            if (au) au.PlayPitched("mlrs_salvo_blast", l.pos, BlastGain, BlastThrottle, l.shooter, 0f, LegacyMatch.BankMlrsExplosion);
            if (OpenZFxPlayer.IsWaterTile(l.pos) && AudioDirector.Has("exp_water_small")) AudioDirector.Fire("exp_water_small", l.pos, -1, 0.6f);   // PL7 splash layer
            Landed++;
        }
            System.Collections.IEnumerator Whoosh(CombatAudio au, Vector3 at, int shooter)
        {
            yield return new WaitForSeconds(WhooshDelay);
            if (au) au.PlayPitched("mlrs_whoosh", at, WhooshGain, 0f, shooter, Random.Range(0.92f, 1.12f), BankWhoosh);
        }
}
}

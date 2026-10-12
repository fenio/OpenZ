using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only (no sim state): every hook here is called from a live view (FortGunView, UnitAim, GrenadeFx, CombatView, LegacyMatch,
    /// voice limits, distant-battle rule, 1/sqrt(n) blast stacking) or AudioDirector's table events (3D world / 2D UI, cooldowns, max
    /// voices, in-view culling, camera-height limits). Nothing is removed: new events and new takes are added to the pools, and every
    /// CombatAudio bank picks without repeating its last takes (PickName).
    /// </summary>
    public static partial class OzSoundV3
    {
        /// <summary>Master switch (harness A/B): false = every sound_v3 hook returns at once (the banks keep their added takes).</summary>
        public static bool Enabled = true;

        // ================================================================================================ CombatAudio banks (clip names)
        public static readonly string[] BankGrenadeBlast = { "s3_exp_tnt_1", "s3_exp_tnt_2", "s3_exp_tnt_3", "s3_exp_legacy_1", "explosion" };
        public static readonly string[] BankMechRocket = { "s3_mech_rocket_a", "mech-rocket-2", "s3_mech_rocket_c", "s3_atom_rocket_1", "s3_atom_rocket_2" };
        public static readonly string[] BankMcRoar = { "rocket", "s3_atom_rocket_3", "s3_atom_rocket_4", "s3_rocket_c" };
        public static readonly string[] BankDriverHit = { "v3-armor-ricochet-a", "s3_ricochet_a", "s3_ricochet_b", "s3_ricochet_c", "s3_bullethit_1" };
        public static readonly string[] BankPickup = { "s3_crate_pickup_a", "s3_crate_pickup_b", "s3_crate_pickup_c" };
        public static readonly string[] BankCaptureStart = { "v3-capture-start", "s3_capture_start_b", "s3_capture_start_c" };
        public static readonly string[] BankEnter = { "enter", "s3_vehicle_enter", "s3_enter_b" };
        public static readonly string[] BankCapture = { "vehicle-capture-sep11", "s3_capture_b", "s3_capture_c" };
        public static readonly string[] BankEngineStart = { "v3-engine-start-a", "s3_engine_start_b", "s3_engine_start_c", "s3_engine_start_d" };
        public static readonly string[] BankRolloutRobot = { "s3_rollout_robot_a", "s3_rollout_robot_b", "s3_rollout_robot_c" };
        public static readonly string[] BankWrench = { "atom-repair-wrench", "s3_wrench_b", "s3_wrench_c", "s3_wrench_d" };
        public static readonly string[] BankMetalHeavy = { "final-metal-land-a", "final-metal-land-c", "s3_metal_heavy_a", "s3_metal_heavy_b", "s3_metal_heavy_c", "s3_heavydebris", "s3_boss_impact", "impact-1" };
        public static readonly string[] BankMetalMedium = { "final-metal-land-a", "final-metal-land-b", "final-metal-land-c", "s3_smash_b", "impact-2", "s3_mech_melee", "s3_road_smash", "s3_metal_heavy_b" };
        public static readonly string[] BankHatchOpen = { "final-metal-land-b", "final-metal-land-c", "sd_hatch_close_b", "s3_hatch_b" };
        public static readonly string[] BankHatchShut = { "final-metal-land-a", "sd_hatch_close_a", "sd_hatch_close_b", "s3_hatch_b" };
        public static readonly string[] BankCrewIn = { "final-metal-land-c", "sd_hatch_close_a", "s3_hatch_b" };
        public static readonly string[] BankGrenadeClink = { "v3-grenade-bounce-a", "s3_grenade_clink_a", "s3_grenade_clink_b", "s3_grenade_clink_c" };
        public static readonly string[] BankSnipePop = { "atom-pop", "s3_pop_b", "s3_pop_c" };
        public static readonly string[] BankSwim = { "swim", "s3_swim_b", "s3_swim_c" };
        public static readonly string[] BankZoneGained = { "v3-flag-success", "sd_zone_flip_sweep", "s3_zone_gained_c" };
        public static readonly string[] BankCactus = { "s3_cactus_a", "s3_cactus_b" };
        public static readonly string[] BankHeavyDebris = { "s3_heavydebris", "s3_metal_heavy_a", "s3_metal_heavy_c" };

        // ================================================================================================ no repeat (every CombatAudio bank)
        static readonly Dictionary<string, int[]> lastPicks = new Dictionary<string, int[]>();
        static readonly Dictionary<string, string[]> lastBank = new Dictionary<string, string[]>();
        /// <summary>CombatAudio.PlayPitched: one of `names`, never one of the last picks of this key (the last 1 for 2-3 takes, the last 2
        /// from 4 takes): no take twice in a row (the audit: every 2-4 clip bank repeated back-to-back 25-50 % of the time).</summary>
        public static string PickName(string key, string[] names)
        {
            int n = names.Length;
            if (n <= 1) return n == 1 ? names[0] : null;
            key = key ?? "";
            if (!lastPicks.TryGetValue(key, out var lp) || !lastBank.TryGetValue(key, out var lb) || !SameBank(lb, names))
            { lp = new[] { -1, -1 }; lastPicks[key] = lp; }
            lastBank[key] = names;
            int avoid = n >= 4 ? 2 : 1, pick = -1;
            for (int tries = 0; tries < 8; tries++)
            {
                int i = Random.Range(0, n);
                if (i == lp[0] || (avoid > 1 && i == lp[1])) continue;
                pick = i; break;
            }
            if (pick < 0) { pick = (Mathf.Max(lp[0], 0) + 1) % n; if (avoid > 1 && pick == lp[1]) pick = (pick + 1) % n; }
            lp[1] = lp[0]; lp[0] = pick;
            return names[pick];
        }

        /// <summary>The same bank (callers often pass a new inline array with the same names each call).</summary>
        static bool SameBank(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ================================================================================================ helpers
        static Camera cam;
        public static bool OnScreen(Vector3 p, float margin = 0.1f)
        {
            if (!cam) cam = Camera.main;
            if (!cam) return false;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.x > -margin && vp.x < 1f + margin && vp.y > -margin && vp.y < 1f + margin;
        }
        static float CamHeight() { var r = Rts(); return r ? r.height : 70f; }
        static RtsCamera rts;
        static RtsCamera Rts() { if (!rts) rts = Object.FindFirstObjectByType<RtsCamera>(); return rts; }
        /// <summary>Scaled-time delay (pauses with the game) through the director's scheduler; without a director: now.</summary>
        static void Later(float s, System.Action a) { var ad = AudioDirector.I; if (ad && s > 0f) ad.Schedule(s, a); else a(); }
        static float Speed => ZGame.I ? Mathf.Max(0.25f, ZGame.I.gameSpeed) : 1f;
        static readonly Dictionary<string, float> gates = new Dictionary<string, float>();
        /// <summary>true at most once per `gap` real seconds for this key.</summary>
        static bool Gate(string key, float gap)
        {
            float now = Time.unscaledTime;
            if (gates.TryGetValue(key, out var t) && now - t < gap) return false;
            gates[key] = now; return true;
        }
        static bool Chance(float p) => Random.value < p;
        static readonly Dictionary<string, float> onceStamp = new Dictionary<string, float>();
        /// <summary>true the first time it is asked for this key and stamp (e.g. the main menu intro's start time).</summary>
        public static bool Once(string key, float stamp)
        {
            if (onceStamp.TryGetValue(key, out var s) && Mathf.Approximately(s, stamp)) return false;
            onceStamp[key] = stamp; return true;
        }

        // ================================================================================================ explosions
        /// <summary>LegacyMatch.MissileBlast: every blast its own throttle slot (was one (key, -1) slot for the whole map: the 2nd grenade /
        /// rocket / shell of a volley within 50 ms was dropped, and a distant blast listed first silenced the on-screen one). The 50 ms
        /// throttle still merges a double report of the SAME blast; CombatAudio's 4 voices and 1/sqrt(n) stacking keep volleys clean.</summary>
        public static int BlastEmitter(ZEvent e) => (e.objId * 7919 + e.x * 31 + e.y) & 0x3fffffff;

        /// <summary>LegacyMatch.MissileBlast: a rocket / shell bursting on a building also throws masonry (dst_debris_concrete, 40 %).</summary>
        public static void OnMissileBlast(ZEvent e, ZObject target, Vector3 at)
        {
            if (!Enabled || e.flag2 || target == null || target.Kind != ZKind.Building || target.IsBridge || !Chance(0.4f)) return;
            Later(0.08f, () => AudioDirector.Fire("dst_debris_concrete", at + Vector3.up * 1.2f, -1, 0.8f));
        }

        /// <summary>LegacyMatch.UnitDeath: a vehicle / gun blast gets a heavy metal-debris tail (Atom heavydebris + the new steel drops),
        /// 0.25-0.4 s after the boom, 60 %, on screen: the parts raining down.</summary>
        public static void DeathTail(ZObject o, Vector3 p)
        {
            if (!Enabled || o == null || (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) || !Chance(0.6f) || !OnScreen(p, 0.2f)) return;
            Later(Random.Range(0.25f, 0.4f), () => CombatAudio.I?.PlayPitched("debris", p, 0.75f, 0.1f, o.Id, Random.Range(0.9f, 1.05f), BankHeavyDebris));
        }

        // ================================================================================================ grenades
        /// <summary>GrenadeFx.Launch: the throw of this frame is held in the robot's hand (its whoosh plays at the release, GrenadeReleased).</summary>
        public static int HeldThrowFrame = -1;
        public const float GainGrenadeThrow = LegacyMatch.GainDefault * 1.9f;   // audit: the throw sat ~5 dB under its own pin click (-39.9 vs -34.8 LUFS)
        /// <summary>LegacyMatch.GrenadeSound (CombatView MissileFired): the throw whoosh, unless GrenadeFx holds this grenade in the hand
        /// (then it plays at the release, 0.56 s later: it landed during the wind-up before).</summary>
        public static bool GrenadeThrowNow(Vector3 from, int emitter, CombatAudio au)
        {
            if (!Enabled) return false;
            if (HeldThrowFrame == Time.frameCount) return true;                                           // GrenadeFx plays it at the release
            PlayThrow(from, emitter, au);
            return true;
        }
        /// <summary>GrenadeFx.Fly: the grenade leaves the hand.</summary>
        public static void GrenadeReleased(Vector3 pos, int emitter) { if (Enabled) PlayThrow(pos, emitter, CombatAudio.I); }
        static void PlayThrow(Vector3 at, int emitter, CombatAudio au)
        {
            if (au) au.PlayPitched("grenade", at, GainGrenadeThrow, 0.1f, emitter, 1f, LegacyMatch.BankGrenade);
        }
        /// <summary>GrenadeFx: the first clink at 88 % of the flight (the old one), then a smaller second bounce 0.12-0.2 s later, and the
        /// fuse hiss while it flies (on screen, close zoom).</summary>
        public static void GrenadeLanded(Vector3 at, float untilBlast)
        {
            if (!Enabled || untilBlast < 0.22f) return;                                                    // a short throw bursts on its first bounce
            Later(Mathf.Min(0.2f, untilBlast * 0.55f), () => CombatAudio.I?.PlayPitched("grenade_land", at, 0.26f, 0.05f, -1, Random.Range(1.05f, 1.2f), BankGrenadeClink));
        }
        public static void GrenadeFuse(Vector3 at)
        {
            if (!Enabled || CamHeight() > 110f || !OnScreen(at)) return;
            AudioDirector.Fire("wpn_grenade_fuse", at, -1);
        }

        // ================================================================================================ rockets / shells in flight
        public const float GainMechRocket = LegacyMatch.GainDefault * 2f;
        /// <summary>CombatView.MissileFired / MechRockets.Fire: the projectile's flight sound travels with it (CombatAudio.PlayAlong, the
        /// (the GUN's shell whoosh, low, only flights over 0.6 s).</summary>
        public static void Flight(string type, Vector3 from, Vector3 to, float simDelay, int emitter, CombatAudio au)
        {
            if (!Enabled || !au) return;
            float secs = OpenZFxPlayer.RealFlight(simDelay, Speed);
            switch (type)
            {
                case "medium":
                    au.PlayAlong("mech_flight", from, to, secs, 6f, LegacyMatch.GainDefault * 1.1f, emitter + 5100000, Random.Range(0.95f, 1.08f), 0.12f, MlrsSalvo.BankWhoosh); break;
                case "missile_cannon":
                    au.PlayAlong("mech_flight", from, to, secs, 6f, LegacyMatch.GainDefault * 1.2f, emitter + 5200000, Random.Range(0.9f, 1.02f), 0.15f, MlrsSalvo.BankWhoosh); break;
                case "light": case "heavy":
                    if (secs < 0.6f) break;
                    au.PlayAlong("shell_flight", from, to, secs, 9.8f, LegacyMatch.GainDefault * (type == "heavy" ? 0.75f : 0.6f), emitter + 5300000,
                        type == "heavy" ? Random.Range(0.8f, 0.9f) : Random.Range(0.95f, 1.1f), 0.05f, LegacyMatch.BankShellWhoosh); break;
            }
        }

        /// <summary>LegacyMatch.LaunchSound: the mechanics after a shot: tank / gun / howitzer breech clank 0.35-0.6 s later (50 %, on screen),
        /// the Tough's reload 0.9 s later (60 %, close zoom).</summary>
        public static void AfterShot(string type, Vector3 from, int emitter)
        {
            if (!Enabled) return;
            switch (type)
            {
                case "light": case "heavy": case "gun": case "howitzer":
                    if (!Chance(0.5f) || !OnScreen(from)) return;
                    Later(Random.Range(0.35f, 0.6f), () => AudioDirector.Fire("wpn_breech_clank", from, emitter, 1f, type == "heavy" || type == "howitzer" ? 0.88f : 1f)); break;
                case "tough":
                    if (!Chance(0.6f) || CamHeight() > 95f || !OnScreen(from)) return;
                    Later(0.9f, () => AudioDirector.Fire("wpn_tough_reload", from, emitter)); break;
            }
        }
        /// <summary>WeaponsV2Runner: the Tough's reload after a real launch (same rule as AfterShot).</summary>
        public static void ToughReload(Vector3 at, int emitter) => AfterShot("tough", at, emitter);
        /// <summary>WeaponsV2Runner: the sniper works the bolt after a shot (50 %, close zoom).</summary>
        public static void SniperBolt(Vector3 at, int emitter)
        {
            if (!Enabled || !Chance(0.5f) || CamHeight() > 95f || !OnScreen(at)) return;
            Later(0.55f, () => AudioDirector.Fire("wpn_sniper_bolt", at, emitter));
        }

        // ================================================================================================ bullets on the ground / buildings
        static float missTokens = 4f, missAt;
        /// <summary>OpenZFxPlayer.Impact (drawn rounds that hit the ground) / WeaponsV2Runner MG + sniper misses: sand or stone impacts
        /// (sd_impact_bullet_sand / _stone, 4 takes each), 35 % of the drawn misses, at most 4 per second map-wide, on screen
        /// (the table events are in-view only). Water keeps its own splash.</summary>
        public static void GroundImpact(Vector3 at, FxSurface s)
        {
            if (!Enabled || s == FxSurface.Water || s == FxSurface.Unit || s == FxSurface.Metal) return;
            float now = Time.unscaledTime; missTokens = Mathf.Min(4f, missTokens + (now - missAt) * 4f); missAt = now;
            if (missTokens < 1f || !Chance(0.35f)) return;
            missTokens -= 1f;
            AudioDirector.Fire(s == FxSurface.Ground ? "imp_bullet_sand" : "imp_bullet_stone", at, -1, Random.Range(0.8f, 1f));
        }
        /// <summary>LegacyMatch.Shot: a round that hits a building also chips its masonry (imp_bullet_stone) when it lands (50 %).</summary>
        public static void ShotLanded(ZObject target, bool hit, Vector3 to, float flight)
        {
            if (!Enabled || !hit || target == null || target.Kind != ZKind.Building || !Chance(0.5f)) return;
            Later(Mathf.Max(0f, flight), () => AudioDirector.Fire("imp_bullet_stone", to, -1, 0.9f));
        }
        static int shrapFrame = -1, shrapN;
        /// <summary>HitReactions.Chips: the shrapnel chips flying off a unit caught in a blast tink (at most 2 per blast frame).</summary>
        public static void Shrapnel(Vector3 p)
        {
            if (!Enabled) return;
            if (shrapFrame != Time.frameCount) { shrapFrame = Time.frameCount; shrapN = 0; }
            if (++shrapN > 2) return;
            Later(Random.Range(0.05f, 0.25f), () => AudioDirector.Fire("imp_shrapnel_tink", p, -1, Random.Range(0.7f, 1f)));
        }

        // ================================================================================================ debris raining down (FxManager.Breakup)
        /// <summary>FxManager.Breakup (fort turret crash, building / bridge deaths, nuke, harbour, rocks, heavy shells, lamps, props): the chunks
        /// land 0.6-1.1 s after the blast (vy 2-5 m/s, g 9.81) in silence before. One landing per call, per 5 m cell every 0.4 s, on screen:
        /// metal = dst_debris_metal (+ exp_debris_rain for big breakups), stone = dst_debris_concrete / dst_rock_chunks, wood = dst_debris_wood.</summary>
        public static void Debris(Vector3 at, string material, float width, int count)
        {
            if (!Enabled || !OnScreen(at, 0.2f)) return;
            if (!Gate($"brk:{Mathf.RoundToInt(at.x / 5f)}:{Mathf.RoundToInt(at.z / 5f)}", 0.4f)) return;
            bool big = width >= 2.5f || count >= 14;
            if (width < 2f && material != "metal") return;                                                  // small props: their own break sound is enough
            float t = Random.Range(0.6f, 0.85f);
            string ev = material == "metal" ? "dst_debris_metal" : material == "wood" ? "dst_debris_wood" : "dst_debris_concrete";
            var p = at + Vector3.down * 0.3f;
            Later(t, () => AudioDirector.Fire(ev, p, -1, big ? 1f : 0.75f));
            if (big) Later(t + Random.Range(0.25f, 0.45f), () => AudioDirector.Fire(material == "metal" || material == "stone" ? "exp_debris_rain" : ev, p, -1, 0.8f));
        }

        // ================================================================================================ turrets (UnitAim.SolveGun)
        sealed class Slewer { public float rate, since, lastStart, lastLock, seen, lastEnd = -99f, stillFor; public bool loop, hadTarget, scanning; public Vector3 pos; }
        static float lastScanStart = -99f;
        static readonly Dictionary<int, Slewer> slew = new Dictionary<int, Slewer>();
        static int slewLoops; static float slewCountAt;
        static float SlewPitch(string t)
        {
            switch (t)
            {
                case "heavy": case "howitzer": return 0.75f;
                case "medium": return 0.9f;
                case "gatling": case "jeep": return 1.25f;
                default: return 1f;
            }
        }
        /// <summary>UnitAim.SolveGun after the eased slew: every tank / gun / tower cannon / MLRS rack that swings onto its target sounds
        /// like a turret: the servo start clunk (mech-servo, 1.2 s cooldown per unit) and the traverse loop (sd_turret_traverse_loop, pitch
        /// per chassis, volume with the slew rate) while it turns over 30 deg/s, the "on target" tick when it settles on a target (30 %),
        /// a quiet lock beep when a missile cannon / MLRS / howitzer acquires one (60 %). On screen, camera under 110 m, 3 loops at most,
        /// Zod's idle 45 deg scan steps at half gain.</summary>
        public static void Slew(ZObject o, Transform at, float yawVel, float pitchVel, bool hasTarget, float aimErr, float tol)
        {
            if (!Enabled || o == null || !at || o.Kind == ZKind.Robot) return;
            var ad = AudioDirector.I; if (!ad) return;
            if (!slew.TryGetValue(o.Id, out var s)) slew[o.Id] = s = new Slewer { lastStart = -9f, lastLock = -9f };
            float now = Time.time, rate = Mathf.Max(Mathf.Abs(yawVel), Mathf.Abs(pitchVel) * 1.5f);
            s.pos = at.position; s.seen = Time.unscaledTime;
            string key = "slew:" + o.Id;
            bool visible = CamHeight() < 110f && OnScreen(s.pos, 0.05f) && !o.Destroyed;
            if (hasTarget && !s.hadTarget && visible && (o.Type == "missile_cannon" || o.Type == "missile_launcher" || o.Type == "howitzer") && Chance(0.6f) && now - s.lastLock > 3f)
            { s.lastLock = now; AudioDirector.Fire("wpn_target_lock", s.pos, o.Id); }
            bool moving = rate > (s.loop ? 12f : 30f) && visible;
            // steps of every idle tank / jeep / gun each started the loop, cut it, started it again: a constant whirring "stuck loop".
            // A turn onto a target keeps its loop; an idle scan step gets it at most every 6 s per turret, one at a time, 1.5 s apart.
            // The loop also holds through the ease dips (stops after 0.15 s under the threshold) instead of stuttering.
            if (moving && !s.loop && !hasTarget)
            {
                bool other = false; foreach (var k in slew) if (k.Value.loop && k.Value.scanning) { other = true; break; }
                if (other || now - s.lastEnd < 6f || Time.unscaledTime - lastScanStart < 1.5f) moving = false;
                else lastScanStart = Time.unscaledTime;
            }
            if (s.loop) { if (moving) s.stillFor = 0f; else if (visible && !o.Destroyed) { s.stillFor += Time.deltaTime; if (s.stillFor < 0.15f) moving = true; } }
            if (moving && !s.loop)
            {
                if (Time.unscaledTime - slewCountAt > 0.5f) { slewCountAt = Time.unscaledTime; slewLoops = 0; foreach (var k in slew) if (k.Value.loop) slewLoops++; }
                if (slewLoops < 3)
                {
                    s.stillFor = 0f;
                    s.loop = true; s.since = now; slewLoops++;
                    s.scanning = !hasTarget;
                    if (!s.scanning && now - s.lastStart > 1.2f) { s.lastStart = now; AudioDirector.Fire("veh_turret_slew_start", s.pos, o.Id, 1f, SlewPitch(o.Type)); }   // the idle scan: the loop only
                    ad.StartLoop(key, "veh_turret_slew_loop", s.pos, at, 0.01f, SlewPitch(o.Type));
                }
            }
            if (s.loop)
            {
                if (!moving)
                {
                    s.loop = false; s.lastEnd = now; ad.StopLoop(key, 0.18f);
                    if (hasTarget && aimErr <= tol * 1.5f && now - s.since > 0.25f && Chance(0.3f)) AudioDirector.Fire("veh_turret_on_target", s.pos, o.Id, 1f, SlewPitch(o.Type));
                }
                else
                {
                    float k = Mathf.Clamp01((rate - 12f) / 120f), v = (0.35f + 0.65f * k) * (s.scanning ? 0.5f : 1f);
                    ad.SetLoop(key, v, SlewPitch(o.Type) * (0.92f + 0.16f * k));
                }
            }
            s.hadTarget = hasTarget; s.rate = rate;
        }
        /// <summary>UnitAim: a unit's rig was removed / the unit died: the loop stops.</summary>
        public static void SlewEnd(ZObject o) { if (o != null && slew.TryGetValue(o.Id, out var s) && s.loop) { s.loop = false; AudioDirector.I?.StopLoop("slew:" + o.Id, 0.2f); } }

        static readonly Dictionary<int, (bool spinning, float at)> spins = new Dictionary<int, (bool, float)>();
        /// <summary>UnitAim.AnimateGun: the jeep MG and the mech gun arm spin their barrels while firing (1080 / 900 deg/s): the gatling's
        /// spin-up / spin-down motor, pitched 1.3 (jeep) / 0.85 (mech), 6 dB under the gatling (the gatling keeps its own, AudioDirector).</summary>
        public static void BarrelSpin(ZObject o, bool firing, float spinSpeed, float max, Vector3 at)
        {
            if (!Enabled || o == null || (o.Type != "jeep" && o.Type != "medium")) return;
            spins.TryGetValue(o.Id, out var st);
            bool jeep = o.Type == "jeep";
            if (firing && !st.spinning && spinSpeed < 0.25f * max) { spins[o.Id] = (true, Time.time); AudioDirector.Fire(jeep ? "wpn_jeep_spinup" : "wpn_mech_arm_spinup", at, o.Id); }
            else if (!firing && st.spinning && spinSpeed > 0.5f * max && Time.time - st.at > 0.3f) { spins[o.Id] = (false, Time.time); AudioDirector.Fire(jeep ? "wpn_jeep_spindown" : "wpn_mech_arm_spindown", at, o.Id); }
            else if (!firing && st.spinning && spinSpeed < 0.05f * max) spins[o.Id] = (false, Time.time);
        }

        // ================================================================================================ fort main gun (FortGunView)
        sealed class FortState { public bool moving, elevating, heavyArmed = true; public float lastHeavy = -99f; public float movedAt, stillFor, lastStart, lastElev, lastTilt, prevTilt, prevK = 1f, lastLock, seen, rate; }
        /// <summary>
        /// turret really turns and reports, "STALE ..." when the loop runs without that reason; null = not a sound_v3 key.</summary>
        public static string LoopReason(string key)
        {
            if (key == null) return null;
            int c = key.IndexOf(':'); if (c < 0 || !int.TryParse(key.Substring(c + 1), out int id)) return null;
            float now = Time.unscaledTime;
            if (key.StartsWith("slew:")) return slew.TryGetValue(id, out var s)
                ? ((s.loop && now - s.seen < 0.5f ? "ok" : "STALE") + $" turret slew {s.rate:0.0} deg/s, seen {now - s.seen:0.0} s ago{(s.scanning ? ", idle scan" : "")}") : "STALE no turret state";
            if (key.StartsWith("fortrav:")) return forts.TryGetValue(id, out var f)
                ? ((f.moving && now - f.seen < 0.5f ? "ok" : "STALE") + $" fort gun turn {f.rate:0.0} deg/s, seen {now - f.seen:0.0} s ago") : "STALE no fort state";
            return null;
        }
        static readonly Dictionary<int, FortState> forts = new Dictionary<int, FortState>();
        /// <summary>The fort gun's heavy body-tilt sound: the head tilt share that counts as the start of the upward tilt, the share under
        /// which the next tilt is armed again, the minimum gap per fort (s); plays / tilt starts skipped (off screen, gap, same tilt).</summary>
        public static float FortTiltStart = 0.06f, FortTiltRearm = 0.03f, FortTiltHeavyGap = 9f;
        public static int FortTiltHeavyPlays, FortTiltHeavySkips;
        /// <summary>
        /// A heavy start clunk + whine the moment the turret starts to turn, the heavy ring-gear loop while it turns
        /// (from 3 deg/s: normal tracking included; volume and pitch with the rate, up to its 38 deg/s), a stop thud when it comes to rest,
        /// a hydraulic elevation groan when the barrel lifts (elevation rate over 1.5 deg/s) and when the whole head tilts up for a shot.
        /// Only while on screen (the loop fades out off screen).</summary>
        public static void FortGunMotion(ZObject fort, Transform yaw, float headingVel, float elevVel, float tilt01, float dt, bool hasTarget)
        {
            if (!Enabled || fort == null || !yaw) return;
            var ad = AudioDirector.I; if (!ad) return;
            if (!forts.TryGetValue(fort.Id, out var s)) forts[fort.Id] = s = new FortState { lastStart = -9f, lastElev = -9f, lastTilt = -9f };
            var p = yaw.position; float now = Time.time, rate = Mathf.Abs(headingVel); s.seen = Time.unscaledTime; s.rate = rate;
            bool vis = OnScreen(p, 0.05f) && !fort.Destroyed;
            string key = "fortrav:" + fort.Id;
            // longer runs the ring-gear loop (it played about half of every 24 s scan cycle, with no gun visibly turning); the loop now
            // needs a real turn: 4 deg/s onto a target, 9 deg/s without one (above the scan's peak).
            if (vis && rate > (s.moving ? (hasTarget ? 3f : 8f) : (hasTarget ? 4f : 9f)))
            {
                s.stillFor = 0f;
                if (!s.moving)
                {
                    s.moving = true; s.movedAt = now;
                    if (now - s.lastStart > 0.8f) { s.lastStart = now; AudioDirector.Fire("veh_fort_traverse_start", p, fort.Id, Mathf.Lerp(0.7f, 1f, rate / 38f)); }
                    ad.StartLoop(key, "veh_fort_traverse_loop", p, yaw, 0.01f, 0.9f);
                }
                float k = Mathf.Clamp01(rate / 38f);
                ad.SetLoop(key, (0.35f + 0.65f * k) * (hasTarget ? 1f : 0.6f), 0.88f + 0.2f * k);
            }
            else if (s.moving)
            {
                s.stillFor += dt;
                if (s.stillFor > 0.15f || !vis)
                {
                    s.moving = false; ad.StopLoop(key, 0.3f);
                    if (vis && now - s.movedAt > 0.5f) AudioDirector.Fire("veh_fort_traverse_stop", p, fort.Id);
                }
            }
            if (vis && Mathf.Abs(elevVel) > 1.5f)
            {
                if (!s.elevating && now - s.lastElev > 1.4f) { s.lastElev = now; AudioDirector.Fire("veh_fort_elevate", p, fort.Id, Mathf.Lerp(0.6f, 1f, Mathf.Abs(elevVel) / 16f), elevVel > 0f ? 1f : 0.9f); }
                s.elevating = true;
            }
            else if (Mathf.Abs(elevVel) < 0.5f) s.elevating = false;
            // the heavy gun BODY starts its upward tilt (the head pitching up for a shot, not the barrel's small aim corrections): the
            // heavy tilt sound, only on screen, once per tilt (re-armed when the body is back down) and at most every FortTiltHeavyGap s per fort
            if (tilt01 < FortTiltRearm) s.heavyArmed = true;
            bool heavy = false;
            if (vis && s.heavyArmed && s.prevTilt < FortTiltStart && tilt01 >= FortTiltStart && now - s.lastHeavy >= FortTiltHeavyGap)
            { s.heavyArmed = false; s.lastHeavy = now; heavy = true; FortTiltHeavyPlays++; AudioDirector.Fire("veh_fort_gun_tilt_heavy", p, fort.Id); }
            else if (s.prevTilt < FortTiltStart && tilt01 >= FortTiltStart) FortTiltHeavySkips++;
            if (vis && s.prevTilt < 0.12f && tilt01 >= 0.12f && now - s.lastTilt > 2f && now - s.lastElev > 0.6f && now - s.lastHeavy > 1.5f && !heavy)
            { s.lastTilt = now; AudioDirector.Fire("veh_fort_elevate", p, fort.Id, 1f, 0.82f); }                              // the head tilts up for the shot (the heavy tilt sound covers its own tilt)
            s.prevTilt = tilt01;
        }
        /// <summary>FortGunView.ReloadRing: the 30 s reload has just filled (breech-lock clank, on screen).</summary>
        public static void FortGunReload(ZObject fort, Vector3 at, float k)
        {
            if (!Enabled || fort == null) return;
            if (!forts.TryGetValue(fort.Id, out var s)) forts[fort.Id] = s = new FortState { lastStart = -9f, lastElev = -9f, lastTilt = -9f };
            // which is ~0.998 when a 30 s reload reaches 0.999 (it moves ~0.001 per frame), so the clank never played; and with a target
            // in range the sim fires on the very tick the reload completes (k drops from ~0.998 to 0), so 0.999 is never seen at all.
            // The breech locks at 98.5 % (~0.45 s before the shot in a siege; the ring reads full), once per reload.
            if (k < 0.9f) s.prevK = 0f;
            else if (k >= 0.985f && s.prevK < 0.5f) { s.prevK = 1f; if (OnScreen(at, 0.1f)) AudioDirector.Fire("wpn_fort_ready", at, fort.Id); }
        }
        /// <summary>FortGunView.ChooseTarget: the fort gun picks a new target (a soft lock beep, 2.5 s cooldown, on screen).</summary>
        public static void FortGunLock(ZObject fort, Vector3 at)
        {
            if (!Enabled || fort == null) return;
            if (!forts.TryGetValue(fort.Id, out var s)) forts[fort.Id] = s = new FortState { lastStart = -9f, lastElev = -9f, lastTilt = -9f };
            if (Time.time - s.lastLock < 2.5f || !OnScreen(at, 0.1f)) return;
            s.lastLock = Time.time; AudioDirector.Fire("wpn_target_lock", at, fort.Id);
        }

        // ================================================================================================ robots: knockdowns
        /// <summary>UnitView.React: a stagger = a servo jolt; a knockdown = the body thud when it lands (fly end) and the servo scramble when
        /// it gets up (lie end). Camera under 110 m (the table events carry the limits).</summary>
        public static void Reacted(UnitView v, int kind, float delay, float flyEnd, float lieEnd)
        {
            if (!Enabled || !v || v.Obj == null || kind < 2) return;
            var tr = v.transform; int id = v.Obj.Id;
            if (kind == 2) { Later(Mathf.Max(0f, delay), () => { if (tr) AudioDirector.Fire("rob_getup", tr.position, id, 0.6f, 1.1f); }); return; }
            Later(Mathf.Max(0f, delay + flyEnd), () => { if (tr) AudioDirector.Fire("rob_knockdown_land", tr.position, id); });
            Later(Mathf.Max(0f, delay + lieEnd), () => { if (tr) AudioDirector.Fire("rob_getup", tr.position, id); });
        }

        // ================================================================================================ vehicles: brake, pivot squeal, suspension, shutdown
        sealed class VehState { public bool moving; public float movingSince, lastYaw, lastPitch, lastRoll; public bool init; }
        static readonly Dictionary<int, VehState> veh = new Dictionary<int, VehState>();
        static float vehNext;
        static bool Tracked(string t) => t == "light" || t == "heavy" || t == "missile_launcher";
        /// <summary>AudioDirector.Update (sound_v3 tick): the vehicles on screen: a brake hiss when a vehicle halts after driving 1.5 s
        /// (30 %), a track squeal when a tracked vehicle pivots in place (hull yaw over 60 deg/s while standing), a suspension creak on
        /// bumps (pitch / roll rate spikes, 4 %, one per vehicle per 2.5 s). Camera under 110 m. The table events carry cooldowns and voice limits.</summary>
        public static void VehicleTick(ZGame game, float dt)
        {
            if (!Enabled || game == null || game.World == null || dt <= 0f) return;
            Watchdog();
            vehNext -= dt; if (vehNext > 0f) return;
            float step = 0.1f; vehNext = step;
            bool close = CamHeight() < 110f;
            foreach (var kv in game.Views)
            {
                var v = kv.Value; var o = v ? v.Obj : null;
                if (!v || o == null || o.Kind != ZKind.Vehicle || v.Dying || !v.isActiveAndEnabled) { if (o != null) veh.Remove(o.Id); continue; }
                if (!veh.TryGetValue(o.Id, out var s)) veh[o.Id] = s = new VehState();
                var e = v.transform.eulerAngles; bool moving = o.IsMoving;
                if (!s.init) { s.init = true; s.lastYaw = e.y; s.lastPitch = e.x; s.lastRoll = e.z; s.moving = moving; s.movingSince = Time.time; continue; }
                float yawRate = Mathf.Abs(Mathf.DeltaAngle(s.lastYaw, e.y)) / step, tilt = (Mathf.Abs(Mathf.DeltaAngle(s.lastPitch, e.x)) + Mathf.Abs(Mathf.DeltaAngle(s.lastRoll, e.z))) / step;
                s.lastYaw = e.y; s.lastPitch = e.x; s.lastRoll = e.z;
                var p = v.transform.position;
                bool vis = close && o.Owner != Team.Neutral && OnScreen(p, 0.05f);
                if (moving != s.moving)
                {
                    if (!moving && vis && Time.time - s.movingSince > 1.5f && Chance(0.3f)) AudioDirector.Fire("veh_brake", p, o.Id, 0.8f, o.Type == "jeep" ? 1.2f : o.Type == "heavy" ? 0.85f : 1f);
                    s.moving = moving; s.movingSince = Time.time;
                }
                if (!vis) continue;
                if (!moving && Tracked(o.Type) && yawRate > 60f) AudioDirector.Fire("veh_tracks_squeal", p, o.Id, Mathf.Clamp01(yawRate / 160f), o.Type == "heavy" ? 0.85f : 1f);
                if (moving && tilt > 30f && Chance(0.04f) && Gate("susp:" + o.Id, 2.5f)) AudioDirector.Fire("veh_suspension", p, o.Id, 0.8f, o.Type == "jeep" ? 1.15f : 0.95f);
            }
        }
        /// <summary>A turret / fort loop whose view stopped reporting (died, tossed, disabled, paused view) fades out.</summary>
        static void Watchdog()
        {
            var ad = AudioDirector.I; if (!ad) return;
            float now = Time.unscaledTime;
            foreach (var kv in slew) if (kv.Value.loop && now - kv.Value.seen > 0.3f) { kv.Value.loop = false; ad.StopLoop("slew:" + kv.Key, 0.2f); }
            foreach (var kv in forts) if (kv.Value.moving && now - kv.Value.seen > 0.3f) { kv.Value.moving = false; ad.StopLoop("fortrav:" + kv.Key, 0.3f); }
        }

        /// <summary>AudioDirector.OnSniped / OnEjected: a vehicle whose driver was shot out (or whose crew left) goes neutral: its engine dies
        /// (sd_engine_shutdown, once) before the loop fades.</summary>
        public static void EngineDies(ZObject v, Vector3 p)
        {
            if (!Enabled || v == null || v.Kind != ZKind.Vehicle || v.Destroyed) return;
            if (!Gate("shutdown:" + v.Id, 3f)) return;
            AudioDirector.Fire("veh_engine_shutdown", p, v.Id, 1f, v.Type == "jeep" ? 1.15f : v.Type == "heavy" ? 0.85f : 1f);
        }
        /// <summary>LegacyEngineAudio: the one tank.mp3 loop per chassis (light 1, heavy .82, MLRS .92, APC / crane 1.08; jeep = car.mp3 at 1).</summary>
        public static float EnginePitch(ZObject v)
        {
            if (v == null) return 1f;
            switch (v.Type) { case "heavy": return 0.82f; case "missile_launcher": return 0.92f; case "apc": case "crane": return 1.08f; default: return 1f; }
        }

        // ================================================================================================ buildings
        static float accentNext = 8f, radarNext;
        /// <summary>
        /// The construction accents (press, air tool,
        /// hoist chains) no longer come every 6-12 s from every producing factory for the whole game. A factory gets ONE accent
        /// inside a short window after something happened there (production start / switch, a unit rolling out: FactoryReason), then
        /// it is quiet until the next event.</summary>
        static readonly Dictionary<int, float> accentWindow = new Dictionary<int, float>();
        public static float AccentWindowSeconds = 7f;
        public static int AccentPlays, AccentSkipped;
        public static void FactoryReason(ZObject b) { if (b != null) accentWindow[b.Id] = Time.time + AccentWindowSeconds; }
        /// <summary>AudioDirector.Update (sound_v3 tick): factory accents (bld_factory_accent: press, air tool, hoist chains; the table's pool
        /// that nothing triggered) every 6-12 s from a producing player factory on screen at close zoom; the nearest enemy radar on screen
        /// turns with its own quieter dish loop (the player's radar keeps bld_radar_loop).</summary>
        public static void BuildingTick(AudioDirector ad, ZGame game, float dt)
        {
            if (!Enabled || !ad || game == null || game.World == null) return;
            accentNext -= dt; radarNext -= dt;
            if (accentNext <= 0f)
            {
                accentNext = Random.Range(2f, 4f);
                if (CamHeight() < 95f)
                {
                    var list = new List<ZObject>();
                    float now = Time.time;
                    foreach (var o in game.World.Objects)
                        if (o.Kind == ZKind.Building && o.Owner == game.player && !o.Destroyed && o.IsProducer && o.Building && OnScreen(game.CenterWorld(o), 0f))
                        {
                            if (accentWindow.TryGetValue(o.Id, out var until) && now < until && until - now < AccentWindowSeconds + 1f) list.Add(o);
                            else AccentSkipped++;
                        }
                    if (list.Count > 0)
                    {
                        var b = list[Random.Range(0, list.Count)]; accentWindow.Remove(b.Id); AccentPlays++;
                        AudioDirector.Fire("bld_factory_accent", game.CenterWorld(b) + Vector3.up * 2f, b.Id);
                        accentNext = Random.Range(6f, 12f);
                    }
                }
            }
            if (radarNext <= 0f)
            {
                radarNext = 0.5f;
                ZObject best = null; float bd = float.MaxValue; var c = Camera.main ? Camera.main.transform.position : Vector3.zero;
                foreach (var o in game.World.Objects)
                {
                    if (o.Kind != ZKind.Building || o.Type != "radar" || o.Destroyed || o.Owner == game.player || o.Owner == Team.Neutral) continue;
                    var p = game.CenterWorld(o); if (!OnScreen(p, 0.05f)) continue;
                    float d = (p - c).sqrMagnitude; if (d < bd) { bd = d; best = o; }
                }
                if (best != null) { var rp = game.CenterWorld(best) + Vector3.up * 3f; if (!ad.LoopActive("radar_enemy")) ad.StartLoop("radar_enemy", "bld_radar_loop_enemy", rp); ad.SetLoop("radar_enemy", 1f, 1f, rp); }
                else ad.StopLoop("radar_enemy", 0.6f);
            }
        }
        /// <summary>FactoryView: the exhaust burst when a unit rolls out (hook factory_unit_out, AudioDirector.OnFactoryAudio).</summary>
        public static void FactoryUnitOut(ZObject b, Vector3 pos) { if (Enabled) AudioDirector.Fire("bld_factory_exhaust", pos + Vector3.up * 2.5f, b != null ? b.Id : -1); }
        /// <summary>FactoryView.UpdateVents: a main vent puff while producing (25 %, close zoom, on screen): a small pneumatic hiss.</summary>
        public static void VentPuff(Vector3 at, ZObject b)
        {
            if (!Enabled || !Chance(0.25f) || CamHeight() > 80f || !OnScreen(at)) return;
            // 0.35-0.7 s per producing factory, two alternating takes = tick, tock). Now rare: one per factory every 9-15 s, one map-wide per 5 s.
            float now = Time.time; int id = b != null ? b.Id : -1;
            if (ventNextAll - now > 6f) ventNextAll = 0f;                                             // a stale time from an earlier Play session
            if (now < ventNextAll || (ventNext.TryGetValue(id, out var nx) && now < nx && nx - now < 16f)) return;
            ventNext[id] = now + Random.Range(9f, 15f); ventNextAll = now + 5f;
            AudioDirector.Fire("bld_factory_vent", at, id, Random.Range(0.7f, 1f)); VentPlays++;
        }
        static readonly Dictionary<int, float> ventNext = new Dictionary<int, float>(); static float ventNextAll;
        public static int VentPlays;
        /// <summary>RulesView.Rebuild: the bridge pieces rise out of the rubble / water (a rising whoosh every other piece at its start time,
        /// a drip for pieces that start under water level) over the rebuild sound.</summary>
        public static void BridgeRebuild(Vector3 c, float span, List<(Vector3 from, float t0)> pieces, float waterY)
        {
            if (!Enabled) return;
            for (int i = 0; i < pieces.Count; i++)
            {
                var pc = pieces[i];
                if (i % 2 == 0) Later(pc.t0, () => AudioDirector.Fire("bld_bridge_rise", pc.from, -1, Random.Range(0.7f, 1f), Random.Range(0.9f, 1.1f)));
                if (pc.from.y < waterY + 0.3f && i % 3 == 0) Later(pc.t0 + 0.15f, () => AudioDirector.Fire("bld_bridge_drip", pc.from + Vector3.up * 1.5f, -1));
            }
        }
        static readonly Dictionary<int, int> bridgeTick = new Dictionary<int, int>();
        /// <summary>RulesView.LateUpdate: the destroyed bridge's countdown badge ticks each second in its last 5 s (on screen).</summary>
        public static void BridgeCountdown(ZObject b, double left, Vector3 c)
        {
            if (!Enabled || b == null || left > 5.0 || left < 0.0) return;
            int s = Mathf.CeilToInt((float)left);
            if (bridgeTick.TryGetValue(b.Id, out var last) && last == s) return;
            bridgeTick[b.Id] = s;
            if (OnScreen(c, 0.1f)) AudioDirector.Fire("bld_bridge_countdown", c + Vector3.up * 2f, b.Id, 1f, s <= 1 ? 1.25f : 1f);
        }
        /// <summary>PropBreakables: the break bank by prop name (cactus = the wet plant snap, mooring bollards = the metal pool); null = keep
        /// the material bank.</summary>
        public static string[] PropBreakBank(string lowerName)
        {
            if (lowerName == null) return null;
            if (lowerName.Contains("cactus")) return BankCactus;
            if (lowerName.Contains("bollard")) return BankMetalMedium;
            return null;
        }
        /// <summary>BaseProp / AudioDirector fire loops: the flare-up when something catches fire (a fresh fire only, once per key).</summary>
        public static void Ignite(string key, Vector3 at, float vol = 1f)
        {
            if (!Enabled || !Gate("ignite:" + key, 30f)) return;
            AudioDirector.Fire("dst_fire_ignite", at, -1, vol);
        }

        // ================================================================================================ UI
        static float readyAt = -10f;
        /// <summary>A unit of the player's is ready (UnitBuilt / CannonStored, the production window's completion stamp): a quiet 2D chime,
        /// once per 1.5 s (Rachel's line is grouped / filtered by the chatter setting; this always marks it).</summary>
        public static void UnitReady()
        {
            if (!Enabled || Time.unscaledTime - readyAt < 1.5f) return;
            readyAt = Time.unscaledTime; AudioDirector.UI("ui_unit_ready");
        }
        static float lockUiAt = -10f, hoverTickAt = -10f, hoverTickedAt = -10f; static object lastHover, hoverTicked;
        /// <summary>OpenZCursors: the cursor becomes attack / enter / grab / repair over a target (a soft lock blip, only on a state change,
        /// 0.25 s throttle) or "nono" (a muted buzz, quieter than ui_error); hovering a new world object with nothing to order ticks softly.</summary>
        public static void CursorChanged(string prev, string cur, object hover)
        {
            if (!Enabled) return;
            float now = Time.unscaledTime;
            if (cur != prev)
            {
                if ((cur == "attack" || cur == "enter" || cur == "grab" || cur == "repair") && now - lockUiAt > 0.25f) { lockUiAt = now; AudioDirector.UI("ui_target_lock", cur == "attack" ? 1f : 0.8f, cur == "attack" ? 1f : 1.1f); lastHover = hover; return; }
                if (cur == "nono" && now - lockUiAt > 0.25f) { lockUiAt = now; AudioDirector.UI("ui_target_nono"); lastHover = hover; return; }
            }
            if (hover != null && !ReferenceEquals(hover, lastHover) && (cur == "arrow" || cur == "move"))
            {
                // object, 0.4 s apart at most, the same object again only after 2 s away from it
                if (hoverTickAt > now) hoverTickAt = hoverTickedAt = -10f;                                 // times from an earlier Play session
                bool again = ReferenceEquals(hover, hoverTicked) && now - hoverTickedAt < 2f;
                if (!again && now - hoverTickAt > 0.4f) { hoverTickAt = now; AudioDirector.UI("ui_world_hover"); }
                hoverTicked = hover;
            }
            if (hover != null && ReferenceEquals(hover, hoverTicked)) hoverTickedAt = now;
            lastHover = hover;
        }
        /// <summary>OzCheatSpawner.Spawn: the dropped unit lands in the 3D world: robots = 3 staggered servo landings, vehicles = a heavy
        /// metal landing + suspension creak, guns = the Atom turret placement.</summary>
        public static void CheatDrop(string type, bool robot, bool cannon, Vector3 at)
        {
            if (!Enabled) return;
            var au = CombatAudio.I;
            if (robot) { for (int i = 0; i < 3; i++) { float d = 0.08f * i + Random.Range(0f, 0.05f); var p = at + Random.insideUnitSphere * 1.2f; Later(d, () => AudioDirector.Fire("rob_land", p, -1, 0.8f, Random.Range(0.9f, 1.1f))); } return; }
            if (cannon) { au?.Play(PlacementPop.PlacedKey, at, PlacementPop.PlacedGain, 0f, -1, PlacementPop.PlacedClip); return; }
            au?.PlayPitched("turret", at, 0.9f, 0.1f, -1, type == "heavy" ? 0.85f : 1f, BankMetalHeavy);
            Later(0.15f, () => AudioDirector.Fire("veh_suspension", at, -1, 1f));
        }
        /// <summary>OpenZRallyPoints.Clear: the rally route was cleared (always true: it only adds the cue to the condition).</summary>
        public static bool RallyCleared() { if (Enabled) AudioDirector.UI("ui_rally_clear"); return true; }
        static float endT0 = -1f; static int endMask;
        /// <summary>OpenZEndScreen.Animate (e = s since the screen opened): the reveal timeline gets its sounds: band whoosh (0.30 s), the four
        /// stat plates landing (1.56 + 0.11 i), the chart rising + its lines drawing (2.3 s), the buttons (3.2 s). Skipped = no cues.</summary>
        public static void EndScreen(float t0, float e, bool skipped, int plates)
        {
            if (!Enabled) return;
            if (!Mathf.Approximately(t0, endT0)) { endT0 = t0; endMask = 0; }
            if (skipped) { endMask = -1; return; }
            void Cue(int bit, float at, string id, float vol = 1f, float pitch = 1f) { if ((endMask & (1 << bit)) == 0 && e >= at) { endMask |= 1 << bit; AudioDirector.UI(id, vol, pitch); } }
            Cue(0, 0.3f, "ui_band_whoosh");
            for (int i = 0; i < Mathf.Min(plates, 6); i++) Cue(1 + i, 1.3f + 0.11f * i + 0.26f, "ui_plate_land", 1f, 1f + 0.03f * i);
            Cue(8, 2.3f, "ui_chart_draw");
            Cue(9, 3.2f, "ui_hover", 0.8f, 0.9f);
        }
        /// <summary>OpenZMainMenu loading screen: a tick on each stage change and the confirm at 100 %.</summary>
        static int loadStage = -1; static bool loadDone;
        public static void LoadingStage(int stage, float shown, System.Action<string> sfx)
        {
            if (!Enabled || sfx == null) return;
            if (stage != loadStage) { if (loadStage >= 0) sfx("ui_count_tick"); loadStage = stage; loadDone = false; }
            if (!loadDone && shown >= 0.999f) { loadDone = true; sfx("ui_confirm"); }
        }
        /// <summary>Pause / main menu sliders: on release a sample on that bus (EFFECTS: a blast; VOICE: a short Rachel line; MUSIC plays
        /// itself), audible while the pause menu holds the listener.</summary>
        public static void SliderPreview(string bus)
        {
            if (!Enabled || bus == null) return;
            if (bus == "SFX") AudioDirector.PreviewBus("ui_preview_sfx");
            else if (bus == "Voice" || bus == "Announcer") AudioDirector.PreviewBus("ui_preview_voice");
            else if (bus == "Robots") AudioDirector.PreviewBus("vo_select_generic");
        }
    }
}

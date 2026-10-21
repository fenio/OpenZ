using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV87 (version 1.1), view side of "hatch drivers fire their own weapon" (rule: Sim/ZObject.DriverFire.cs). Every shot the
    /// driver of a light / medium / heavy tank fires in the sim (ZEventType.DriverShot) is drawn here from the figure in the hatch
    /// with the SAME effect and sound his robot type has on foot (no new assets):
    /// <list type="bullet">
    /// <item>grunt, psycho, sniper, laser: WeaponsV2Runner, the code that draws these robots on foot (WeaponsV2.Shot, as
    /// CombatView.DrawShot calls it): the grunt's bolt, the psycho's machine-gun bursts with tracers and the mg clips, the sniper's
    /// crack with its vapour trail and echo tail, the laser's double tap of green bolts with zip and zap. The runner keeps one rig
    /// per shooter; a tank's rig has no robot view, so its rounds leave the point this file gives it: the hatch figure's muzzle,
    /// read again every frame after WeaponHold (<see cref="DriverMuzzleNow"/>), and they stop when he drops back in.</item>
    /// <item>pyro: OpenZPyroFire's jet (HatchShot): the stream, sticky fire, burning ground, lights and the flame loop, from his
    /// flamer's muzzle; it ends when he drops back in.</item>
    /// <item>tough: his bazooka rocket with back-blast, launch and flight sound (it flies for the sim's own flight time and ends
    /// where the sim's blast is drawn: no second, cosmetic blast). electrobot: his tesla bolt (OpenZElectroFx.DriverBolt; he holds
    /// no gun in a hatch, the bolt leaves from between his arms).</item>
    /// </list>
    /// The 1.0 hatch picture's simple effects (bolt, tracer, FxManager.Flame / Laser with one clip) stay only as the fallback: the
    /// runner or the jet is switched off or its assets are missing, or the figure is not out in the frame of the shot.
    /// How often a shot is SEEN (every shot counts in the sim): grunt, laser, tough, electrobot every shot; the psycho on the
    /// runner's own burst clock; the pyro's jet is re-cued at most every <see cref="DriverRapidGap"/>; the sniper on the on-foot
    /// aimed cadence (WeaponsV2Logic.SniperCadence, 1.05 to 1.6 s between cracks). A killing shot always shows.
    /// The figure kicks back on each shot. With the rule off ('Zod exact') nothing arrives here and the commander fires as a
    /// picture with the tank's own shots as in 1.0.
    /// The APC's lone driver and the crane's driver need nothing from this file: their shots are their vehicle's own Hitscan /
    /// MissileFired events and leave the figure's muzzle through UnitView.MuzzlePos (UnitView.CraneHatch.cs).
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>DV87 proof counters: driver shots drawn; of those, drawn from the hatch rim because the figure was not out
        /// (off screen until this frame, a fast game speed); shots not shown on their own (the pyro's jet between two cues, the
        /// sniper's aimed cadence); tesla bolts handed to OpenZElectroFx; shots handed to WeaponsV2Runner (the on-foot grunt /
        /// psycho / sniper / laser effect); jet cues handed to OpenZPyroFire; shots drawn with the simple 1.0 fallback effect.</summary>
        public static int DriverShotsDrawn, DriverShotsNoFigure, DriverShotsThinned, DriverBolts, DriverShotsOnFoot, DriverJets, DriverShotsFallback;
        /// <summary>DV87, fallback effects only: his shots count now, so they sound like the same weapon on foot (the 1.0 picture
        /// played them at commanderShotGain under the tank's gun). The runner and the jet play at their own on-foot levels.</summary>
        public static float DriverShotGain = 1f;
        /// <summary>DV87: the Psycho and the Pyro fire every 0.1 s of game time. Real seconds between two kicks of the figure, two
        /// cues of the Pyro's jet (on foot it is re-cued every 0.2 s, held 0.4 s) and two fallback shots.</summary>
        public const float DriverRapidGap = 0.16f;

        readonly Dictionary<int, float> driverDrawnAt = new Dictionary<int, float>();
        readonly Dictionary<int, WeaponsV2Logic.SniperCadence> driverAim = new Dictionary<int, WeaponsV2Logic.SniperCadence>();

        Transform DriverTargetBody(ZObject t) =>
            t == null ? null : game.Views.TryGetValue(t.Id, out var tv) && tv ? tv.transform : game.StaticViews.TryGetValue(t.Id, out var sgo) && sgo ? sgo.transform : null;

        /// <summary>DV87 (WeaponsV2Runner, OpenZPyroFire; they run after WeaponHold posed his gun): where the rounds of vehicle
        /// `id`'s hatch driver leave right now. False = no driver figure is up in its hatch (he dropped back in, the vehicle is
        /// off screen or gone): nothing more leaves it.</summary>
        public static bool DriverMuzzleNow(int id, out Vector3 p)
        {
            p = default;
            if (!I || I.game == null || !I.game.Views.TryGetValue(id, out var v) || !v || !I.rigs.TryGetValue(v, out var r)) return false;
            return I.DriverMuzzle(r, out p);
        }

        /// <summary>The muzzle of the figure in the hatch; the Electrobot (no gun shown in a hatch) and a figure without a muzzle
        /// node: in front of his chest. False = he is not out.</summary>
        bool DriverMuzzle(Rig r, out Vector3 p)
        {
            p = default;
            if (!r.lid || !r.crew || !r.crew.activeInHierarchy || r.crewPop <= 0.3f) return false;
            float s = r.crewBase * r.crewScale * CrewFit(r, r.crew);
            bool gun = r.crewWeapon && r.crewWeapon.gameObject.activeSelf;
            if (!r.crewMuzzle) r.crewMuzzle = FindMuzzle(r.crew);
            p = gun && r.crewMuzzle ? r.crewMuzzle.position
                                    : r.crew.transform.position + r.root.up * (r.cmdShoulder * s) + (r.crewAim * Vector3.back) * ((gun ? 0.6f : 0.35f) * s);
            return true;
        }

        /// <summary>Update, one sim DriverShot event: objId = the tank, otherId = his target, x / y = the aim point (a rocket: where
        /// it lands), flag = hit; a rocket: value = flight s, radius = splash px; the Electrobot: value = HP removed, flag =
        /// stunned, flag2 = killed.</summary>
        void DriverShotSeen(ZEvent e)
        {
            var v = View(e.objId);
            if (!v || v.Obj == null || !v.gameObject.activeInHierarchy) return;
            var r = RigOf(v); if (r == null) return;
            var veh = r.obj; var t = game.World.GetAny(e.otherId);
            string type = veh.DriverGunType;
            bool killed = t != null && t.Destroyed;
            float now = Time.time;
            // psycho, pyro (a shot every 0.1 s): one beat per DriverRapidGap for the kick, the jet's cue and the fallback shot
            bool beat = veh.DriverGunCycle >= 0.2 || killed || !driverDrawnAt.TryGetValue(veh.Id, out float last) || now - last >= DriverRapidGap;
            if (type == "pyro" && !beat) { DriverShotsThinned++; return; }
            if (type == "sniper")
            {   // he aims like a sniper on foot: a crack every 1.05 to 1.6 s, the killing shot at once
                if (!driverAim.TryGetValue(veh.Id, out var cadence)) driverAim[veh.Id] = cadence = new WeaponsV2Logic.SniperCadence(veh.Id * 131 + 7);
                if (!cadence.Allow(now, killed)) { DriverShotsThinned++; return; }
            }
            if (beat) driverDrawnAt[veh.Id] = now;

            // where the shot leaves: his muzzle; no figure out: the hatch rim
            var up = r.root.up;
            bool figure = DriverMuzzle(r, out var from);
            if (figure) { if (beat) r.kick = 1f; }                                                             // recoil: back, away from the aim (PlaceCommander)
            else
            {
                from = Wp(r, HatchTopL(r));
                if (r.round) from += up * Vector3.Dot(r.round.RimWorld - from, up);
                from += up * 0.4f;
            }
            float gs = game ? Mathf.Max(0.05f, game.gameSpeed) : 1f;
            int em = veh.Id + CommanderEmitter;
            var z = OpenZFxPlayer.I; var fx = FxManager.I; var au = sounds ? CombatAudio.I : null;
            bool live = z && z.isActiveAndEnabled;

            if (type == "tough" && e.radius > 0)
            {   // his bazooka rocket: the sim's missile. It ends on the point the sim's blast is drawn (CombatView, MissileExploded:
                // inside a vehicle / gun / building footprint on the wall that faces this tank), after the sim's own flight time
                var land = ZGame.PxToWorld(e.x, e.y); land.y = game.GroundY(land.x, land.z);
                var end = land + Vector3.up * 0.25f;
                if (t != null && !t.Destroyed && t.Kind != ZKind.Robot && t.Kind != ZKind.MapItem &&
                    Mathf.Abs(e.x - t.CenterX) <= t.WidthPix * 0.44f && Mathf.Abs(e.y - t.CenterY) <= t.HeightPix * 0.44f)
                {
                    var body = DriverTargetBody(t);
                    end = body && HitSurface.OnBody(body, v.transform.position + Vector3.up * 1.2f, land + Vector3.up * 1f, out var wall, out _) ? wall : land + Vector3.up * 0.75f;
                }
                if (t != null && t.IsFortDefender) end = FortDefenderView.RoofPoint(end);
                var rd = end - from; float rl = rd.magnitude; var rdir = rl > 1e-3f ? rd / rl : -r.root.forward;
                var flat = new Vector3(rdir.x, 0f, rdir.z); flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                if (live)
                {
                    OpenZFxPlayer.NextSimEnd = e.end;                                                          // the rocket goes on the frame of the sim's blast
                    try { z.Launch(from, end, e.value, FxWeapon.Bazooka, true); }                              // Launch takes sim seconds
                    finally { OpenZFxPlayer.NextSimEnd = -1; }
                }
                else if (fx) fx.Projectile(from, end, "rocket", OpenZFxPlayer.RealFlight(e.value, gs), 6f);
                LegacyMatch.BackBlast(from - rdir * 0.8f, flat, fx);
                if (au) { LegacyMatch.LaunchSound("tough", from, em, au); LegacyMatch.RocketFlight(from, end, e.value, em, au); }
                DriverDrawn(figure); CommanderRockets++;
                return;
            }

            // direct fire: on the target's drawn body; a miss lands on the ground beside it
            bool hit = e.flag || type == ZElectro.Type;
            var aim = t != null ? AimPoint(t) : ZGame.PxToWorld(e.x, e.y) + Vector3.up;
            Vector3 to;
            if (!hit) to = HitSurface.MissGround(from, aim + new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(-0.4f, 0.8f), Random.Range(-1.6f, 1.6f)));
            else
            {
                to = aim + Random.insideUnitSphere * 0.3f;
                var body = DriverTargetBody(t);
                if (body && HitSurface.OnBody(body, from, to, out var hp, out _)) to = hp;
            }
            var d = to - from; float dist = d.magnitude; if (dist < 0.05f) return;
            var dir = d / dist;

            if (type == ZElectro.Type)
            {   // his tesla bolt, the on-foot bolt code with a given start point
                OpenZElectroFx.DriverBolt(from, new ZEvent { type = ZEventType.ElectroBolt, objId = e.objId, otherId = e.otherId, x = e.x, y = e.y, value = e.value, flag = e.flag, flag2 = e.flag2 }, veh, t);
                DriverDrawn(figure); DriverBolts++;
                return;
            }
            if (figure && t != null)
            {   // the robot's own on-foot effect and sound, from the figure. The shot goes in as the plain hit or miss it is (no driver
                // sniping from a hatch), with the vehicle as the shooter: its id is the runner's rig, the jet's key and the sound emitter
                var shot = new ZEvent { type = ZEventType.Hitscan, objId = veh.Id, otherId = t.Id, x = e.x, y = e.y, flag = hit };
                if (WeaponsV2.Shot(null, shot, veh, t, type, null, from, to))                                  // grunt, psycho, sniper, laser; no view: the rounds leave `from` / DriverMuzzleNow
                {
                    DriverDrawn(true); DriverShotsOnFoot++; if (type == "laser") CommanderLasers++;
                    return;
                }
                if (type == "pyro" && OpenZPyroFire.HatchShot(shot, veh, t, from, to))
                {
                    DriverDrawn(true); DriverJets++; CommanderFlames++;
                    return;
                }
            }

            // fallback: the simple effects of the 1.0 hatch picture (the runner / the jet is off or its assets are missing, or no figure is out)
            if (!beat) { DriverShotsThinned++; return; }                                                       // the psycho without the runner: a bolt per beat
            DriverDrawn(figure); DriverShotsFallback++;
            switch (type)
            {
                case "pyro":
                    if (fx) fx.Flame(from, to);
                    if (live) z.Muzzle(from, dir, FxWeapon.Rifle);
                    if (au) au.Play("flame", from, 0.75f * DriverShotGain, 0.6f, em, "flame_loop1");
                    CommanderFlames++;
                    break;
                case "laser":
                    if (fx) fx.Laser(from, to);
                    if (au)
                    {
                        au.PlayPitched("laser", from, WeaponsV2.GainLaserFire * DriverShotGain, 0.15f, em, 1f, WeaponsV2.BankLaserFire);
                        if (hit) au.PlayPitched("laser_hit", to, WeaponsV2.GainLaserZap * DriverShotGain, 0.15f, em, 1f, WeaponsV2.BankLaserZap);
                    }
                    CommanderLasers++;
                    break;
                default:
                {   // grunt (the crane driver's rifle) and psycho: the bolt; the sniper: his tracer
                    var w = LegacyMatch.ShotWeapon(type);
                    float flight = 0f;
                    if (live) flight = z.Shot(from, to, w, LegacyMatch.SurfaceOf(t, hit), LegacyMatch.ShotSpeed(gs));
                    else if (fx) fx.MuzzleFlash(from, d, false);
                    if (killed) BlastWave.NoteDrawn(veh.Id, t, flight);                                        // the robot falls when the drawn round arrives
                    if (!au) break;
                    if (w == FxWeapon.Bolt) au.PlayPitched("bolt", from, LegacyMatch.GainBolt * DriverShotGain, 0.15f, em, 1f, LegacyMatch.BankBolt);
                    else if (w == FxWeapon.Sniper) au.PlayPitched("sniper", from, LegacyMatch.GainSniper * DriverShotGain, 0.15f, em, 0f, LegacyMatch.BankSniper);
                    else au.PlayPitched("rifle", from, LegacyMatch.GainDefault * DriverShotGain, 0.15f, em, 1f, LegacyMatch.BankRifleSingle);
                    break;
                }
            }
        }

        static void DriverDrawn(bool figure) { DriverShotsDrawn++; CommanderShots++; if (!figure) DriverShotsNoFigure++; }
    }
}

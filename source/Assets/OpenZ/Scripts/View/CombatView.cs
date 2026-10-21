using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// rockets / shells / grenades flying over the missile delay, explosions scaled by the splash radius), sounds, deaths,
    /// captures (owner colour), ejected crews (spawned views), crates and rocks disappearing.
    /// combatview: a shot whose gun is not on target yet is held by UnitAim and comes back through Replay once the muzzle
    /// is aligned (Zod snaps turrets client-side; the sim never waits); grenades fly as real grenades (GrenadeFx).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CombatView : MonoBehaviour
    {
        ZGame game; RulesView rules;
        public int Handled { get; private set; }

        /// <summary>Zod's client bullet, flame and laser effects travel 300 px/s = 37.5 m/s (ebullet.cpp:7, eflame.cpp:10, elaser.cpp:9).</summary>
        public const float ZodBulletMps = 300f / 8f;
        // parity P5: Z draws and plays a hitscan shot on the client's own fire-animation clock (ZodClientFire), not on every server
        // damage roll: about half as many shots as rolls (grunt 1/s for 2 rolls/s). Damage stays on the sim. `last` = the latest
        // roll (hit / miss / driver hit), redrawn when the clock falls due.
        sealed class FireClock { public ZodClientFire clock; public double armedUntil = -1; public ZEvent last; public bool killDrawn; }
        readonly Dictionary<int, FireClock> clocks = new Dictionary<int, FireClock>();
        readonly List<int> expired = new List<int>();
        readonly List<ZEvent> due = new List<ZEvent>();
        public int DrawnShots { get; private set; }                                                    // parity checks: drawn vs rolled

        void Awake() { game = GetComponent<ZGame>(); if (!game) game = FindFirstObjectByType<ZGame>(); }
        void Start() { rules = game ? game.GetComponent<RulesView>() : null; }

        UnitView ViewOf(int id) => id >= 0 && game.Views.TryGetValue(id, out var v) ? v : null;

        /// <summary>combatview: UnitAim replays a held Hitscan / MissileFired event once the gun is on target.</summary>
        public void Replay(ZEvent e)
        {
            if (game == null || game.World == null) return;
            var a = game.World.GetAny(e.objId); var b = game.World.GetAny(e.otherId);
            if (e.type == ZEventType.Hitscan) Hitscan(e, a, b, FxManager.I, CombatAudio.I);
            else if (e.type == ZEventType.MissileFired) MissileFired(e, a, b, FxManager.I, CombatAudio.I);
        }

        /// <summary>deaths_mech_v1: a robot knocked down by a blast held this hitscan shot (UnitView.HoldShotWhileDown); it is drawn
        /// now that the robot is up. The damage was on the sim at the time.</summary>
        public void DrawHeld(ZEvent e)
        {
            if (game == null || game.World == null || e.type != ZEventType.Hitscan) return;
            DrawShot(e, game.World.GetAny(e.objId), game.World.GetAny(e.otherId), FxManager.I, CombatAudio.I);
        }

        Vector3 PosOf(ZObject o, float up = 0.9f)
        {
            if (o == null) return Vector3.zero;
            var v = ViewOf(o.Id);
            return v ? (up > 0 ? v.AimPos() : v.transform.position) : game.CenterWorld(o) + Vector3.up * up;
        }

        Vector3 PxGround(int x, int y) { var p = ZGame.PxToWorld(x, y); p.y = game.GroundY(p.x, p.z); return p; }

        //      from the shooter first meets the target's body, not at its centre. The body is the sim footprint (Zod's hit box)
        //      inset 12 %, as tall as the model class; robots keep their aim point.
        static float BodyHeight(ZObject t) => t.IsShuttle ? 5.1f : t.Kind == ZKind.Building ? (t.IsFort ? 7.5f : t.IsBridge ? 1.2f : 5f) : t.Kind == ZKind.Cannon ? 1.6f : t.Type == "heavy" || t.Type == "medium" ? 2.6f : 2.1f;

        bool BodyBounds(ZObject t, out Bounds b)
        {
            b = default;
            if (t == null || t.Kind == ZKind.Robot || t.Kind == ZKind.MapItem) return false;
            var p0 = ZGame.PxToWorld(t.LocX, t.LocY); var p1 = ZGame.PxToWorld(t.LocX + t.WidthPix, t.LocY + t.HeightPix);
            var c = (p0 + p1) * 0.5f; float gy = game.GroundY(c.x, c.z), h = BodyHeight(t);
            var size = new Vector3(Mathf.Abs(p1.x - p0.x), h, Mathf.Abs(p1.z - p0.z));
            var v = ViewOf(t.Id);
            if (v && t.Kind != ZKind.Building) c = v.transform.position;                                // moving units: the drawn body
            if (v && v.OnTower) gy = v.transform.position.y;                                            // crews_v1: a tower gun's body is on its pad
            if (v && t.IsShuttle) { if (OzShuttleAim.Air(t)) gy = v.transform.position.y; size.x = size.z = 6.2f; }   // DV118: the ship's own size, up where it is drawn
            b = new Bounds(new Vector3(c.x, gy + h * 0.5f, c.z), new Vector3(size.x * 0.88f, h, size.z * 0.88f));
            return true;
        }

        /// <summary>The point where the shot from `from` toward `to` enters the target's body; `to` when it cannot tell.</summary>
        Vector3 SurfaceHit(ZObject t, Vector3 from, Vector3 to)
        {
            if (!BodyBounds(t, out var b) || b.Contains(from)) return to;
            var dir = to - from; float len = dir.magnitude; if (len < 0.05f) return to;
            if (!b.Contains(to)) { var tc = b.center; tc.y = Mathf.Clamp(to.y, b.min.y + 0.3f, b.max.y - 0.2f); dir = tc - from; len = dir.magnitude; }   // aim into the body
            var ray = new Ray(from, dir / len);
            return b.IntersectRay(ray, out float d) && d <= len + 0.01f ? ray.GetPoint(Mathf.Max(0f, d - 0.05f)) : to;
        }

        /// <summary>IM1: the target's drawn body (unit view, or the static view of a building / rock / prop).</summary>
        Transform BodyOf(ZObject t)
        {
            if (t == null) return null;
            var v = ViewOf(t.Id); if (v) return v.transform;
            return game.StaticViews.TryGetValue(t.Id, out var go) && go && go.activeInHierarchy ? go.transform : null;
        }

        /// <summary>
        /// target's drawn meshes (HitSurface); the footprint box (bug #13) only when the body has nothing to test.</summary>
        /// <summary>DV118: the height of the playable shuttle while it flies (zero for every other target).</summary>
        static Vector3 AirLift(ZObject t) { var sv = t != null && t.ShuttleAirborne ? OzPlayableShuttle.Of(t) : null; return sv ? Vector3.up * sv.ShownHeight : Vector3.zero; }   // DV118: every drawn shuttle

        Vector3 OnSurface(ZObject t, Vector3 from, Vector3 aim)
        {
            var b = BodyOf(t);
            if (b && HitSurface.OnBody(b, from, aim, out var p, out _)) return p;
            return SurfaceHit(t, from, aim);
        }

        static float Strength(int radius) => 0.2f + 0.02f * radius;          // plan/04: 40 px -> 1.0, 80 px -> 1.8

        void LateUpdate()
        {
            if (game == null || game.World == null) return;
            var fx = FxManager.I; var au = CombatAudio.I;
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i]; Handled++;
                var a = game.World.GetAny(e.objId); var b = game.World.GetAny(e.otherId);   // deleted earlier this frame still counts
                if (OpenZElectroFx.Wants(e.type)) OpenZElectroFx.Handle(e, a, b);            // electrobot_v1: tesla bolt, stun FX, zap deaths, deploy strike, barks
                switch (e.type)
                {
                    case ZEventType.Hitscan: Hitscan(e, a, b, fx, au); break;
                    case ZEventType.MissileFired: MissileFired(e, a, b, fx, au); break;
                    case ZEventType.MissileExploded:
                    {
                        var at = OzShuttleAim.AirAim(b, PxGround(e.x, e.y), 0f);                                          // DV118: a blast at the flying shuttle bursts up at its hull
                        if (b != null && b.IsFortDefender) at = FortDefenderView.RoofPoint(at);             // fort_defender_v1 DV41: on the roof where he stands
                        if (b != null && !b.Destroyed && BodyBounds(b, out var bb) && bb.Contains(new Vector3(at.x, bb.center.y, at.z)))
                        {   // bug #13: a shell that lands inside a building / vehicle footprint bursts on the wall facing the shooter
                            var src = a != null ? (ViewOf(a.Id) ? ViewOf(a.Id).transform.position : game.CenterWorld(a)) + Vector3.up * 1.2f : at + Vector3.up * 8f;
                            at = OnSurface(b, src, at + Vector3.up * 1f);                             // IM1: on the drawn wall
                        }
                        OzShuttleAim.NoteBlast(e, a, b, at);                                            // DV118: the gallery's counts, the last hit on the ship
                        LegacyMatch.MissileBlast(e, a, b, at, fx, au);                                 // legacy_match: size + sound per source (LEGACY_MATCH.md 1)
                        NoteBlast(at, e.radius);                                                      // deaths_mech_v1: ragdolls fly from this blast's centre
                        OzDeathTrace.Blast(e, at);
                        HitReactions.OnBlast(game, e, a, at);                                         // deaths_mech_v1: survivors are knocked down / stagger, shrapnel
                        HeavyChips.OnBlast(game, e, a, b, at);
                        break;
                    }
                    case ZEventType.Death: Death(e, a, fx, au); break;
                    case ZEventType.RunOverHit: RunOverView.Hit(game, e, a, b); break;
                    case ZEventType.Sniped: Sniped(e, a, b, fx, au); break;
                    case ZEventType.DriverHit: DriverHit(e, a, b, fx, au); break;
                    case ZEventType.Entered:
                    {
                        var rv = ViewOf(e.objId); var p = PosOf(b, 0);
                        if (rv) rv.Hide();
                        if (a != null) foreach (var m in a.Minions) if (m.KillAt >= 0) ViewOf(m.Id)?.Hide();   // APC takes the squad
                        if (au) { if (e.flag && b != null) au.Play("enter", p, 0.65f, 0, b.Id, OzSoundV3.BankEnter); else StartCoroutine(EnterSounds(b, p)); }   // apc_v1 DV15: passengers board an own APC (no capture)
                        break;
                    }
                    case ZEventType.OwnerChanged: { var v = ViewOf(e.objId); if (v && a != null) v.SetOwner(CaptureHold.Shown(a)); rules?.Handle(e); break; }   // boarding_nb_v1 NB17
                    case ZEventType.Created: if (a != null) game.SpawnUnitView(a); break;
                    case ZEventType.Ejected:
                        if (au && a != null && a.Kind == ZKind.Vehicle && !a.IsShuttle && BoardingView.I && BoardingView.I.sounds) BoardingView.Latch(PosOf(a, 0), e.objId);
                        if (au) au.Play("exit", PosOf(a, 0), 0.8f, 0, e.objId, "exit"); break;
                    case ZEventType.GrenadesPicked:
                    {
                        if (GrenadeCrateV2.Picked(game, e, a, au)) break;
                        game.HideStatic(e.otherId);
                        if (au) au.Play("pickup", PosOf(a, 0), 0.8f, 0, e.objId, OzSoundV3.BankPickup);   // sound_v3: sd_crate_pickup + 2 takes (was the grenade landing clip)
                        break;
                    }
                    case ZEventType.Revived:
                        game.ReviveStatic(e.objId); rules?.Handle(e);
                        if (a != null && a.IsBridge && FxManager.I) FxManager.I.ClearRubble(game.CenterWorld(a), 8f);   // no rubble on the new deck
                        break;
                    default: rules?.Handle(e); break;                                   // flags, production, repair, crane, fort, game over
                }
            }
            ev.Clear();
            StepFireClocks(fx, au);
        }

        void Hitscan(ZEvent e, ZObject a, ZObject t, FxManager fx, CombatAudio au)
        {
            if (a == null || t == null) return;
            // parity P5: a roll only arms the attacker's Zod client fire clock; StepFireClocks draws the shots that fall due through
            // DrawShot (UnitAim hold / replay + LegacyMatch.Shot as before). A shot UnitAim held and now replays is drawn as is.
            if (!UnitAim.Replaying && ArmFireClock(e, a, t, fx, au)) return;
            DrawShot(e, a, t, fx, au);
        }

        /// <summary>parity P5: records a damage roll on the attacker's Zod client fire clock. False = draw this roll now (APC crew:
        /// one bullet per roll, vapc.cpp:134-152; types without a Zod fire sprite clock).</summary>
        bool ArmFireClock(ZEvent e, ZObject a, ZObject t, FxManager fx, CombatAudio au)
        {
            if (a.Kind == ZKind.Vehicle && a.Type == "apc") return false;
            var kind = ZodClientFire.KindOf(a.Type);
            if (kind == ZodClientFire.Kind.PerRoll) return false;
            double now = game.World.Time;
            if (!clocks.TryGetValue(a.Id, out var c)) clocks[a.Id] = c = new FireClock { clock = new ZodClientFire(kind, a.Id * 7919 + 17) };
            if (now > c.armedUntil || c.last.otherId != t.Id) { c.clock.Reset(now); c.killDrawn = false; }   // a new attack: the fire sprite starts over (zrobot.cpp:96,161)
            c.armedUntil = now + a.AttackSpeed + 0.15; c.last = e;
            if (WeaponsV2.KillRoll(e, t) && !c.killDrawn) { c.killDrawn = true; DrawShot(e, a, t, fx, au); }   // the killing roll always shows (its death plays this frame); weapons_v2 review: so does the driver snipe (the sim disengages every attacker that tick)
            return true;
        }

        /// <summary>parity P5: steps every attacking unit's Zod client fire clock to the sim time and draws the shots that fell due
        /// (at most one per unit and frame, like the Zod client).</summary>
        void StepFireClocks(FxManager fx, CombatAudio au)
        {
            if (clocks.Count == 0) return;
            double now = game.World.Time;
            expired.Clear(); due.Clear();
            foreach (var kv in clocks)
            {
                var c = kv.Value;
                if (now > c.armedUntil) { expired.Add(kv.Key); continue; }                             // stopped attacking
                if (c.clock.Advance(now) == 0) continue;
                var a = game.World.Get(kv.Key); var t = game.World.Get(c.last.otherId);
                if (a == null || t == null || t.Destroyed) { expired.Add(kv.Key); continue; }
                due.Add(c.last);
            }
            foreach (var id in expired) clocks.Remove(id);
            foreach (var e in due) DrawShot(e, game.World.Get(e.objId), game.World.Get(e.otherId), fx, au);
            due.Clear();
        }

        /// <summary>One visible hitscan shot: UnitAim may hold it until the gun is on target (then CombatView.Replay), else muzzle,
        /// </summary>
        void DrawShot(ZEvent e, ZObject a, ZObject t, FxManager fx, CombatAudio au)
        {
            if (a == null || t == null) return;
            var av = ViewOf(e.objId);
            if (av && av.HoldShotWhileDown(e, t)) return; if (BoardingView.HoldShotWhileExiting(e, a, t)) return;   /* K441: still walking out of a carrier: drawn when its walk is over (View/BoardingView.ExitWalk.cs) */                                             // deaths_mech_v1: knocked down: drawn when it is up
            if (UnitAim.HoldShot(av, e, t)) return;                                                   // combatview: turn onto the target first
            if (WeaponsV2.SkipShot(e, a, t, av)) return;                                               // weapons_v2: the sniper aims (visible shots only; Zod's rolls go on)
            DrawnShots++;
            if (av && !WeaponsV2.OwnsRecoil(a)) av.OnFire();                                           // weapons_v2: the runner kicks when the round really leaves
            Vector3 from = av ? av.MuzzlePos() : PosOf(a), to = PosOf(t);
            if (a.Kind == ZKind.Vehicle && a.Type == "apc" && !BoardingView.TryCrewMuzzle(a.Id, out _) && ApcSideHatches.ShotFrom(av, to, out var hatchMuzzle)) from = hatchMuzzle;
            string type = a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type;   // apc_v1 DV15
            Vector3 chest = default; bool crewAim = (CrewAim.SmallArms(type) || e.flag2) && CrewAim.Point(t, out chest);
            if (crewAim) to = CrewAim.Impact(from, chest, e.flag);                                    // on the chest from any side (no hull test), misses fly past him
            else if (!e.flag && OzShuttleAim.High(t)) to = OzShuttleAim.SkyMiss(from, t);           // DV118: a miss at the flying shuttle flies past it into the sky
            else if (!e.flag) to = HitSurface.MissGround(from, to + new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(-0.4f, 0.8f), Random.Range(-1.6f, 1.6f)));   // miss: IM1 on the ground where the line lands
            else if (e.flag2) to = SeatPos(t);                                                     // driver hit: the round goes into the hatch / seat
            else to = OnSurface(t, from, OzShuttleAim.HitAim(t, to) + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.3f, 0.5f), Random.Range(-0.5f, 0.5f)));   // bug #13 / IM1: on the real surface
            to = UnitView.HeadshotShotEnd(e, a, t, ViewOf(t.Id), to);
            OzShuttleAim.NoteShot(e, a, t, from, to);                                                // DV118: the gallery's counts, the last hit on the ship                                   // DV116: a sniper's headshot ends at the head (UnitView.Headshot.cs)
            if (WeaponsV2.Shot(this, e, a, t, type, av, from, to)) return;                             // weapons_v2: bolt / MG burst / laser bolts / crack from the drawn muzzle
            switch (type)
            {
                case "pyro":
                    if (!OpenZPyroFire.Shot(e, a, t, av, from, to)) { if (fx) fx.Flame(from, to); au?.Play("flame", from, 0.75f, 0.9f, e.objId, "flame_loop1"); }
                    break;
                case "laser": if (fx) fx.Laser(from, to); au?.Play("laser", from, 0.8f, 0.3f, e.objId, AudioDirector.BankLaserFire); au?.Play("laser_hit", to, 0.5f, 0.3f, e.objId, AudioDirector.BankLaser); break;   // fire zip at the gun, zap on the target
                default:
                    LegacyMatch.Shot(e, t, type, from, to, fx, au);
                    if (type == "gatling" && a.Kind == ZKind.Cannon) GatlingBrass.Eject(av, from, to, e.objId);
                    // 30 % of hits, at most one per target every 0.7 s (CombatAudio throttle), quiet under the gunfire
                    // hitsparks_v1: OpenZHitSparks plays the varied ricochets now; this ping stays only without it
                    if (au && !OpenZHitSparks.Active && e.flag && !e.flag2 && t.Kind != ZKind.Robot && t.Kind != ZKind.MapItem && Random.value < 0.3f)
                        StartCoroutine(MetalPing(au, to, t.Id, Vector3.Distance(from, to) / 37.5f));
                    break;
            }
        }

        System.Collections.IEnumerator MetalPing(CombatAudio au, Vector3 at, int target, float delay)
        {
            if (delay > 0.02f) yield return new WaitForSeconds(Mathf.Min(delay, 1.5f));
            if (au) au.PlayPitched("metal_ping", at, 0.32f, 0.7f, target, Random.Range(0.92f, 1.12f), "v3-armor-ricochet-a", "impact-1", "impact-2");
        }

        // ------------------------------------------------------------------ drivers (Zod: zobject.cpp:2187-2199, zserver.cpp:1808-1874)
        /// <summary>Where the driver sits: tanks expose the commander in the turret hatch while the lid is open, jeeps and
        /// cannons seat him behind the gun.</summary>
        Vector3 SeatPos(ZObject v)
        {
            if (BoardingView.TryCrewChest(v.Id, out var chest)) return chest;
            var view = ViewOf(v.Id);
            var p = view ? view.transform.position : game.CenterWorld(v);
            return p + Vector3.up * (v.HasLid ? 2.1f : v.Kind == ZKind.Cannon ? 1.5f : 1.35f);
        }

        /// <summary>hitsparks_v1: the travel direction of the round that hits a seat (shooter's muzzle -> seat), for the spark spray.</summary>
        Vector3 ShooterDir(ZObject by, Vector3 p)
        {
            var bv = by != null ? ViewOf(by.Id) : null;
            var from = bv ? bv.MuzzlePos() : by != null ? game.CenterWorld(by) + Vector3.up : p + Vector3.up;
            return p - from;
        }

        /// <summary>Non-lethal driver hit (Zod flashes the driver sprite white, zvehicle.cpp:298-341): a spark and a ping at
        /// the seat so the ~20-40 driver hits of a match read, the player's own driver grunts (Alan, throttled).</summary>
        void DriverHit(ZEvent e, ZObject v, ZObject by, FxManager fx, CombatAudio au)
        {
            if (v == null) return;
            var p = SeatPos(v);
            if (!OpenZHitSparks.Driver(p, ShooterDir(by, p), false) && fx) { fx.MuzzleFlash(p, false); fx.Pop(p, 0.35f); }   // hitsparks_v1: spark burst at the seat
            if (au)
            {
                au.Play("driverhit", p, 0.8f, 0.25f, e.objId, OzSoundV3.BankDriverHit);   // sound_v3: 5 takes
                if (v.Owner == game.player) au.Voice("driver_hurt", new[] { "alan_ugh_1", "alan_ugh_2", "alan_aargh_1", "alan_aargh_2" }, 0.8f, 6f, true);
            }
        }

        /// <summary>Driver killed (SNIPE_OBJECT): the vehicle is already neutral in the sim (OwnerChanged repaints it grey,
        /// the sim stopped it and cleared its orders). Burst at the seat, a death cry, Alan's line, and Zod's flying body
        /// (ERobotTurrent, zplayer_events.cpp:1117-1134) in the colour of the team that lost it.</summary>
        void Sniped(ZEvent e, ZObject v, ZObject by, FxManager fx, CombatAudio au)
        {
            if (v == null) return;
            var lost = (Team)(int)e.value;                                   // sim patch 02; Neutral (0) on an unpatched sim
            var p = SeatPos(v);
            if (!OpenZHitSparks.Driver(p, ShooterDir(by, p), true) && fx) { fx.MuzzleFlash(p, true); fx.Pop(p, 1.0f); }   // hitsparks_v1: big spark burst at the seat
            if (au)
            {
                au.Play("sniped", p, 1f, 0, e.objId, OzSoundV3.BankDriverHit);   // sound_v3: 5 takes
                if (!AudioDirector.DriverDeath(p, e.objId)) au.Play("death", p, 0.8f, 0, e.objId, "death");   // death_voices_v1: a bullet death (cry / body)
                if (lost == game.player) au.Voice("driver_down", new[] { "alan_nooo_1", "alan_nooo_2", "alan_oh_my_god_1", "alan_oh_my_god_2" }, 1f, 4f, true);
                else if (by != null && by.Owner == game.player) au.Voice("driver_sniped", new[] { "alan_gotcha_1", "alan_gotcha_2", "alan_good_hit_1", "alan_good_hit_2", "alan_nice_one_1", "alan_nice_one_2" }, 1f, 4f, true);
            }
            if (!game.GetComponent("BoardingView")) StartCoroutine(DriverBody(v, lost, by, p));   // BoardingView flings its seated crew itself
            PlacementPop.Sniped(game, v, p);
        }

        /// <summary>The dead driver thrown out of the hatch/seat: a real ragdoll (capped), else Zod's flip arc; pops out after 12 s.</summary>
        System.Collections.IEnumerator DriverBody(ZObject v, Team lost, ZObject by, Vector3 p)
        {
            var prefab = game.Prefab(v.DriverType == "laser" && !game.Prefab("Z_bs01_laser") ? "Z_robot" : "Z_bs01_" + v.DriverType);   // robots_v3: the v3 laser when present
            if (!prefab) prefab = game.Prefab("Z_robot");
            if (!prefab) yield break;
            var go = Instantiate(prefab, p, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            UnitLookMotionGate.KeepSkinnedCrisp(go);
            go.name = $"sniped_driver_{v.Id}";
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = lost;
            OzGlowFade.Begin(go);                                                                         // a thrown driver's glows go out
            var kv = by != null ? ViewOf(by.Id) : null;
            var push = p - (kv ? kv.transform.position : by != null ? game.CenterWorld(by) : p + Random.insideUnitSphere); push.y = 0;
            var s0 = go.transform.localScale;
            if (!(RobotRagdoll.CanStart() && RobotRagdoll.Launch(go, push, 1.6f)))
            {   // flip arc: 1.2 s, 2-4 m away, tumbling, lands on the ground
                var anim = go.GetComponentInChildren<Animator>(); if (anim) anim.enabled = false;
                if (push.sqrMagnitude < 0.01f) push = Vector3.forward; push.Normalize();
                var to = p + push * Random.Range(2f, 4f); to.y = game.GroundY(to.x, to.z);
                var axis = Vector3.Cross(Vector3.up, push);
                for (float t = 0; t < 1.2f; t += Time.deltaTime)
                {
                    if (!go) yield break;
                    float k = t / 1.2f; var q = Vector3.Lerp(p, to, k); q.y += Mathf.Sin(k * Mathf.PI) * 2.2f;
                    go.transform.position = q; go.transform.rotation = Quaternion.AngleAxis(k * 540f, axis) * Quaternion.Euler(0, go.transform.eulerAngles.y, 0);
                    yield return null;
                }
                if (go) { go.transform.position = to; go.transform.rotation = Quaternion.AngleAxis(90f, axis) * Quaternion.Euler(0, go.transform.eulerAngles.y, 0); }
                FxManager.I?.Dust(to, 0.4f);
            }
            yield return new WaitForSeconds(12f);
            for (float t = 0; t < 0.35f && go; t += Time.deltaTime) { go.transform.localScale = s0 * (1f - t / 0.35f); yield return null; }
            if (go) Destroy(go);
        }

        void MissileFired(ZEvent e, ZObject a, ZObject t, FxManager fx, CombatAudio au)
        {
            if (a == null) return;
            var av = ViewOf(e.objId);
            if (av) av.ShotWhileDown();                                                               // deaths_mech_v1: a rocket / grenade must leave now: up fast
            if (WeaponsV2.HoldMissile(this, av, e, a, t)) return;                                      // weapons_v2: bazooka raised + settled first (lands on the sim's blast)
            if (UnitAim.HoldShot(av, e, t)) return;                                                   // combatview: turret / body onto the target first
            string type = a.IsFort ? "howitzer" : a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type;   // apc_v1 DV15
            Vector3 from = av ? av.MuzzlePos() : PosOf(a), to = OzShuttleAim.AirAim(t, PxGround(e.x, e.y) + Vector3.up * 0.25f, e.value);   // DV118: up to the flying shuttle (the hull's middle, where it will be)
            if (a.Kind == ZKind.Vehicle && a.Type == "apc" && !BoardingView.TryCrewMuzzle(a.Id, out _) && ApcSideHatches.ShotFrom(av, to, out var hatchMuzzle)) from = hatchMuzzle;   // the passengers' rockets and grenades leave a side hatch too; DV87: not while the lone driver is up in the roof hatch
            if (t != null && !e.flag && BodyBounds(t, out var tb) && tb.Contains(new Vector3(to.x, tb.center.y, to.z)))
            {
                if (LegacyMatch.Eased(type))
                {
                    // IM1 OnSurface from the shooter's body + 1.2 m), not on the footprint box 0.3-1.1 m away from it
                    var src = (av ? av.transform.position : game.CenterWorld(a)) + Vector3.up * 1.2f;
                    to = OnSurface(t, src, OzShuttleAim.AirAim(t, PxGround(e.x, e.y) + Vector3.up * 1f, 0f));
                }
                else to = OzShuttleAim.Air(t) ? OnSurface(t, from, to + Vector3.up * 0.75f) : SurfaceHit(t, from, to + Vector3.up * 0.75f);   // DV118: on the flying ship's drawn hull                              // bug #13: the shell ends on the wall it hits
            }
            if (t != null && t.IsFortDefender) to = FortDefenderView.RoofPoint(to);                  // fort_defender_v1 DV41: he stands on the fort roof
            float delay = e.value;
            bool grenade = e.flag;
            if (!(a.IsFort && FortGunView.Handles(a.Id))) OzShuttleAim.NoteFired(e, a, t, from, to);   // DV118: the gallery's counts (the fort's own shell: FortGunView)
            if (a.IsFort && FortGunView.Handles(a.Id)) return;                                       // DV7: the fort's turret draws its own shell + boom
            if (grenade)
            {
                av?.OnGrenadeThrow();
                if (!GrenadeFx.Throw(av, from + Vector3.up * 0.4f, to, delay))                           // combatview: a real grenade (held, thrown, fuse spark, trail)
                {
                    LegacyMatch.GrenadeFlight(from + Vector3.up * 0.4f, to, delay, fx);
                }
                LegacyMatch.GrenadeSound(from, e.objId, au);
                return;
            }
            if (type == "tough" && WeaponsV2.LaunchTough(av, a, e, to, delay)) return;                 // weapons_v2: rocket + back-blast from the drawn tube's sockets, recoil
            bool rocket = type == "tough" || type == "light" || type == "missile_launcher" || type == "missile_cannon";
            if (av) av.OnFire();
            if (type == "tough") ToughWarhead.Fired(av, a, false);
            if (type == "tough" && fx)
            {   // bazooka back-blast: flash, smoke and dust out of the rear of the tube
                var dir = (to - from); dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                var back = BS01Stance.BackBlast(av, from - dir * 1.3f);                                // robot_models_v2: the v2 bazooka's BazookaBackBlast socket
                LegacyMatch.BackBlast(back, dir, fx);
            }
            if (type == "medium") from = MechRockets.LeftMuzzle(game, av, a.Id, from);
            if (type == "missile_cannon") from = MissileCannonSalvo.LeftMuzzle(game, av, a.Id, from);  // MC1: rocket 1 from the left pod, side kick
            OpenZFxPlayer.NextSimEnd = e.end;
            try { LegacyMatch.Launch(a, type, from, to, delay, fx); }
            finally { OpenZFxPlayer.NextSimEnd = -1; }
            if (type != "missile_cannon") LegacyMatch.LaunchSound(type, from, e.objId, au);
            if (type == "tough") LegacyMatch.RocketFlight(from, to, delay, e.objId, au);
            OzSoundV3.Flight(type, from, to, delay, e.objId, au);                                      // sound_v3: mech / missile-cannon rockets + tank shells in flight               // SOUND-bundle: the bazooka rocket's roar flies with it (APC crew / fallback path)
            LegacyMatch.GunMuzzleBlast(type, from, to, delay);
            if (type == "howitzer") LegacyMatch.HowitzerWhistle(to, delay, e.objId, au);
            if (type == "missile_launcher") MlrsSalvo.Follow(game, a, av, from, to, delay, e.radius);
            if (type == "medium") MechRockets.Follow(game, a, av, to, delay);
            if (type == "missile_cannon") MissileCannonSalvo.Follow(game, a, av, from, to, delay);    // MC1: launch sound, the twin side by side, 3 small blasts
        }

        /// <summary>
        /// owner flips, engine start (vehicles), Rachel "Vehicle captured" when the player took it.</summary>
        System.Collections.IEnumerator EnterSounds(ZObject v, Vector3 p)
        {
            var au = CombatAudio.I; if (!au || v == null) yield break;
            au.Play("capture_start", p, 0.7f, 0, v.Id, OzSoundV3.BankCaptureStart);   // sound_v3: 3 takes each
            au.Play("enter", p, 0.65f, 0, v.Id, OzSoundV3.BankEnter);
            while (CaptureHold.IsHeld(v.Id)) yield return null;                                        // boarding_nb_v1 NB17: the rest once the robot sits inside
            if (v.Destroyed || game.World.Get(v.Id) != v || v.Owner == Team.Neutral) yield break;     // (lost again during the climb-in: no capture cue)
            p = PosOf(v, 0);
            if (v.Kind == ZKind.Cannon) au.Play("gun_enter", p, 0.72f, 0, v.Id, "mech-servo-1", "mech-servo-2");
            au.Play("capture", p, 0.9f, 0, v.Id, OzSoundV3.BankCapture);
            if (v.Kind == ZKind.Vehicle) { yield return new WaitForSeconds(0.15f); au.Play("engine_start", p, 0.5f, 0, v.Id, OzSoundV3.BankEngineStart); }
            yield return new WaitForSeconds(0.1f);
            if (v.Owner == game.player && v.Kind == ZKind.Vehicle) au.Voice("vehicle_captured", new[] { "rachel_vehicle_captured_1", "rachel_vehicle_captured_2" }, 1f, 3f, false);
        }

        // The sim emits MissileExploded, then the Death events of its victims, in the same event list; the blasts drawn this
        // frame are kept so a splash death is thrown from the real blast centre (was: 60 % of the way from the killer)
        readonly List<(int frame, Vector3 at, float r)> blasts = new List<(int, Vector3, float)>();
        void NoteBlast(Vector3 at, int radiusPx)
        {
            if (blasts.Count > 0 && blasts[0].frame != Time.frameCount) blasts.Clear();
            blasts.Add((Time.frameCount, at, radiusPx / 8f));
        }
        bool BlastNear(Vector3 p, out Vector3 at, out float proximity)
        {
            at = p; proximity = 1f; float best = float.MaxValue;
            foreach (var b in blasts)
            {
                if (b.frame != Time.frameCount) continue;
                var d = p - b.at; d.y = 0f; float m = d.magnitude;
                if (m > b.r + 1.5f || m >= best) continue;
                best = m; at = b.at; proximity = BlastPower(m, b.r);
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// the throw power of one robot = the blast's size (sqrt(splash radius / 5 m), 0.75-1.45: a grenade less, an MLRS / fort shell
        /// more) x its falloff at that robot (1 at the centre .. 0.22 at the splash edge, (1 - d/r)^1.3), so the robot at the
        /// impact flies hardest and highest and one at the edge only stumbles over (RobotRagdoll lift follows the power).</summary>
        public static float BlastPower(float distM, float radiusM)
        {
            float size = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.5f, radiusM) / 5f), 0.75f, 1.45f);
            float f = Mathf.Pow(Mathf.Clamp01(1f - distM / Mathf.Max(0.5f, radiusM + 0.5f)), 1.3f);
            return size * Mathf.Lerp(0.22f, 1.25f, f);
        }

        /// <summary>combat_nb_v1 NB3: the robot flies (ragdoll / pop / melt, dust and body sound) `wait` s after its sim death, when the
        /// blast front reaches it (BlastWave); its view held its pose until then (UnitView.HoldForDeath).</summary>
        System.Collections.IEnumerator RobotDeathLater(UnitView v, ZEvent e, ZObject o, Vector3 from, float prox, float wait, CombatAudio au)
        {
            // H0 round 3 (GP3): a robot killed by a round is thrown on the frame the DRAWN round arrives: the drawing code notes its killing
            // round in this frame's LateUpdate (WeaponsV2Runner / LegacyMatch.Shot / OpenZPyroFire), so after one frame the throw waits for
            // exactly that time (the impact spark uses the same time); blasts and unnoted rounds keep BlastWave's estimate
            float due = Time.time + wait; int f0 = Time.frameCount;
            yield return null;
            if (!e.flag2 && BlastWave.DrawnArrival(e.otherId, o.Id, f0, out float at)) due = at;
            while (Time.time < due) yield return null;
            if (!v) yield break;
            var p = v.transform.position;
            OzDeathTrace.DeathEvent(e, o, from);
            v.Die(e.flag, e.flag2, from, e.otherId, prox);
            BlastWave.NoteThrown(o.Id);                                                                 // H0 round 3: the GP3 proof compares it with the round's arrival
            if (!v.Popped) LegacyMatch.RobotDeath(p, from, e.flag2, au);
        }

        void Death(ZEvent e, ZObject o, FxManager fx, CombatAudio au)
        {
            if (o == null) return;
            var v = ViewOf(o.Id);
            var p = v ? v.transform.position : game.CenterWorld(o);
            switch (o.Kind)
            {
                case ZKind.Robot:
                    if (o.Squashed && v) { v.Squash(game.World.GetAny(e.otherId)); break; }
                    if (o.Eaten && v) { v.EatenByCrocodile(); break; }                                        // level 16: taken by a crocodile (UnitView.Eaten.cs, CrocodileView.cs)
                    var kv = ViewOf(e.otherId); var from = kv ? kv.transform.position : p + Random.insideUnitSphere;   // thrown away from the killer
                    float prox = 1f;
                    if (e.flag2 && BlastNear(p, out var bc, out var bk)) { from = bc; prox = bk; }                 // deaths_mech_v1: this frame's blast centre
                    else if (e.flag2 && kv) from = Vector3.Lerp(from, p, 0.6f) + Random.insideUnitSphere * 0.6f;   // an older splash hit: near the impact
                    float wave = BlastWave.RobotDelay(game, e, o, out var waveFrom, out bool ripple);             // combat_nb_v1 NB3: when the front reaches it
                    if (ripple) { from = waveFrom; var dr = p - waveFrom; dr.y = 0f; prox = BlastPower(dr.magnitude, 5f); }
                    {
                        var raw = from; bool has = HitReactions.ShooterPos(game, game.World.GetAny(e.otherId), out var sp);
                        if (has) from = HitReactions.SafeFrom(p, from, sp);
                        HitReactions.NoteImpulse(o, e.flag2 ? "death_splash" : e.flag ? "death_fire" : "death_shot", p, raw, from, has ? sp : raw);
                    }
                    if (v && wave > 0.015f) { v.HoldForDeath(wave + 0.5f); StartCoroutine(RobotDeathLater(v, e, o, from, prox, wave, au)); break; }
                    OzDeathTrace.DeathEvent(e, o, from);
                    if (v) v.Die(e.flag, e.flag2, from, e.otherId, prox);
                    if (!(v && v.Popped)) LegacyMatch.RobotDeath(p, from, e.flag2, au);                             // a pop plays its own crunch + smoke
                    break;
                case ZKind.Vehicle: case ZKind.Cannon:
                    if (VehicleDeathSync.Hold(this, game, e, o, v, au)) break;                               // version 1.1 item 202: it explodes when its killing round / MLRS rocket is drawn hitting it
                    if (OzShuttleBreakup.TakesDeath(o, v, p, fx, au)) break;                                 // DV118: the shuttle falls apart into its pieces (View/OzShuttleBreakup.cs)
                    if (v) v.Die(false, true, p);
                    ScorchDecals.Add(p, 2.2f, ScorchKind.Wreck);
                    LegacyMatch.UnitDeath(o, p, fx, au);                                                 // legacy_match: size per chassis + wreck fire
                    break;
                case ZKind.Building:
                    if (o.IsBridge)
                    {
                        int cleared = ScorchDecals.ClearDeck(ZGame.PxToWorld(o.LocX, o.LocY), ZGame.PxToWorld(o.LocX + o.WidthPix, o.LocY + o.HeightPix));
                        if (cleared > 0) Debug.Log($"[OpenZ] bridge #{o.Id} died: {cleared} scorch mark(s) on its deck faded");
                    }
                    ScorchDecals.Add(p, 4.5f, ScorchKind.Wreck);
                    LegacyMatch.BuildingDeath(o, p, fx);
                    if (rules) rules.BuildingDeath(o, p); else game.RuinStatic(o.Id);
                    break;
                default:
                    if (o.Type == "flag") break;
                    if (o.Type == "rock" && game.StaticViews.TryGetValue(o.Id, out var rgo) && rgo && rgo.GetComponent<RockPillar>() is RockPillar pil)
                    {
                        var blast = ViewOf(e.otherId) ? ViewOf(e.otherId).transform.position : p;
                        pil.Break(blast);                                                            // plays dst_rock_crash_large itself
                        break;
                    }
                    if (o.Type == "hut" && L16Hut.SimDied(game, o, ViewOf(e.otherId) ? ViewOf(e.otherId).transform.position : p)) break;   // level 16: the thatched hut breaks itself (straw and wood, its fallen state, the wood sound)
                    if (o.Type == "hut" && game.StaticViews.TryGetValue(o.Id, out var hgo) && hgo && hgo.GetComponentInChildren<BaseProp>(true) is BaseProp bprop)
                    {   // base_props_v1: the military base set breaks itself (fuel fireball / glass / sparks, chunks, ruin, fire, sounds)
                        bprop.Break(ViewOf(e.otherId) ? ViewOf(e.otherId).transform.position : p, o.Id);
                        break;
                    }
                    if (o.Type == ZWorld.DetailPropType) { if (!OzTreeCrush.HoldsDeath(game, e, o)) DetailPropHub.SimDied(o, ViewOf(e.otherId) ? ViewOf(e.otherId).transform.position : p); break; }
                    if (o.Type == ZWorld.WreckType && HarbourDetails.SimWreckDied(o, ViewOf(e.otherId) ? ViewOf(e.otherId).transform.position : p)) break;   // l17_details_v1 DV39: the wreck breaks and sinks (no stone burst)
                    if (PropToss.TryToss(game, o, p)) break;                                             // deaths_mech_v1: barrels fly to the sim's debris point
                    if (o.Type == "grenades")
                    {
                        int cr = GrenadeCrateV2.OnDeath(game, o, au);
                        if (cr == 1) break;
                        if (cr == 2) { if (fx) LegacyMatch.CrateDeath(o, p, fx, au); break; }
                    }
                    game.HideStatic(o.Id);
                    if (fx)
                    {
                        if (o.Type == "grenades") LegacyMatch.CrateDeath(o, p, fx, au);
                        else
                        {
                            var rp = o.Type == "rock" ? PxGround(o.CenterX, o.LocY + 40) : p;                         // the rock model sits on its bottom tile
                            LegacyMatch.PropDeath(o, rp + Vector3.up * 0.6f, fx);
                            fx.Breakup(rp + Vector3.up * 0.5f, "stone", 3f, 18, 1.8f);
                            fx.DemolitionDust(rp, 2.2f, 2.2f, 0.45f);
                        }
                    }
                    if (o.Type == "rock" && AudioDirector.Has("dst_rock_crash_large")) AudioDirector.Fire("dst_rock_crash_large", p, o.Id);
                    else au?.Play("rock", p, 0.8f, 0.05f, -1, "v3-rock-break-a", "v3-rock-break-b");
                    break;
            }
        }
    }
}

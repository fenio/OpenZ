using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Z parity against constants copied from the Zod source (refs/zod_engine; judge audit
    /// measured on a REAL-time clock through ZGame.Advance (the game's own sim clock) at the scenes' serialized gameSpeed,
    /// so a leftover 2x/4x, a per-frame/per-tick unit mix-up or a wrong px-to-metre scale fails here even when the
    /// sim-time rows above pass. Also checks the camera framing, the Zod client shot cadence and the projectile flight.
    /// </summary>
    public static partial class OpenZParity
    {
        const double ZodPxPerMetre = 8.0;                    // 1 tile = 16 px = 2 m (OpenZ world scale)
        const double ZodBulletPxPerS = 300.0;                // ebullet.cpp:7, eflame.cpp:10, elaser.cpp:9
        const double ZodTilesAcross = (800 - 100) / 16.0;    // zplayer.cpp:156 800x600, constants.h:17 HUD_WIDTH 100 -> 43.75 tiles

        /// <summary>Zod default_settings.txt after CensorSettings (zsettings.cpp:332-390) and the x10000 int truncation
        /// (zobject.cpp:183,189). move px/s, range px, interval s, damage and health int, missile px/s, splash px.</summary>
        static readonly (string type, string cat, int move, int radius, double interval, int dmg, double chance, double snipe, int hp, int missile, int splash)[] ZodUnits =
        {
            ("grunt", "robot", 14, 120, 0.500, 11, 0.70, 0.30, 1081, 0, 0),
            ("psycho", "robot", 12, 120, 0.100, 26, 0.65, 0.30, 1756, 0, 0),
            ("sniper", "robot", 14, 144, 0.400, 70, 0.80, 0.80, 1756, 0, 0),
            ("tough", "robot", 12, 120, 1.442, 1666, 0, 0, 3378, 150, 40),
            ("pyro", "robot", 12, 120, 0.100, 104, 0.70, 0, 2702, 0, 0),
            ("laser", "robot", 14, 136, 0.400, 177, 0.70, 0.60, 2027, 0, 0),
            ("jeep", "vehicle", 17, 120, 0.100, 27, 0.65, 0.40, 1756, 0, 0),
            ("light", "vehicle", 14, 120, 1.128, 2083, 0, 0, 3378, 225, 40),
            ("medium", "vehicle", 12, 128, 2.336, 3333, 0, 0, 6756, 160, 45),
            ("heavy", "vehicle", 9, 144, 4.088, 5000, 0, 0, 8378, 135, 50),
            ("apc", "vehicle", 14, 0, 0, 0, 0, 0, 6756, 0, 0),
            ("missile_launcher", "vehicle", 6, 160, 4.454, 8378, 0, 0, 6756, 70, 80),
            ("crane", "vehicle", 14, 0, 0, 0, 0, 0, 10000, 0, 0),
            ("gatling", "cannon", 0, 120, 0.100, 39, 0.65, 0.40, 1756, 0, 0),
            ("gun", "cannon", 0, 128, 2.254, 3125, 0, 0, 3378, 225, 40),
            ("howitzer", "cannon", 0, 200, 4.860, 4166, 0, 0, 3378, 95, 40),
            ("missile_cannon", "cannon", 0, 144, 1.124, 8333, 0, 0, 3378, 128, 50),
        };

        static (string type, string cat, int move, int radius, double interval, int dmg, double chance, double snipe, int hp, int missile, int splash) Zod(string type) =>
            ZodUnits.First(u => u.type == type);

        static void ZodReferenceTests(List<Row> rows)
        {
            // ---- 1. every unit the sim creates carries Zod's numbers (literals above, not OpenZ's own parse)
            foreach (var z in ZodUnits)
            {
                var w = Legacy(1);
                var o = w.CreateObject(z.cat, z.type, 40 * 16, 40 * 16, Team.Red);
                bool armed = z.type != "apc" && z.type != "crane";                              // APC fires with its crew (vapc.cpp)
                bool ok = o.MoveSpeed == z.move && o.MaxHealth == z.hp &&
                          (!armed || (o.AttackRadius == z.radius && Math.Abs(o.AttackSpeed - z.interval) < 1e-6 && o.Damage == z.dmg &&
                                      Math.Abs(o.DamageChance - z.chance) < 1e-6 && Math.Abs(o.SnipeChance - z.snipe) < 1e-6 &&
                                      o.MissileSpeed == z.missile && o.DamageRadius == z.splash));
                rows.Add(new Row
                {
                    test = $"Zod ref {z.type}: move / range / interval / dmg / hit / snipe / HP / missile / splash",
                    expected = $"{z.move} / {z.radius} / {z.interval:F3} / {z.dmg} / {z.chance:0.##} / {z.snipe:0.##} / {z.hp} / {z.missile} / {z.splash}",
                    measured = $"{o.MoveSpeed} / {o.AttackRadius} / {o.AttackSpeed:F3} / {o.Damage} / {o.DamageChance:0.##} / {o.SnipeChance:0.##} / {o.MaxHealth} / {o.MissileSpeed} / {o.DamageRadius}",
                    pass = ok,
                    note = $"{z.move / ZodPxPerMetre:F3} m/s, range {z.radius / ZodPxPerMetre:F2} m, missile {z.missile / ZodPxPerMetre:F2} m/s, splash {z.splash / ZodPxPerMetre:F2} m"
                });
            }

            // ---- 2. the game speed the next Play starts with (scene value, and the bot demo flag as it is set right now)
            float l02 = SceneFloat("L02_Desert", "OpenZ.ZGame", "gameSpeed", 1f), l17 = SceneFloat("L17_CarPark", "OpenZ.ZGame", "gameSpeed", 1f);
            bool demo = PlayerPrefs.GetInt("oz_botmatch", 0) == 1;
            float start = ZGame.StartSpeed(l02, demo, PlayerPrefs.GetFloat("oz_demo_speed", 1f));
            float demoStart = ZGame.StartSpeed(l02, true, PlayerPrefs.GetFloat("oz_demo_speed", 1f));
            rows.Add(new Row { test = "normal play game speed (scenes, bot demo flag, demo speed pref)", expected = "1 / 1 / 1 (Zod game_speed 1.0, ztime.cpp:12)",
                               measured = $"L02 {l02} L17 {l17} / next Play {start} (demo flag {demo}) / demo {demoStart}",
                               pass = l02 == 1f && l17 == 1f && start == 1f && demoStart == 1f });

            // ---- 3. on-screen speed per REAL second through ZGame.Advance at 60 and 144 fps (Zod: px/s of ztime = real s x 1.0)
            foreach (var (type, cls, fps) in new[] { ("grunt", ZClass.Ground, 60), ("grunt", ZClass.Ground, 144), ("grunt", ZClass.Road, 60), ("grunt", ZClass.Water, 60),
                                                      ("jeep", ZClass.Road, 60), ("heavy", ZClass.Ground, 60), ("missile_launcher", ZClass.Ground, 30) })
            {
                var z = Zod(type);
                double factor = cls == ZClass.Road ? 1.689 : cls == ZClass.Water ? 0.7 : 1.0;   // constants.h:23-24
                double expected = z.move * factor / ZodPxPerMetre;
                double measured = RealClockSpeed(type, z.cat, cls, fps, start);
                rows.Add(new Row { test = $"REAL-clock {type} on {cls.ToString().ToLower()} at {fps} fps", expected = $"{expected:F3} m/s ({z.move} px/s x {factor} / 8)",
                                   measured = $"{measured:F3} m/s", pass = Math.Abs(measured - expected) <= expected * 0.02,
                                   note = "ZGame.Advance with the start game speed; metres per real second" });
            }

            // ---- 4. fire interval per REAL second (Zod: attack_speed seconds, zobject.cpp:188,2108; + up to one loop)
            foreach (var type in new[] { "grunt", "psycho", "light", "heavy" })
            {
                var z = Zod(type);
                var (interval, flight, zodFlight) = RealClockFire(type, z.cat, start, z.missile);
                bool missile = z.missile > 0;
                rows.Add(new Row { test = $"REAL-clock {type} fire interval" + (missile ? " / missile flight at 96 px" : ""),
                                   expected = $"{z.interval:F3}-{z.interval + 0.016:F3} s" + (missile ? $" / {zodFlight:F3} s" : ""),
                                   measured = $"{interval:F4} s" + (missile ? $" / {flight:F3} s" : ""),
                                   pass = interval >= z.interval - 1e-6 && interval <= z.interval + 0.016 && (!missile || Math.Abs(flight - zodFlight) <= 0.011),
                                   note = "Zod fires on the first server loop after the cooldown (10-15.6 ms loop)" });
            }

            // ---- 5. camera framing like Zod's 800x600 view (43.75 x 35.25 tiles), pitch/FOV/height from the scenes
            foreach (var scene in new[] { "L02_Desert", "L17_CarPark" })
            {
                float h = SceneFloat(scene, "OpenZ.RtsCamera", "height", -1), pitch = SceneFloat(scene, "OpenZ.RtsCamera", "pitch", 55), fov = SceneFloatAny(scene, "field of view", 32);
                double across = 2 * (h / Math.Sin(pitch * Math.PI / 180)) * Math.Tan(fov * Math.PI / 360) * 16.0 / 9.0 / 2.0;   // tiles across the screen centre, 16:9
                rows.Add(new Row { test = $"{scene} start camera framing (tiles across the centre, 16:9)", expected = $"{ZodTilesAcross:F2} +/- 6 % (Zod 800x600; 70 m)",
                                   measured = $"{across:F1} tiles at {h} m, pitch {pitch}, FOV {fov}", pass = Math.Abs(across - ZodTilesAcross) <= ZodTilesAcross * 0.06,
                                   note = $"on-screen motion {ZodTilesAcross / Math.Max(0.1, across):F2}x Zod" });
            }

            // ---- 6. visible shots on the Zod client clock (not one per damage roll), hitscan effect speed
            foreach (var (type, zodPeriod) in new[] { ("grunt", 0.998), ("sniper", 0.778), ("psycho", 0.170), ("jeep", 0.170), ("gatling", 0.170), ("pyro", 0.215), ("laser", 0.529) })
            {
                var clock = new ZodClientFire(ZodClientFire.KindOf(type), 5);
                clock.Reset(0); int shots = 0; double t = 0, first = -1, last = 0;
                for (int i = 0; i < 200000 && shots < 4000; i++) { t += 0.01; int n = clock.Advance(t); if (n > 0) { shots += n; if (first < 0) first = t; last = t; } }
                double period = (last - first) / Math.Max(1, shots - 1);
                rows.Add(new Row { test = $"{type} visible shot every (Zod client fire clock)", expected = $"{zodPeriod:F3} s", measured = $"{period:F3} s over {shots} shots",
                                   pass = Math.Abs(period - zodPeriod) <= zodPeriod * 0.03,
                                   note = $"damage roll every {Zod(type).interval:F3} s: {period / Zod(type).interval:F2} rolls per drawn shot" });
            }
            rows.Add(new Row { test = "hitscan bullet / bolt effect speed (LegacyMatch.Shot -> OpenZFxPlayer.Shot)", expected = $"{ZodBulletPxPerS / ZodPxPerMetre:F2} m/s (300 px/s) at game speed 1",
                               measured = $"{CombatView.ZodBulletMps:F2} m/s, drawn at {LegacyMatch.ShotSpeed(1f):F2} m/s (x1) / {LegacyMatch.ShotSpeed(4f):F2} m/s (x4)",
                               pass = Math.Abs(CombatView.ZodBulletMps - ZodBulletPxPerS / ZodPxPerMetre) < 0.01 && Math.Abs(LegacyMatch.ShotSpeed(1f) - 37.5f) < 0.01f && Math.Abs(LegacyMatch.ShotSpeed(4f) - 150f) < 0.05f });
            foreach (var (type, clip) in new[] { ("grunt", 0.2f), ("sniper", 0.2f), ("psycho", 0.2f), ("pyro", 0.2f), ("laser", 0.2f) })
            {
                float rate = ZodClientFire.ShootClipRate(type, clip);
                double loop = clip / rate, zodPeriod = ZodClientFire.MeanPeriod(ZodClientFire.KindOf(type));
                rows.Add(new Row { test = $"{type} standing `shooting` loop vs Zod fire cycle (P6)", expected = $"one loop per cycle, >= {zodPeriod:F3} s, rate <= 1",
                                   measured = $"rate {rate:F2}: loop {loop:F3} s", pass = rate <= 1f && loop >= zodPeriod - 1e-3 && (rate >= 1f || Math.Abs(loop - zodPeriod) < 1e-3) });
            }

            // ---- 7. projectiles: real flight = Zod flight at 1x, delay / speed otherwise; tank shells and grenades not white
            //         (the weapon per unit is legacy_match's table, LegacyMatch.MissileWeapon, the one CombatView launches with)
            foreach (var type in new[] { "tough", "light", "medium", "heavy", "missile_launcher", "gun", "howitzer", "missile_cannon" })
            {
                var z = Zod(type);
                float simDelay = (float)(z.radius / (double)z.missile);                             // at max range
                float at1 = OpenZFxPlayer.RealFlight(simDelay, 1f), at4 = OpenZFxPlayer.RealFlight(simDelay, 4f);
                var shooter = Legacy(1).CreateObject(z.cat, type, 40 * 16, 40 * 16, Team.Red);
                var spec = OpenZFxCatalog.ProjectileOf(LegacyMatch.MissileWeapon(shooter, type));
                float lum = spec.bodyColor.r * 0.2126f + spec.bodyColor.g * 0.7152f + spec.bodyColor.b * 0.0722f;
                bool rocket = spec.mesh == "FX_Rocket";
                rows.Add(new Row { test = $"{type} projectile: real flight at max range (1x / 4x), legacy look", expected = $"{simDelay:F3} s / {simDelay / 4f:F3} s, {(rocket ? "rocket with exhaust" : "dark shell body")}",
                                   measured = $"{at1:F3} s / {at4:F3} s, {spec.mesh} body luminance {lum:F2}{(spec.exhaust ? " + exhaust" : "")}",
                                   pass = Math.Abs(at1 - simDelay) < 1e-4 && Math.Abs(at4 - simDelay / 4f) < 1e-3 && (rocket ? spec.exhaust : lum < 0.5f) });
            }
            {
                var g = OpenZFxCatalog.ProjectileOf(FxWeapon.Grenade);
                float lum = g.bodyColor.r * 0.2126f + g.bodyColor.g * 0.7152f + g.bodyColor.b * 0.0722f;
                rows.Add(new Row { test = "grenade projectile look (legacy grenade.glb)", expected = "grenade mesh, dark body", measured = $"{g.mesh}, luminance {lum:F2}",
                                   pass = g.mesh == "grenade" && lum < 0.5f });
            }
        }

        /// <summary>Metres per REAL second of one unit crossing a straight single-class stretch, driven by ZGame.Advance.</summary>
        static double RealClockSpeed(string type, string cat, ZClass cls, int fps, float gameSpeed)
        {
            var w = Legacy(1);
            bool robot = cat == "robot";
            if (!FindRun(w, cls, robot ? 1 : 2, 10, out int tx0, out int tx1, out int ty)) return 0;
            var u = w.CreateObject(cat, type, tx0 * 16, ty * 16, Team.Red);
            w.SendWaypoints(Team.Red, u, new[] { ZWaypoint.Move(tx1 * 16 + (robot ? 8 : 16), u.CenterY) });
            double acc = 0, dist = 0, real = 0, dt = 1.0 / fps;
            for (int f = 0; f < fps * 120 && (u.Waypoints.Count > 0 || f < fps); f++)
            {
                double x0 = u.PosX, y0 = u.PosY;
                var c0 = w.Map.ClassAt(u.CenterX >> 4, u.CenterY >> 4);
                ZGame.Advance(w, ref acc, dt, gameSpeed);
                w.Events.Clear();
                var c1 = w.Map.ClassAt(u.CenterX >> 4, u.CenterY >> 4);
                double d = Math.Sqrt((u.PosX - x0) * (u.PosX - x0) + (u.PosY - y0) * (u.PosY - y0));
                if (c0 == cls && c1 == cls && u.IsMoving) { dist += d; real += dt; }       // frames without a tick count too (144 Hz)
            }
            return real > 0 ? dist / ZodPxPerMetre / real : 0;
        }

        /// <summary>Mean REAL seconds between damage rolls (Hitscan or MissileFired events) against a frozen dummy about 6 tiles
        /// away, through ZGame.Advance at 60 fps; for missiles also the real flight the view gives the first one
        /// (OpenZFxPlayer.RealFlight of the event delay) and Zod's flight for the same shot: distance from the attacker's
        /// centre to the aim point / attack_missile_speed (zobject.cpp:2173, zdamagemissile.h:23-33) at game_speed 1.</summary>
        static (double interval, double flight, double zodFlight) RealClockFire(string type, string cat, float gameSpeed, int zodMissileSpeed)
        {
            var w = Legacy(7);
            var a = cat == "robot" ? w.CreateRobotGroup(type, 56 * 16, 14 * 16, Team.Red, 1) : w.CreateObject(cat, type, 56 * 16, 14 * 16, Team.Red);
            Dummy(w, "robot", "grunt", 62, 14, Team.Blue).MoveSpeed = 0;                         // frozen: never dodges out of range
            double acc = 0, real = 0, dt = 1.0 / 60, first = -1, last = -1, flight = -1, zodFlight = -1; int shots = 0;
            for (int f = 0; f < 60 * 300 && shots < 60; f++)
            {
                double simBefore = w.Time;
                ZGame.Advance(w, ref acc, dt, gameSpeed);
                foreach (var e in w.Events)
                    if ((e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired) && e.objId == a.Id && !(e.type == ZEventType.MissileFired && e.flag))
                    {
                        // event sim time -> real time on this frame's clock (sim advanced linearly with real time inside the frame)
                        double realAt = real + (gameSpeed > 0 ? (w.Time - simBefore) / gameSpeed : 0);
                        shots++; if (first < 0) first = realAt; last = realAt;
                        if (e.type == ZEventType.MissileFired && flight < 0)
                        {
                            flight = OpenZFxPlayer.RealFlight(e.value, gameSpeed);
                            zodFlight = Math.Sqrt((double)(a.CenterX - e.x) * (a.CenterX - e.x) + (double)(a.CenterY - e.y) * (a.CenterY - e.y)) / Math.Max(1, zodMissileSpeed);
                        }
                    }
                w.Events.Clear();
                real += dt;
            }
            return (shots > 1 ? (last - first) / (shots - 1) : 0, flight, zodFlight);
        }

        static string SceneText(string scene) => File.ReadAllText(Path.Combine(Application.dataPath, "OpenZ", "Scenes", scene + ".unity"));

        /// <summary>A float field of the first MonoBehaviour of class `cls` in a scene file (Unity YAML).</summary>
        static float SceneFloat(string scene, string cls, string field, float fallback)
        {
            var text = SceneText(scene);
            int at = text.IndexOf("Assembly-CSharp::" + cls + "\n", StringComparison.Ordinal);
            if (at < 0) at = text.IndexOf("Assembly-CSharp::" + cls, StringComparison.Ordinal);
            if (at < 0) return fallback;
            int end = text.IndexOf("--- !u!", at, StringComparison.Ordinal); if (end < 0) end = text.Length;
            var m = Regex.Match(text.Substring(at, end - at), "\\n  " + Regex.Escape(field) + ": ([-0-9.eE]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        static float SceneFloatAny(string scene, string field, float fallback)
        {
            var m = Regex.Match(SceneText(scene), "\\n  " + Regex.Escape(field) + ": ([-0-9.eE]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }
    }
}

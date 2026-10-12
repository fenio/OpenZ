using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The view-side match table: how big every explosion is (per source, not per splash radius), which weapon look a shot or
    /// projectile uses and which sound bank plays at which gain, with the values of our earlier projects (explosion radii,
    /// s = radius / 4, local rocket 1.2 / shell .85 / heavy shell 1.65, vehicle 1.7, the destruction banks). Zod gameplay is
    /// untouched: only the view reads this.
    /// </summary>
    public static class LegacyMatch
    {
        // Clip names = file names in Resources/OpenZ/Sfx (README_IMPORT step 1 copies the missing ones).
        public static readonly string[] BankExplosion = { "explosion", "explosion-v1-20260909", "explosion-v2-20260909", "rocket-blast-sep11", "s3_exp_legacy_1", "s3_exp_heavy_1", "explosion-heavy-2", "s3_exp_atom_1" };
        public static readonly string[] BankHeavy = { "heavy", "explosion-v1-20260909", "explosion-v2-20260909", "rocket-blast-sep11", "s3_exp_atom_heavy_1", "s3_exp_atom_heavy_2", "s3_exp_atom_heavy_3", "s3_exp_atom_heavy_4", "s3_exp_atom_heavy_5" };   // sound_v3: + 5 takes
        public static readonly string[] BankMlrsExplosion = { "explosion-v1-20260909", "explosion-v2-20260909", "rocket-blast-sep11" };
        public static readonly string[] BankTankImpact = { "v3-tank-impact-a", "v3-tank-impact-b", "v3-tank-impact-c" };
        public static readonly string[] BankTankShot = { "cannon-1", "v3-tank-shot-b", "s3_tankshot_c", "s3_tankshot_d" };   // sound_v3: + 2 takes
        public static readonly string[] BankHeavyTankShot = { "heavy-tank-1", "heavy-tank-2", "heavy-tank-3", "heavy-tank-4", "heavy-tank-5" };
        public static readonly string[] BankCannon = { "cannon", "cannon-2", "s3_cannon_b", "s3_cannon_c" };
        public static readonly string[] BankRocket = { "rocket", "s3_rocket_b", "s3_rocket_c", "s3_rocket_d", "s3_rocket_e" };
        public static readonly string[] BankMlrsLaunch = { "mlrs-launch-v1-20260909", "mlrs-launch-v2-20260909", "mlrs-launch-v3-20260909" };
        public static readonly string[] BankBolt = { "bolt1", "bolt2", "bolt3", "bolt4" };
        public static readonly string[] BankSniper = { "legacy-sniper" };
        public static readonly string[] BankRifleBurst = { "v3-rifle-burst-a", "v3-rifle-burst-b", "v3-rifle-burst-c" };
        public static readonly string[] BankRifleSingle = { "v3-rifle-single-a", "v3-rifle-single-b", "v3-rifle-single-c" };
        public static readonly string[] BankRocketArmor = { "v3-rocket-armor-a", "s3_rocket_armor_b", "s3_rocket_armor_c", "s3_rocket_armor_d" };   // sound_v3: the table's v3-tank-impact takes at this level
        public static readonly string[] BankGrenade = { "grenade", "s3_grenade_throw_1", "grenade-throw-2", "s3_atom_throw_1", "s3_atom_throw_2" };   // sound_v3: + 4 throw takes (both games hold 2 throw recordings: 3 are pitch / EQ / layer variations)
        public static readonly string[] BankDeath = { "death" };
        public static readonly string[] BankCollapse = { "building-collapse-20260909", "collapse" };                                               // crane
        public static readonly string[] BankMechStep = { "final-metal-land-a", "final-metal-land-b", "final-metal-land-c" };

        public const float GainExplosion = 0.86f, GainHeavy = 0.95f, GainTankImpact = 0.86f, GainTankShot = 0.98f, GainHeavyTankShot = 1.0f,
            GainBolt = 0.60f,
            GainSniper = 0.72f, GainRocketArmor = 0.76f, GainMechStep = 0.48f, GainDefault = 0.55f, GainCollapse = 0.95f;

        // ------------------------------------------------------------------------------------------ explosion size table
        public struct BlastSpec
        {
            public string label; public FxBlastKind kind; public float s, legRadius, wreck;
            public string key; public float gain, pitch; public string[] clips;
        }
        static BlastSpec B(string label, FxBlastKind kind, float s, float legRadius, string key, string[] clips, float gain, float pitch = 1f, float wreck = 0f) =>
            new BlastSpec { label = label, kind = kind, s = s, legRadius = legRadius, key = key, clips = clips, gain = gain, pitch = pitch, wreck = wreck };

        public static readonly BlastSpec Grenade = B("grenade", FxBlastKind.Grenade, 0.75f, 3.0f, "explosion", OzSoundV3.BankGrenadeBlast, GainExplosion, 1.1f)   /* sound_v3: own bank, x1 (was x0.8 + a -21 LUFS take) */;
        public static readonly BlastSpec Bazooka = B("bazooka rocket", FxBlastKind.Rocket, 1.0f, 4.0f, "explosion", BankExplosion, GainExplosion);
        public static readonly BlastSpec LightShell = B("light tank shell", FxBlastKind.Shell, 0.95f, 3.8f, "tank_impact", BankTankImpact, GainTankImpact);
        public static readonly BlastSpec MediumShell = B("medium (mech) shell", FxBlastKind.Shell, 1.2f, 4.25f, "tank_impact", BankTankImpact, GainTankImpact, 0.97f);
        public static readonly BlastSpec MechRocket = B("mech rocket (1 of 2)", FxBlastKind.Rocket, 0.95f, 3.4f, "explosion", BankExplosion, GainExplosion * 0.8f);
        public static readonly BlastSpec HeavyShell = B("heavy tank shell", FxBlastKind.HeavyShell, 1.65f, 4.5f, "tank_impact", BankTankImpact, GainTankImpact, 0.92f);
        public static readonly BlastSpec GunShell = B("gun (cannon) shell", FxBlastKind.Shell, 1.05f, 4.25f, "explosion", BankExplosion, GainExplosion);
        public static readonly BlastSpec HowitzerShell = B("howitzer shell", FxBlastKind.Shell, 1.4f, 5.0f, "explosion", BankExplosion, GainExplosion, 0.95f);
        /// <summary>
        /// blast (sim splash 52 px = 6.5 m, ZObject.FortGunSplashBonusPx), deeper and louder than the howitzer's.</summary>
        public static readonly BlastSpec FortShell = B("fort gun shell", FxBlastKind.Shell, 1.75f, 6.4f, "explosion", BankExplosion, GainExplosion * 1.15f, 0.86f);
        public static readonly BlastSpec MlrsRocket = B("MLRS rocket", FxBlastKind.Rocket, 1.4f, 5.6f, "heavy", BankMlrsExplosion, GainHeavy, 0f);
        public static readonly BlastSpec MissileCannonRocket = B("missile cannon rocket", FxBlastKind.Rocket, 1.25f, 5.0f, "explosion", BankExplosion, GainExplosion);
        /// <summary></summary>
        public static readonly BlastSpec MissileCannonSmall = B("missile cannon rocket (1 of 3 blasts)", FxBlastKind.Rocket, 0.8f, 3.2f, "explosion", BankExplosion, GainExplosion * 0.75f);

        public static readonly BlastSpec DeathJeep = B("jeep", FxBlastKind.Vehicle, 1.4f, 3.9f, "vdeath", BankHeavy, GainHeavy * 0.85f, 1.05f, OpenZFxCatalog.WreckScaleCar);
        public static readonly BlastSpec DeathApc = B("APC", FxBlastKind.Vehicle, 1.55f, 4.4f, "vdeath", BankHeavy, GainHeavy, 1f, 1.25f);
        public static readonly BlastSpec DeathCrane = B("crane", FxBlastKind.Vehicle, 1.5f, 4.2f, "collapse", BankCollapse, GainCollapse, 1f, 1.2f);
        public static readonly BlastSpec DeathLight = B("light tank", FxBlastKind.Vehicle, 1.7f, 4.6f, "vdeath", BankHeavy, GainHeavy, 1f, OpenZFxCatalog.WreckScaleTank);
        public static readonly BlastSpec DeathMlrs = B("missile launcher (MLRS)", FxBlastKind.Vehicle, 1.8f, 4.8f, "vdeath", BankHeavy, GainHeavy, 0.97f, OpenZFxCatalog.WreckScaleTank);
        public static readonly BlastSpec DeathMedium = B("medium tank (mech)", FxBlastKind.Vehicle, 1.9f, 5.0f, "vdeath", BankHeavy, GainHeavy, 0.95f, OpenZFxCatalog.WreckScaleMech);
        public static readonly BlastSpec DeathHeavy = B("heavy tank", FxBlastKind.Vehicle, 2.1f, 5.4f, "vdeath", BankHeavy, GainHeavy, 0.9f, 1.5f);
        public static readonly BlastSpec DeathCannon = B("gun / gatling / howitzer / missile cannon", FxBlastKind.Cannon, 1.3f, 4.2f, "vdeath", BankHeavy, GainHeavy, 1f, OpenZFxCatalog.WreckScaleCannon);
        // buildings: the collapse sound stays with RulesView.BuildingDeath (RulesView.cs:142); this is the blast
        public static readonly BlastSpec DeathFactory = B("robot / vehicle factory", FxBlastKind.Building, 2.4f, 6.5f, null, null, 0f);
        public static readonly BlastSpec DeathFort = B("fort (front / back)", FxBlastKind.Building, 2.7f, 7.2f, null, null, 0f);
        public static readonly BlastSpec DeathRepair = B("repair station", FxBlastKind.Building, 1.9f, 5.4f, null, null, 0f);
        public static readonly BlastSpec DeathRadar = B("radar", FxBlastKind.Building, 1.7f, 5.0f, null, null, 0f);
        public static readonly BlastSpec DeathRock = B("rock (non-pillar)", FxBlastKind.Rock, 1.3f, 4.5f, null, null, 0f);
        public static readonly BlastSpec DeathHut = B("hut / prop", FxBlastKind.Rock, 1.0f, 3.5f, null, null, 0f);
        /// <summary>
        /// cannon turret explodes where it lands: the sim's debris missile (40 HP, r 40 px) draws a small fireball (Exp2 Small, about
        /// the 0.7 table blast), a 1.6 m scorch and dust on its explode frame = the landing frame (TossLanded keeps the metal clang and
        /// the tank impact on top); an explosion take at pitch 1.1, gain under every vehicle death (0.52 vs 0.81-0.95); no camera shake.
        /// In water: a splash and droplets, no fireball and no scorch.</summary>
        public static readonly string[] BankTurretLand = { "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909" };
        public static readonly BlastSpec TurretLand = B("tossed turret landing", FxBlastKind.Shell, 0.7f, 2.8f, "turret_land", BankTurretLand, 0.52f, 1.1f);
        public static int TurretLandBlasts, TurretLandSplashes;
        public static readonly BlastSpec DeathCrate = B("grenade / ammo crate", FxBlastKind.Crate, 0.65f, 2.6f, "explosion", BankExplosion, GainExplosion * 0.7f, 1.12f);

        static string TypeOf(ZObject a) => a == null ? "" : a.IsFort ? "howitzer" : a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type;   // DV7: the fort gun fires howitzer shells

        public static BlastSpec ForMissile(ZObject attacker, bool grenade, bool debris, int radiusPx)
        {
            if (grenade) return Grenade;
            if (debris && attacker != null && attacker.Type == "grenades") return Grenade;
            if (debris && attacker != null && (attacker.Kind == ZKind.Vehicle || attacker.Kind == ZKind.Cannon)) return TurretLand;
            if (debris)
            {
                float r = Mathf.Max(0.3f, radiusPx / OpenZFxCatalog.PxPerMetre / 4f) * 0.7f;
                return B("turret debris", FxBlastKind.Debris, r, 0f, "debris", BankTankImpact, 0.7f);
            }
            switch (TypeOf(attacker))
            {
                case "tough": return Bazooka;
                case "light": return LightShell;
                case "medium": return MechRocket;
                case "heavy": return HeavyShell;
                case "missile_launcher": return MlrsRocket;
                case "missile_cannon": return MissileCannonSmall;                                     // MC1: 1 of 3 small blasts
                case "gun": return GunShell;
                case "howitzer": return attacker != null && attacker.IsFort ? FortShell : HowitzerShell;
            }
            float rm = radiusPx / OpenZFxCatalog.PxPerMetre;
            return rm > OpenZFxCatalog.HeavySoundRadiusM
                ? B("missile r" + radiusPx, FxBlastKind.Missile, rm / 4f, rm, "heavy", BankHeavy, GainHeavy)
                : B("missile r" + radiusPx, FxBlastKind.Missile, Mathf.Max(0.3f, rm / 4f), rm, "explosion", BankExplosion, GainExplosion);
        }

        public static BlastSpec ForDeath(ZObject o)
        {
            switch (o.Type)
            {
                case "jeep": return DeathJeep;
                case "apc": return DeathApc;
                case "crane": return DeathCrane;
                case "light": return DeathLight;
                case "missile_launcher": return DeathMlrs;
                case "medium": return DeathMedium;
                case "heavy": return DeathHeavy;
                case "robot_factory": case "vehicle_factory": return DeathFactory;
                case "fort_front": case "fort_back": return DeathFort;
                case "repair": return DeathRepair;
                case "radar": return DeathRadar;
                case "rock": return DeathRock;
                case "grenades": return DeathCrate;
            }
            if (o.Kind == ZKind.Cannon) return DeathCannon;
            if (o.Kind == ZKind.Vehicle) return DeathLight;
            if (o.Kind == ZKind.Building) return DeathFactory;
            return DeathHut;
        }

        // ------------------------------------------------------------------------------------------ blasts
        static bool Live(out OpenZFxPlayer z) { z = OpenZFxPlayer.I; return z && z.isActiveAndEnabled; }

        /// <summary></summary>
        public static void DoBlast(Vector3 at, BlastSpec d, int id, FxManager fx)
        {
            float legacy = d.s / OpenZFxCatalog.LegacyToM12;
            if (Live(out var z)) { FxManager.RaiseExploded(at, legacy); z.BlastSized(at, d.s, d.legRadius, d.kind, id); }
            else fx?.Explosion(at, legacy);
        }

        static void Sound(BlastSpec d, Vector3 at, CombatAudio au, int emitter = -1, float throttle = 0.05f)
        {
            if (au && d.clips != null && d.clips.Length > 0) au.PlayPitched(d.key, at, d.gain, throttle, emitter, d.pitch, d.clips);
        }

        /// <summary></summary>
        public static void MissileBlast(ZEvent e, ZObject attacker, ZObject target, Vector3 at, FxManager fx, CombatAudio au)
        {
            var d = ForMissile(attacker, e.flag, e.flag2, e.radius);
            int bid = e.objId * 7919 + e.x * 31 + e.y;
            if (d.label == TurretLand.label) { TurretLanding(at, d, bid, fx, au); return; }
            var xk = Exp2ForMissile(e, attacker, target, d, at, bid, out float xU);
            if (xk != Exp2Kind.None && Live(out var xz)) { FxManager.RaiseExploded(at, d.s / OpenZFxCatalog.LegacyToM12); xz.Exp2(at + Vector3.up * 0.2f, xU, xk, bid, d.s, d.kind, false, xk == Exp2Kind.Medium); }
            else DoBlast(at + Vector3.up * 0.2f, d, bid, fx);
            if (d.label == MlrsRocket.label && Random.value < OpenZFxCatalog.ShakeMlrsChance) OpenZFxShake.I?.Kick(at, OpenZFxCatalog.ShakeMlrsPx, "mlrs");   // an MLRS salvo landing in view kicks the camera now and then
            Sound(d, at, au, OzSoundV3.BlastEmitter(e));                                            // sound_v3: every blast its own slot (volleys lost 2nd+ blasts)
            OzSoundV3.OnMissileBlast(e, target, at);                                                // sound_v3: masonry on buildings
            if (!e.flag && !e.flag2 && TypeOf(attacker) == "howitzer") OpenZHeavyShells.Impact(at, attacker != null && attacker.IsFort);   // nuke_fx_v3: crater, debris, shake by distance
            if (au && !e.flag && !e.flag2 && target != null && (target.Kind == ZKind.Vehicle || target.Kind == ZKind.Cannon) &&
                (d.kind == FxBlastKind.Rocket) && Mathf.Abs(target.CenterX - e.x) <= e.radius && Mathf.Abs(target.CenterY - e.y) <= e.radius)
                au.PlayPitched("rocket_armor", at, GainRocketArmor, 0.15f, target.Id, 1f, BankRocketArmor);
        }

        static void TurretLanding(Vector3 at, BlastSpec d, int bid, FxManager fx, CombatAudio au)
        {
            if (OpenZFxPlayer.IsWaterTile(at))
            {   // into the river / canal: a splash, no fireball, no scorch
                if (Live(out var w)) { w.Splash(at, 1.3f); w.Droplets(at + Vector3.up * 0.2f, 1.1f, 14); w.Ripple(at, 0.9f); }
                if (au) au.PlayPitched("turret_splash", at, 0.75f, 0.05f, -1, 1f, "sd_debris_splash_a", "sd_debris_splash_b");
                TurretLandSplashes++; return;
            }
            if (Live(out var z)) { FxManager.RaiseExploded(at, d.s / OpenZFxCatalog.LegacyToM12); z.Exp2(at + Vector3.up * 0.2f, 3f, Exp2Kind.Small, bid, d.s, d.kind, false, false); z.Dust(at, 0.9f); }
            else DoBlast(at + Vector3.up * 0.2f, d, bid, fx);
            ScorchDecals.Add(at, 1.6f, ScorchKind.Explosion);
            Sound(d, at, au, -1, 0.05f);
            TurretLandBlasts++;
        }

        /// <summary></summary>
        public static void UnitDeath(ZObject o, Vector3 p, FxManager fx, CombatAudio au)
        {
            var d = ForDeath(o);
            var xk = Exp2ForDeath(o, d, p, out float xU);
            if (xk != Exp2Kind.None && Live(out var xz)) { FxManager.RaiseExploded(p, d.s / OpenZFxCatalog.LegacyToM12); xz.Exp2(p + Vector3.up * 0.6f, xU, xk, o.Id, d.s, d.kind, false, true); }
            else DoBlast(p + Vector3.up * 0.6f, d, o.Id, fx);
            if (d.wreck > 0f && Live(out var z)) z.WreckFire(p, d.wreck);
            Sound(d, p, au, -1, 0f);
            OzSoundV3.DeathTail(o, p);                                                              // sound_v3: metal parts raining down
        }

        public static void BuildingDeath(ZObject o, Vector3 p, FxManager fx)
        {
            var d = ForDeath(o);
            DoBlast(p + Vector3.up * 2f, d, o.Id, fx);
            // dust and camera (the collapse logic stays); bridges keep the current look
            if (o != null && !o.IsBridge && OpenZFxPlayer.Pick(Exp2Kind.Big, 0.4f, p, o.Id * 613 + 17) != Exp2Kind.None && Live(out var z))
                z.Exp2(p + Vector3.up * 2f, Mathf.Clamp(2.3f * d.s, 4f, 8f), Exp2Kind.Big, o.Id * 613 + 17, d.s, d.kind, true, false);
        }

        /// <summary>Spec section 9 for a sim missile: fort cannon shell -> Big 50 %; MLRS / Tough rockets -> Salvo once per volley
        /// (50 %); a shell on a building -> BuildingHit 40 %, on a vehicle / cannon -> Medium 30 %, on a robot -> Small 30 %; grenades,
        /// turret debris, mech rockets and missile cannon triples (their cosmetic twins stay current) -> current only.
        /// U (spec size unit) comes from the event's M12 strength so exp2 matches the current blast's on-screen size class.</summary>
        static Exp2Kind Exp2ForMissile(ZEvent e, ZObject attacker, ZObject target, BlastSpec d, Vector3 at, int id, out float U)
        {
            U = 0f;
            if (e.flag || e.flag2 || attacker == null) return Exp2Kind.None;
            string t = TypeOf(attacker);
            if (t == "missile_launcher" || t == "tough")
            {
                U = t == "tough" ? 1.6f : 1.9f;
                return OpenZFxPlayer.VolleyExp2(attacker.Id, at, id) ? Exp2Kind.Salvo : Exp2Kind.None;
            }
            bool shell = t == "light" || t == "heavy" || t == "gun" || t == "howitzer";
            if (!shell) return Exp2Kind.None;
            if (attacker.IsFort) { U = Mathf.Clamp(2.6f * d.s, 3f, 8f); return OpenZFxPlayer.Pick(Exp2Kind.Big, 0.5f, at, id); }
            if (target != null && !target.Destroyed && target.Kind == ZKind.Building && !target.IsBridge) { U = Mathf.Clamp(3f * d.s, 3f, 6f); return OpenZFxPlayer.Pick(Exp2Kind.BuildingHit, 0.4f, at, id); }
            if (target != null && (target.Kind == ZKind.Vehicle || target.Kind == ZKind.Cannon)) { U = Mathf.Clamp(3f * ForDeath(target).s, 3.5f, 6f); return OpenZFxPlayer.Pick(Exp2Kind.Medium, 0.3f, at, id); }
            if (target != null && target.Kind == ZKind.Robot) { U = Mathf.Clamp(4.5f * d.s, 2.5f, 6f); return OpenZFxPlayer.Pick(Exp2Kind.Small, 0.3f, at, id); }
            return Exp2Kind.None;
        }

        /// <summary>Spec section 9 vehicle deaths (45 %): heavy tank / APC / MLRS / medium (mech) -> Big, jeep / light tank / crane /
        /// cannons -> Medium.</summary>
        static Exp2Kind Exp2ForDeath(ZObject o, BlastSpec d, Vector3 p, out float U)
        {
            U = 0f;
            if (o == null || (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon)) return Exp2Kind.None;
            bool big = o.Type == "heavy" || o.Type == "apc" || o.Type == "missile_launcher" || o.Type == "medium";
            U = big ? Mathf.Clamp(2.3f * d.s, 3.5f, 6f) : Mathf.Clamp(3f * d.s, 3.5f, 6f);
            return OpenZFxPlayer.Pick(big ? Exp2Kind.Big : Exp2Kind.Medium, 0.45f, p, o.Id * 977 + 5);
        }

        public static void PropDeath(ZObject o, Vector3 at, FxManager fx) => DoBlast(at, ForDeath(o), o.Id, fx);

        public static void CrateDeath(ZObject o, Vector3 p, FxManager fx, CombatAudio au)
        {
            DoBlast(p + Vector3.up * 0.3f, DeathCrate, o.Id, fx);
            if (Live(out var z)) z.Pop(p + Vector3.up * 0.3f, 1f); else fx?.Pop(p + Vector3.up * 0.3f, 1f);
            Sound(DeathCrate, p, au, -1, 0f);
        }

        /// <summary></summary>
        public static void RobotDeath(Vector3 p, Vector3 from, bool blast, CombatAudio au)
        {
            if (Live(out var z)) z.RobotDeathDust(p, p - from, blast);
            if (!AudioDirector.DeathVoicesLive) au?.PlayPitched("death", p, GainDefault, 0.15f, -1, 1f, BankDeath);   // death_voices_v1: the director plays it by type
        }

        // ------------------------------------------------------------------------------------------ shots (hitscan)
        public static FxSurface SurfaceOf(ZObject t, bool hit) =>
            !hit || t == null ? FxSurface.Auto : t.Kind == ZKind.Robot ? FxSurface.Unit : t.Kind == ZKind.MapItem ? FxSurface.Rock : FxSurface.Metal;

        /// <summary>
        /// bolt1-4.mp3; snipers keep the LEG tracer + legacy-sniper;
        /// the gatling keeps MG tracers + rifle bursts.</summary>
        public static FxWeapon ShotWeapon(string type)
        {
            switch (type)
            {
                case "grunt": case "psycho": case "jeep": return FxWeapon.Bolt;
                case "sniper": return FxWeapon.Sniper;
                case "gatling": return FxWeapon.MachineGun;
                default: return FxWeapon.Rifle;
            }
        }

        /// <summary>parity P5: Zod's client bullet / flame / laser effects move 300 px/s = 37.5 m/s of game time (ebullet.cpp:7,
        /// </summary>
        public static float ShotSpeed(float gameSpeed) => CombatView.ZodBulletMps * Mathf.Max(0.25f, gameSpeed);

        public static void Shot(ZEvent e, ZObject target, string type, Vector3 from, Vector3 to, FxManager fx, CombatAudio au)
        {
            if (type == "crane") type = "grunt";                                                       // vehicles_nb_v1 NB24: the crane driver's rifle = the grunt's bolt (DV31)
            var w = ShotWeapon(type);
            if (Live(out var z))
            {
                float mps = ShotSpeed(ZGame.I ? ZGame.I.gameSpeed : 1f), fl = z.Shot(from, to, w, SurfaceOf(target, e.flag), mps);
                if (target != null && target.Destroyed) BlastWave.NoteDrawn(e.objId, target, fl);
                OzSoundV3.ShotLanded(target, e.flag, to, fl);                                          // sound_v3: bullets on buildings            // H0 round 3: a killing round (GP3 proof)
                var run = WeaponsV2Runner.I; if (run) run.NoteMps(type + " round", mps);                     // H0 round 3: the speed it was drawn at (GP3)
            }
            else fx?.Bullet(from, to);
            if (!au) return;
            switch (w)
            {
                case FxWeapon.Bolt: au.PlayPitched("bolt", from, GainBolt, type == "grunt" ? 0.2f : 0.3f, e.objId, 1f, BankBolt); break;   // v3_bolt, rate +-3.5 %
                case FxWeapon.Sniper: au.PlayPitched("sniper", from, GainSniper, 0f, e.objId, 0f, BankSniper); break;                    // authored rate (:1487)
                case FxWeapon.MachineGun: au.PlayPitched("burst", from, GainDefault, 0.32f, e.objId, 1f, BankRifleBurst); break;
                default: au.PlayPitched("rifle", from, GainDefault, 0.2f, e.objId, 1f, BankRifleSingle); break;
            }
        }

        // ------------------------------------------------------------------------------------------ projectiles
        /// <summary>
        /// = HEAVY_TANK_SHELL look, gun / howitzer = CANNON shell, tough = BAZOOKA, missile launcher = MLRS, missile cannon = rocket.</summary>
        public static FxWeapon MissileWeapon(ZObject a, string type)
        {
            switch (type)
            {
                case "tough": return FxWeapon.Bazooka;
                case "missile_launcher": return FxWeapon.Mlrs;
                case "missile_cannon": return FxWeapon.Rocket;
                case "light": return FxWeapon.TankShell;
                case "medium": return FxWeapon.Rocket;
                case "heavy": return FxWeapon.HeavyShell;
            }
            return a != null && a.Kind == ZKind.Cannon ? FxWeapon.Cannon : FxWeapon.TankShell;
        }

        static bool IsRocket(FxWeapon w) => w == FxWeapon.Bazooka || w == FxWeapon.Rocket || w == FxWeapon.Mlrs;

        /// <summary>H0 round 3 (GP3 proof): every drawn shell / rocket as it leaves (shooter, type, its drawn flight in s).</summary>
        public static event System.Action<ZObject, string, float> Launched;

        /// <summary>
        /// (OpenZFxPlayer.RocketEasing); the mech's pod rockets ("medium") and every shell keep their constant speed.</summary>
        public static bool Eased(string type) => type == "tough" || type == "missile_launcher" || type == "missile_cannon";

        public static void Launch(ZObject a, string type, Vector3 from, Vector3 to, float delay, FxManager fx)
        {
            Launched?.Invoke(a, type, delay);
            var w = MissileWeapon(a, type);
            if (type == "howitzer" && OpenZHeavyShells.Launch(from, to, delay, a != null && a.IsFort)) return;   // nuke_fx_v3: big glowing shell, same arc + flight time
            if (Live(out var z)) { z.Launch(from, to, delay, w, Eased(type)); return; }
            if (!fx) return;
            bool rocket = IsRocket(w);
            fx.LaunchFlash(from, to - from, rocket ? 0.9f : 1.2f);
            fx.Projectile(from, to, rocket ? "rocket" : "shell", OpenZFxPlayer.RealFlight(delay, ZGame.I ? ZGame.I.gameSpeed : 1f), rocket ? 6f : 9.8f);   // parity P5: legacy pool flies real s
        }

        /// <summary></summary>
        public static void GrenadeFlight(Vector3 from, Vector3 to, float delay, FxManager fx)
        {
            if (Live(out var z)) z.Launch(from, to, delay, FxWeapon.Grenade);
            else fx?.Projectile(from, to, "bomb", OpenZFxPlayer.RealFlight(delay, ZGame.I ? ZGame.I.gameSpeed : 1f), 9.8f);   // parity P5: legacy pool flies real s
        }

        /// <summary></summary>
        public static void GrenadeSound(Vector3 from, int emitter, CombatAudio au)
        {   // sound_v3: the whoosh at the real release (GrenadeFx holds the grenade 0.56 s), x1.9, 5 takes; the old call stays the fallback
            if (OzSoundV3.GrenadeThrowNow(from, emitter, au)) return;
            au?.PlayPitched("grenade", from, GainDefault, 0.2f, emitter, 1f, BankGrenade);
        }

        public static void BackBlast(Vector3 back, Vector3 dir, FxManager fx)
        {
            if (Live(out var z)) { z.BackBlast(back, -dir); return; }
            if (fx) { fx.LaunchFlash(back, -dir, 0.6f); fx.Dust(back + Vector3.down * 0.6f, 0.7f); fx.Pop(back, 0.4f); }
        }

        // (OpenZFxPlayer.Launch -> the TankShell muzzle), a bigger M12 launch flash + ground light, a ring of LEG smoke puffs blown out
        // around the bore (the channel's drag stalls it into a ring), a forward blast cloud and dust kicked off the ground below
        public const float HeavyBlastFlashK = 1.35f, HeavyBlastGroundLight = 1.3f, HeavyBlastDust = 0.9f;
        public const int HeavyRingPuffs = 16;

        /// <summary>
        /// The heavy tank's blast (flash, smoke ring, dust) scaled per gun, plus a spark burst out of the barrel.</summary>
        public static void GunMuzzleBlast(string type, Vector3 from, Vector3 to, float delay)
        {
            float k; switch (type) { case "heavy": k = 1f; break; case "howitzer": k = 0.95f; break; case "gun": k = 0.8f; break; case "light": k = 0.65f; break; default: return; }
            if (k >= 1f) HeavyMuzzleBlast(from, to, delay); else ScaledMuzzleBlast(from, to, delay, k);
            var dir = to - from; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            OpenZHitSparks.Burst(from + dir * 0.3f, dir, FxWeapon.Cannon, FxSurface.Metal, 0.7f + 0.5f * k, false);
        }

        static void ScaledMuzzleBlast(Vector3 from, Vector3 to, float delay, float k)
        {
            if (!Live(out var z)) return;
            float t = OpenZFxPlayer.RealFlight(delay, ZGame.I ? ZGame.I.gameSpeed : 1f);
            var dir = (to - from) / t; dir.y += OpenZFxCatalog.GravityOf(FxWeapon.Cannon) * t * 0.5f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            z.LaunchFlash(from + dir * 0.3f, dir, HeavyBlastFlashK * k, HeavyBlastGroundLight * k);
            var right = Vector3.Cross(Vector3.up, dir); right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            var up = Vector3.Cross(dir, right).normalized;
            var c = from + dir * 0.5f; var smoke = OpenZFxCatalog.M12Smoke.color;
            int puffs = Mathf.Max(4, Mathf.RoundToInt(HeavyRingPuffs * k));
            for (int i = 0; i < puffs; i++)
            {
                float a = i * Mathf.PI * 2f / puffs + Random.Range(-0.12f, 0.12f);
                var radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                z.EmitChannel("leg_smoke", c + radial * 0.2f, radial * Random.Range(2.2f, 3f) * k + dir * Random.Range(0.5f, 1f),
                    Random.Range(0.9f, 1.3f) * k, Random.Range(0.45f, 0.65f), smoke, 0.7f);
            }
            for (int j = 0; j < 3; j++)
                z.EmitChannel("m12_smoke", from + dir * (0.6f + j * 0.45f), dir * Random.Range(2f, 4f) + Random.insideUnitSphere * 0.35f,
                    Random.Range(0.6f, 0.95f) * k, Random.Range(0.6f, 0.9f), smoke, OpenZFxCatalog.M12Smoke.alpha);
        }

        public static void HeavyMuzzleBlast(Vector3 from, Vector3 to, float delay)
        {
            if (!Live(out var z)) return;
            float t = OpenZFxPlayer.RealFlight(delay, ZGame.I ? ZGame.I.gameSpeed : 1f);             // along the barrel = the shell's launch velocity
            var dir = (to - from) / t; dir.y += OpenZFxCatalog.GravityOf(FxWeapon.HeavyShell) * t * 0.5f;   // (OpenZFxPlayer.Launch, M12 projectile())
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            z.LaunchFlash(from + dir * 0.35f, dir, HeavyBlastFlashK, HeavyBlastGroundLight);
            var right = Vector3.Cross(Vector3.up, dir); right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            var up = Vector3.Cross(dir, right).normalized;
            var c = from + dir * 0.6f; var smoke = OpenZFxCatalog.M12Smoke.color;
            for (int i = 0; i < HeavyRingPuffs; i++)
            {
                float a = i * Mathf.PI * 2f / HeavyRingPuffs + Random.Range(-0.12f, 0.12f);
                var radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                z.EmitChannel("leg_smoke", c + radial * 0.25f, radial * Random.Range(2.6f, 3.4f) + dir * Random.Range(0.6f, 1.2f),
                    Random.Range(1.1f, 1.5f), Random.Range(0.5f, 0.7f), smoke, 0.75f);
            }
            for (int j = 0; j < 5; j++)
                z.EmitChannel("m12_smoke", from + dir * (0.8f + j * 0.55f), dir * Random.Range(2.5f, 5f) + Random.insideUnitSphere * 0.4f,
                    Random.Range(0.8f, 1.2f), Random.Range(0.7f, 1.0f), smoke, OpenZFxCatalog.M12Smoke.alpha);
            float gy = ZGame.I ? ZGame.I.GroundY(from.x, from.z) : from.y - 2f;
            if (from.y - gy < 3.5f) z.Dust(new Vector3(from.x + dir.x * 1.6f, gy + 0.15f, from.z + dir.z * 1.6f), HeavyBlastDust);
        }

        /// <summary>
        /// MLRS -> mlrsLaunch (.55, authored pitch), ROCKET / BAZOOKA -> rocket (.55), SHELL / CANNON -> cannon (.55).</summary>
        /// <summary>
        /// plays it at the tube; this plays the same clip on a source that flies with the rocket (from its sustained part, 0.18 s in),
        /// so the roar travels from the Tough to the impact. 3D (Weapons bus, local curve), a little under the launch copy.</summary>
        public const float GainRocketFlight = GainDefault * 1.5f, RocketFlightStart = 0.18f, RocketFlightGravity = 6f;
        public static void RocketFlight(Vector3 from, Vector3 to, float simDelay, int emitter, CombatAudio au)
        {
            if (!au) return;
            float secs = OpenZFxPlayer.RealFlight(simDelay, ZGame.I ? ZGame.I.gameSpeed : 1f);
            au.PlayAlong("rocket_flight", from, to, secs, RocketFlightGravity, GainRocketFlight, emitter, 1f, RocketFlightStart, BankRocket);
        }

        public static void LaunchSound(string type, Vector3 from, int emitter, CombatAudio au)
        {
            if (!au) return;
            switch (type)
            {
                case "heavy": au.PlayPitched("heavyshot", from, GainHeavyTankShot, 0.1f, emitter, 1f, BankHeavyTankShot);   /* sound_v3: 0.2 -> 0.1 s (HeavyPairGap 0.36 sim s) */ break;   // mlrs_heavy_v1: own voices (light-tank shots never steal them)
                case "light": au.PlayPitched("tankshot", from, GainTankShot, 0.2f, emitter, 1f, BankTankShot); break;
                case "medium": au.PlayPitched("mech_rocket", from, OzSoundV3.GainMechRocket, 0.05f, emitter, 1f, OzSoundV3.BankMechRocket); break;
                case "missile_launcher": au.PlayPitched("launch", from, GainDefault * 2.2f, 0.2f, emitter, 0f, BankMlrsLaunch); break;
                case "tough": au.PlayPitched("rocket", from, GainDefault * 2f, 0.2f, emitter, 1f, BankRocket); break;
                case "missile_cannon": au.PlayPitched("mc_rocket", from, GainDefault * 2f, 0.2f, emitter, 1f, OzSoundV3.BankMcRoar); break;
                case "gun":
                    au.PlayPitched("gunshot", from, 1f, 0.2f, emitter, 1.03f, BankCannon);
                    au.StartCoroutine(Whoosh(au, from, emitter));
                    break;
                case "howitzer":                                                                                // deeper: heavy boom + the cannon crack
                    au.PlayPitched("gunshot", from, GainHeavyTankShot, 0.2f, emitter, 0.86f, BankHeavyTankShot);
                    au.PlayPitched("cannon", from, GainDefault * 1.6f, 0.2f, emitter, 0.96f, BankCannon);
                    au.PlayPitched("howitzer_boom", from, GainHeavyTankShot, 0.2f, emitter, 1f, BankHowitzerBoom);
                    break;                                                                                         // incoming whistle: HowitzerWhistle (NB2)
                default: au.PlayPitched("cannon", from, GainDefault * 1.6f, 0.2f, emitter, 1f, BankCannon); break;
            }
            OzSoundV3.AfterShot(type, from, emitter);                                                  // sound_v3: breech clank / reload
        }

        public static readonly string[] BankShellWhoosh = { "demo-flyby-whoosh", "rocket-2" };
        /// <summary></summary>
        public static readonly string[] BankHowitzerBoom = { "oz_boom_howitzer_a", "oz_boom_howitzer_b", "oz_boom_howitzer_c" };
        public static readonly string[] BankShellWhistle = { "sd_shell_whistle_a", "sd_shell_whistle_b", "s3_shell_whistle_c", "s3_shell_whistle_d" };   // sound_v3

        /// <summary>
        /// whistle at the target that ends on the impact (its last 1.6 s of flight), pitched down a little for weight.</summary>
        public static void HowitzerWhistle(Vector3 to, float delay, int emitter, CombatAudio au)
        {
            if (!au) return;
            if (OpenZHeavyShells.Enabled) return;
            float flight = OpenZFxPlayer.RealFlight(delay, ZGame.I ? ZGame.I.gameSpeed : 1f);
            au.StartCoroutine(WhistleAt(au, to, Mathf.Max(0f, flight - 1.6f), emitter));
        }
        static System.Collections.IEnumerator WhistleAt(CombatAudio au, Vector3 at, float wait, int emitter)
        {
            if (wait > 0f) yield return new WaitForSeconds(wait);
            if (au) au.PlayPitched("shell_whistle", at + Vector3.up * 6f, GainHeavy, 0.3f, emitter, Random.Range(0.82f, 0.92f), BankShellWhistle);
        }
        static System.Collections.IEnumerator Whoosh(CombatAudio au, Vector3 at, int emitter)
        {
            yield return new WaitForSeconds(0.08f);
            if (au) au.PlayPitched("shell_whoosh", at, GainDefault * 1.2f, 0f, emitter, Random.Range(0.9f, 1.15f), BankShellWhoosh);
        }

        // ------------------------------------------------------------------------------------------ engines
        /// <summary>Engine loop bank: TANK / MLRS -> tank.mp3, MECH -> none (steps), everything else -> car.mp3.</summary>
        public static string EngineLoop(ZObject v)
        {
            if (v == null || v.Kind != ZKind.Vehicle) return null;
            switch (v.Type)
            {
                case "light": case "heavy": case "missile_launcher": case "apc": case "crane": return "tank";
                case "medium": return null;
                default: return "car";                                                                    // jeep
            }
        }
    }
}

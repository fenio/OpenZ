using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Renders one sim unit: continuous sim position on the terrain, heading from the velocity, robot animation
    /// (idle / walking / run_forward / shooting / grenade_throw) at Zod's speed ratio (SpeedOffsetPercent), owner colour,
    /// turret / cannon aim at the attack target, and Zod's client deaths (zrobot.cpp:369-378): blown through the air after a
    /// missile hit, melted after a flame hit, else a death animation; vehicles and cannons leave a burnt wreck that sinks.
    /// slew to their target, jeeps lean into turns, weave and bounce, moving vehicles kick up dust. A tank's turret flies
    /// off on death and lands exactly where Zod's turret debris missile explodes. Units keep a little space: a view-side
    /// separation pushes overlapping units apart (Zod's sim lets them overlap; the sim positions stay untouched).
    /// </summary>
    [DefaultExecutionOrder(60)]   // after CombatView, so a death seen this frame animates instead of vanishing
    public partial class UnitView : MonoBehaviour                                                  // deaths_mech_v1: + UnitView.Deaths.cs
    {
        public ZObject Obj { get; private set; }
        ZGame game;
        Animator anim;
        TeamPaint paint;
        Transform turret, muzzle; Quaternion turretRest;
        string state;
        float yaw, yOffset, formation, throwUntil, upWeight; string upState;
        SkinnedMeshRenderer skin; MaterialPropertyBlock bandBlock; WeaponHold hold;
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi");
        Vector3 disp, dispVel; bool dispInit;                                                   // smoothed robot display position
        // minimum dwell, not every flicker of the sim's IsMoving (Zod's 1 s ScuffleUnits nudges, squad stops, spacing pushes)
        bool walkShown, sepTouch; float gaitT = 10f;                                          // sepTouch: within the spacing + 0.12 m of a unit
        const float WalkOn = 0.3f, WalkOff = 0.15f, MinStand = 0.25f, MinWalk = 0.45f;          // drawn root speed (m/s); dwell (s)
        // (Sim/ZSeparation.cs Gate: queue / hold / sidestep, PushApart, Unstack, regroup) a walking robot's drawn body often
        // creeps or stops while Zod's Dx/Dy stays set, and the legs read that sim velocity: full strides on a creeping body,
        // walk <-> run flicker, a body turning to one-tick sidestep headings. The legs now read the DRAWN body only: stride = its
        // real step speed (slew-limited), gait = its speed with hysteresis, facing = its low-passed direction; a standing robot
        // ignores sim jostle below StandHold (m). Vehicles: HullSteer (path-following hull, dead zone, hold, commit).
        public static float StandHold = 0.12f, BodyTau = 0.2f, FaceMin = 0.25f, FaceDead = 6f, StrideSlew = 6f;
        /// <summary>The BS-01 walking clip's own ground speed at 1x and model scale 1 (m/s), measured in Play by RobotLegIK's stride probe.</summary>
        public static float WalkStrideMps = 0.922f;
        /// <summary>The walking clip's steady-stance foot speed in humanoid units per second at 1x (walking_legfix.anim: 0.885 hu/s both feet;
        /// run_forward 1.573 = 1.78x, the existing /1.8): x Animator.humanScale x the animator's scale = its real ground speed (grunt 1.23 m/s).
        /// The first probe value (0.922 m/s) was biased low by end-of-swing glide frames.</summary>
        public static float WalkStrideHu = 0.885f;
        Vector3 lastDisp, bodyVel; float strideSpeed; bool runShown;
        HullSteer hullSteer;
        static readonly Vector2[] Slots = { Vector2.zero, new Vector2(-1.15f, -1.3f), new Vector2(1.15f, -1.3f), new Vector2(0f, -2.6f) };
        const float TurnRate = 400f;
        float modelYaw;
        VehicleMotion motion; float animMul = 1f, animOffset;
        float shotAge = 10f, fireAnimUntil;
        Transform weapon; Vector3 weaponRest; Vector3 turretRestPos;
        readonly List<(Transform t, Vector3 rest)> barrels = new List<(Transform, Vector3)>();
        bool IsTough => Obj.Type == "tough";
        static float Recoil(float age) => age < 0 || age > 1 ? 0 : (1 - Mathf.Exp(-age / 0.018f)) * Mathf.Exp(-age / 0.23f) * 1.5f;
        float turretAim; bool turretInit; float lean, prevYaw, swerve, bounceT, dustT;
        Quaternion tilt = Quaternion.identity;
        Vector3 basePos, sepOffset, sepCorr; float radius, weight, maxSep; float bandH = -1f;
        static readonly List<UnitView> Live = new List<UnitView>(); static int lastSolve = -1;
        static bool[] sepOn = new bool[256];
        /// <summary>Static view obstacles (x, z, radius; radius 0 = gone): L17 lamp posts stand on walkable Zod tiles, so robots
        /// </summary>
        public static readonly List<Vector3> Obstacles = new List<Vector3>();
        Transform tossed; Vector3 tossFrom, tossTo, tossAxis; float tossT, tossDur = -1, tossSpin, tossApex; bool tossLanded;
        UnitAim gunAim; bool popping; float heading;                                             // combatview: gun aim, removal pop, path heading
        public BS01Stance Stance => stance;                                                        // robot_models_v2 review: RobotLegIK lets a kneeling / prone sniper's legs go
        double clearPx;                                                                            // robot_models_v2 review: v3 body building clearance (px)
        BS01Stance stance;                                                                        // robot_models_v2: sniper kneel / prone, tough prepare / aim / fire, psycho burst shake
        /// <summary>robot_models_v2: UnitAim holds a rocket while the Tough's tough_prepare still lowers the tube onto the target.</summary>
        public bool StanceHoldsFire => (stance && stance.HoldsFire) || ToughAimEase.HoldsFire(this);   // K324: also while a Tough's drawn tube still turns onto its aim (View/ToughAimEase.cs)
        // crews_v1: a cannon on one of Zod's 4 fort turret spots (bfort.cpp:534-548) is drawn exactly on its tower pad, on the ring
        // mount (TowerMounts), never on the terrain under its sim centre (that was 3.7 m off the pad and inside the fort)
        int towerState, towerPad = -1, towerTries; Transform towerFort; Vector3 towerAxis;
        /// <summary>crews_v1: this cannon stands on a fort tower pad (BoardingView, SelectionOverlay, CombatView read it).</summary>
        public bool OnTower => towerState > 0 && towerFort;
        public Transform TowerFort => towerFort;
        /// <summary>crews_v2: the tower pad index (TowerMounts.PadLocal) of a tower gun.</summary>
        public int TowerPad => towerPad;

        bool IsTank => Obj.Type == "light" || Obj.Type == "medium" || Obj.Type == "heavy";
        float HullRate => Obj.Kind == ZKind.Robot ? TurnRate : VehicleSlip.Enabled ? VehicleSlip.RateOf(Obj.Type) : Obj.Type == "jeep" ? 380f : 150f;   // vehicles_nb_v1 NB21: per chassis
        float TurretRate => Obj.Type == "jeep" ? 420f : Obj.Kind == ZKind.Cannon ? 140f : 110f;

        enum Death { None, Anim, Airborne, Melt, Wreck, Gone, Ragdoll }
        RobotRagdoll ragdoll;
        Death death; float deathT; Vector3 deathFrom, deathTo, spinAxis; float smokeT;

        public bool Dying => death != Death.None;
        /// <summary>Seconds since this unit died (0 while it lives).</summary>
        public float DeadFor => death != Death.None ? deathT : 0f;
        /// <summary>ui_combat_v1: Time.time of the last shot this view showed (OnFire). BoardingView's hatch commander fires with it.</summary>
        public float LastFireAt { get; private set; } = -100f;

        public void Bind(ZGame g, ZObject o)
        {
            game = g; Obj = o;
            anim = GetComponentInChildren<Animator>();
            paint = GetComponent<TeamPaint>();
            // unit models have their front (head, rifle, gun, barrel, muzzle) at -z: the view's yaw means "front direction", so
            // those models are drawn 180 deg around; keep the placed look at bind (U3/U4 bug: they drove and aimed backwards)
            modelYaw = UnitAim.ModelYaw(gameObject, o, 180f);                                            // every unit model (robot rifle/head, mech gun, vehicle muzzles) faces -z; unsplit imported cannons +x (combatview)
            yaw = transform.eulerAngles.y - modelYaw;
            heading = yaw;
            yOffset = o.Kind == ZKind.Robot ? (o.Type == "laser" && !GetComponent<RobotV3>() ? -0.08f : -0.03f) : -0.01f;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!turret && (t.name.Contains("TurretYaw") || t.name == "MechWaistYaw")) turret = t;
                if (t.name == "Muzzle" || t.name == "BazookaMuzzle" || t.name == "RifleMuzzle") muzzle = t;          // sockets, not mesh parts
                else if (!muzzle && t.name.Contains("Muzzle") && !t.name.Contains("_")) muzzle = t;
            }
            if (turret) { turretRest = Quaternion.Inverse(transform.rotation) * turret.rotation; turretRestPos = turret.localPosition; }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!weapon && (t.name == "Rifle_Heavy" || t.name == "Bazooka")) { weapon = t; weaponRest = t.localPosition; }
                if (t != turret && (t.name.Contains("Recoil") || t.name.Contains("Barrel"))) barrels.Add((t, t.localPosition));
            }
            if (anim) { anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; SetState("idle", 0f); }
            if (anim && o.Kind == ZKind.Robot) RobotLegIK.Attach(anim);                            // legs_rootcause: feet on the clips' own foot goals, knees forward (BS-01 humanoid only)
            radius = o.Kind == ZKind.Robot ? (o.Type == "tough" ? 0.95f : 0.8f) : o.Kind == ZKind.Cannon ? 1.5f : 1.95f;
            if (o.Kind == ZKind.Robot) { radius = RobotV3.ViewRadius(this, radius); clearPx = RobotV3.ClearancePx(this, radius); }   // robot_models_v2 review: v3 body width
            skin = o.Kind == ZKind.Robot ? GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (o.Kind == ZKind.Robot) hold = WeaponHold.Attach(gameObject);
            stance = o.Kind == ZKind.Robot ? GetComponent<BS01Stance>() : null;                        // robot_models_v2
            if (stance) stance.Bind(this, o, anim);
            if (o.Kind == ZKind.Vehicle) WheelSplit.Apply(gameObject, o.Type);                                // vehicles_nb_v1 NB23: APC / crane wheels cut out to spin (before VehicleMotion scans)
            if (o.Kind == ZKind.Vehicle) { motion = GetComponent<VehicleMotion>(); if (!motion) motion = gameObject.AddComponent<VehicleMotion>(); }
            // no lockstep squads: each robot gets its own animation phase and a few percent of speed (seeded by id)
            animMul = 1f + ((o.Id * 0.6180339f) % 1f - 0.5f) * 0.08f;
            animOffset = (o.Id * 0.3819660f) % 1f * 0.6f;
            weight = o.Kind == ZKind.Cannon ? 0f : o.Kind == ZKind.Robot ? 1f : 0.35f;
            if (o.Kind != ZKind.Robot)
            {
                // ~1.7 m from its centre), not a fixed guess
                var rs = GetComponentsInChildren<Renderer>(); if (rs.Length > 0)
                {
                    var bb = rs[0].bounds; foreach (var r in rs) if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) bb.Encapsulate(r.bounds);
                    float half = Mathf.Max(bb.extents.x, bb.extents.z);
                    radius = Mathf.Clamp(half * (o.Kind == ZKind.Cannon ? 0.95f : 0.8f), radius, 3f);
                }
            }
            maxSep = o.Kind == ZKind.Robot ? 3.2f : 3.0f;                                                    // enough to walk clear of a cannon; long trucks need 3 m
            // (zserver.cpp:623-657) and only spreads it by walking; a minion born on its leader starts at its formation slot
            // (view only, eases back as the sim's own 14 px spacing takes over), so the squad never reads as one blob
            if (o.IsMinion && o.Leader != null && Mathf.Abs(o.LocX - o.Leader.LocX) + Mathf.Abs(o.LocY - o.Leader.LocY) <= 4)
            {
                var sl = Slots[Mathf.Min(o.MemberIndex, Slots.Length - 1)];
                sepOffset = new Vector3(sl.x, 0f, sl.y) * 0.85f;
            }
            swerve = (o.Id * 0.618f) % 1f * 6.28f;
            if (!Live.Contains(this)) Live.Add(this);
            UnitLookMotionGate.KeepSkinnedCrisp(gameObject);                                             // a skinned body never smears under the motion blur option
            if (o.Kind == ZKind.Cannon) ToyBarrels.Apply(gameObject, o.Type);                            // crews_v2: fat toy howitzer barrel (before UnitAim)
            if (o.Kind == ZKind.Cannon) GunNose.Apply(gameObject, o.Type);
            if (o.Kind == ZKind.Vehicle && o.Type == "apc") { ApcDome.Ensure(this); ApcRearDoor.Ensure(this); ApcSideHatches.Ensure(this); }
            if (o.Kind == ZKind.Cannon) ResolveTower();                                                 // crews_v1: tower size before UnitAim measures the gun
            gunAim = UnitAim.Attach(this, o);                                                            // combatview: guns point where they shoot
            BindFortDefender();                                                                          // fort_defender_v1 DV41: on the fort roof (UnitView.FortDefender.cs)
            Sync(1f);
            UnitLookMotionGate.KeepUnitCrisp(gameObject);                                               // a living unit never blurs: hull, turret, wheels, guns, kit (parts thrown at its death are switched back by the thrower)
            if (paint && o != null && o.IsRobot) paint.Apply();                                          // the robots' team paint gain needs the bound unit: a robot built by a factory is painted before it is bound
            ExitGrowBind();
        }

        // ------------------------------------------------------------------ points for FX
        public Vector3 MuzzlePos()
        {
            if (CraneMuzzle(out var cm)) return cm;                                                    // vehicles_nb_v1 NB24: the crane's hatch driver's rifle
            if (gunAim && gunAim.TryMuzzle(out var gm)) return gm;                                     // combatview: the aimed (next) barrel
            if (muzzle) return muzzle.position;
            float h = Obj.Kind == ZKind.Robot ? 1.25f : Obj.Kind == ZKind.Cannon ? 1.3f : 1.45f, f = Obj.Kind == ZKind.Robot ? 0.55f : 1.2f;
            Vector3 fwd = Quaternion.Euler(0, AimYaw(), 0) * Vector3.forward;
            return transform.position + Vector3.up * h + fwd * f;
        }
        public Vector3 AimPos() => transform.position + Vector3.up * (Obj.Kind == ZKind.Robot ? 0.95f : 1.0f);

        float AimYaw()
        {
            var t = Obj.AttackObject;
            if (t == null || t.Destroyed) return yaw;
            var tp = game.CenterWorld(t, false); var d = tp - transform.position;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>motion_v1: vehicle hull yaw (HullSteer): follows the smoothed sim path with a dead zone, a minimum hold and a
        /// manoeuvre commit; stays still while the turret tracks (UnitAim keeps the turret's world bearing while the hull turns).</summary>
        float SteerHull(bool moving, bool engaged, float dt)
        {
            if (hullSteer == null) hullSteer = new HullSteer(yaw);
            return hullSteer.Step(Obj, moving, engaged ? AimYaw() : float.NaN, HullRate, Obj.Type == "jeep", dt);
        }

        public void OnGrenadeThrow() { throwUntil = Time.time + 1.1f; }                               // combatview: the whole 1.2 s throw (release .56 s)

        /// <summary>A shot left this unit (hitscan or missile): recoil, and the Tough's bazooka_fire.</summary>
        public void OnFire()
        {
            if (Obj != null && Obj.Kind == ZKind.Vehicle && (Obj.Type == "crane" || Obj.ApcDriverUp)) { LastFireAt = Time.time; return; }   // vehicles_nb_v1 NB24: the hatch driver's rifle (DV31): no hull jolt; DV87: the APC's lone driver in the roof hatch too
            shotAge = 0f; LastFireAt = Time.time;
            if (gunAim) { gunAim.OnFire(); HeavyBarrelKick(); }                                        // combatview: barrel recoil, spin-up, next barrel; vehicles_nb_v1 NB6: hull rocks to the barrel's side
            if (stance && stance.OnFire()) return;                                                     // robot_models_v2: the stance plays the kneel / prone / tough fire clip
            if (IsTough && anim)
            {
                fireAnimUntil = Time.time + 0.9f;
                if ((Obj.IsMoving || walkShown) && anim.layerCount > 1) { anim.CrossFadeInFixedTime("up_bazooka_fire", 0.05f, 1, 0f); upState = "up_bazooka_fire"; }   // fires on the move (take2 legs verify: the SHOWN gait, as Sync plays it)
                else { anim.speed = animMul; anim.CrossFadeInFixedTime("bazooka_fire", 0.05f, 0, 0f); state = "bazooka_fire"; }
            }
        }

        void ApplyRecoil(float dt)
        {
            shotAge += dt;
            float r = Recoil(shotAge);
            var front = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            if (weapon) weapon.localPosition = weaponRest;
            bool gunRig = gunAim && gunAim.HasRig;                                                       // combatview: UnitAim slides the barrels along their axis
            if (turret && (!gunRig || Obj.Type == "heavy")) turret.localPosition = turretRestPos;    // mlrs_heavy_v1: the heavy's turret shoves too
            if (!gunRig) foreach (var (t, rest) in barrels) if (t) t.localPosition = rest;
            if (r <= 0.001f) return;
            if (Obj.Kind == ZKind.Robot)
            {
                if (weapon) weapon.position -= (Quaternion.Euler(0, AimYaw(), 0) * Vector3.forward) * r * (IsTough ? 0.12f : 0.07f) * RecoilFeel.Weapon(Obj.Type);
                transform.position -= front * r * (IsTough ? ToughKick.BodyJolt(this, 0.06f) : Obj.Type == "pyro" ? 0.01f : 0.03f) * RecoilFeel.Body(Obj.Type);   // K307: a Tough's boots stay planted, his kick is drawn by View/ToughKick.cs   // shoulder jolt (pyro_fire_v2: a flamer barely kicks; ~4.6 drawn shots/s made it shake)
                RecoilRock(r);
                return;
            }
            var aim = Quaternion.Euler(0, turret ? turretAim : yaw, 0) * Vector3.forward;
            if (gunRig) aim = gunAim.RecoilDirection(aim);                                               // the hull jolts against the real barrel
            if (turret && !gunRig) turret.position -= aim * r * (Obj.Kind == ZKind.Cannon ? 0.2f : 0.3f);
            if (!gunRig) foreach (var (t, _) in barrels) if (t) t.position -= aim * r * 0.35f;                    // barrels recoil
            // .09 rad + .13 m back, MLRS .14 rad); the heavy dips on a damped spring and rocks back once (~5 deg at .15 s, -1.2 at .5 s)
            bool heavyHull = Obj.Type == "heavy";
            if (heavyHull && turret && gunRig) turret.position -= aim * r * 0.12f;                   // the turret shoves on its ring
            float hullTilt = heavyHull ? 5.2f * 1.5f * Mathf.Exp(-shotAge / 0.35f) * Mathf.Sin(shotAge * 9f) : r * (Obj.Type == "missile_launcher" ? 4f : 2.5f);
            float hullK = OnTower ? 1f : RecoilFeel.Hull(Obj.Type, Obj.Kind), tiltK = OnTower ? 1f : RecoilFeel.HullTilt(Obj.Type, Obj.Kind);
            transform.position -= aim * r * (heavyHull ? 0.13f : 0.08f) * hullK;                     // the hull jolts
            transform.rotation = Quaternion.AngleAxis(hullTilt * tiltK, Vector3.Cross(Vector3.up, aim)) * transform.rotation;
        }
        public void SetOwner(Team t) { if (paint && paint.Team != t) paint.Team = t; }

        // ------------------------------------------------------------------ death
        public void Die(bool fire, bool missile, Vector3 blast) => Die(fire, missile, blast, -1, 1f);

        /// <summary>deaths_mech_v1: killerId seeds the pop variant (Zod rand() % 4) and the Shares mix; proximity = how close to the
        /// blast centre (CombatView.BlastNear, 1 .. 1.6).</summary>
        public void Die(bool fire, bool missile, Vector3 blast, int killerId, float proximity = 1f)
        {
            if (death != Death.None) return;
            if (FortDefenderDropsIn()) return;                                                         // fort_defender_v1 DV41: shot while half down the well
            AnticCancel("death", true);                                                                 // robot antics: props off, the weapon back before the death pose / pop
            if (TryGetComponent(out RobotWeaponPose wpose)) wpose.BeforeDeath();                       // weapons_v2: ragdoll from the kneeling / lying pose
            if (hold) hold.Suspended = true;
            if (reactKind != 0) EndReaction();
            deathT = 0; deathFrom = transform.position;
            if (Obj != null) OzGlowFade.Begin(gameObject, Obj.Kind != ZKind.Robot);                      // its glows fade out and stay off on the body (a wreck: the team glow of its paint)
            OzDeathTrace.Died(this, fire, missile, blast);
            if (Obj.Kind == ZKind.Robot)
            {
                // deaths_mech_v1: Zod's client rule (zrobot.cpp:369-378): a splash hit in the last 1.5 s -> the body flies (ragdoll +
                // Zod's 3.5-4.4 s flight, RagdollFlight), a flame -> melt, every other kill -> one of four pop deaths (RobotPopDeath);
                // picked deterministically from the robot and killer ids (RobotDeathRule), on the blast / kill frame
                var killer = killerId >= 0 ? game.World.GetAny(killerId) : null;
                var kind = RobotDeathRule.Pick(Obj.Id, killerId, killer != null ? killer.Type : null, fire, missile, out int variant);
                if (fdMan && kind == RobotDeathKind.Pop) kind = RobotDeathKind.Ragdoll;                     // fort_defender_v1 DV41: thrown off the roof deck (its pop bits would fall through it)
                if (HeadshotDeath(blast, killerId)) { Live.Remove(this); return; }                         // DV116: a sniper's headshot: the head falls off, the body collapses (UnitView.Headshot.cs)
                uint fh = RobotDeathRule.Hash(Obj.Id, killerId, 4u);
                if (kind == RobotDeathKind.Pop && RobotPopDeath.Play(this, variant, blast, RobotDeathRule.Hash(Obj.Id, killerId, 3u)))
                {
                    Popped = true; OzDeathTrace.Mode(this, "pop " + variant);
                    Hide(); return;                                                                     // the model is gone at once
                }
                if (kind != RobotDeathKind.Melt && RobotRagdoll.CanStart() && (ragdoll = RobotRagdoll.Launch(gameObject, transform.position - blast, missile && RagdollFlight.ZodFlight ? 0.02f : RagdollStrength(missile, proximity),
                        missile ? Mathf.Clamp(proximity * 0.85f, 0.25f, 1.3f) : 1f, RobotDeathRule.Hash(Obj.Id, killerId, 9u))))
                {
                    death = Death.Ragdoll;
                    if (missile) RagdollFlight.Begin(gameObject, blast, fh, RobotHeight());
                    RagdollFlail.Begin(gameObject, missile ? 1f : 0.55f, fh);                                      // limbs flail in the air
                }
                else if (missile)
                {
                    death = Death.Airborne;
                    if (RagdollFlight.ZodFlight) { airT = 3.5f + 0.1f * (fh % 10u); airH = (1.3f + 0.01f * ((fh >> 4) % 200u)) * RagdollFlight.ApexPerRise * RobotHeight() / 3.5f; }   // Zod ERobotTurrent
                    else { airT = 1.0f + 0.04f * (fh % 10u); airH = 1.6f + 0.1f * ((fh >> 4) % 10u); }
                    var away = transform.position - blast; away.y = 0; if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0;
                    deathTo = transform.position + away.normalized * RagdollFlight.MaxLandM * (0.35f + 0.65f * ((fh >> 16) & 1023u) / 1023f);
                    deathTo = OzRockSolid.BodyLanding(transform.position, deathTo, airH, RobotHeight(), ref away);   // items 331 + 332: it lies on free ground (no rock)
                    deathTo.y = FortDefenderView.SurfaceY(new Vector3(deathTo.x, transform.position.y, deathTo.z), game.GroundY(deathTo.x, deathTo.z)) - 0.05f;   // fort_defender_v1: a roof under it
                    spinAxis = Vector3.Cross(Vector3.up, away.normalized);
                    if (anim) { anim.speed = 1; anim.CrossFadeInFixedTime("blast_airborne", 0.05f); }
                    OzBodySounds.FlightThrown(transform.position, deathTo, airT, airH, GetInstanceID());          // J15: the soft whoosh of the thrown body
                }
                else if (fire) { death = Death.Melt; if (anim) { anim.speed = 0.7f; anim.CrossFadeInFixedTime("death_side", 0.1f); } }
                else { death = Death.Anim; if (anim) { anim.speed = 1; anim.CrossFadeInFixedTime(Random.value < 0.5f ? "death_back" : "death_side", 0.1f); } }
                OzDeathTrace.Mode(this, death == Death.Ragdoll && missile && RagdollFlight.ZodFlight ? "ragdoll + Zod flight" : death.ToString());
            }
            else
            {
                BeginWreck();                                                                       // deaths_mech_v1: burn, sparks, flip / break, smoulder, fade (UnitView.Deaths.cs)
            }
            if (Obj.Kind == ZKind.Robot) Live.Remove(this);
        }

        /// <summary>Classic Z: the turret is blown up into the air, spinning, and crashes down. It lands where and when the
        /// sim's turret debris missile explodes (vlight.cpp:261-278: 40 HP r40, 3-4 s later, random spot nearby).</summary>
        void TossTurret()
        {
            ZMissile m = null;
            foreach (var x in game.World.PendingMissiles) if (x.debris && x.attackerId == Obj.Id) { m = x; break; }
            if (m == null) foreach (var x in game.World.QueuedMissiles) if (x.debris && x.attackerId == Obj.Id) { m = x; break; }   // deaths_mech_v1: died after ProcessMissiles this tick
            tossFrom = turret.position;
            if (m != null) { tossTo = ZGame.PxToWorld(m.x, m.y); tossDur = Mathf.Max(0.6f, OpenZFxPlayer.RealFlight((float)(m.explodeTime - game.World.Time), game.gameSpeed)); }   // deaths_mech_v1: sim s -> real s (lands on the sim's blast at any game speed)
            else { tossTo = transform.position + new Vector3(Random.Range(-4f, 4f), 0, Random.Range(-4f, 4f)); tossDur = 3.5f; }
            tossTo.y = game.GroundY(tossTo.x, tossTo.z) + 0.35f;
            if (OzWaterLanding.OpenWaterAt(tossTo, out float tossWater)) tossTo.y = tossWater + 0.1f;   // b27: over open water it comes down on the surface (the water landing rule takes it from there)
            tossApex = Mathf.Min(7f, 9.81f * tossDur * tossDur / 8f);                                // a real 3.5 s arc peaks at 15 m, out of the RTS view
            OzRockSolid.TurretLanding(Obj.Id, turret, tossFrom, ref tossTo, ref tossApex);                      // items 331 + 332: it lands on free ground (no rock), its arc clear of rocks
            tossed = turret; turret = null; tossed.SetParent(null, true);
            foreach (var r in tossed.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            tossAxis = Random.onUnitSphere; tossSpin = Random.Range(300f, 620f); tossT = 0; tossLanded = false;
            FxManager.I?.Explosion(tossFrom, 1.2f);
        }

        void UpdateToss(float dt)
        {
            if (!tossed) return;
            tossT += dt;
            if (tossLanded) return;                                                                     // UpdateWreck's "landed + 3 s" never came and tank wrecks stayed forever
            float k = Mathf.Clamp01(tossT / tossDur);
            var p = Vector3.Lerp(tossFrom, tossTo, k); p.y += 4f * tossApex * k * (1 - k);
            tossed.position = p;
            tossed.rotation = Quaternion.AngleAxis(tossSpin * dt, tossAxis) * tossed.rotation;
            if (k >= 1)
            {
                tossLanded = true;
                var tossRest = Quaternion.Euler(Random.Range(-35f, 35f), Random.Range(0f, 360f), Random.Range(100f, 160f));   // lands on its side
                tossRest = OzTreeCrush.TurretRest(tossed, p, tossRest, Obj);                                // item 360: it lies on the trees it fells, never through a standing one (OzTreeCrush.cs)
                tossed.rotation = OzHeavyLanding.TurretRest(tossed, p, tossRest, new Vector3((tossTo.x - tossFrom.x) / Mathf.Max(0.1f, tossDur), (tossTo.y - tossFrom.y - 4f * tossApex) / Mathf.Max(0.1f, tossDur), (tossTo.z - tossFrom.z) / Mathf.Max(0.1f, tossDur)));   // item 172: no snap: it tips onto its side, bounces a little, lies on the ground
                if (!OzWaterLanding.Enter(tossed, "turret", new Vector3((tossTo.x - tossFrom.x) / Mathf.Max(0.1f, tossDur), -4f * tossApex / Mathf.Max(0.1f, tossDur), (tossTo.z - tossFrom.z) / Mathf.Max(0.1f, tossDur)), false, false))   // b27: into water = the shared rule (splash, rings, one sound, it rests partly under the surface)
                    TossLanded(p);                                                                      // deaths_mech_v1: clang + dust + kick, only on screen
            }
        }

        public void Hide() { AnticCancel("removed", true); death = Death.Gone; Live.Remove(this); if (tossed) Destroy(tossed.gameObject); gameObject.SetActive(false); }
        void OnDestroy() { AnticCancel("destroyed", true); Live.Remove(this); if (tossed) Destroy(tossed.gameObject); StopWreckFire(0.5f); }

        void UpdateDeath(float dt)
        {
            deathT += dt;
            switch (death)
            {
                case Death.Airborne:
                {
                    float T = airT; float k = Mathf.Clamp01(deathT / T);                            // deaths_mech_v1: Zod's 3.5-4.4 s (was 1.3 s)
                    var p = Vector3.Lerp(deathFrom, deathTo, k); p.y += Mathf.Sin(k * Mathf.PI) * airH;
                    transform.position = p;
                    if (k >= 1f && deathT - dt < T) OzBodySounds.FlightLanded(p, deathFrom, deathTo, T, airH, GetInstanceID());   // J15: the metal body thud on its landing frame
                    if (k < 1) transform.rotation = Quaternion.AngleAxis(Mathf.SmoothStep(0, 1, k) * 100f, spinAxis) * Quaternion.Euler(0, yaw + modelYaw, 0);   // falls back, no spin
                    else if (deathT < T + 0.05f && anim) anim.CrossFadeInFixedTime("death_back", 0.05f);
                    if (deathT > T + 4.5f) PopAway();
                    break;
                }
                case Death.Melt:
                {
                    float k = Mathf.Clamp01(deathT / 2.7f);                          // 17 frames x 0.16 s
                    transform.localScale = new Vector3(1, Mathf.Lerp(1f, 0.12f, k), 1);
                    if (paint && k >= paint.Charred) paint.Charred = k;                      // pyro_fire_v2: the melt never un-chars a robot the fire already charred
                    if (k >= 1) Hide();
                    break;
                }
                case Death.Anim: if (deathT > 5f) PopAway(); break;
                case Death.Ragdoll:
                    if (deathT > 13f) PopAway();
                    break;
                case Death.Wreck: UpdateWreck(dt); break;                                           // deaths_mech_v1: burn, sparks, flip / break, fade (UnitView.Deaths.cs)
            }
        }

        /// <summary>
        /// the view hides.</summary>
        void PopAway()
        {
            if (popping) return;
            popping = true;
            if (ragdoll && !ragdoll.Frozen) ragdoll.Freeze();
            Vector3? ground = null;
            if (Obj.Kind == ZKind.Robot)
                foreach (var t in GetComponentsInChildren<Transform>())
                    if (t.name == "mixamorigHips") { var h = t.position; ground = new Vector3(h.x, FortDefenderView.SurfaceY(h, OzRockBody.CorpseFloor(gameObject, h, game.GroundY(h.x, h.z))), h.z); break; }   // fort_defender_v1: on a fort roof
            if (Obj.Kind != ZKind.Robot && FxManager.I) FxManager.I.Dust(transform.position + Vector3.up * 0.5f, 0.8f);
            if (tossed) PopOut.Begin(tossed, PopOut.Style.Debris, null, false, false);
            PopOut.Begin(transform, Obj.Kind == ZKind.Robot ? PopOut.Style.Corpse : PopOut.Style.Debris, Hide, false, true, ground);
        }

        void Sink(float t, float depth)
        {
            transform.position += Vector3.down * depth * Time.deltaTime / 3f;
            if (t > 3f) Hide();
        }

        // ------------------------------------------------------------------ per frame
        /// <summary>
        /// is built at the root where it is drawn this frame (placed in LateUpdate, every boot was drawn a frame's root step off its pin).
        /// Deaths / removals keep their LateUpdate handling after CombatView (a unit removed this frame is skipped here).</summary>
        public static bool SyncInUpdate = true;
        int syncedFrame = -1;
        void Update()
        {
            if (!SyncInUpdate || Obj == null || death != Death.None || Obj.Destroyed) return;
            if (OpenZEnding.Holds(Obj.Id) || game == null || game.World == null || game.World.Get(Obj.Id) != Obj) return;
            Sync(Time.deltaTime); syncedFrame = Time.frameCount;
        }

        void LateUpdate()
        {
            if (Obj == null || death == Death.Gone) return;
            if (death != Death.None) { UpdateDeath(Time.deltaTime); return; }
            if (OpenZEnding.Holds(Obj.Id)) return;                                  // endings_v1: dead in the sim, dies in the nuke's wave (OpenZEnding moves it)
            if (deathHoldUntil > Time.time) return;                                 // combat_nb_v1 NB3: dead in the sim, thrown when the blast front reaches it (BlastWave)
            if (game.World.Get(Obj.Id) != Obj) { if (OzShuttleBreakup.KeepsDrawn(this)) return; Hide(); return; }   // entered a vehicle, removed (K256h: a shot-down shuttle stays drawn until its break-up takes it)
            if (((Time.frameCount + Obj.Id) & 31) == 0) UnitLookMotionGate.KeepUnitCrisp(gameObject);     // parts added after the bind (hatch, lid, kit) stay out of the motion blur too; about twice a second per unit, spread over the frames
            if (syncedFrame != Time.frameCount) Sync(Time.deltaTime);
            if (exitGrowing) ExitGrowStep(Time.deltaTime);
        }

        void Sync(float dt)
        {
            if (Obj.Destroyed) return;
            if (towerState == 0 && Obj.Kind == ZKind.Cannon) ResolveTower();                          // crews_v1
            SetOwner(CaptureHold.Shown(Obj));                                                          // boarding_nb_v1 NB17: grey until the robot sits inside
            var p = game.CenterWorldSmooth(Obj);                                                        // parity P4: Zod client SmoothMove, no tick judder
            // OpenZ pathing (Sim/ZPathing.cs): a unit held in a queue / at a lane keeps Zod's heading (Dx/Dy) but stands: no walk
            // cycle, dust, bounce or turning in place. With the layer on the squad keeps 14 px (1.75 m) in the sim: no view slots.
            bool moving = Obj.IsMoving && !Obj.PathHeld;
            bool simSpacing = game.World.Pathing != null && game.World.Pathing.Enabled;
            // view-only squad formation: in Zod every member walks the leader's exact path on top of the others (no
            // formation offsets, spread only by ScuffleUnits when idle), so a moving squad would read as one robot.
            bool squadMoving = !simSpacing && Obj.IsMinion && Obj.IsMoving && Obj.Leader.IsMoving;
            formation = Mathf.MoveTowards(formation, squadMoving ? 1f : 0f, dt * 1.5f);
            if (formation > 0f && Obj.MemberIndex > 0)
            {
                var s = Slots[Mathf.Min(Obj.MemberIndex, Slots.Length - 1)];
                // slots turn with the leader's smoothed heading, not with each member's own (snapping) yaw
                float slotYaw = Obj.Leader != null && game.Views.TryGetValue(Obj.Leader.Id, out var lv) && lv ? lv.heading : heading;   // leader may be dead while the formation eases out; combatview: the path heading (a shooting leader may face backwards)
                p += Quaternion.Euler(0, slotYaw, 0) * new Vector3(s.x, 0, s.y) * Mathf.SmoothStep(0, 1, formation);
            }
            basePos = p;
            SolveSeparation();
            sepOffset += Vector3.ClampMagnitude(sepCorr * Mathf.Min(1f, 12f * dt), 2f * dt);          // relax the overlap, at most 2 m/s (parity P4: no fast view-only slides)
            // drift back when free (take2 legs): a robot only while it walks (a standing robot never skates); a vehicle only while
            // clear of every unit (spacing + 0.12 m), so a parked tank neither shoves standing robots nor stays ~2 m off its sim spot
            if (sepCorr.sqrMagnitude < 1e-6f && (Obj.Kind == ZKind.Robot ? Obj.IsMoving || walkShown || SettleNow() : !sepTouch))   // K420: a saved robot steps back onto its rules place (View/UnitView.Settle.cs)
                // members apart while stacked) pulled the drawn V together for ~4 s once the sim had spread it (sniper pair 0.9 m drawn vs 2.7 m sim)
                sepOffset = Vector3.MoveTowards(sepOffset, Vector3.zero, (Obj.Kind == ZKind.Robot ? 0.8f : 0.2f) * dt);
            if (sepOffset.magnitude > maxSep) sepOffset = sepOffset.normalized * maxSep;
            p += sepOffset;
            if (Obj.Kind == ZKind.Robot)
            {
                // damped, never faster than the sim); only a real teleport (> 6 m: exits, spawns) snaps
                float simSpeed = Mathf.Sqrt(Obj.Dx * Obj.Dx + Obj.Dy * Obj.Dy) / 8f;
                if (!dispInit || (disp - p).sqrMagnitude > 36f) { disp = p; dispVel = Vector3.zero; dispInit = true; lastDisp = p; bodyVel = Vector3.zero; strideSpeed = 0f; }
                else if (!walkShown && (p - disp).sqrMagnitude < StandHold * StandHold) dispVel = Vector3.zero;   // motion_v1: a standing robot does not creep with sim jostle
                else
                {   // parity P4 ("cap view smoothing at sim speed"): the spring keeps a 1.15x margin (a plain 1.0x maxSpeed settles a few %
                    // BELOW the sim speed, so the view would fall further behind the longer a unit walks) and each frame's step is
                    // clamped to ViewSpeedCap x max(own Zod speed, current sim speed) x gameSpeed (dt is real time; at 2x / 4x the sim
                    // covers 2x / 4x per real second): the drawn robot never outruns the sim and never falls behind into a 6 m snap
                    float cap = Mathf.Max(Obj.MoveSpeed / 8f, simSpeed) * Mathf.Max(0.25f, game.gameSpeed); var prev = disp;
                    disp = Vector3.SmoothDamp(disp, p, ref dispVel, 0.16f, cap * 1.15f, dt);
                    disp = prev + Vector3.ClampMagnitude(disp - prev, cap * ViewSpeedCap * dt);
                }
                p = disp;
                // motion_v1: what the drawn body really did this frame (after the P4 cap): low-passed for the facing, slew-limited for the stride
                var step = disp - lastDisp; step.y = 0f; lastDisp = disp;
                float stepSpeed = dt > 0f ? step.magnitude / dt : 0f;
                if (dt > 0f) bodyVel = Vector3.Lerp(bodyVel, step / dt, 1f - Mathf.Exp(-dt / BodyTau));
                strideSpeed = Mathf.MoveTowards(strideSpeed, stepSpeed, StrideSlew * dt);
                // take2 legs: walk while the drawn robot visibly moves (sim step, squad slot, spacing push), stand while it stands;
                // hysteresis (0.3 / 0.15 m/s) + dwell (stand >= 0.25 s, walk >= 0.45 s = one step); a robot that stops to shoot stands at once
                // motion_v1: the drawn speed alone decides (the old sim term `simWalk && shown > 0.05` kept the legs cycling on a held body)
                float shown = strideSpeed; bool simWalk = moving && Obj.MoveSpeed > 0;
                bool aiming = !simWalk && Obj.AttackObject != null && !Obj.AttackObject.Destroyed;
                bool wantWalk = walkShown ? shown > WalkOff : shown > WalkOn;
                gaitT += dt;
                if (wantWalk != walkShown && (gaitT >= (walkShown ? MinWalk : MinStand) || (!wantWalk && aiming))) { walkShown = wantWalk; gaitT = 0f; }
                if (!walkShown) runShown = false;
            }

            bool engaged = Obj.AttackObject != null && !Obj.AttackObject.Destroyed;
            bool vehicle = Obj.Kind == ZKind.Vehicle;
            float speed01 = moving && Obj.MoveSpeed > 0 ? Mathf.Clamp01(Mathf.Sqrt(Obj.Dx * Obj.Dx + Obj.Dy * Obj.Dy) / (Obj.MoveSpeed * 1.689f)) : 0f;
            var moveDir = moving ? new Vector3(Obj.Dx, 0, -Obj.Dy).normalized : Vector3.zero;
            var bodyDir = new Vector3(bodyVel.x, 0f, bodyVel.z);
            bool bodyGoes = Obj.Kind == ZKind.Robot && walkShown && bodyDir.sqrMagnitude > FaceMin * FaceMin;   // motion_v1: the drawn robot visibly walks
            var legDir = bodyGoes ? bodyDir.normalized : moving || !walkShown ? moveDir : new Vector3(dispVel.x, 0f, dispVel.z).normalized;   // take2 legs; motion_v1: where the drawn body goes
            prevYaw = yaw;
            bool anticFaces = antic != null && antic.OwnsFacing;                                     // fishing / the lounger: the antic alone turns the robot (AnticFace)
            if (anticFaces) { }
            else if (vehicle) { yaw = ExitHull(SteerHull(moving, engaged, dt), dt); p = NoCrab(p, moving, speed01, moveDir, dt); ExitHoldSlip(); }   // vehicles_nb_v1 NB21/22: steers into sideways moves, never glides;                                     // motion_v1: path-following hull (dead zone, hold, commit); the turret tracks
            else if (bodyGoes)
            {   // motion_v1: a walking robot faces where its drawn body goes (low-passed), not the sim's one-tick sidestep / re-aim headings
                float to = Mathf.Atan2(legDir.x, legDir.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, to)) > FaceDead) yaw = Mathf.MoveTowardsAngle(yaw, to, HullRate * dt);
            }
            else if (moving) yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg, HullRate * dt);
            else if (walkShown && !engaged && dispVel.sqrMagnitude > 0.09f)                         // take2 legs: eased into place = steps there
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(legDir.x, legDir.z) * Mathf.Rad2Deg, HullRate * dt);
            else if (!gunAim && engaged && (Obj.Kind == ZKind.Robot || (Obj.Kind == ZKind.Cannon && !turret)))
                yaw = Mathf.MoveTowardsAngle(yaw, AimYaw(), TurnRate * 0.6f * dt);        // robots and turret-less cannons turn to shoot
            if (!anticFaces && gunAim && (Obj.Kind == ZKind.Robot || (Obj.Kind == ZKind.Cannon && !turret)))     // combatview: face the shot fast and eased
                yaw = gunAim.AdjustBodyYaw(yaw, Mathf.Atan2(legDir.x, legDir.z) * Mathf.Rad2Deg, Obj.IsMoving || walkShown, dt);   // take2 legs: legDir / walkShown
            heading = Obj.IsMoving ? Mathf.MoveTowardsAngle(heading, Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg, HullRate * dt) : yaw;   // combatview: formation follows the path
            float yawRate = dt > 0 ? Mathf.DeltaAngle(prevYaw, yaw) / dt : 0f;

            if (vehicle)
            {
                if (Obj.Type == "jeep" && moving && !(VehicleSlip.Enabled && slip != null))                     // vehicles_nb_v1 NB22: NoCrab steers the weave
                {   // jeeps race: a light weave across the path and a bouncy suspension
                    swerve += dt * 2.3f;
                    p += Vector3.Cross(Vector3.up, moveDir) * Mathf.Sin(swerve) * 0.22f * speed01;
                }
                // sit on the slope: ground normal from four samples around the hull
                var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var right = Vector3.Cross(Vector3.up, fwd);
                float hf = game.GroundY(p.x + fwd.x * 1.5f, p.z + fwd.z * 1.5f), hb = game.GroundY(p.x - fwd.x * 1.5f, p.z - fwd.z * 1.5f);
                float hr = game.GroundY(p.x + right.x * 1.1f, p.z + right.z * 1.1f), hl = game.GroundY(p.x - right.x * 1.1f, p.z - right.z * 1.1f);
                float hc = game.GroundY(p.x, p.z);                                                  // off a deck edge / bank step: ignore that sample
                if (Mathf.Abs(hf - hc) > 0.7f) hf = hc; if (Mathf.Abs(hb - hc) > 0.7f) hb = hc; if (Mathf.Abs(hr - hc) > 0.7f) hr = hc; if (Mathf.Abs(hl - hc) > 0.7f) hl = hc;
                var n = Vector3.Cross(fwd * 3f + Vector3.up * (hf - hb), -(right * 2.2f + Vector3.up * (hr - hl))).normalized;
                if (n.y < 0) n = -n;
                tilt = Quaternion.Slerp(tilt, Quaternion.FromToRotation(Vector3.up, n), 1f - Mathf.Exp(-8f * dt));
                p.y = Mathf.Max((hf + hb + hr + hl) * 0.25f, hc) + yOffset;   // wheels on top, never inside a bump
                float speedMs = moving ? Mathf.Sqrt(Obj.Dx * Obj.Dx + Obj.Dy * Obj.Dy) / 8f : 0f;      // px/s -> m/s
                if (slip != null && VehicleSlip.Enabled) speedMs = slip.AlongSpeed; else                    // vehicles_nb_v1 NB21: wheels / tracks roll with the drawn motion
                if (hullSteer != null && hullSteer.Reversing) speedMs = -speedMs;                         // motion_v1: backing up: wheels / tracks roll back
                var feel = motion ? motion.Step(speedMs, yawRate, dt) : Quaternion.identity;
                if (motion) p.y += motion.Bounce * speed01;
                if (moving && FxManager.I && !DustTrails.Active && !Obj.IsShuttle && (dustT -= dt) <= 0)   // tracks package: DustTrails owns surface-aware dust
                {
                    dustT = Obj.Type == "jeep" ? 0.12f : 0.3f;
                    FxManager.I.Dust(p - moveDir * 1.8f + Vector3.up * 0.2f, Obj.Type == "jeep" ? 0.35f : 0.28f);
                }
                transform.position = p;
                transform.rotation = tilt * Quaternion.Euler(0, yaw + modelYaw, 0) * feel;
            }
            else
            {
                if (towerState > 0 && towerFort)                                                       // crews_v1: the gun's turn axis on the pad centre, on the ring mount
                    p = TowerMounts.SeatRoot(towerFort, towerPad, Quaternion.Euler(0, yaw + modelYaw, 0), towerAxis, Mathf.Abs(transform.lossyScale.x));   // tower_gun_fix: the shared rule, same maths
                else if (!PlaceFortDefender(ref p)) { p = GrabKeepOut(p, dt); p.y = game.GroundY(p.x, p.z) + yOffset; }
                transform.position = p;
                transform.rotation = Quaternion.Euler(0, yaw + modelYaw, 0);
                if (reactKind != 0) ApplyReaction(dt);                                               // deaths_mech_v1: knockdown / stagger / flinch (view only)
            }
            if (turret && !(gunAim && gunAim.HasRig))                                                 // combatview: UnitAim solves yaw + pitch on the muzzle
            {
                float want = engaged ? AimYaw() : yaw;
                if (!turretInit) { turretAim = want; turretInit = true; }
                if (!Obj.UnmannedHeld) turretAim = Mathf.MoveTowardsAngle(turretAim, want, TurretRate * dt);   /* DV79 unmanned stop: nobody inside, the turret stays where it points */                     // turrets slew instead of snapping
                turret.rotation = Quaternion.AngleAxis(turretAim - yaw, transform.up) * (transform.rotation * turretRest);
            }
            ApplyRecoil(dt);
            if (Obj.Kind == ZKind.Robot && skin)
            {
                // carries the armature's 1/60 scale, so the band can't be measured in object space)
                bandBlock ??= new MaterialPropertyBlock();
                skin.GetPropertyBlock(bandBlock, 0);
                float baseY = transform.position.y;
                if (bandH <= 0f)
                {
                    float top = float.NegativeInfinity;
                    foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) top = Mathf.Max(top, r.bounds.max.y);
                    bandH = float.IsInfinity(top) ? 3.5f : Mathf.Clamp(top - baseY, 1.2f, 4.5f);
                }
                bandBlock.SetFloat(AccentLoId, baseY + 0.55f * bandH); bandBlock.SetFloat(AccentHiId, baseY + 0.98f * bandH);   // BS-01 (3.5 m): 1.93..3.43 m as before
                skin.SetPropertyBlock(bandBlock, 0);
            }

            if (!anim) return;
            EnsurePhase();
            if (AnticAnim(dt)) return;                                                                // robot antics: idle fun moves (UnitView.Antics.cs); cancels on the same frame
            if (FortDefenderAnim(dt)) return;                                                         // fort_defender_v1 DV41: climbing out of / into the hatch
            if (GrabAnim(dt)) return;
            string wantUp = null;
            if (hold) hold.Suspended = Time.time < throwUntil;
            if (stance && stance.Drive(engaged, walkShown && (Obj.MoveSpeed > 0 || fdMan), Time.time < throwUntil, ref state, animMul, dt))
            {   // robot_models_v2: a kneeling / prone sniper, a preparing / aiming / firing Tough or a firing psycho owns the base layer; the upper-body layer eases out
                if (anim.layerCount > 1) { upWeight = Mathf.MoveTowards(upWeight, 0f, dt * 5f); anim.SetLayerWeight(1, upWeight); }
                return;
            }
            if (ReactAnim(dt)) return;                                                              // deaths_mech_v1: knocked down: the reaction owns the pose
            float speedWas = anim.speed;
            if (Time.time < throwUntil) { SetState("grenade_throw", 0.08f); anim.speed = 1; }
            else if (walkShown && (Obj.MoveSpeed > 0 || fdMan))
            {
                // motion_v1: run while the sim runs; a held / creeping moment keeps the last gait (no walk <-> run flicker)
                if (Obj.IsMoving) runShown = (Obj.Leader ?? Obj).IsRunning || Obj.FortRushOn || Obj.YieldRunOn;                  // boarding_nb_v1 DV34: rushing a fort door
                bool running = runShown;
                // motion_v1: stride = the drawn body's real speed (= Zod's SpeedOffsetPercent while it keeps up with the sim: ground,
                // road, running); the sim velocity stays set while the pathing layer queues / holds the unit, so it slid the feet
                // speed (MoveSpeed / 8 = 1.75 m/s for a grunt), so a robot walking 2.6 m/s played the clip at 1.3x and every planted
                // boot skated forward ~1.5 m/s: the clip rate now follows the drawn speed over the clip's own stride (x model scale)
                float native = anim.isHuman ? WalkStrideHu * anim.humanScale * Mathf.Max(0.1f, anim.transform.lossyScale.y) : WalkStrideMps * Mathf.Max(0.1f, transform.lossyScale.y);
                float speedPct = Mathf.Clamp(strideSpeed / native, 0.35f, 3.2f);
                SetState(gunAim ? gunAim.Locomotion(running, yaw, Mathf.Atan2(legDir.x, legDir.z) * Mathf.Rad2Deg) : running ? "run_forward" : "walking", 0.15f);   // combatview: strafe / back-pedal while shooting
                anim.speed = (running ? speedPct / 1.8f : speedPct) * animMul;
                wantUp = IsTough ? (Time.time < fireAnimUntil ? "up_bazooka_fire" : "up_bazooka_idle") : engaged ? "up_shooting" : "aim_hold";
            }
            else if (IsTough && Time.time < fireAnimUntil && state == "bazooka_fire") { }         // let bazooka_fire play out (take2 legs verify: only while the base layer plays it, never walking legs on a standing Tough)
            else if (engaged) { SetState(IsTough ? "bazooka_idle" : "shooting", 0.12f); anim.speed = animMul * (IsTough ? 1f : ShootRate()); }   // parity P6: one loop per Zod fire cycle
            else { SetState(IsTough ? "bazooka_idle" : "idle", 0.2f); anim.speed = animMul; }
            // state, so the moment a robot stopped, the walk clip still fading out jumped from its 0.35x stride speed to the idle's 1x
            // and the fading legs snapped forward. Speed changes now ease at 4 / s (a 0.35 -> 1 change takes 0.16 s); throws keep 1x.
            if (Time.time >= throwUntil && Mathf.Abs(anim.speed - speedWas) > 1e-4f) anim.speed = Mathf.MoveTowards(speedWas, anim.speed, 4f * dt);
            if (anim.layerCount > 1)
            {
                upWeight = Mathf.MoveTowards(upWeight, wantUp != null ? 1f : 0f, dt * 5f);             // eased in/out, no pop
                anim.SetLayerWeight(1, upWeight);
                if (wantUp != null && wantUp != upState && !(upState == "up_bazooka_fire" && Time.time < fireAnimUntil))
                { anim.CrossFadeInFixedTime(wantUp, 0.15f, 1, animOffset); upState = wantUp; }
            }
        }

        /// <summary>crews_v1: is this cannon on a fort turret spot? Then it keeps its pad for good: no view spacing (it stands on
        /// the fort, 9 m above everything), the tower size (0.72, OpenZSetup) for one placed from the fort's store at run time,
        /// and the gun's rest front turned outward from the fort. Retries while the fort's static view is not bound yet.</summary>
        void ResolveTower()
        {
            int s = TowerMounts.Resolve(game, Obj, out towerFort, out towerPad);
            if (s == 0 && ++towerTries < 120) return;
            towerState = s > 0 && towerFort ? 1 : -1;
            if (towerState < 0) { towerFort = null; return; }
            radius = 0f; weight = 0f; sepOffset = Vector3.zero;
            if (TryGetComponent<TeamPaint>(out var tp)) tp.FortPalette = true;                        // TG2: the fort's palette, not the field khaki
            if (transform.localScale.x > 0.9f) transform.localScale *= TowerMounts.CannonScaleFor(Obj.Type);   // a missile cannon smaller: its launcher inside the rim
            yaw = TowerMounts.OutwardHeading(towerFort, towerPad) + TowerMounts.RestYawOffset(Obj.Type) - modelYaw;
            heading = yaw; turretInit = false;
            transform.rotation = Quaternion.Euler(0, yaw + modelYaw, 0);
            // its root, the imported turrets 0-0.42 m beside it), in root units, y ignored
            towerAxis = TowerMounts.FootprintCentreLocal(transform);                                   // crews_v2: the base plate's centre on the pad centre
        }

        /// <summary>The view spacing follows the sim's run-over rule (A/B switch for checks; true = the rule below).</summary>
        public static bool SpacingFollowsRunOver = true;
        static bool DeadRobot(ZObject o) => o.Kind == ZKind.Robot && (o.Destroyed || o.KillAt >= 0);
        /// <summary>Pairs the view spacing leaves alone because the sim does: a moving crewed vehicle and the enemy robot it drives
        /// into / over (ZWorld.RunOverPair, exempt from the sim's spacing gate in ZPathing.PairExempt), and a robot that is already
        /// dead in the sim (its body is no obstacle). Without this the drawn vehicle was shoved back and sideways by every robot it
        /// ran over (up to 0.7 m at 2 m/s, drifting back at 0.2 m/s) while the sim drove straight on, and the knocked-down robot
        /// slid out from under the hull. View only: sim positions are never touched.</summary>
        static bool SimLetsThrough(ZObject a, ZObject b) =>
            SpacingFollowsRunOver && (DeadRobot(a) || DeadRobot(b) || ZWorld.RunOverPair(a, b) || ZWorld.RunOverPair(b, a));

        /// <summary>View-side spacing: overlapping units (robots 0.42 m, vehicles 1.75 m, cannons 1.5 m radius) push apart,
        /// cannons and wrecks never move, vehicles move less than robots. Runs once per frame for all views.</summary>
        static void SolveSeparation()
        {
            if (lastSolve == Time.frameCount) return;
            lastSolve = Time.frameCount;
            Live.RemoveAll(v => !v);
            if (sepOn.Length < Live.Count) sepOn = new bool[Mathf.NextPowerOfTwo(Live.Count + 1)];
            for (int q = 0; q < Live.Count; q++)
            {
                // ~7000 calls a frame with 120 views); nothing in the pair loop changes a view's active state or its Obj
                var v = Live[q]; v.sepCorr = Vector3.zero; v.sepTouch = false; sepOn[q] = v.isActiveAndEnabled && v.Obj != null;
            }
            for (int i = 0; i < Live.Count; i++)
            {
                var a = Live[i]; if (!sepOn[i]) continue;
                var pa = a.basePos + a.sepOffset;
                for (int j = i + 1; j < Live.Count; j++)
                {
                    var b = Live[j]; if (!sepOn[j]) continue;
                    float r = a.radius + b.radius;
                    var d = pa - (b.basePos + b.sepOffset); d.y = 0;
                    float sq = d.sqrMagnitude, rt = r + 0.12f; if (sq >= rt * rt) continue;
                    if (SimLetsThrough(a.Obj, b.Obj) || AirPair(a.Obj, b.Obj)) continue;                   // run-over pairs and dead robots: no view push either; K256j (S6): nor a shuttle up in the air (UnitView.AirPair.cs)
                    a.sepTouch = b.sepTouch = true;                                                        // take2 legs verify: close enough that drifting back would push
                    if (sq >= r * r) continue;
                    float wa = PairWeight(a, b), wb = PairWeight(b, a); if (wa + wb <= 0f) continue;   // vehicles_nb_v1 NB21/22: robots never shove a standing vehicle
                    float dist = Mathf.Sqrt(sq);
                    var dir = dist > 1e-4f ? d / dist : Quaternion.Euler(0, (a.Obj.Id * 137 + b.Obj.Id * 71) % 360, 0) * Vector3.forward;
                    float need = r - dist;
                    a.sepCorr += dir * need * wa / (wa + wb);
                    b.sepCorr -= dir * need * wb / (wa + wb);
                }
                if (a.Obj.Kind == ZKind.Robot && a.weight > 0f)
                    for (int k = 0; k < Obstacles.Count; k++)
                    {   // lamp posts: a robot keeps its body clear of the pole
                        var o = Obstacles[k]; if (o.z <= 0f) continue;
                        float dx = pa.x - o.x, dz = pa.z - o.y, rr = o.z + a.radius * 0.7f;
                        float sq = dx * dx + dz * dz; if (sq >= rr * rr) continue;
                        float dist = Mathf.Sqrt(sq); var dir = dist > 1e-4f ? new Vector3(dx / dist, 0, dz / dist) : Vector3.right;
                        a.sepCorr += dir * (rr - dist); a.sepTouch = true;
                    }
                if (a.weight > 0f && a.Obj.Kind != ZKind.Cannon && !a.Obj.PathInBuildingStage && !a.ExitInside() && a.game != null && a.game.World != null)
                {   // los_clearance_v1 (DV22): view-side spacing never slides a model into a building's mesh tiles or a prop (view only)
                    var cl = a.game.World.Clearance;
                    if (cl != null && cl.Enabled && cl.PushOut(pa.x * 8.0, -pa.z * 8.0, a.Obj.Kind == ZKind.Robot ? System.Math.Max(a.IsTough ? 6.0 : 5.0, a.clearPx) /* robot_models_v2 review: v3 body */ : 14.0, a.Obj.PathSoftPass, out double cox, out double coy))   /* pathing v3 DV47: only the debris tiles skipped */
                    { a.sepCorr += new Vector3((float)(cox / 8.0), 0f, (float)(-coy / 8.0)); a.sepTouch = true; }
                }
            }
        }

        /// <summary>
        /// most this x max(its own Zod move speed, its current sim speed incl. road / running) towards its sim position (squad slot
        /// </summary>
        public static float ViewSpeedCap = 1f;

        /// <summary>parity P6: the standing `shooting` loop (Robot/shooting.anim 0.2 s, BS-01 Humanoid clip 5 frames) slowed so one
        /// loop lasts one Zod client fire cycle (grunt 0.998 s, sniper 0.778, laser 0.529; psycho / pyro stay 1x). The walking
        /// branch (take2 legs gait) keeps its stride speed. Off = the old 1x kick loop.</summary>
        public static bool ZodShootCadence = true;
        float shootRate = -1f;
        float ShootRate()
        {
            if (!ZodShootCadence) return 1f;
            if (shootRate < 0f)
            {
                float len = 0.2f;
                var rc = anim ? anim.runtimeAnimatorController : null;
                if (rc) foreach (var c in rc.animationClips) if (c && c.name == "shooting") { len = c.length; break; }
                shootRate = ZodClientFire.ShootClipRate(Obj.Type, len);
            }
            return shootRate;
        }

        void SetState(string s, float fade)
        {
            if (state == s) return;
            string from = state;
            state = s;
            if (fade <= 0) { anim.Play(s, 0, Random.value); phaseSet = anim.isInitialized; }
            else if (s == "idle" || s == "bazooka_idle")                                          // long idle loops: a normalised golden-ratio phase per robot, so a squad never idles in step
                anim.CrossFade(s, Mathf.Clamp(IdleFade * Mathf.Max(1f, Mathf.Abs(anim.speed)) / Mathf.Max(0.05f, anim.GetCurrentAnimatorStateInfo(0).length), 0.02f, 1f), 0,   // legs_rootcause: a real 0.2 s settle (0.08 of the
                    Mathf.Repeat((Obj != null ? Obj.Id : 0) * 0.6180339f, 1f));                             // 0.2 s shooting loop was 16 ms: a one-frame leg snap); normalised target offset
            else if (IsGait(from) && IsGait(s) && anim.isInitialized)
            {   // legs_rootcause: walk <-> run <-> strafe / back-pedal keeps the step phase (walking_legfix and run_forward_legfix put the
                // left foot forward at 0.785 / 0.781 of their cycles); restarting at animOffset threw the feet up to half a step
                var cur = anim.GetCurrentAnimatorStateInfo(0);
                var shown = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : cur;
                anim.CrossFade(s, Mathf.Clamp(fade / Mathf.Max(0.1f, cur.length), 0.02f, 0.5f), 0, Mathf.Repeat(shown.normalizedTime, 1f));
            }
            else anim.CrossFadeInFixedTime(s, fade, 0, animOffset);   // own phase per robot
        }
        static bool IsGait(string s) => s == "walking" || s == "run_forward" || s == "run_backward" || s == "strafe_left" || s == "strafe_right";
        /// <summary>legs_rootcause: seconds of the cross-fade into idle / bazooka_idle (was 0.08 of the source state's length).</summary>
        public static float IdleFade = 0.2f;
        bool phaseSet;
        /// <summary>Bind runs before the Animator is initialised, so its random start phase was lost and whole squads idled in
        /// </summary>
        void EnsurePhase()
        {
            if (phaseSet || !anim || !anim.isInitialized || string.IsNullOrEmpty(state)) return;
            phaseSet = true;
            anim.Play(state, 0, Mathf.Repeat(animOffset * 1.7f + (Obj != null ? Obj.Id * 0.137f : Random.value), 1f));
        }
    }
}

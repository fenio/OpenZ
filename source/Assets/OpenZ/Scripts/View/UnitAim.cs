using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// One per unit view, added by UnitView.Bind.
    ///
    /// Zod has no turn-before-fire delay: the server fires on its cooldown only (zobject.cpp:2102-2213 never reads a
    /// direction) and the client SNAPS the turret / robot direction to the target (zvehicle.cpp:140-153 SetAttackObject,
    /// vlight.cpp:119-126, cgatling.cpp:193-197 + 246-258, zcannon.cpp:30-43, zrobot.cpp:139-143). So the sim stays 1:1
    /// and the view does the work:
    ///  * pre-aim: the gun tracks the sim's AttackObject, the target of an Attack / Agro waypoint, or the exact unit the
    ///    sim's next passive check will engage (first enemy in World.Objects order within attack range, like
    ///    CheckPassiveEngage, zobject.cpp:1603-1853), so the barrel is already on target when the first shot comes;
    ///  * a shot that arrives while the gun is still off target is HELD (UnitAim.HoldShot from CombatView) and replayed
    ///    through CombatView.Replay the moment the muzzle is aligned (or after a short cap), so every tracer, flash,
    ///    rocket and shell leaves the real muzzle toward the target; missiles fly the remaining flight time so the sim's
    ///    detonation time is untouched;
    ///    pitch joint about the barrel's hinge, 3 iterations) with an eased slew per unit type; the pitch follows range
    ///    OpenZFxPlayer.Launch, which flies the sim delay / gameSpeed), recoil
    ///    kick, gatling / jeep / mech barrels spin up while firing and spin down after, MLRS rockets leave the rack;
    ///  * robots: the body turns to the target fast and eased (UnitView asks AdjustBodyYaw); the torso (Spine, Spine1,
    ///    elevation, 3 iterations) and WeaponHold (order 80) lays the rifle / bazooka exactly along TryAimDirection (yaw +
    ///    pitch, ballistic for rockets); without WeaponHold the solve points the weapon muzzle itself; moving robots that
    ///    are shooting walk forward with a torso twist, or strafe / back-pedal when the target is behind (Locomotion);
    ///  * idle turrets step around like Zod's (45 deg per second, APC 0.2 s: vlight.cpp:119-134, cgatling.cpp:229-236).
    /// Runs in LateUpdate after UnitView (60) placed the unit and after the Animator wrote the pose.
    /// </summary>
    [DefaultExecutionOrder(62)]
    public partial class UnitAim : MonoBehaviour                                                    // KH2: + View/UnitAim.Hero.cs (the heroes' torso and face)
    {
        /// <summary></summary>
        public static int MlrsAxisFixed;
        /// <summary>Zod's idle turret scan (45 deg steps). Off = idle turrets face the hull front.</summary>
        public static bool IdleScan = true;
        /// <summary>True while a held shot is replayed through CombatView (so it is not held twice).</summary>
        public static bool Replaying { get; private set; }

        // ------------------------------------------------------------------ tuning
        public struct Slew
        {
            public float yawSpeed, yawSmooth, pitchSpeed, pitchSmooth;   // deg/s cap and SmoothDamp time (s): fast, eased
            public float pitchMin, pitchMax;                             // elevation limits (deg) relative to the modelled rest pose
            public float tol;                                            // aligned when the muzzle is within tol deg of the shot
            public float kickDeg, recoilK, spinMax;                      // muzzle climb, recoil slide factor, barrel spin (deg/s)
        }

        public static Slew SlewFor(string type, ZKind kind)
        {
            switch (type)
            {
                case "jeep": return new Slew { yawSpeed = 540, yawSmooth = 0.06f, pitchSpeed = 240, pitchSmooth = 0.06f, pitchMin = -12, pitchMax = 35, tol = 5, kickDeg = 2f, recoilK = 0.24f, spinMax = 1080 };
                case "gatling": return new Slew { yawSpeed = 420, yawSmooth = 0.07f, pitchSpeed = 200, pitchSmooth = 0.07f, pitchMin = -10, pitchMax = 30, tol = 5, kickDeg = 1f, recoilK = 0.1f, spinMax = 1440 };
                case "light": return new Slew { yawSpeed = 240, yawSmooth = 0.10f, pitchSpeed = 90, pitchSmooth = 0.08f, pitchMin = -10, pitchMax = 30, tol = 4, kickDeg = 5.2f, recoilK = 0.66f };
                case "medium": return new Slew { yawSpeed = 260, yawSmooth = 0.09f, pitchSpeed = 140, pitchSmooth = 0.08f, pitchMin = -45, pitchMax = 60, tol = 5, kickDeg = 3.4f, recoilK = 0.24f, spinMax = 900 };
                case "heavy": return new Slew { yawSpeed = 150, yawSmooth = 0.14f, pitchSpeed = 60, pitchSmooth = 0.10f, pitchMin = -10, pitchMax = 25, tol = 4, kickDeg = 7f, recoilK = 0.66f };
                case "missile_launcher": return new Slew { yawSpeed = 160, yawSmooth = 0.12f, pitchSpeed = 70, pitchSmooth = 0.10f, pitchMin = -20, pitchMax = 57, tol = 6, kickDeg = 8f, recoilK = 0f };
                case "gun": return new Slew { yawSpeed = 220, yawSmooth = 0.10f, pitchSpeed = 90, pitchSmooth = 0.08f, pitchMin = -15, pitchMax = 40, tol = 4, kickDeg = 3.4f, recoilK = 0.38f };
                case "howitzer": return new Slew { yawSpeed = 130, yawSmooth = 0.14f, pitchSpeed = 45, pitchSmooth = 0.12f, pitchMin = -25, pitchMax = 22, tol = 4, kickDeg = 3.4f, recoilK = 0.38f };
                case "missile_cannon": return new Slew { yawSpeed = 160, yawSmooth = 0.12f, pitchSpeed = 60, pitchSmooth = 0.10f, pitchMin = -30, pitchMax = 30, tol = 6, kickDeg = 3f, recoilK = 0f };
            }
            if (kind == ZKind.Robot && type == "sniper" && WeaponsV2.Enabled) return new Slew { yawSpeed = 300, yawSmooth = 0.15f, pitchSpeed = 220, pitchSmooth = 0.12f, pitchMin = -45, pitchMax = 35, tol = 5 };   // weapons_v2
            if (kind == ZKind.Robot) return new Slew { yawSpeed = 720, yawSmooth = 0.07f, pitchSpeed = 400, pitchSmooth = 0.06f, pitchMin = -45, pitchMax = 35, tol = 10 };
            return new Slew { yawSpeed = 200, yawSmooth = 0.10f, pitchSpeed = 80, pitchSmooth = 0.08f, pitchMin = -15, pitchMax = 35, tol = 5, kickDeg = 3.4f, recoilK = 0.24f, spinMax = 900 };
        }

        /// <summary>Gravity of the visual projectile, so the barrel elevation matches the arc the shell really flies: rockets 6,
        /// (OpenZFxCatalog.GravityOf: Bazooka / Rocket / Mlrs 6, else 9.8). The howitzer uses 9.8 too:
        /// its 95 px/s shell then climbs 19-41 deg over its 10-25 m range, inside its pitch limits (rest 42 deg, -25 / +22).</summary>
        public static float ProjectileGravity(string type, bool rocket) => rocket ? 6f : 9.8f;

        static bool IsRocket(string t) => t == "tough" || t == "light" || t == "missile_launcher" || t == "missile_cannon";   // CombatView.MissileFired

        /// <summary>Model yaw for UnitView (front direction offset). The imported gatling / howitzer / missile cannon face +x
        /// (a cannon that was not split into a turret).</summary>
        public static float ModelYaw(GameObject go, ZObject o, float def)
        {
            if (o == null || o.Kind != ZKind.Cannon) return def;
            if (o.Type != "gatling" && o.Type != "howitzer" && o.Type != "missile_cannon") return def;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("TurretYaw")) return def;   // split: the base never turns
            return 270f;
        }

        // ------------------------------------------------------------------ state
        sealed class Joint { public Transform t; public Quaternion rest; public Vector3 axisL; public float ang, vel; }
        sealed class Muzzle { public Transform t; public Vector3 dirL; public Transform recoil, rocket; public Renderer[] rocketR; public bool pitched; public float reloadAt = -1; public Vector3 rocketRest, rocketBackL; public float slideAt = -1; }   // mlrs_heavy_v1: + reload slide

        UnitView view; ZObject obj; ZGame game; Slew slew; string wtype;
        Slew vetBaseSlew; bool vetSlewOn;                                                  // veterancy_v1 DV12: quicker aim (view only)
        Joint yawJ, pitchJ;
        Transform spin; Quaternion spinRest; Vector3 spinAxisL; float spinAngle, spinSpeed;
        readonly List<Muzzle> muzzles = new List<Muzzle>();
        readonly List<(Transform t, Vector3 rest, Vector3 backL)> recoils = new List<(Transform, Vector3, Vector3)>();
        int slot, firedSlot; bool advance;
        readonly Dictionary<Transform, float> recoilAge = new Dictionary<Transform, float>();   // vehicles_nb_v1 NB6: each barrel's own recoil clock
        float pitchSign = 1f, shotAge = 10f, lastShot = -10f; bool pitchAtLimit;
        float idleYaw, nextIdleStep; bool wasIdle = true;
        float bodyYawNow, bodyVel; int bodyFrame = -100;
        float lastHullYaw; bool hullYawInit;                                                        // motion_v1: turret holds its world bearing while the hull turns

        bool robot; Transform[] spine; Quaternion[] animated, written; readonly float[] spineW = { 0.25f, 0.35f, 0.40f };
        Transform rMuzzle; Vector3 rDirL; float tYaw, tPitch, tYawVel, tPitchVel; Vector3 hinge = Vector3.right; bool faceTarget;
        Quaternion chestDelta = Quaternion.identity;                                              // torso aim rotation on top of the clip (Spine2, world)
        Vector3 aimDir, chestFront; bool hasAimDir; int posedFrame = -100;                          // WeaponHold poses the weapon after us
        float throwUntil; Vector3 throwPoint;

        ZObject predicted; float nextScan;
        Vector3 holdPoint; float holdUntil; bool holdBallistic; float holdT, holdG;

        struct Held { public ZEvent e; public Vector3 point; public float at, maxWait; public bool ballistic, grenade; public float T, g; }
        readonly List<Held> held = new List<Held>(); float nextRelease;
        static CombatView combat;

        public static int StatShots, StatHeld, StatTimeouts; public static float StatMaxError, StatSumError;
        public int Shots { get; private set; }
        public int HeldCount { get; private set; }
        public float MaxHoldMs { get; private set; }
        public float MaxFireError { get; private set; }
        /// <summary>
        /// staged line-up (squad members included) from the level's other units instead of mixing both in one header.</summary>
        public class ShotStat { public string Type, WorstHow = ""; public Team Owner; public ZKind Kind; public int Shots, Held, Timeouts, Snaps; public float MaxHoldMs, MaxErr, SumErr; }
        public static readonly Dictionary<int, ShotStat> StatById = new Dictionary<int, ShotStat>();
        public static string StatWorstWho = "";
        ShotStat MyStat()
        {
            ShotStat st; if (!StatById.TryGetValue(obj.Id, out st)) StatById[obj.Id] = st = new ShotStat { Type = obj.Type, Owner = obj.Owner, Kind = obj.Kind };
            return st;
        }
        public static void ResetStats() { StatShots = StatHeld = StatTimeouts = StatSnaps = 0; StatMaxError = StatSumError = 0f; StatById.Clear(); StatWorstWho = ""; }
        void Count(float err, float heldFor, bool timeout, string how)
        {
            if (err > StatMaxError) StatWorstWho = $"{obj.Owner} {obj.Type} #{obj.Id} ({how})";
            var st = MyStat(); st.Shots++; st.SumErr += err; if (err > st.MaxErr) { st.MaxErr = err; st.WorstHow = how; }
            if (heldFor > 0f) { st.Held++; st.MaxHoldMs = Mathf.Max(st.MaxHoldMs, heldFor * 1000f); }
            if (timeout) st.Timeouts++;
            Shots++; StatShots++; StatSumError += err; StatMaxError = Mathf.Max(StatMaxError, err); MaxFireError = Mathf.Max(MaxFireError, err);
            if (heldFor > 0f) { HeldCount++; StatHeld++; MaxHoldMs = Mathf.Max(MaxHoldMs, heldFor * 1000f); }
            if (timeout) StatTimeouts++;
        }

        /// <summary>Angle (deg) between the muzzle and the shot direction after this frame's solve.</summary>
        public float AimError { get; private set; } = 180f;
        /// <summary>A yaw / pitch gun rig with at least one muzzle (vehicles, cannons, the mech).</summary>
        public bool HasRig => !robot && (yawJ != null || pitchJ != null) && muzzles.Count > 0;
        public bool IsRobot => robot;
        public int HeldShots => held.Count;

        static float Recoil(float age) => age < 0 || age > 1 ? 0 : (1 - Mathf.Exp(-age / 0.018f)) * Mathf.Exp(-age / 0.23f) * 1.5f;
        float Speed => game ? Mathf.Max(0.25f, game.gameSpeed) : 1f;

        // ------------------------------------------------------------------ setup
        public static UnitAim Attach(UnitView v, ZObject o)
        {
            if (!v || o == null) return null;
            var a = v.GetComponent<UnitAim>(); if (!a) a = v.gameObject.AddComponent<UnitAim>();
            a.Bind(v, o);
            return a;
        }

        void Bind(UnitView v, ZObject o)
        {
            view = v; obj = o; game = ZGame.I; robot = o.Kind == ZKind.Robot;
            wtype = o.Kind == ZKind.Vehicle && o.Type == "apc" ? o.DriverType : o.Type;
            slew = SlewFor(o.Type, o.Kind);
            nextScan = Random.value * 0.25f;
            muzzles.Clear(); recoils.Clear(); yawJ = pitchJ = null; spin = null; spine = null; rMuzzle = null;
            bodyYawNow = transform.eulerAngles.y - ModelYaw(gameObject, o, 180f);                     // UnitView's yaw space
            if (robot) BindRobot(); else BindGun();
        }

        void BindRobot()
        {
            Transform s0 = null, s1 = null, s2 = null, weapon = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "mixamorigSpine": s0 = t; break;
                    case "mixamorigSpine1": s1 = t; break;
                    case "mixamorigSpine2": s2 = t; break;
                    case "RifleMuzzle": case "BazookaMuzzle": rMuzzle = t; break;
                    case "Rifle_Heavy": case "Bazooka": weapon = t; break;
                    case "Muzzle": if (WeaponsV2.Bs01TorsoAim && !rMuzzle) rMuzzle = t; break;           // weapons_v2: BS-01 weapons (BS01_Weapon_*/Muzzle)
                    default: if (WeaponsV2.Bs01TorsoAim && !weapon && t.name.StartsWith("BS01_Weapon_")) weapon = t; break;
                }
            }
            HeroSpine(ref s0, ref s1, ref s2);                                                        // KH2 (item 198): a hero's spine from his humanoid rig (View/UnitAim.Hero.cs)
            if (s0 && s1 && s2)
            {   // the torso aim is a rotation ON TOP of the animated clip pose (chestDelta), measured against the body front, so it
                // never depends on how the prefab happened to be posed when it was saved (Z_robot's stored Hips are pitched ~44 deg)
                spine = new[] { s0, s1, s2 }; animated = new Quaternion[3]; written = new Quaternion[3];
                for (int i = 0; i < 3; i++) animated[i] = written[i] = spine[i].localRotation;
            }
            if (rMuzzle)
            {
                rDirL = Vector3.back;
                if (weapon)
                {
                    var along = rMuzzle.position - weapon.position;
                    if (along.sqrMagnitude > 1e-6f && Vector3.Dot(rMuzzle.TransformDirection(rDirL), along.normalized) < 0.3f)
                        rDirL = rMuzzle.InverseTransformDirection(along.normalized);
                }
            }
        }

        void BindGun()
        {
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                string n = t.name; if (n.EndsWith("_Surface")) continue;
                if (yawJ == null && (n.EndsWith("TurretYaw") || n == "MLRSRackYaw" || n == "MechWaistYaw")) yawJ = new Joint { t = t, rest = t.localRotation };
                else if (pitchJ == null && (n.EndsWith("BarrelPitch") || n == "MLRSRackPitch" || n == "MechGunShoulder")) pitchJ = new Joint { t = t, rest = t.localRotation };
                else if (!spin && n.EndsWith("BarrelSpin")) { spin = t; spinRest = t.localRotation; }
                bool isMuzzle = (n == "Muzzle" || n.StartsWith("Muzzle_") || n.EndsWith("Muzzle") || n.Contains("Muzzle_"))
                                && !n.Contains("Rocket") && !n.Contains("Dark") && n != "RifleMuzzle" && n != "BazookaMuzzle";
                if (isMuzzle) muzzles.Add(new Muzzle { t = t });
            }
            if (yawJ != null && !yawJ.t.parent) yawJ = null;
            if (pitchJ != null && !pitchJ.t.parent) pitchJ = null;
            muzzles.Sort((a, b) => string.CompareOrdinal(a.t.name, b.t.name));
            var up = transform.up;
            foreach (var m in muzzles)
            {
                var pivot = pitchJ != null && m.t.IsChildOf(pitchJ.t) ? pitchJ.t : yawJ != null && m.t.IsChildOf(yawJ.t) ? yawJ.t : transform;
                m.dirL = Vector3.back;
                var along = m.t.position - pivot.position;
                if (along.sqrMagnitude > 0.04f && Vector3.Dot(m.t.TransformDirection(m.dirL), along.normalized) < 0.2f)
                    m.dirL = m.t.InverseTransformDirection(along.normalized);                         // socket without an authored rotation
                m.pitched = pitchJ != null && m.t.IsChildOf(pitchJ.t);
                for (var p = m.t.parent; p && p != transform; p = p.parent)
                {
                    if (!m.recoil && p.name.Contains("Recoil") && !p.name.Contains("RocketPod")) m.recoil = p;
                    if (!m.rocket && p.name.Contains("LoadedRocket")) m.rocket = p;
                }
                if (m.rocket) m.rocketR = m.rocket.GetComponentsInChildren<Renderer>(true);             // cached: no allocation per shot
                if (m.rocket && m.rocket.parent)
                {   // mlrs_heavy_v1: reload slide-in: the loaded rocket's rest and the tube's rear direction in the rack's space
                    m.rocketRest = m.rocket.localPosition;
                    m.rocketBackL = m.rocket.parent.InverseTransformDirection(-m.t.TransformDirection(m.dirL)).normalized;
                    // drifts up through the rack top when the muzzle socket's aim axis is a little off the tube
                    var rmf = m.rocket.GetComponentInChildren<MeshFilter>();
                    if (rmf && rmf.sharedMesh)
                    {
                        var e = rmf.sharedMesh.bounds.extents;
                        var ax = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
                        var dl = m.rocket.parent.InverseTransformDirection(rmf.transform.TransformDirection(ax)).normalized;
                        float dd = Vector3.Dot(dl, m.rocketBackL);
                        if (Mathf.Abs(dd) > 0.7f) { m.rocketBackL = dd > 0f ? dl : -dl; MlrsAxisFixed++; }
                    }
                }
                if (m.recoil && m.recoil.parent && !recoils.Exists(r => r.t == m.recoil))
                    recoils.Add((m.recoil, m.recoil.localPosition, m.recoil.parent.InverseTransformDirection(-m.t.TransformDirection(m.dirL)).normalized));
            }
            if (muzzles.Count == 0) return;
            var d0 = MuzzleDir(muzzles[0]);
            if (yawJ != null) yawJ.axisL = yawJ.t.parent.InverseTransformDirection(up).normalized;
            if (pitchJ != null)
            {
                var h = Vector3.Cross(up, d0); if (h.sqrMagnitude < 0.01f) h = pitchJ.t.parent.right;
                pitchJ.axisL = pitchJ.t.parent.InverseTransformDirection(h.normalized).normalized;
                var hw = pitchJ.t.parent.TransformDirection(pitchJ.axisL);
                pitchSign = (Quaternion.AngleAxis(5f, hw) * d0).y > d0.y ? 1f : -1f;                  // + angle raises the muzzle
            }
            if (spin && spin.parent) spinAxisL = spin.parent.InverseTransformDirection(d0).normalized;
        }

        void OnDisable() { held.Clear(); }

        // ------------------------------------------------------------------ public hooks (UnitView / CombatView / GrenadeFx)
        /// <summary>Muzzle of the gun that fires next (vehicles: round-robin twin barrels / MLRS tubes), robots: rifle or bazooka.</summary>
        public bool TryMuzzle(out Vector3 p)
        {
            if (robot && rMuzzle) { p = rMuzzle.position; return true; }
            if (!robot && muzzles.Count > 0) { p = muzzles[slot].t.position; return true; }
            p = default; return false;
        }

        /// <summary>vehicles_nb_v1 NB6: with DV30 the heavy tank's cycle shell leaves the LEFT barrel (HeavyTankMuzzle_L, slot 0) and
        /// </summary>
        public void PickBarrel(ZEvent e)
        {
            if (robot || wtype != "heavy" || !ZWorld.HeavyPairDeviation || e.type != ZEventType.MissileFired || e.flag || muzzles.Count != 2) return;
            slot = e.flag2 ? 1 : 0;
        }

        /// <summary>vehicles_nb_v1 NB6: +1 / -1 = the barrel that fired last sits right / left of the turret axis.</summary>
        public float FiredSide
        {
            get
            {
                if (muzzles.Count == 0) return 1f;
                var m = muzzles[firedSlot < muzzles.Count ? firedSlot : 0]; if (!m.t) return 1f;
                var pivot = yawJ != null && yawJ.t ? yawJ.t : transform;
                return Vector3.Dot(m.t.position - pivot.position, pivot.right) >= 0f ? 1f : -1f;
            }
        }

        /// <summary>Horizontal direction the gun fires along (hull recoil jolt), or `fallback`.</summary>
        public Vector3 RecoilDirection(Vector3 fallback)
        {
            if (muzzles.Count == 0) return fallback;
            var d = MuzzleDir(muzzles[firedSlot < muzzles.Count ? firedSlot : 0]); d.y = 0;
            return d.sqrMagnitude > 1e-4f ? d.normalized : fallback;
        }

        /// <summary>A shot left this unit: recoil slide + muzzle kick, barrel spin-up, MLRS rocket leaves the rack.</summary>
        public void OnFire()
        {
            shotAge = 0f; lastShot = Time.time; firedSlot = slot; advance = true;
            if (!robot && slot < muzzles.Count && muzzles[slot].recoil) recoilAge[muzzles[slot].recoil] = 0f;   // vehicles_nb_v1 NB6
            if (!robot && slot < muzzles.Count && muzzles[slot].rocketR != null)
            {
                var m = muzzles[slot];
                foreach (var r in m.rocketR) if (r) r.enabled = false;
                if (m.slideAt >= 0f && m.rocket) { m.rocket.localPosition = m.rocketRest; m.slideAt = -1f; }   // mlrs_heavy_v1: fired mid slide-in
                m.reloadAt = Time.time + (wtype == "missile_launcher" ? MlrsSalvo.ReloadAfter / Speed : 3.5f);   // mlrs_heavy_v1: tube by tube, back before Zod's next salvo (4.454 s)
            }
        }

        /// <summary>WeaponHold (order 80, after the Animator and after this): the world direction the rifle / bazooka barrel
        /// must point along now (at the target, ballistic for bazooka rockets), kept within 75 deg of the chest front so a
        /// gun never points behind its robot. False = no target (WeaponHold keeps its ready carry).</summary>
        public bool TryAimDirection(out Vector3 dir)
        {
            dir = default;
            if (!robot) return false;
            posedFrame = Time.frameCount;                                                             // WeaponHold is posing the weapon
            if (!hasAimDir || !view || view.Dying || Time.time < throwUntil) return false;
            var up = transform.up;
            var cf = Vector3.ProjectOnPlane(chestFront, up); var af = Vector3.ProjectOnPlane(aimDir, up);
            if (cf.sqrMagnitude < 1e-4f || af.sqrMagnitude < 1e-4f) { dir = aimDir; return true; }
            float off = Vector3.SignedAngle(cf, af, up), clamped = Mathf.Clamp(off, -75f, 75f);
            float elev = Mathf.Clamp(90f - Vector3.Angle(up, aimDir), -45f, 45f);
            var flat = Quaternion.AngleAxis(clamped, up) * cf.normalized;
            dir = Quaternion.AngleAxis(-elev, Vector3.Cross(up, flat).normalized) * flat;               // + elevation raises the barrel
            return true;
        }

        /// <summary>Grenade throw: the body faces the landing point for `seconds` (GrenadeFx releases at .56 s).</summary>
        public void BeginThrow(Vector3 to, float seconds) { throwPoint = to; throwUntil = Time.time + seconds; }

        /// <summary>mlrs_heavy_v1: keep the gun on the last shot's point `seconds` longer (MlrsSalvo: the ripple leaves along one aim).</summary>
        public void KeepAim(float seconds) { if (Time.time < holdUntil) holdUntil = Mathf.Max(holdUntil, Time.time + seconds); }

        /// <summary>UnitView: body yaw for robots (face the shot, eased) and turret-less cannons (the whole cannon turns).</summary>
        public float AdjustBodyYaw(float yaw, float moveYaw, bool moving, float dt)
        {
            if (!view || obj == null) return yaw;
            // UnitView turned a moving body toward its path just before this call; when the body faces the target instead, ease
            // on from last frame's result, so the path turn (400 deg/s) never fights the aim (it left the body ~11 deg off)
            float prev = Time.frameCount - bodyFrame == 1 ? bodyYawNow : yaw;
            bodyFrame = Time.frameCount;
            bool throwing = robot && Time.time < throwUntil;
            Vector3 p;
            if (throwing) p = throwPoint;
            else if (!Target(out p, out _, out _, out _)) { faceTarget = false; bodyVel = 0f; bodyYawNow = yaw; return yaw; }
            var d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude < 0.04f) { bodyVel = 0f; bodyYawNow = yaw; return yaw; }
            float aimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (robot && moving && !throwing)
            {
                bool firing = held.Count > 0 || Time.time - lastShot < 1.2f;
                float rel = Mathf.Abs(Mathf.DeltaAngle(moveYaw, aimYaw));
                faceTarget = firing && (faceTarget ? rel > 60f : rel > 85f);
                if (!faceTarget) { bodyVel = 0f; bodyYawNow = yaw; return yaw; }                    // walk forward, the torso twists (<= 80 deg)
            }
            float from = moving ? prev : yaw;
            float goal = from + Mathf.DeltaAngle(from, aimYaw);
            yaw = Mathf.SmoothDamp(from, goal, ref bodyVel, slew.yawSmooth * HurrySmooth, slew.yawSpeed * HurrySpeed, dt);
            bodyYawNow = yaw;
            return yaw;
        }

        /// <summary>UnitView: locomotion clip for a moving robot whose body faces its target (strafe / back-pedal).
        /// legs_rootcause: the fixed 50 / 130 deg cuts flipped walk / strafe / back-pedal at frame rate while the body tracked a
        /// moving target (each flip = a new cross-fade = a leg pop); now 10 deg of hysteresis on each side of both cuts and a
        /// LocoDwell hold between families (walk and run stay one family: UnitView's running flag switches those).</summary>
        public string Locomotion(bool running, float bodyYaw, float moveYaw)
        {
            string fwd = running ? "run_forward" : "walking", want = fwd;
            if (robot && faceTarget)
            {
                float rel = Mathf.DeltaAngle(bodyYaw, moveYaw), a = Mathf.Abs(rel);
                float fwdMax = IsFwdLoco(loco) ? 60f : 40f, backMin = loco == "run_backward" ? 120f : 140f;
                want = a <= fwdMax ? fwd : a >= backMin ? "run_backward" : rel > 0 ? "strafe_right" : "strafe_left";
            }
            bool sameFamily = want == loco || (IsFwdLoco(want) && IsFwdLoco(loco));
            if (!sameFamily && loco != null && Time.time - locoAt < LocoDwell) want = IsFwdLoco(loco) ? fwd : loco;
            if (!(want == loco || (IsFwdLoco(want) && IsFwdLoco(loco)))) locoAt = Time.time;
            loco = want;
            return want;
        }
        string loco; float locoAt = -10f;
        /// <summary>legs_rootcause: seconds a forward / strafe / back-pedal choice holds before the family may change again.</summary>
        public static float LocoDwell = 0.4f;
        static bool IsFwdLoco(string s) => s == "walking" || s == "run_forward";

        /// <summary>CombatView (Hitscan / MissileFired): true = the gun is not on target yet, the shot is held and replayed
        /// through CombatView.Replay once aligned (FX, sound and recoil then leave the real muzzle).</summary>
        public static bool HoldShot(UnitView av, ZEvent e, ZObject target)
        {
            if (!av) return false;
            var a = av.GetComponent<UnitAim>();
            if (a) a.PickBarrel(e);                                                                   // vehicles_nb_v1 NB6: DV30 pair = left barrel, then right
            if (Replaying) return false;
            return a && a.isActiveAndEnabled && a.Hold(e, target);
        }

        // ------------------------------------------------------------------ held shots
        bool Hold(ZEvent e, ZObject t)
        {
            if (!view || view.Dying || obj == null || game == null) return false;
            bool missile = e.type == ZEventType.MissileFired, grenade = missile && e.flag;
            if (!robot && !HasRig && !(obj.Kind == ZKind.Cannon && Time.frameCount - bodyFrame <= 2)) return false;   // APC / crane: nothing to turn
            if (grenade && !robot) return false;
            var p = missile ? OzShuttleAim.AirAim(t, GroundAt(e.x, e.y) + Vector3.up * 0.25f, e.value) : t != null ? AimPointOf(t) : holdPoint;   // DV118: up to the flying shuttle
            bool ballistic = missile && !grenade;
            float T = missile ? Mathf.Max(0.05f, e.value) : 0f, g = ballistic ? ProjectileGravity(wtype, IsRocket(wtype)) : 0f;
            float flightReal = T / Speed;
            float maxWait = grenade ? Mathf.Min(0.3f, 0.3f * flightReal) : missile ? Mathf.Min(0.8f, 0.6f * flightReal) : robot ? 0.5f : 0.35f;
            // A gun rig waits up to 90 % of the shell's flight (1.5 s cap; the shell then
            // flies the rest faster so it still lands on the sim's detonation) and up to 0.8 s for a hitscan burst, instead of 60 % / 0.35 s
            if (!robot && HasRig) maxWait = missile ? Mathf.Min(1.5f, 0.9f * flightReal) : 0.8f;
            if (!robot && missile) maxWait = Mathf.Max(0.25f, maxWait);
            bool urgent = !missile && (t == null || t.Destroyed);                                // kill shot: its death plays this frame
            if (urgent || maxWait < 0.03f)
            {
                FlushHeld();
                float eu = ErrorTo(p, ballistic, T, g, grenade);
                if (eu > slew.tol && Snap(p, ballistic, T, g)) eu = ErrorTo(p, ballistic, T, g, grenade);
                Count(eu, 0f, false, "kill shot, no hold"); Mark(p, ballistic, T, g); return false;
            }
            float err0 = ErrorTo(p, ballistic, T, g, grenade);
            if (robot && missile && !grenade && view.StanceHoldsFire) err0 = 180f;                    // robot_models_v2: the Tough still lowers the tube (tough_prepare)
            if (held.Count == 0 && err0 <= slew.tol) { Count(err0, 0f, false, "on target, no hold"); Mark(p, ballistic, T, g); return false; }
            held.Add(new Held { e = e, point = p, at = Time.time, maxWait = maxWait, ballistic = ballistic, grenade = grenade, T = T, g = g });
            if (held.Count > 16) { var h = held[0]; held.RemoveAt(0); Replay(h); }
            return true;
        }

        void Mark(Vector3 p, bool ballistic, float T, float g)
        {
            holdPoint = p; holdUntil = Time.time + 0.35f; holdBallistic = ballistic; holdT = T; holdG = g; lastShot = Time.time;
        }

        void ProcessHeld()
        {
            while (held.Count > 0)
            {
                var h = held[0];
                float waited = Time.time - h.at, T = Mathf.Max(0.05f, h.T - waited * Speed);
                bool ready = view.Dying || waited >= h.maxWait || (ErrorTo(h.point, h.ballistic, T, h.g, h.grenade) <= slew.tol && !view.StanceHoldsFire);   // robot_models_v2
                if (!ready || Time.time < nextRelease) break;
                held.RemoveAt(0);
                Replay(h);
                nextRelease = held.Count > 0 ? Time.time + Mathf.Max(0f, held[0].at - h.at) : 0f;   // keep the original cadence
            }
        }

        /// <summary></summary>
        bool Hurry => held.Count > 0 && Time.time - held[0].at > 0.3f * held[0].maxWait;
        float HurrySpeed => Hurry ? 2.5f : 1f;
        float HurrySmooth => Hurry ? 0.45f : 1f;
        public static int StatSnaps;

        /// <summary>
        /// shot about to leave spawns from the muzzle's real pose along the barrel. False = nothing to snap (APC, turret-less cannon).</summary>
        bool Snap(Vector3 P, bool ballistic, float T, float g)
        {
            if (robot)
            {
                if (spine == null || !rMuzzle || Time.time < throwUntil) return false;
                float ty = tYaw, tp = tPitch;
                SolveTorso(P, ballistic, T, g, ref ty, ref tp);
                tYaw = ty; tPitch = tp; tYawVel = tPitchVel = 0f;
                ApplySpine(tYaw, tPitch, transform.up);
                for (int i = 0; i < spine.Length; i++) written[i] = spine[i].localRotation;
                chestFront = chestDelta * BodyFront(); aimDir = Desired(rMuzzle.position, P, ballistic, T, g); hasAimDir = true;
                StatSnaps++; MyStat().Snaps++; return true;
            }
            if (!HasRig || slot >= muzzles.Count || !muzzles[slot].t) return false;
            if (yawJ != null && (!yawJ.t || !yawJ.t.parent)) return false;                            // turret tossed
            float gy = yawJ != null ? yawJ.ang : 0f, gp = pitchJ != null ? pitchJ.ang : 0f;
            SolveGunFor(P, ballistic, T, g, ref gy, ref gp);
            if (yawJ != null) { yawJ.ang = Wrap(gy); yawJ.vel = 0f; }
            if (pitchJ != null) { pitchJ.ang = gp; pitchJ.vel = 0f; }
            Pose(yawJ != null ? yawJ.ang : 0f, pitchJ != null ? pitchJ.ang : 0f, 0f);
            StatSnaps++; MyStat().Snaps++; return true;
        }

        void FlushHeld()
        {
            while (held.Count > 0) { var h = held[0]; held.RemoveAt(0); Replay(h); }
        }

        void Replay(Held h)
        {
            var e = h.e;
            float waited = Time.time - h.at, Tn = Mathf.Max(0.05f, h.T - waited * Speed), err = ErrorTo(h.point, h.ballistic, Tn, h.g, h.grenade);
            // A shot released by its wait cap while the gun is still off target snaps the gun onto it first
            if (err > slew.tol && !view.Dying && Snap(h.point, h.ballistic, Tn, h.g)) err = ErrorTo(h.point, h.ballistic, Tn, h.g, h.grenade);
            Count(err, Mathf.Max(0.0001f, waited), err > slew.tol, $"held {waited * 1000f:F0} ms, cap {h.maxWait * 1000f:F0} ms");
            if (e.type == ZEventType.MissileFired) e.value = Mathf.Max(0.05f, e.value - (Time.time - h.at) * Speed);   // same detonation time
            Mark(h.point, h.ballistic, e.value, h.g);
            if (!combat && game) combat = game.GetComponent<CombatView>();                          // ZGame.Awake adds it to the ZGame object
            if (!combat) combat = FindFirstObjectByType<CombatView>();
            if (!combat) return;
            Replaying = true;
            try { combat.Replay(e); } finally { Replaying = false; }
        }

        float ErrorTo(Vector3 p, bool ballistic, float T, float g, bool grenade)
        {
            if (robot)
            {
                if (grenade || !rMuzzle)
                {   // throws: only the body has to face the landing point
                    var d = p - transform.position; d.y = 0;
                    return d.sqrMagnitude < 0.04f ? 0f : Mathf.Abs(Mathf.DeltaAngle(bodyYawNow, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg));
                }
                if (spine == null) return Vector3.Angle(rMuzzle.TransformDirection(rDirL), Desired(rMuzzle.position, p, ballistic, T, g));
                return Vector3.Angle(PointerDir(), Desired(PointerPos(), p, ballistic, T, g));           // bazooka rockets arc
            }
            if (HasRig)
            {
                var m = slot < muzzles.Count ? muzzles[slot] : null;
                if (m == null || !m.t) return 0f;                                                     // turret tossed / destroyed while a shot was held
                return GunError(m, Desired(m.t.position, p, ballistic, T, g));
            }
            var f = p - transform.position; f.y = 0;                                                  // turret-less cannon: the body turns
            return f.sqrMagnitude < 0.04f ? 0f : Mathf.Abs(Mathf.DeltaAngle(bodyYawNow, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg));
        }

        // ------------------------------------------------------------------ targets
        Vector3 GroundAt(int px, int py)
        {
            var w = ZGame.PxToWorld(px, py); w.y = game.GroundY(w.x, w.z); return w;
        }

        Vector3 AimPointOf(ZObject t)
        {
            if (game.Views.TryGetValue(t.Id, out var v) && v && v.isActiveAndEnabled) return OzShuttleAim.HitAim(t, v.AimPos());   // DV118: the flying ship's hull, not under its belly
            return game.CenterWorld(t) + Vector3.up * (t.Kind == ZKind.Building ? 1.5f : 1.0f);
        }

        static bool HasGrenades(ZObject o) => o.Grenades > 0 || (o.Leader != null && o.Leader.Grenades > 0);

        /// <summary>Where the gun should point now: a held shot, the shot just fired (.35 s), the sim's attack target, or
        /// the predicted one. Missile units aim at the ground point the shell will land on (ballistic launch direction).</summary>
        bool Target(out Vector3 p, out bool ballistic, out float T, out float g)
        {
            p = default; ballistic = false; T = 0; g = 0;
            if (obj == null || game == null) return false;
            if (held.Count > 0)
            {
                var h = held[0]; p = h.point; ballistic = h.ballistic; g = h.g; T = Mathf.Max(0.05f, h.T - (Time.time - h.at) * Speed);
                return true;
            }
            if (Time.time < holdUntil) { p = holdPoint; ballistic = holdBallistic; T = holdT; g = holdG; return true; }
            var t = obj.AttackObject;
            if (t == null || t.Destroyed) t = predicted;
            if (t == null || t.Destroyed || t.KillAt >= 0) return false;
            bool missile = obj.DamageIsMissile && !HasGrenades(obj);
            if (missile)
            {
                p = OzShuttleAim.AirAim(t, GroundAt(t.CenterX, t.CenterY) + Vector3.up * 0.25f, 0f);   // DV118: up to the flying shuttle
                if (obj.MissileSpeed > 0)
                {
                    float dx = t.CenterX - obj.CenterX, dy = t.CenterY - obj.CenterY;
                    ballistic = true; g = ProjectileGravity(wtype, IsRocket(wtype));
                    T = Mathf.Max(0.05f, Mathf.Sqrt(dx * dx + dy * dy) / obj.MissileSpeed);         // zdamagemissile.h CalcExplodeTime
                }
            }
            else p = AimPointOf(t);
            return true;
        }

        /// <summary>The object the sim will engage: an Attack / Agro waypoint target, else what CheckPassiveEngage picks next
        /// (zobject.cpp:1603-1853: first enemy unit in object order within attack range; moving robots never auto-engage).</summary>
        ZObject Predict()
        {
            var o = obj; var w = game.World;
            if (w == null || !o.CanAttack() || o.Owner == Team.Neutral) return null;
            if (o.AttackObject != null && !o.AttackObject.Destroyed) return o.AttackObject;
            if (o.Waypoints.Count > 0)
            {
                var wp = o.Waypoints[0];
                if ((wp.mode == WpMode.Attack || wp.mode == WpMode.Agro) && wp.refId >= 0)
                {
                    var t = w.Get(wp.refId);
                    if (t != null && !t.Destroyed && o.CanAttackObject(t))
                    {
                        long dx = t.CenterX - o.CenterX, dy = t.CenterY - o.CenterY, r = o.AttackRadius + 64;
                        if (dx * dx + dy * dy <= r * r) return t;
                    }
                }
            }
            if (o.IsRobot && o.IsMoving) return null;
            foreach (var t in w.Objects)
                if (t.IsUnit && !t.Destroyed && t.KillAt < 0 && t.Owner != Team.Neutral && t.Owner != o.Owner && o.CanAttackObject(t) && o.WithinAttackRadius(t))
                    return t;
            return null;
        }

        void Update()
        {
            if (!view || obj == null || game == null) return;
            if ((nextScan -= Time.deltaTime) > 0) return;
            nextScan = 0.25f;
            predicted = view.Dying ? null : Predict();
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!view || obj == null) { enabled = false; return; }
            if (view.Reacting) return;                                                                 // deaths_mech_v1: knocked down (the torso solve would twist the fall)
            if (view.Dying) { FlushHeld(); return; }                                                   // its last shots still leave the gun
            float dt = Time.deltaTime;
            VeteranMarks.AimSlew(obj, ref slew, ref vetBaseSlew, ref vetSlewOn);                       // veterancy_v1 DV12: veterans x1.35 slew
            if (robot) SolveRobot(dt);
            else
            {
                if (HasRig) SolveGun(dt);
                AnimateGun(dt);
            }
            ProcessHeld();
            if (advance) { advance = false; if (muzzles.Count > 1) slot = (slot + 1) % muzzles.Count; }
        }

        Vector3 MuzzleDir(Muzzle m) => m.t.TransformDirection(m.dirL).normalized;

        static float Elev(Vector3 v, Vector3 up) => 90f - Vector3.Angle(up, v);

        /// <summary>Aim error of a gun: the bearing always counts; the elevation only where the gun can pitch and the wanted
        /// elevation is inside its limits (a fixed-elevation gatling or a barrel at its stop still fires, like Zod).</summary>
        float GunError(Muzzle m, Vector3 desired)
        {
            var f = MuzzleDir(m);
            bool yawOk = yawJ != null && yawJ.t && yawJ.t.parent;                                     // the joint can be gone after a turret toss
            var ax = yawOk ? yawJ.t.parent.TransformDirection(yawJ.axisL) : transform.up;
            float yawErr = yawOk ? Mathf.Abs(SignedOnPlane(f, desired, ax)) : Vector3.Angle(f, desired);
            if (pitchJ == null || !m.pitched || pitchAtLimit) return yawErr;
            return Mathf.Max(yawErr, Mathf.Abs(Elev(f, ax) - Elev(desired, ax)));
        }

        /// <summary>Launch direction toward p. T = the sim's flight time (sim seconds, like ZEvent.value); the visual projectile
        /// flies T / gameSpeed real seconds (OpenZFxPlayer.Launch, GrenadeFx), so the arc is computed in real seconds.</summary>
        Vector3 Desired(Vector3 from, Vector3 p, bool ballistic, float T, float g)
        {
            T /= Speed;
            if (!ballistic || T <= 0.02f) return (p - from).normalized;
            var v = (p - from) / T; v.y += g * T * 0.5f;                                              // M12 projectile(): v = d / t, v.y += g t / 2
            return v.normalized;
        }

        static float SignedOnPlane(Vector3 f, Vector3 d, Vector3 axis)
        {
            var a = Vector3.ProjectOnPlane(f, axis); var b = Vector3.ProjectOnPlane(d, axis);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(a, b, axis);
        }

        static float Wrap(float a) => Mathf.DeltaAngle(0f, a);

        void Pose(float yaw, float pitch, float kick)
        {
            if (yawJ != null) yawJ.t.localRotation = Quaternion.AngleAxis(yaw, yawJ.axisL) * yawJ.rest;
            if (pitchJ != null) pitchJ.t.localRotation = Quaternion.AngleAxis(pitch + kick * pitchSign, pitchJ.axisL) * pitchJ.rest;
        }

        float ClampPitch(float p) => Mathf.Clamp(p * pitchSign, slew.pitchMin, slew.pitchMax) * pitchSign;

        /// <summary>aimVehicle solve for the current barrel (3 iterations, the joints posed while solving): gy / gp in =
        /// the start angles, out = the angles that put the muzzle on P. Returns P after the minimum-range clamp.</summary>
        Vector3 SolveGunFor(Vector3 P, bool ballistic, float T, float g, ref float gy, ref float gp)
        {
            var m = muzzles[slot];
            var pivot = (yawJ != null ? yawJ.t : pitchJ.t).position;
            var flat = P - pivot; flat.y = 0;
            if (flat.magnitude < 4f)
            {   // a point inside the mount's swept volume keeps its bearing at a minimum range
                var dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.ProjectOnPlane(MuzzleDir(m), Vector3.up).normalized;
                P = new Vector3(pivot.x + dir.x * 4f, P.y, pivot.z + dir.z * 4f);
            }
            for (int i = 0; i < 3; i++)
            {
                if (yawJ != null)
                {
                    Pose(gy, gp, 0f);
                    var yawAxis = yawJ.t.parent.TransformDirection(yawJ.axisL);
                    if (muzzles.Count > 1)
                    {
                        // around): solving for the NEXT muzzle made a multi-barrel turret jump by the pods' side offset on every shot. The
                        // yaw now points the muzzles' mean at the target (pitch still per barrel); a single barrel is unchanged.
                        // K433 (item 432): with the target behind, the barrels' errors fell on both sides of +-180 and their mean was about 0:
                        // the turret stayed turned away and fired from its back. Each error is now taken on the first barrel's side (View/UnitAim.BehindFix.cs).
                        float acc = 0f, first = 0f;
                        for (int k = 0; k < muzzles.Count; k++)
                        {
                            var mm = muzzles[k];
                            float a = SignedOnPlane(MuzzleDir(mm), Desired(mm.t.position, P, ballistic, T, g), yawAxis);
                            if (k == 0) first = a; else a = SameSide(a, first);
                            acc += a;
                        }
                        gy += acc / muzzles.Count;
                    }
                    else gy += SignedOnPlane(MuzzleDir(m), Desired(m.t.position, P, ballistic, T, g), yawAxis);
                }
                if (pitchJ != null && m.pitched)
                {
                    Pose(gy, gp, 0f);
                    float want = gp + SignedOnPlane(MuzzleDir(m), Desired(m.t.position, P, ballistic, T, g), pitchJ.t.parent.TransformDirection(pitchJ.axisL));
                    gp = ClampPitch(want);
                    pitchAtLimit = Mathf.Abs(want - gp) > 0.5f;
                }
            }
            return P;
        }

        /// <summary>aimVehicle: yaw about the mount's up, pitch about the barrel hinge, 3 iterations on the
        /// real muzzle; then an eased slew toward that solution (never a snap), then the recoil kick on top.</summary>
        void SolveGun(float dt)
        {
            var m = muzzles[slot];
            {   // motion_v1: the turret is its own joint: when the hull turns, the turret keeps its world bearing and only its own eased
                // slew moves it (the hull's turn no longer drags the gun off its target and back)
                var hf = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                float hy = hf.sqrMagnitude > 1e-6f ? Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg : lastHullYaw;
                if (hullYawInit && yawJ != null && obj.Kind == ZKind.Vehicle) yawJ.ang = Wrap(yawJ.ang - Mathf.DeltaAngle(lastHullYaw, hy));
                lastHullYaw = hy; hullYawInit = true;
            }
            float yaw = yawJ != null ? yawJ.ang : 0f, pitch = pitchJ != null ? pitchJ.ang : 0f;
            bool has = Target(out var P, out bool ballistic, out float T, out float g);
            float gy = yaw, gp = pitch;
            if (has)
            {
                wasIdle = false;
                P = SolveGunFor(P, ballistic, T, g, ref gy, ref gp);
            }
            else
            {
                gy = IdleYaw(yaw); gp = 0f;
            }
            float hs = HurrySpeed, hm = HurrySmooth;
            if (yawJ != null)
            {
                float goal = yawJ.ang + Mathf.DeltaAngle(yawJ.ang, gy);
                yawJ.ang = Wrap(Mathf.SmoothDamp(yawJ.ang, goal, ref yawJ.vel, slew.yawSmooth * hm, slew.yawSpeed * hs, dt));
            }
            if (pitchJ != null) pitchJ.ang = Mathf.SmoothDamp(pitchJ.ang, gp, ref pitchJ.vel, slew.pitchSmooth * hm, slew.pitchSpeed * hs, dt);
            float ya = yawJ != null ? yawJ.ang : 0f, pa = pitchJ != null ? pitchJ.ang : 0f;
            Pose(ya, pa, 0f);
            AimError = has ? GunError(m, Desired(m.t.position, P, ballistic, T, g)) : 180f;
            OzSoundV3.Slew(obj, yawJ != null && yawJ.t ? yawJ.t : transform, yawJ != null ? yawJ.vel : 0f, pitchJ != null ? pitchJ.vel : 0f, has, AimError, slew.tol);   // sound_v3
            float kick = Recoil(shotAge) * slew.kickDeg;
            if (kick > 0.01f && pitchJ != null) Pose(ya, pa, kick);                                    // the muzzle climbs, then settles
        }

        /// <summary>Zod idle turret: one 45 deg step per second (APC 0.2 s) while crewed and standing; driving = hull front;
        /// empty (neutral) = still.</summary>
        float IdleYaw(float current)
        {
            if (obj.Owner == Team.Neutral) { wasIdle = true; return current; }
            if (obj.IsMoving || !IdleScan) { wasIdle = true; idleYaw = 0f; return 0f; }
            if (!wasIdle) { wasIdle = true; idleYaw = current; nextIdleStep = Time.time + 1f; }         // scan on from where the gun stopped
            if (Time.time >= nextIdleStep) { nextIdleStep = Time.time + (obj.Type == "apc" ? 0.2f : 1f); idleYaw = Wrap(idleYaw + 45f); }
            return idleYaw;
        }

        /// <summary>Barrel spin (gatling, jeep, mech gun: spin up .25 s while firing, spin down 1.2 s), recoil slide along the
        /// </summary>
        void AnimateGun(float dt)
        {
            shotAge += dt;
            if (spin)
            {
                bool firing = held.Count > 0 || Time.time - lastShot < 0.4f;
                float max = slew.spinMax > 0 ? slew.spinMax : 900f, target = firing ? max : 0f;
                OzSoundV3.BarrelSpin(obj, firing, spinSpeed, max, spin.position);                         // sound_v3: jeep / mech spin-up, spin-down
                spinSpeed = Mathf.MoveTowards(spinSpeed, target, (firing ? max / 0.25f : max / 1.2f) * dt);
                spinAngle = Mathf.Repeat(spinAngle + spinSpeed * dt, 360f);
                spin.localRotation = Quaternion.AngleAxis(spinAngle, spinAxisL) * spinRest;
            }
            float r = Recoil(shotAge) * slew.recoilK;
            var fired = firedSlot < muzzles.Count ? muzzles[firedSlot].recoil : null;
            foreach (var (t, rest, backL) in recoils)
            {   // vehicles_nb_v1 NB6: every barrel runs its own recoil envelope (the heavy's left barrel is still sliding home when the right one fires)
                if (!t) continue;
                float rr = t == fired ? r : 0f;
                if (recoilAge.TryGetValue(t, out float age)) { age += dt; recoilAge[t] = age; rr = Recoil(age) * slew.recoilK; }
                t.localPosition = rr > 0.0005f ? rest + backL * rr : rest;
            }
            foreach (var m in muzzles)
            {
                if (m.rocketR != null && m.reloadAt > 0 && Time.time >= m.reloadAt)
                {
                    m.reloadAt = -1;
                    foreach (var rr in m.rocketR) if (rr) rr.enabled = true;
                    if (m.rocket && wtype == "missile_launcher") { m.slideAt = Time.time; MlrsSalvo.ReloadCue(obj.Id, m.rocket.position); }   // mlrs_heavy_v1: slides into its tube
                }
                if (m.slideAt >= 0f && m.rocket)
                {   // mlrs_heavy_v1: reload slide-in along the tube (ease-out cubic); the muzzle socket rides along
                    float k = (Time.time - m.slideAt) / MlrsSalvo.ReloadSlide;
                    if (k >= 1f) { m.slideAt = -1f; m.rocket.localPosition = m.rocketRest; }
                    else m.rocket.localPosition = m.rocketRest + m.rocketBackL * (MlrsSalvo.ReloadSlideM * (1f - k) * (1f - k) * (1f - k));
                }
            }
        }

        /// <summary>aimInfantry on a chubby robot: the animated torso (Spine .25, Spine1 .35, Spine2 .40)
        /// turns (+-80 deg) and bends (45 up, 35 down) so the weapon muzzle points at the target; eased, never snapping.</summary>
        void SolveRobot(float dt)
        {
            if (spine == null || !rMuzzle) return;
            for (int i = 0; i < spine.Length; i++)
            {   // Animator culled off screen (CullUpdateTransforms) leaves last frame's corrected bones: start from the clip pose
                if (spine[i].localRotation == written[i]) spine[i].localRotation = animated[i];
                else animated[i] = spine[i].localRotation;
            }
            bool throwing = Time.time < throwUntil;
            Vector3 P = default; bool ballistic = false; float T = 0f, g = 0f;
            bool has = !throwing && Target(out P, out ballistic, out T, out g);
            var up = transform.up;
            float gy = 0f, gp = 0f;
            if (has) { gy = tYaw; gp = tPitch; SolveTorso(P, ballistic, T, g, ref gy, ref gp); }
            float hs = HurrySpeed, hm = HurrySmooth;
            tYaw = Mathf.SmoothDamp(tYaw, gy, ref tYawVel, 0.06f * hm, 600f * hs, dt);
            tPitch = Mathf.SmoothDamp(tPitch, gp, ref tPitchVel, 0.06f * hm, 400f * hs, dt);
            ApplySpine(tYaw, tPitch, up);
            for (int i = 0; i < spine.Length; i++) written[i] = spine[i].localRotation;
            chestFront = chestDelta * BodyFront();
            hasAimDir = has;
            if (has) aimDir = Desired(rMuzzle.position, P, ballistic, T, g);
            AimError = has ? Vector3.Angle(PointerDir(), Desired(PointerPos(), P, ballistic, T, g)) : 180f;
            HeroHead(has, P, dt);                                                                     // KH2 (item 198): a hero's face turns to the target
        }

        /// <summary>aimInfantry torso solve (3 iterations, the spine posed while solving): gy / gp in = start, out = the solution for P.</summary>
        void SolveTorso(Vector3 P, bool ballistic, float T, float g, ref float gy, ref float gp)
        {
            var up = transform.up;
            var d0 = P - PointerPos(); var hz = Vector3.ProjectOnPlane(d0, up);
            if (hz.sqrMagnitude > 1e-4f) hinge = Vector3.Cross(up, hz.normalized).normalized;
            for (int it = 0; it < 3; it++)
            {
                ApplySpine(gy, gp, up);
                gy = Mathf.Clamp(gy + SignedOnPlane(PointerDir(), Desired(PointerPos(), P, ballistic, T, g), up), -80f, 80f);
                ApplySpine(gy, gp, up);
                gp = Mathf.Clamp(gp + SignedOnPlane(PointerDir(), Desired(PointerPos(), P, ballistic, T, g), hinge), -45f, 35f);
            }
        }

        /// <summary>What the torso solve points: the weapon muzzle, or, when WeaponHold re-poses the weapon along our aim
        /// every frame (it runs after us), the upper body's front: the body front (robots face -z, like WeaponHold) turned by
        /// the torso aim rotation chestDelta, so the upper body turns to the target and the gun follows.</summary>
        bool WeaponPosed => Time.frameCount - posedFrame <= 2;
        Vector3 BodyFront() { var f = -transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }
        Vector3 PointerPos() => WeaponPosed ? spine[2].position : rMuzzle.position;
        Vector3 PointerDir() => WeaponPosed ? chestDelta * BodyFront() : rMuzzle.TransformDirection(rDirL);

        void ApplySpine(float yaw, float pitch, Vector3 up)
        {
            for (int i = 0; i < spine.Length; i++) spine[i].localRotation = animated[i];
            if (Mathf.Abs(yaw) < 0.01f && Mathf.Abs(pitch) < 0.01f) { chestDelta = Quaternion.identity; return; }
            var clip = spine[2].rotation;                                                             // the chest as the clip posed it
            for (int i = 0; i < spine.Length; i++)                                                    // root to tip: each adds its share
                spine[i].rotation = Quaternion.AngleAxis(spineW[i] * pitch, hinge) * Quaternion.AngleAxis(spineW[i] * yaw, up) * spine[i].rotation;
            chestDelta = spine[2].rotation * Quaternion.Inverse(clip);
        }
    }
}

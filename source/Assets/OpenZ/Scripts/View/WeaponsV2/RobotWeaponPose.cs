using OpenZ.Sim;
using UnityEngine;
using static OpenZ.WeaponsV2Logic;

namespace OpenZ
{
    /// <summary>
    /// weapons_v2: the body side of the robot weapons, one per grunt / psycho / sniper / tough / laser view (added by WeaponsV2Runner).
    /// Runs at execution order 61: after UnitView (60) placed the root and the Animator wrote the clip pose, before UnitAim (62,
    /// torso aim on top of this pose), WeaponHold (80, weapon + hand IK, asks ModifyAim / Kick) and BS01TypeKit (85).
    /// <list type="bullet">
    /// <item>
    /// bazooka muzzle-up on the shoulder, raise it onto the target (0.38 s) and settle (0.22 s) before the first rocket (retry: the sim
    /// gives that rocket 0.8 s of prepare, deviation DV11, and SyncToSim hurries the raise at 2-4x game speed; with DV11 off CombatView's
    /// rocket waits for Ready through WeaponsV2.HoldMissile), aim along the rocket's real ballistic launch direction, then per rocket
    /// a real recoil: the tube climbs 10 deg and shoves back along its axis, the body rocks back 7 deg about the planted feet (review
    /// again 1.6 s after the last target (= ZWorld.ToughCarrySeconds). Servo whirr when it raises.</item>
    /// <item>
    /// shoot and nothing moves it, it kneels (right knee on the ground, left foot planted) or lies prone (propped on the elbows, legs
    /// spread, head up) for the whole engagement: random but stable per engagement (WeaponsV2Logic.PickStance), prone only for long
    /// shots; never while moving, wading, holding grenades or dying; it gets up the moment it moves. Procedural on the Humanoid bones
    /// (segment directions + two-bone IK from the live bone lengths, feet / knee / elbows on the ground height), blended in / out
    /// (kneel 0.45 / 0.35 s, prone 0.8 / 0.6 s). Hooks the clip names kneel_aim / prone_aim when the robot_models_v2 controller has them.
    /// A slow deliberate aim sway and a rifle kick per aimed shot.</item>
    /// <item>PSYCHO / GRUNT / LASER: muzzle climb and jitter per visible round (MG: every round of the burst).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(61)]
    public class RobotWeaponPose : MonoBehaviour
    {
        public UnitView View { get; private set; }
        public string Type { get; private set; }
        public Transform Weapon { get; private set; }
        public Transform Muzzle { get; private set; }
        /// <summary>review: the psycho MG's lower barrel (BS01_Weapon_Psycho `Muzzle_B`, bs01/README_IMPORT.md): the rounds alternate
        /// between the two barrels (they all came out of the top barrel).</summary>
        public Transform MuzzleB { get; private set; }
        public Transform BackBlast { get; private set; }
        /// <summary>Weapon-local points (BS-01 weapon space: grip at 0, barrel -Z, up +Y; measured by tools/bl_probe_rig.py on the weapon
        /// FBX): the sniper scope's front lens and the MG's ejection port on the receiver's right side (-X). Sockets named Scope / EjectPort
        /// (robot_models_v2) override them.</summary>
        public Vector3 ScopeLocal = new Vector3(0f, 0.26f, -0.60f), EjectLocal = new Vector3(-0.13f, 0.22f, -0.30f);   // probe: scope y .05-.63 m top .31; receiver .18-.50

        ZGame game; Animator anim; bool human; Transform scopeSock, ejectSock, feedSock;
        float scale = 1f;

        // ---- kicks (all types)
        float kickAt = -100f, kickClimb, kickShove;
        public void OnRound(float climbDeg, float shoveM) { kickAt = Time.time; kickClimb = climbDeg * RecoilFeel.Climb(Type); kickShove = shoveM; shotClipAt = Time.time; }

        // ---- tough
        public ToughTimeline Tough { get; private set; }
        /// <summary>
        /// replaces the old 4.5 deg tilt + 12 cm root slide that dragged the planted feet.</summary>
        public const float RockBackDeg = 7f;
        float servoAt = -100f; Vector3 lastRoot; Quaternion lastRootRot; bool rootWritten;
        ZObject predicted; float nextScan; bool extWasBusy;

        // ---- sniper
        public Stance Want { get; private set; } = Stance.Stand;
        public Stance Current { get; private set; } = Stance.Stand;
        public float PoseWeight { get; private set; }
        public int Engagements { get; private set; }
        int engTarget = -1; float lastEngagedAt = -100f, poseRaw; Vector3 prevRoot; float drawnSpeed; bool prevInit;
        public int StanceKneels, StanceProne, StanceStand;        // WeaponsV2Check counters (per engagement)
        float thudAt = -100f; bool landed;

        // pose bones (Humanoid)
        Transform hips, spine, chest, upperChest, neck, head, lUp, lLow, lFoot, lToe, rUp, rLow, rFoot, rToe, lArm, lFore, lHand, rArm, rFore, rHand;
        Transform[] poseBones; Quaternion[] animLocal, written; Vector3 hipsAnimPos, hipsWritten; bool hasWritten;
        float ankleH0 = -1f;
        /// <summary>
        /// stance, that stance plays the clips (down -> aim loop -> fire per aimed shot -> up) instead of the procedural pose.</summary>
        public static string KneelDownClip = "sniper_kneel_down", KneelAimClip = "sniper_kneel_aim", KneelFireClip = "sniper_kneel_fire", KneelUpClip = "sniper_kneel_up",
            ProneDownClip = "sniper_prone_down", ProneAimClip = "sniper_prone_aim", ProneFireClip = "sniper_prone_fire", ProneUpClip = "sniper_prone_up",
            ToughPrepare = "tough_prepare", ToughFire = "tough_fire";
        bool clipKneel, clipProne, hasToughPrepare, hasToughFire;
        /// <summary>false = this package does not drive the sniper stances (another owner, e.g. a UnitView patch, does).</summary>
        public static bool DriveStances = true;
        string clipState; float clipStateAt, shotClipAt = -100f; bool clipUpPlayed, stanceUp;

        //      BS-01 snipers (kneel / prone clips) and Toughs (tough_prepare / tough_aim / tough_fire) through UnitView + UnitAim hooks, and
        //      holds the first rocket while tough_prepare raises the tube (UnitAim, BS01Stance.HoldsFire). With both packages in, only
        //      ONE may drive each thing: when a BS01Stance component sits on this view, this package steps back automatically (no
        //      procedural stance, no stance / tough clips, no own rocket hold while BS01Stance holds it); the sight, glint, crack,
        //      tube raise (WeaponHold aim), recoil and FX stay. Found by type NAME (no compile dependency on the other package).
        Component ext; float nextExtScan; System.Reflection.PropertyInfo extPose, extBusy, extHolds;
        /// <summary>True when robot_models_v2's BS01Stance drives this robot's stance / tough clips.</summary>
        public bool External => ext;
        void ScanExternal(float now)
        {
            if (ext || now < nextExtScan) return;
            nextExtScan = now + 1f;
            ext = GetComponent("BS01Stance");
            if (!ext) return;
            var ty = ext.GetType();
            extPose = ty.GetProperty("Pose"); extBusy = ty.GetProperty("Busy"); extHolds = ty.GetProperty("HoldsFire");
        }
        T ExtGet<T>(System.Reflection.PropertyInfo p, T fallback)
        {
            if (!ext || p == null) return fallback;
            try { return p.GetValue(ext) is T v ? v : fallback; } catch (System.Exception) { return fallback; }
        }
        /// <summary>robot_models_v2's BS01Stance is holding this Tough's first rocket for tough_prepare (UnitAim holds it).</summary>
        public bool ExternalHoldsFire => ExtGet(extHolds, false);

        public static RobotWeaponPose Attach(UnitView v)
        {
            if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot) return null;
            var p = v.GetComponent<RobotWeaponPose>(); if (!p) p = v.gameObject.AddComponent<RobotWeaponPose>();
            p.Bind(v);
            return p;
        }

        void Bind(UnitView v)
        {
            View = v; Type = v.Obj.Type; game = ZGame.I;
            anim = GetComponentInChildren<Animator>();
            human = anim && anim.isHuman;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!Weapon && t.name.StartsWith("BS01_Weapon_")) Weapon = t;                     // the outer node (WeaponHold poses it)
                else if (!Weapon && (t.name == "Rifle_Heavy" || t.name == "Bazooka")) Weapon = t;
            }
            if (Weapon)
                foreach (var t in Weapon.GetComponentsInChildren<Transform>(true))
                {
                    if (!Muzzle && (t.name == "Muzzle" || t.name == "RifleMuzzle" || t.name == "BazookaMuzzle" || t.name == "MG_Muzzle")) Muzzle = t;
                    if (!MuzzleB && (t.name == "Muzzle_B" || t.name == "MG_Muzzle_B")) MuzzleB = t;
                    if (!BackBlast && (t.name == "BackBlast" || t.name == "BazookaBackBlast" || t.name == "Backblast" || t.name == "TubeRear")) BackBlast = t;
                    if (t.name == "Scope" || t.name == "ScopeLens") scopeSock = t;
                    if (t.name == "EjectPort" || t.name == "Eject" || t.name == "CasingPort") ejectSock = t;
                    if (t.name == "MG_FeedPort" || t.name == "FeedPort") feedSock = t;
                }
            if (!Muzzle) foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "RifleMuzzle" || t.name == "Muzzle") { Muzzle = t; break; }
            if (scopeSock && Space) ScopeLocal = Space.InverseTransformPoint(scopeSock.position);
            if (ejectSock && Space) EjectLocal = Space.InverseTransformPoint(ejectSock.position);
            else if (feedSock && Space)
            {   // retry: robot_models_v2's psycho MG (BS01_Weapon_Psycho_v2) is belt-fed from the back drum through MG_FeedPort on the gun's
                // LEFT (+X): the brass leaves on the other side, never out of the belt
                var feed = Space.InverseTransformPoint(feedSock.position);
                if (Mathf.Sign(feed.x) == Mathf.Sign(EjectLocal.x)) EjectLocal.x = -EjectLocal.x;
                EjectLocal.z = feed.z; EjectLocal.y = Mathf.Min(EjectLocal.y, feed.y + 0.02f);
            }
            if (Type == "tough") Tough = new ToughTimeline(v.Obj.Id * 31 + 7);
            if (human)
            {
                hips = anim.GetBoneTransform(HumanBodyBones.Hips); spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                chest = anim.GetBoneTransform(HumanBodyBones.Chest); upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                neck = anim.GetBoneTransform(HumanBodyBones.Neck); head = anim.GetBoneTransform(HumanBodyBones.Head);
                lUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); lLow = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot); lToe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
                rUp = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg); rLow = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot); rToe = anim.GetBoneTransform(HumanBodyBones.RightToes);
                lArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                if (!upperChest) upperChest = chest;
                poseBones = new[] { hips, spine, chest, upperChest, neck, head, lUp, lLow, lFoot, rUp, rLow, rFoot, lArm, lFore, rArm, rFore };
                bool ok = true; foreach (var b in poseBones) ok &= b;
                ok &= lToe && rToe && lHand && rHand;
                if (!ok) poseBones = null;
                else { animLocal = new Quaternion[poseBones.Length]; written = new Quaternion[poseBones.Length]; }
                clipKneel = Has(KneelAimClip); clipProne = Has(ProneAimClip);
            }
            if (anim) { hasToughPrepare = Has(ToughPrepare); hasToughFire = Has(ToughFire); }
            float h = Height(); scale = h > 0.5f ? h / 2.338f : Mathf.Max(0.3f, transform.lossyScale.y);   // BS-01 is 2.338 m at scale 1
        }

        float Height()
        {
            float top = float.NegativeInfinity, bot = float.PositiveInfinity;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) { top = Mathf.Max(top, r.bounds.max.y); bot = Mathf.Min(bot, r.bounds.min.y); }
            return float.IsInfinity(top) ? 0f : top - bot;
        }

        public float Scale => scale;
        /// <summary>The weapon space the sockets live in (the socket's parent; BS-01: the weapon root, barrel -Z, up +Y).</summary>
        public Transform Space => Muzzle && Muzzle.parent ? Muzzle.parent : Weapon;
        Vector3 Front() { var f = -transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }   // robot models face -z
        float GroundY(Vector3 p) => game ? game.GroundY(p.x, p.z) : transform.position.y;

        // ------------------------------------------------------------------------------------------ aim / kick (WeaponHold)
        static Vector3 Tilt(Vector3 v, Vector3 toward, float deg)
        {
            var ax = Vector3.Cross(v, toward); if (ax.sqrMagnitude < 1e-8f || Mathf.Abs(deg) < 1e-3f) return v;
            return Quaternion.AngleAxis(deg, ax.normalized) * v;
        }

        /// <summary>WeaponHold: the aim after carry / raise / ballistic / climb / sway.</summary>
        public Vector3 ModifyAim(Vector3 aim)
        {
            if (View == null || View.Dying) return aim;
            float now = Time.time, age = now - kickAt;
            if (Tough != null)
            {
                if (TryBallistic(out var ball)) aim = KeepHeading(ball, aim);
                if (External) return aim;                                                            // retry: BS01Stance carries / lowers / climbs the tube (WeaponHold.carryPitch): only the ballistic launch line (+ the shove) from here
                float carry = 1f - Tough.RaiseShown;
                aim = Tilt(aim, Vector3.up, 38f * carry);                                            // carried muzzle-up on the shoulder
                aim = Tilt(aim, Vector3.up, 1.6f * Wobble(now - Tough.RaisedAt));                   // settle after the raise
                aim = Tilt(aim, Vector3.up, kickClimb * Kick(age) + 2.2f * Wobble(age - 0.12f));   // recoil climb + settle
                return aim;
            }
            float climb = kickClimb * Kick(age);
            if (Type == "sniper")
            {   // deliberate: a slow breathing sway while it aims (smaller prone / kneeling), a sharp climb per aimed shot
                float steady = Current == Stance.Prone ? 0.25f : Current == Stance.Kneel ? 0.5f : 1f;
                float t = now + View.Obj.Id * 0.37f;
                aim = Tilt(aim, Vector3.up, climb + 0.55f * steady * Mathf.Sin(t * 1.7f));
                aim = Tilt(aim, Vector3.Cross(Vector3.up, aim), 0.45f * steady * Mathf.Sin(t * 1.1f + 1.3f));
                return aim;
            }
            if (climb > 0.01f)
            {   // MG / rifle / laser: climb + a little random side jitter per round (the psycho shakes)
                float j = Mathf.Sin(kickAt * 91.7f) * climb * 0.45f;
                aim = Tilt(Tilt(aim, Vector3.up, climb), Vector3.Cross(Vector3.up, aim), j);
            }
            return aim;
        }

        /// <summary>
        /// the target, which UnitAim keeps within 75 deg of the chest, or the body front when UnitAim has no target yet).</summary>
        public const float MaxBallisticYaw = 20f;

        /// <summary>
        /// a new engagement) swung the tube backwards through the helmet while UnitView was still turning the body (up to ~0.3 s).
        /// The ballistic line keeps its elevation, its heading stays within MaxBallisticYaw of the incoming aim; for a target in front
        /// both agree (a few degrees), so nothing changes there.</summary>
        static Vector3 KeepHeading(Vector3 ball, Vector3 aim)
        {
            var fb = new Vector3(ball.x, 0f, ball.z); var fa = new Vector3(aim.x, 0f, aim.z);
            if (fb.sqrMagnitude < 1e-6f || fa.sqrMagnitude < 1e-6f) return ball;
            float off = Vector3.SignedAngle(fa, fb, Vector3.up);
            if (Mathf.Abs(off) <= MaxBallisticYaw) return ball;
            float el = Mathf.Asin(Mathf.Clamp(ball.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            var flat = Quaternion.AngleAxis(Mathf.Sign(off) * MaxBallisticYaw, Vector3.up) * fa.normalized;
            return Tilt(flat, Vector3.up, el);
        }

        /// <summary>WeaponHold.ShoulderHold: the bazooka's rest point shoves back along the tube on each rocket.</summary>
        public Vector3 KickOffset(Vector3 aim) => -aim.normalized * kickShove * scale * Kick(Time.time - kickAt);

        /// <summary>The direction the rocket really leaves in (OpenZFxPlayer.Launch: v = (to - from) / T + up g T / 2, g = 6, T = the
        /// sim flight / game speed), so the tube points where the rocket goes.</summary>
        bool TryBallistic(out Vector3 dir)
        {
            dir = default;
            var o = View.Obj; var t = o.AttackObject;
            if (t == null || t.Destroyed) t = predicted;
            if (t == null || t.Destroyed || game == null || !Muzzle) return false;
            var from = Muzzle.position; var to = OzShuttleAim.AirAim(t, game.CenterWorld(t) + Vector3.up * 0.25f, 0f);   // DV118: up to the flying shuttle
            float dxp = t.CenterX - o.CenterX, dyp = t.CenterY - o.CenterY;
            float T = Mathf.Max(0.12f, Mathf.Sqrt(dxp * dxp + dyp * dyp) / Mathf.Max(1, o.MissileSpeed)) / Mathf.Max(0.25f, game.gameSpeed);
            var v = (to - from) / T; v.y += OpenZFxCatalog.GravityOf(FxWeapon.Bazooka) * T * 0.5f;
            if (v.sqrMagnitude < 1e-4f) return false;
            dir = v.normalized;
            float el = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float elHi = OzShuttleAim.High(t) ? 50f : 30f;                                            // DV118: the tube lifts higher at the flying shuttle
            if (el > elHi || el < -20f) { var flat = new Vector3(dir.x, 0f, dir.z).normalized; dir = Tilt(flat, Vector3.up, Mathf.Clamp(el, -20f, elHi)); }
            return true;
        }

        // ------------------------------------------------------------------------------------------ tough (runner hooks)
        public float ToughReadyIn() => Tough == null ? 0f : Tough.ReadyIn(Time.time);
        public void OnToughFire() { if (Tough == null) return; Tough.OnFire(Time.time); OnRound(External ? 0f : 10f, 0.16f); if (!External && !View.Reacting) OnToughFireClip(); }   // review: BS01Stance plays its own tough_fire (retry: and climbs its own tube); rebase: never over a deaths_mech_v1 knockdown

        /// <summary>retry: the sniper is (going) prone: its first visible shot waits until it lies steady (prone down 0.8 s).</summary>
        public bool GoingProne => External ? ExtGet(extBusy, false) && ExtGet(extPose, "") == "prone" : Want == Stance.Prone && Current != Stance.Stand && poseRaw < 1f;

        /// <summary>The target the sim will engage (UnitAim.Predict's rule, zobject.cpp:1603-1853): the raise starts as soon as a
        /// Tough stands still with an enemy in range, up to one passive-engage check (0.5 s) before the sim engages.</summary>
        ZObject Predict()
        {
            var o = View.Obj; var w = game ? game.World : null;
            if (w == null || !o.CanAttack() || o.Owner == Team.Neutral) return null;
            if (o.AttackObject != null && !o.AttackObject.Destroyed) return o.AttackObject;
            if (o.IsMoving) return null;
            foreach (var t in w.Objects)
                if (t.IsUnit && !t.Destroyed && t.KillAt < 0 && t.Owner != Team.Neutral && t.Owner != o.Owner && o.CanAttackObject(t) && o.WithinAttackRadius(t)) return t;
            return null;
        }

        void UpdateTough(float dt)
        {
            float now = Time.time;
            if ((nextScan -= dt) <= 0f) { nextScan = 0.25f; predicted = Predict(); }
            var o = View.Obj;
            bool target = (o.AttackObject != null && !o.AttackObject.Destroyed) || (predicted != null && !predicted.Destroyed);
            float before = Tough.Raise01;
            // retry, DV11 (ZObject.ToughPrepare.cs): the sim says when the first rocket leaves; raise faster if the normal raise + settle
            // would not be done by then (game speed 2-4x), so the rocket always leaves a steady tube at Zod's speed
            var w = game ? game.World : null;
            if (w != null && o.ToughReadyAt > 0) Tough.SyncToSim(now, (float)(o.ToughReadyAt - w.Time) / Mathf.Max(0.25f, game.gameSpeed));
            Tough.Step(now, dt, target);
            if (!WeaponsV2.Enabled) return;                                                           // review: the A/B toggle also turns the body recoil / brace / servo off
            if (View.Reacting) { rootWritten = false; return; }
            if (!External) ToughClips(before);                                                        // review: BS01Stance plays tough_prepare itself
            bool rising = External ? ExtGet(extBusy, false) && !extWasBusy : before <= 0.01f && Tough.Raise01 > 0.01f;   // retry: with BS01Stance the whirr goes with ITS tough_prepare
            if (External) extWasBusy = ExtGet(extBusy, false);
            if (rising && now - servoAt > 1.5f && CombatAudio.I)
            {   servoAt = now; CombatAudio.I.PlayPitched("servo", transform.position + Vector3.up * 1.8f * scale, WeaponsV2.GainServo, 0.5f, o.Id, 1f, WeaponsV2.Pick(WeaponsV2.BankServo)); }
            if (External) return;                                                                     // retry: tough_fire (BS01Stance) kicks the body + steps back itself: never twice
            // body: braced into the tube while aiming, thrown back by each rocket (about the feet), then settles
            // stand, the robot rocks onto its heels); it is no longer also slid 12 cm back, which dragged the planted feet over the ground
            if (rootWritten && transform.position == lastRoot && transform.rotation == lastRootRot) return;   // UnitView did not place the root this frame
            float k = Kick(now - Tough.FiredAt), wob = Wobble(now - Tough.FiredAt - 0.15f);
            var F = Front();
            float tiltBack = RockBackDeg * k + 1.2f * wob - 2.0f * Tough.RaiseShown * (1f - k);
            if (Mathf.Abs(tiltBack) > 0.01f) transform.rotation = Quaternion.AngleAxis(tiltBack, Vector3.Cross(Vector3.up, -F).normalized) * transform.rotation;
            lastRoot = transform.position; lastRootRot = transform.rotation; rootWritten = true;
        }

        // ------------------------------------------------------------------------------------------ sniper stances
        void UpdateSniper(float dt)
        {
            float now = Time.time; var o = View.Obj;
            if (View.Reacting)
            {
                // (never EndClip here: that would cross-fade UnitView's shooting / idle over blast_airborne); the stance comes back after it is up
                poseRaw = 0f; PoseWeight = 0f; hasWritten = false; clipActive = false; clipUpPlayed = false; prevInit = false;
                return;
            }
            var p = transform.position;
            if (prevInit && dt > 0f) drawnSpeed = Mathf.Lerp(drawnSpeed, Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prevRoot.x, 0, prevRoot.z)) / dt, 1f - Mathf.Exp(-dt / 0.15f));
            prevRoot = p; prevInit = true;
            bool engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
            EngagementStance();
            bool wet = OpenZFxPlayer.I && OpenZFxPlayer.I.isActiveAndEnabled && OpenZFxPlayer.I.WetAt(p);
            bool grenades = o.Grenades > 0 || (o.Leader != null && o.Leader.Grenades > 0);
            bool recent = engaged || now - lastEngagedAt < 1.5f;                                      // between rolls / target switch: stay down
            bool can = recent && !o.IsMoving && drawnSpeed < 0.35f && !wet && !grenades && !View.OnTower
                       && WeaponsV2.Enabled;                                                          // review: the A/B toggle stands them up (was: stayed down)
            var target = SniperCoverView.Drawn(o, can ? Want : Stance.Stand);   // DV122: the rules' stance while they decide it (View/SniperCoverView.cs)
            if (!recent) engTarget = -1;
            // one pose at a time: blend the current one out before another comes in
            if (target != Current && poseRaw <= 0f) { Current = target; landed = false; }
            bool up = target == Current && Current != Stance.Stand;
            stanceUp = up;
            float downT = Current == Stance.Prone ? ProneDown : KneelDown, upT = Current == Stance.Prone ? ProneUp : KneelUp;
            if (o.IsMoving) upT *= 0.6f;                                                               // gets up fast when ordered away
            poseRaw = Mathf.Clamp01(poseRaw + (up ? dt / downT : -dt / upT));
            PoseWeight = poseRaw * poseRaw * (3f - 2f * poseRaw);
            if (up && !landed && poseRaw >= 1f)
            {
                landed = true;
                if (now - thudAt > 2f && CombatAudio.I) { thudAt = now; CombatAudio.I.PlayPitched("servo", p, WeaponsV2.GainThud, 0.5f, o.Id, 1f, WeaponsV2.Pick(WeaponsV2.BankThud)); }
                if (Current == Stance.Prone && OpenZFxPlayer.I) OpenZFxPlayer.I.Dust(p + Front() * 0.6f * scale, 0.35f);
            }
            if (up && poseRaw < 0.05f && CombatAudio.I && now - servoAt > 1.5f) { servoAt = now; CombatAudio.I.PlayPitched("servo", p + Vector3.up, WeaponsV2.GainServo * 0.8f, 0.5f, o.Id, 1f, WeaponsV2.Pick(WeaponsV2.BankServo)); }
            ApplyPose();
        }

        /// <summary>The stance of the current engagement: picked once when a new engagement starts (a new target while standing, or any
        /// target after 1.5 s without one; a target switch while down keeps the stance), WeaponsV2Logic.PickStance (random but stable:
        /// hash of unit id + engagement number; prone only on long shots). Idempotent within an engagement, so robot_models_v2's
        /// BS01Stance may ask it every frame (SniperGoesDown).</summary>
        public Stance EngagementStance()
        {
            var o = View != null ? View.Obj : null; float now = Time.time;
            if (o == null || o.AttackObject == null || o.AttackObject.Destroyed) return Want;
            bool fresh = o.AttackObject.Id != engTarget;
            if (fresh && SniperStanceGate.NewEngagement(External, PoseWeight, now - lastEngagedAt))   // K248b: with BS01Stance on the robot a new stance only for a new fight (View/SniperStanceGate.cs)
            {   // a new engagement: pick its stance once (stable while it lasts)
                Engagements++;
                float range01 = o.AttackRadius > 0 ? Mathf.Sqrt((float)(o.AttackObject.CenterX - o.CenterX) * (o.AttackObject.CenterX - o.CenterX) +
                                                                (float)(o.AttackObject.CenterY - o.CenterY) * (o.AttackObject.CenterY - o.CenterY)) / o.AttackRadius : 0f;
                Want = PickStance(o.Id, Engagements, range01);
                if (Want == Stance.Prone && o.IsFortDefender) Want = Stance.Kneel;                  // DV111: the fort's roof sniper kneels in his sandbag nest, never lies on the deck
                if (Want == Stance.Prone) StanceProne++; else if (Want == Stance.Kneel) StanceKneels++; else StanceStand++;
            }
            engTarget = o.AttackObject.Id; lastEngagedAt = now;
            return Want;
        }

        /// <summary>
        /// for "sometimes" (stand 22 % / kneel 43 % / prone 35 % on long shots, kneel 40 % on short ones, stable per engagement), BS01Stance
        /// alone always goes down. false = stay standing this engagement; pose = "kneel" / "prone" (null = BS01Stance's own choice: this
        /// package is off, or the robot has no RobotWeaponPose yet).</summary>
        public static bool SniperGoesDown(Component stance, out string pose)
        {
            pose = null;
            if (!WeaponsV2.Enabled || !stance) return true;
            if (!stance.TryGetComponent(out RobotWeaponPose p) && stance.TryGetComponent(out UnitView uv)) p = Attach(uv);   // before the runner's 0.5 s attach scan
            if (!p || p.View == null || p.Type != "sniper") return true;
            var st = p.EngagementStance();
            if (st == Stance.Stand) return false;
            pose = st == Stance.Prone && p.Has(ProneDownClip) ? "prone" : "kneel";
            return true;
        }

        /// <summary>UnitView.Die (weapons_v2 hook): re-pose now, so the ragdoll starts from the kneeling / lying body, not the clip's.</summary>
        public void BeforeDeath() { if (PoseWeight > 0.001f && poseBones != null && anim && anim.enabled) ApplyPose(); }

        void ApplyPose()
        {
            if (poseBones == null) return;
            // Animator culled (CullUpdateTransforms) left last frame's pose: start again from the clip pose we saved
            if (hasWritten)
            {
                for (int i = 0; i < poseBones.Length; i++) if (poseBones[i].localRotation == written[i]) poseBones[i].localRotation = animLocal[i];
                if (hips.localPosition == hipsWritten) hips.localPosition = hipsAnimPos;
            }
            if (PoseWeight <= 0.001f)
            {
                hasWritten = false;
                if (clipActive) EndClip();
                clipUpPlayed = false;
                if (drawnSpeed < 0.1f && Current == Stance.Stand) MeasureStanding();
                return;
            }
            if ((Current == Stance.Kneel && clipKneel) || (Current == Stance.Prone && clipProne)) { hasWritten = false; PlayClip(); return; }
            for (int i = 0; i < poseBones.Length; i++) animLocal[i] = poseBones[i].localRotation;
            hipsAnimPos = hips.localPosition;
            if (Current == Stance.Kneel) Kneel(); else Prone();
            float w = PoseWeight;
            if (w < 0.999f)
            {
                for (int i = 0; i < poseBones.Length; i++) poseBones[i].localRotation = Quaternion.Slerp(animLocal[i], poseBones[i].localRotation, w);
                hips.localPosition = Vector3.Lerp(hipsAnimPos, hips.localPosition, w);
            }
            for (int i = 0; i < poseBones.Length; i++) written[i] = poseBones[i].localRotation;
            hipsWritten = hips.localPosition; hasWritten = true;
        }

        void MeasureStanding()
        {
            if (lFoot == null) return;
            float h = lFoot.position.y - GroundY(lFoot.position);
            if (h > 0.05f * scale && h < 0.6f * scale) ankleH0 = ankleH0 < 0f ? h : Mathf.Lerp(ankleH0, h, 0.1f);
        }

        bool Has(string state) => anim && anim.runtimeAnimatorController && anim.HasState(0, Animator.StringToHash(state));

        /// <summary>robot_models_v2 route: the controller has the stance clips. Down (if present) while the weight rises, then the aim
        /// loop, the fire clip for each aimed shot, the up clip while the weight falls; re-asserted when UnitView cross-fades its own
        /// state in; played at 1x (UnitView slows engaged robots to Zod's fire cycle through anim.speed).</summary>
        void PlayClip()
        {
            bool kneel = Current == Stance.Kneel;
            string down = kneel ? KneelDownClip : ProneDownClip, aim = kneel ? KneelAimClip : ProneAimClip, fire = kneel ? KneelFireClip : ProneFireClip, upc = kneel ? KneelUpClip : ProneUpClip;
            float now = Time.time, downLen = kneel ? KneelDown_s : ProneDown_s;
            string want;
            if (!stanceUp) want = Has(upc) ? upc : aim;                                                 // getting up
            else if (poseRaw < 1f && Has(down)) want = down;                                            // going down
            else if (now - shotClipAt < 0.42f && Has(fire)) want = fire;
            else want = aim;
            anim.speed = 1f;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            bool inIt = info.IsName(want) || (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName(want));
            bool oneShot = want == down || want == fire || want == upc;
            if (want == upc && clipUpPlayed && inIt) return;
            if (!inIt || (want != clipState && !oneShot))
            {
                if (oneShot && want == clipState && now - clipStateAt < (want == down ? downLen : 0.42f)) return;   // do not restart a one-shot
                anim.CrossFadeInFixedTime(want, want == fire ? 0.05f : 0.15f, 0);
                clipState = want; clipStateAt = now; clipActive = true;
                if (want == upc) clipUpPlayed = true;
            }
        }
        const float KneelDown_s = 0.417f, ProneDown_s = 0.833f;          // robot_models_v2 clip lengths (v2_anim_report.json)

        /// <summary>Tough clips of robot_models_v2 (when present): tough_prepare while the bazooka rises (started 0.23 s in, so its
        /// 0.83 s end lands on Ready at 0.6 s), tough_fire on each rocket; UnitView's bazooka_idle / bazooka_fire states do the rest.</summary>
        void ToughClips(float before)
        {
            if (!anim) return;
            if (hasToughPrepare && before <= 0.01f && Tough.Raise01 > 0.01f) anim.CrossFadeInFixedTime(ToughPrepare, 0.12f, 0, 0.23f);
        }
        public void OnToughFireClip() { if (hasToughFire && anim) anim.CrossFadeInFixedTime(ToughFire, 0.05f, 0); }

        /// <summary>Back to UnitView's own base state (it only re-sets a state when its choice changes, so it would stay in the clip).</summary>
        void EndClip()
        {
            clipActive = false;
            var o = View.Obj; bool engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
            if (!o.IsMoving) anim.CrossFadeInFixedTime(engaged ? "shooting" : "idle", 0.25f, 0);
        }
        bool clipActive;

        static void AimBone(Transform bone, Vector3 childWorld, Vector3 dir)
        {
            var cur = childWorld - bone.position;
            if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        /// <summary>Analytic two-bone IK (WeaponHold.TwoBone): upper a, lower b, end c to target, knee / elbow toward the hint.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
            var at = target - a.position; float d = Mathf.Clamp(at.magnitude, 0.01f, la + lb - 0.001f);
            var dir = at.normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1f, 1f), sinA = Mathf.Sqrt(1 - cosA * cosA);
            var hp = hint - a.position; var perp = hp - dir * Vector3.Dot(hp, dir);
            perp = perp.sqrMagnitude < 1e-6f ? Vector3.up : perp.normalized;
            var elbow = a.position + dir * (la * cosA) + perp * (la * sinA);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + dir * d - b.position) * b.rotation;
        }

        /// <summary>One knee down: right knee on the ground under the hip, right shin back along the ground on its toes, left foot
        /// planted 0.4 m ahead (two-bone IK), torso leaning 8 deg into the rifle. BS-01 numbers are scaled by the live model height.</summary>
        void Kneel()
        {
            var U = Vector3.up; var F = Front(); var R = Vector3.Cross(U, F).normalized;
            float s = scale, gy = GroundY(transform.position);
            float Lt = Vector3.Distance(rUp.position, rLow.position), Ls = Vector3.Distance(rLow.position, rFoot.position);
            float kneeClear = 0.13f * s, back = 0.10f * s;
            float hipY = gy + kneeClear + Mathf.Sqrt(Mathf.Max(0.01f * s * s, Lt * Lt - back * back));
            hips.position += U * (hipY - rUp.position.y) + F * (0.04f * s);
            spine.rotation = Quaternion.FromToRotation(U, (U + F * 0.14f).normalized) * spine.rotation;   // 8 deg forward
            var kneeR = new Vector3(rUp.position.x, gy + kneeClear, rUp.position.z) - F * back + R * (0.04f * s);
            AimBone(rUp, rLow.position, kneeR - rUp.position);
            float ankleH = 0.26f * s, rise = ankleH - kneeClear;
            AimBone(rLow, rFoot.position, -F * Mathf.Sqrt(Mathf.Max(0.001f, Ls * Ls - rise * rise)) + U * rise);
            AimBone(rFoot, rToe.position, (-U * 0.62f - F * 0.78f).normalized);
            float a0 = ankleH0 > 0f ? ankleH0 : 0.31f * s;
            var ankleL = new Vector3(lUp.position.x, gy + a0, lUp.position.z) + F * (0.40f * s) - R * (0.05f * s);
            TwoBone(lUp, lLow, lFoot, ankleL, lLow.position + F * s + U * 0.4f * s);
            AimBone(lFoot, lToe.position, (F * 0.67f - U * 0.74f).normalized);
        }

        /// <summary>Prone: the body pitched flat along its front, pelvis 0.32 m up (the belly clears the ground), chest propped on the
        /// elbows (spine rising 17 / 27 / 32 deg), head up looking forward, legs back and spread, toes in the ground, elbows on the
        /// ground ahead of the shoulders, forearms forward to the rifle (WeaponHold then puts the rifle in the right palm on the aim
        /// and the left fist on the foregrip).</summary>
        void Prone()
        {
            var U = Vector3.up; var F = Front(); var R = Vector3.Cross(U, F).normalized;
            float s = scale, gy = GroundY(transform.position + F * 0.3f * s);
            AimBone(hips, spine.position, (F + U * 0.12f).normalized);                                    // the whole body pitches forward
            var root = transform.position;
            hips.position = new Vector3(root.x, gy + 0.32f * s, root.z) - F * (0.25f * s);
            AimBone(spine, chest.position, (F + U * 0.30f).normalized);
            if (upperChest != chest) AimBone(chest, upperChest.position, (F + U * 0.50f).normalized);
            AimBone(upperChest, neck.position, (F + U * 0.62f).normalized);
            AimBone(neck, head.position, (F * 0.55f + U * 0.83f).normalized);
            AimBone(lUp, lLow.position, (-F - R * 0.20f - U * 0.10f).normalized);
            AimBone(lLow, lFoot.position, (-F - R * 0.12f - U * 0.04f).normalized);
            AimBone(lFoot, lToe.position, (-U * 0.6f - F * 0.8f).normalized);
            AimBone(rUp, rLow.position, (-F + R * 0.20f - U * 0.10f).normalized);
            AimBone(rLow, rFoot.position, (-F + R * 0.12f - U * 0.04f).normalized);
            AimBone(rFoot, rToe.position, (-U * 0.6f - F * 0.8f).normalized);
            float gArm = GroundY(neck.position);
            Arm(rArm, rFore, rHand, F, R, U, gArm, -1f);
            Arm(lArm, lFore, lHand, F, R, U, gArm, 1f);
        }

        void Arm(Transform arm, Transform fore, Transform hand, Vector3 F, Vector3 R, Vector3 U, float gy, float inward)
        {
            float La = Vector3.Distance(arm.position, fore.position);
            float elbowY = gy + 0.12f * scale;
            float v = Mathf.Clamp((arm.position.y - elbowY) / Mathf.Max(0.01f, La), 0.25f, 0.98f), hz = Mathf.Sqrt(1f - v * v);
            AimBone(arm, fore.position, -U * v + (F * 0.93f + R * (0.37f * inward)).normalized * hz);
            AimBone(fore, hand.position, (F * 0.94f + R * (0.30f * inward) + U * 0.12f).normalized);
        }

        // ------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (View == null || View.Obj == null) { enabled = false; return; }
            if (View.Dying) { PoseWeight = 0f; poseRaw = 0f; hasWritten = false; return; }
            float dt = Time.deltaTime;
            ScanExternal(Time.time);
            if (Type == "tough" && Tough != null) UpdateTough(dt);
            else if (Type == "sniper")
            {
                if (External) MirrorExternal();                                                       // review: robot_models_v2 drives the stance
                else if (human && DriveStances) UpdateSniper(dt);
                SniperShootClip();
            }
        }

        /// <summary>review: BS01Stance's stance, read for the aim sway and the ground dust of the crack (its clips pose the body).</summary>
        void MirrorExternal()
        {
            EngagementStance();                                                                       // retry: engagement tracking (SniperGoesDown asks it too)
            bool busy = ExtGet(extBusy, false); string pose = ExtGet(extPose, "");
            Current = !busy ? Stance.Stand : pose == "prone" ? Stance.Prone : pose == "kneel" ? Stance.Kneel : Stance.Stand;
            PoseWeight = Current == Stance.Stand ? 0f : 1f; poseRaw = 0f; hasWritten = false;
        }

        //      Zod client fire cycle (parity P6: 0.778 s), which matched the drawn shots before; weapons_v2 draws an aimed shot only every
        //      1.05-1.6 s, so the robot kicked once or twice per visible shot with NO shot, also while the laser sight was on: it read
        //      like the grunts' shooting, not like aiming. Now: one kick at the clip's own rate on each VISIBLE aimed
        //      shot (OnRound), then the pose holds still on the loop's start frame (the aim) until the next one. The sim is untouched.
        static readonly int ShootingHash = Animator.StringToHash("shooting");
        float kickFor = -100f, kickUntil = -100f; bool kickHold;
        void SniperShootClip()
        {
            if (!anim || !anim.isActiveAndEnabled || !anim.isInitialized) return;
            var o = View.Obj;
            bool engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
            if (!WeaponsV2.Enabled || !engaged || o.IsMoving || clipActive || View.Reacting || (External && ExtGet(extBusy, false))) { kickHold = false; return; }   // rebase: + deaths_mech_v1 knockdown   // retry: a STANDING sniper next to BS01Stance too
            if (anim.IsInTransition(0)) return;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash != ShootingHash) { kickHold = false; return; }
            float now = Time.time;
            if (shotClipAt > kickFor && now - shotClipAt < 0.3f)
            {   // a visible aimed shot left the rifle: one kick from the clip's start at its own rate
                kickFor = shotClipAt; kickUntil = now + Mathf.Clamp(info.length, 0.1f, 0.8f);
                anim.Play(ShootingHash, 0, 0f); anim.speed = 1f; kickHold = false;
                return;
            }
            if (now < kickUntil) { anim.speed = 1f; return; }                                         // the kick plays out (UnitView set the slow P6 rate)
            if (!kickHold) { anim.Play(ShootingHash, 0, 0f); kickHold = true; }                      // loop start = the aim pose (a loop ends where it starts)
            anim.speed = 0f;                                                                          // hold the aim; UnitView re-sets the speed every frame
        }
    }
}

using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Chooses the BS-01 base-layer clips of three robot types while they fight standing still, then hands the layer back to UnitView.
    /// The sim is untouched: this only picks clips (and, for the Tough without weapons_v2, the carry pitch of the tube in WeaponHold).
    ///  * Sniper: an engaged sniper that stands still goes down (kneel for near targets, prone for far ones, a quarter of the squad
    ///    picks the other one), aims, fires with a small kick (sniper_*_fire on every SHOWN shot), stays down a moment after the target
    ///    is gone, gets up (faster when it has to walk off). Never in water, never while throwing a grenade, never while dying.
    ///  * Tough: as soon as it stands still with an enemy in range (the UnitAim / weapons_v2 Predict rule) or engages, tough_prepare
    ///    (0.83 s: step, brace, lean into the tube while the tube comes down from its 16 deg carry pitch = Zod's 0.7-1.0 s aim hold,
    ///    rtough.cpp:211; weapons_v2's DV11 fires the first rocket 0.8 s after the engage) then tough_aim; every rocket plays tough_fire
    ///    (the blast throws the upper body back, the tube climbs, the right foot steps back, recovers) and returns to the aim; 1.6 s after
    ///    the last target (the sim's DV11 carry window) the tube goes back to the carry (bazooka_idle = tough_carry via the override).
    ///    Without DV11 the first rocket is held while the tube still comes down (HoldsFire: UnitAim holds it within its own maxWait, same
    ///    detonation time).
    ///  * Psycho: engaged and standing: psycho_fire (a shaking hip-fire loop) while rounds leave the MG (UnitView.OnFire, per round with
    ///    weapons_v2), psycho_aim (braced, still) in the pauses between bursts: the body shakes exactly while the gun fires. Walking, it
    ///    carries the MG one-handed at the hip (WeaponHold.leftHandFree eases to 1: the left fist rests on its gut, psycho_hold /
    ///    psycho_walkfire upper layer), because the fat body's left arm cannot cross the unbladed walking chest to the carry handle.
    /// UnitView calls Drive() from Sync (before its own base-layer choice) and OnFire() from its OnFire.
    /// leaves the Tough's carry pitch / swing-down / climb and body recoil and the sniper stance clips to it (it keeps the ballistic launch
    /// line, the tube shove, the servo whirr, FX and sounds, and decides WHETHER a sniper goes down: its optional edit R1 patches the
    /// `case St.Up:` lines of DriveSniper, keep them as they are). WeaponHold.maxCarryPitch caps the pitch at what the v2 Tough carries.
    /// here in its exact text (its apply.py reports them "already"); the v3 Tough carries its tube at 8 deg (menu: carryPitch).
    /// </summary>
    [DisallowMultipleComponent]
    public partial class BS01Stance : MonoBehaviour
    {
        public enum Kind { None, Sniper, Tough, Psycho }
        public Kind kind = Kind.None;

        [Header("Sniper")]
        [Tooltip("Targets farther than this fraction of the attack range are shot lying down; nearer ones from one knee")]
        [Range(0f, 1f)] public float proneBeyond = 0.6f;
        [Tooltip("Per-robot variety: this share of snipers always picks the other stance")]
        [Range(0f, 1f)] public float variety = 0.25f;
        [Tooltip("Seconds a sniper stays down after its last target (a new target in that time: no get-up / get-down)")]
        public float linger = 2.5f;
        [Tooltip("Get-up speed when the sniper is ordered to walk (the drawn body already moves)")]
        public float hurryUp = 1.7f;
        [Header("Tough")]
        [Tooltip("Seconds the tube stays up after the last target before it goes back to the carry (the sim's DV11 carry window 1.6 s)")]
        public float toughLinger = 1.6f;
        [Tooltip("Hold the first rocket while tough_prepare lowers the tube onto the target (UnitAim holds it within its maxWait)")]
        public bool holdRocketWhilePreparing = true;
        [Tooltip("tough_fire: WeaponHold's chest levelling starts at this value and eases back to its own over 0.6 s, so the upper-body " +
                 "kick of the clip shows instead of being levelled away")]
        [Range(0f, 1f)] public float kickChestLevel = 0.2f;
        [Tooltip("Tube pitch (deg, muzzle up) while the Tough only carries the bazooka; tough_prepare lowers it onto the target")]
        public float carryPitch = 16f;
        [Tooltip("Muzzle climb of the tube on each rocket (deg)")]
        public float fireClimb = 9f;
        [Header("Psycho")]
        [Tooltip("Seconds without a round before the psycho stops shaking (psycho_fire -> psycho_aim)")]
        public float burstGap = 0.2f;
        [Tooltip("robots_v3: false = both fists stay on the v3 gatling's rear handles while walking (it has two); true = the v2 one-handed " +
                 "walking carry (WeaponHold.leftHandFree eases to 1 while walking)")]
        public bool psychoOneHandWalk = true;

        public bool Busy => st != St.Up;
        /// <summary>robot_models_v2 REVIEW: a sniper kneeling / lying (incl. getting down / up) poses its own legs; RobotLegIK lets go
        /// (its stance lock holds ankles at the standing bind height, flattens the boots and bends the knees forward: a prone robot's
        /// legs would fold up) and the clip's own foot IK places the feet.</summary>
        public bool PosesLegs => kind == Kind.Sniper && st != St.Up;
        /// <summary>RobotLegIK: true while this robot's BS01Stance poses its legs (see PosesLegs).</summary>
        public static bool LegsPosed(UnitView v) { var s = v ? v.Stance : null; return s && s.PosesLegs; }
        /// <summary>UnitAim / weapons_v2: a Tough rocket arriving now should wait (the tube is still coming down onto the target).</summary>
        public bool HoldsFire => holdRocketWhilePreparing && kind == Kind.Tough &&
                                 ((st == St.Prepare && t < prepareLen * 0.85f) || (st == St.Up && wantPrepare));
        /// <summary>"kneel" / "prone" while a sniper is down (weapons_v2 reads it for the aim sway and the crack's ground dust).</summary>
        public string Pose => st == St.Up ? "" : pose;

        enum St { Up, Down, Aim, Fire, GetUp, Prepare, ToughAim, ToughFire, PsychoAim, PsychoFire }
        St st = St.Up;
        string pose = "";
        float t, len, lastEngaged = -100f, lastRound = -100f, nextScan, pitchNow = -1f, pitchFrom;
        bool wantPrepare, predicted, lastWalking;
        float prepareLen = 0.833f, toughFireLen = 1.0f;
        readonly Dictionary<string, float> lengths = new Dictionary<string, float>();
        UnitView view; ZObject obj; Animator anim; WeaponHold hold; float holdLevel = -1f;

        static readonly Dictionary<string, float> DefaultLen = new Dictionary<string, float>
        {
            { "sniper_kneel_down", 0.417f }, { "sniper_kneel_fire", 0.417f }, { "sniper_kneel_up", 0.333f },
            { "sniper_prone_down", 0.833f }, { "sniper_prone_fire", 0.417f }, { "sniper_prone_up", 0.583f },
            { "tough_prepare", 0.833f }, { "tough_fire", 1.0f }, { "psycho_fire", 0.5f },
        };

        public void Bind(UnitView v, ZObject o, Animator a)
        {
            view = v; obj = o; anim = a;
            if (kind == Kind.None && o != null)
                kind = o.Type == "sniper" ? Kind.Sniper : o.Type == "tough" ? Kind.Tough : o.Type == "psycho" ? Kind.Psycho : Kind.None;
            lengths.Clear();
            var rc = a ? a.runtimeAnimatorController : null;
            if (rc) foreach (var c in rc.animationClips) if (c && !lengths.ContainsKey(c.name)) lengths[c.name] = c.length;
            prepareLen = Len("tough_prepare"); toughFireLen = Len("tough_fire");
            hold = GetComponent<WeaponHold>(); holdLevel = hold ? hold.bazookaChestLevel : -1f;
            st = St.Up; pitchNow = -1f;
        }

        float Len(string clip) => lengths.TryGetValue(clip, out var l) && l > 0.01f ? l : DefaultLen.TryGetValue(clip, out var d) ? d : 0.5f;
        bool Has(string state) => anim && anim.HasState(0, Animator.StringToHash(state));

        void Play(string state, float fade, float clipLen, float spd = 1f)
        {
            RestoreLevel();
            if (!Has(state)) return;
            anim.CrossFadeInFixedTime(state, fade, 0, 0f);
            t = 0f; len = clipLen; anim.speed = spd;
        }

        /// <summary>UnitView.Sync, before its own base-layer choice. True = the stance owns the base layer this frame.
        /// viewState is UnitView's cached base state: set to "stance" while a stance plays and back to null (or the state it left the
        /// Animator in) when it hands over, so UnitView re-issues (or keeps) its own choice.</summary>
        public bool Drive(bool engaged, bool walking, bool throwing, ref string viewState, float animMul, float dt)
        {
            lastWalking = walking;
            if (kind == Kind.Psycho && hold) hold.leftHandFree = Mathf.MoveTowards(hold.leftHandFree, psychoOneHandWalk && walking && !throwing ? 1f : 0f, dt * 4f);
            if (kind == Kind.None || !anim || !anim.isActiveAndEnabled || view == null || view.Dying || view.Reacting)   // weapons_v2 rebase: a deaths_mech_v1 knockdown owns the body
            {
                if (st != St.Up) { RestoreLevel(); if (view != null && !view.Dying) viewState = null; }
                st = St.Up; wantPrepare = false; return false;
            }
            if (engaged) lastEngaged = Time.time;
            t += dt * Mathf.Max(0.1f, anim.speed);
            switch (kind)
            {
                case Kind.Sniper: if (RulesDraw) return RuleSniper(throwing, ref viewState, animMul, dt); return SteadySniper(engaged, walking, throwing, ref viewState, animMul, dt);   // K248b: the stance holds through a fight (BS01Stance.Steady.cs); DV122: the rules' stance, drawn (BS01Stance.Rules.cs)
                case Kind.Psycho: return DrivePsycho(engaged, walking, throwing, ref viewState, animMul);
                default:
                    bool own = DriveTough(engaged, walking, throwing, ref viewState, animMul, dt);
                    UpdatePitch(dt);
                    return own;
            }
        }

        // ------------------------------------------------------------------------------------------------ sniper
        bool DriveSniper(bool engaged, bool walking, bool throwing, ref string viewState, float animMul)
        {
            switch (st)
            {
                case St.Up:
                    if (!engaged || walking || throwing || InWater()) return false;
                    if (!RobotWeaponPose.SniperGoesDown(this, out var wv2Pose)) return false;                    // weapons_v2: sometimes, stable per engagement
                    pose = wv2Pose ?? ChoosePose();
                    Play($"sniper_{pose}_down", 0.12f, Len($"sniper_{pose}_down"), animMul);
                    st = St.Down; viewState = "stance"; return true;
                case St.Down:
                    if (walking || throwing) return GetUp(walking);
                    if (t >= len * 0.97f) { Play($"sniper_{pose}_aim", 0.10f, 99f, animMul); st = St.Aim; }
                    return true;
                case St.Aim:
                    if (walking || throwing) return GetUp(walking);
                    if (!engaged && Time.time - lastEngaged > SteadyLinger) return GetUp(false);   // K248b: down until the fight is over (BS01Stance.Steady.cs)
                    return true;
                case St.Fire:
                    if (walking || throwing) return GetUp(walking);
                    if (t >= len * 0.95f) { Play($"sniper_{pose}_aim", 0.08f, 99f, animMul); st = St.Aim; }
                    return true;
                case St.GetUp:
                    if (t >= len * 0.9f)
                    {   // hand back: UnitView cross-fades to what it wants (idle / walking / shooting) this frame
                        st = St.Up; viewState = null; anim.speed = animMul; return false;
                    }
                    return true;
            }
            st = St.Up; return false;
        }

        bool GetUp(bool hurry)
        {
            Play($"sniper_{pose}_up", 0.08f, Len($"sniper_{pose}_up"), hurry ? hurryUp : 1f);
            st = St.GetUp; return true;
        }

        string ChoosePose()
        {
            if (!Has("sniper_prone_down")) return "kneel";
            float frac = 0f;
            var tgt = obj?.AttackObject;
            if (tgt != null && obj.AttackRadius > 0)
            {
                float dx = tgt.CenterX - obj.CenterX, dy = tgt.CenterY - obj.CenterY;
                frac = Mathf.Sqrt(dx * dx + dy * dy) / obj.AttackRadius;
            }
            bool prone = frac >= proneBeyond;
            if (((obj != null ? obj.Id : 0) * 0.6180339f) % 1f < variety) prone = !prone;      // a few squad members pick the other one
            return prone ? "prone" : "kneel";
        }

        bool InWater()
        {
            try { return OpenZFxPlayer.IsWaterTile(transform.position); } catch (System.Exception) { return false; }
        }

        // ------------------------------------------------------------------------------------------------ psycho
        bool DrivePsycho(bool engaged, bool walking, bool throwing, ref string viewState, float animMul)
        {
            bool stand = engaged && !walking && !throwing && Has("psycho_fire") && Has("psycho_aim");
            if (!stand)
            {
                if (st != St.Up) { st = St.Up; viewState = null; anim.speed = animMul; }   // UnitView re-issues its own state this frame
                return false;
            }
            bool firing = Time.time - lastRound < burstGap;
            var want = firing ? St.PsychoFire : St.PsychoAim;
            if (want != st)
            {
                if (want == St.PsychoFire) Play("psycho_fire", st == St.Up ? 0.12f : 0.05f, 99f, animMul);
                else Play("psycho_aim", 0.15f, 99f, animMul);
                st = want; viewState = "stance";
            }
            anim.speed = animMul;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ tough
        bool DriveTough(bool engaged, bool walking, bool throwing, ref string viewState, float animMul, float dt)
        {
            if ((nextScan -= dt) <= 0f) { nextScan = 0.25f; predicted = Predict(); }
            bool threat = engaged || predicted;
            wantPrepare = threat && !walking && !throwing;
            switch (st)
            {
                case St.Up:
                    if (!wantPrepare || !Has("tough_prepare")) return false;
                    Play("tough_prepare", 0.10f, prepareLen, animMul);
                    pitchFrom = Mathf.Max(0f, pitchNow);
                    st = St.Prepare; viewState = "stance"; return true;
                case St.Prepare:
                    if (walking || throwing) return Release(ref viewState);
                    if (t >= prepareLen * 0.97f) { Play("tough_aim", 0.08f, 99f, animMul); st = St.ToughAim; }
                    return true;
                case St.ToughAim:
                    if (walking || throwing) return Release(ref viewState);
                    if (threat) lastEngaged = Time.time;
                    if (!threat && Time.time - lastEngaged > toughLinger)
                    {   // lower to the carry: bazooka_idle (tough_carry via the override); UnitView keeps it (its state is bazooka_idle)
                        if (Has("bazooka_idle")) anim.CrossFadeInFixedTime("bazooka_idle", 0.35f, 0, 0f);
                        anim.speed = animMul; st = St.Up; viewState = "bazooka_idle"; return false;
                    }
                    return true;
                case St.ToughFire:
                    if (walking || throwing) return Release(ref viewState);
                    if (t >= toughFireLen * 0.96f) { Play("tough_aim", 0.10f, 99f, animMul); st = St.ToughAim; }
                    else if (hold && holdLevel >= 0f) hold.bazookaChestLevel = Mathf.Lerp(kickChestLevel, holdLevel, Mathf.Clamp01((t - 0.05f) / 0.55f));
                    return true;
            }
            st = St.Up; return false;
        }

        /// <summary>The target the sim will engage (the UnitAim.Predict / weapons_v2 rule, zobject.cpp:1603-1853): a Tough standing still
        /// with an enemy in range starts to prepare up to one passive-engage check (0.5 s) before the sim engages.</summary>
        bool Predict()
        {
            var o = obj; var g = ZGame.I; var w = g ? g.World : null;
            if (o == null || w == null || !o.CanAttack() || o.Owner == Team.Neutral || o.IsMoving) return false;
            if (o.AttackObject != null && !o.AttackObject.Destroyed) return true;
            foreach (var x in w.Objects)
                if (x.IsUnit && !x.Destroyed && x.KillAt < 0 && x.Owner != Team.Neutral && x.Owner != o.Owner && o.CanAttackObject(x) && o.WithinAttackRadius(x)) return true;
            return false;
        }

        bool Release(ref string viewState) { RestoreLevel(); st = St.Up; viewState = null; if (anim) anim.speed = 1f; return false; }

        void RestoreLevel() { if (hold && holdLevel >= 0f) hold.bazookaChestLevel = holdLevel; }

        /// <summary>Tough tube pitch (WeaponHold.carryPitch): carried pitched up, tough_prepare lowers it onto the target, each rocket climbs
        /// it (weapons_v2 leaves the pitch to this when it is on the robot).</summary>
        void UpdatePitch(float dt)
        {
            if (!hold) return;
            if (pitchNow < 0f) pitchNow = st == St.Up ? carryPitch : 0f;
            switch (st)
            {
                case St.Prepare:
                    float k = Mathf.Clamp01(t / Mathf.Max(0.05f, prepareLen * 0.7f));
                    pitchNow = pitchFrom * (1f - k * k * (3f - 2f * k)); break;
                case St.ToughAim: pitchNow = Mathf.MoveTowards(pitchNow, 0f, 120f * dt); break;
                case St.ToughFire: pitchNow = fireClimb * (t < 0.04f ? t / 0.04f : Mathf.Exp(-(t - 0.04f) / 0.16f)); break;
                default: pitchNow = Mathf.MoveTowards(pitchNow, lastWalking || !wantPrepare ? carryPitch : 0f, 45f * dt); break;
            }
            hold.carryPitch = pitchNow;
        }

        void OnDisable() { RestoreLevel(); if (hold) { hold.carryPitch = 0f; hold.leftHandFree = 0f; } }

        /// <summary>UnitView.OnFire (a shown shot / MG round / rocket left this unit). True = handled here (UnitView skips its own Tough
        /// fire clip).</summary>
        public bool OnFire()
        {
            if (kind == Kind.None || !anim) return false;
            if (kind == Kind.Psycho) { lastRound = Time.time; return false; }
            if (kind == Kind.Sniper)
            {
                if (st == St.Aim || st == St.Fire || (st == St.Down && t >= len * 0.6f))
                { Play($"sniper_{pose}_fire", 0.03f, Len($"sniper_{pose}_fire")); st = St.Fire; return true; }
                return false;
            }
            if (st == St.Prepare || st == St.ToughAim || st == St.ToughFire)
            {   // bazooka_fire = tough_fire through the override: the blast throws the upper body back, a step back, recover
                Play("bazooka_fire", 0.04f, toughFireLen); st = St.ToughFire; return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------ sockets
        /// <summary>CombatView: the Tough's back-blast point (v2 bazooka socket BazookaBackBlast), else the fallback.</summary>
        public static Vector3 BackBlast(UnitView av, Vector3 fallback)
        {
            if (!av) return fallback;
            var s = av.GetComponent<BS01Stance>();
            if (!s) return fallback;
            if (!s.backBlast && !s.searched)
            {
                s.searched = true;
                foreach (var tr in av.GetComponentsInChildren<Transform>(true)) if (tr.name == "BazookaBackBlast") { s.backBlast = tr; break; }
            }
            return s.backBlast ? s.backBlast.position : fallback;
        }
        Transform backBlast; bool searched;
    }
}

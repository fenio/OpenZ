using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public enum AnticKind { Turn, Scan, Neck, Phone, Drink, Fish, Sunbed }

    /// <summary>
    /// One robot antic: a short chain of authored humanoid clips (the antic states of RobotBS01.controller; the five V3 override
    /// controllers inherit them and play them through the humanoid retarget) with its events on the authored frames (24 fps),
    /// its props and its sounds. UnitView.AnticAnim ticks it every frame while it owns the base layer; any order, threat,
    /// selection or hit cancels it at once (UnitView.AnticCancel -> Finish: props pop off, the weapon pops back, the normal
    /// state machine cross-fades out of the clip on that same frame). View only: nothing here writes the sim.
    /// </summary>
    public sealed class AnticRun
    {
        public readonly UnitView View;
        public readonly AnticKind Kind;
        public readonly float StartedAt;
        public bool Finished { get; private set; }
        /// <summary>The current clip state; "start" before the first Tick (a cancel on the start frame read steps[-1] and threw in the log line).</summary>
        public string Step => step < 0 ? "start" : step < steps.Count ? steps[step].state : "end";
        public float Age => Time.time - StartedAt;
        public bool PosesLegs => Kind == AnticKind.Sunbed;
        public bool StowsWeapon => Kind == AnticKind.Phone || Kind == AnticKind.Drink || Kind == AnticKind.Fish || Kind == AnticKind.Sunbed;
        public readonly List<string> Trace = new List<string>();

        struct St { public string state; public float len, off, fade; public bool loop; }
        struct Ev { public int step; public float t; public string id; }
        readonly List<St> steps = new List<St>();
        readonly List<Ev> evs = new List<Ev>();
        int step = -1, evNext; float stepT;
        readonly int id; readonly Team team;
        readonly float k;                                                       // world metres per authored metre at this robot

        // ---- props and spots
        Transform phone, rod, can, bobber, fish, bed, parasol, weapon; Vector3 weaponScale = Vector3.one; bool weaponHidden;
        readonly List<Renderer> weaponOff = new List<Renderer>();             // weapon renderers that were already off at the stow (they stay off)
        Transform rodTip, lineAttach, reelHandle, fishMouth; Quaternion reelRest; float reelAngle;
        LineRenderer line; static Material lineMat;
        readonly AnticSpot spot;
        Vector3 bobFrom; float bobFlyT = -1f, bobLandAt = -1f, bobPhase, rippleT, dipT = -1f; bool bobIn, fishOut; float fishOutT;
        Vector3 bobPos;
        AnticCan tossed;
        float faceYaw = float.NaN;
        /// <summary>This antic turns the robot to a spot (fishing faces the water, the lounger its bed): UnitView's walk / aim facing
        /// stays out while it runs (it pulled the angler back toward his last walking direction, off the cast line).</summary>
        public bool OwnsFacing => !float.IsNaN(faceYaw);

        public const float Fps = 24f;
        static float F(int frame) => frame / Fps;

        public AnticRun(UnitView v, AnticKind kind, AnticSpot spot)
        {
            View = v; Kind = kind; this.spot = spot; StartedAt = Time.time;
            id = v.Obj != null ? v.Obj.Id : -1; team = v.Obj != null ? v.Obj.Owner : Team.Neutral;
            k = AnticSockets.WorldScale(v);
            switch (kind)
            {
                case AnticKind.Turn:
                    Add("antic_turn", 64); On(0, 2, "servo"); On(0, 26, "servo"); break;
                case AnticKind.Scan:
                    Add("antic_scan360", 96); On(0, 0, "scan_whirr"); for (int f = 6; f <= 90; f += 6) On(0, f, "scan_step"); break;
                case AnticKind.Neck:
                    Add("antic_neck_stretch", 72); On(0, 22, "neck"); On(0, 48, "neck"); break;                     // each servo take holds two moves; the event's 1 s cooldown would drop f36
                case AnticKind.Phone:
                {   // longer phone (about 11-17 s): raise + use, then the use part (frames 24-96) again 1-3 times, then the last use + lower
                    int mids = Random.Range(1, 4);
                    AddSeg("antic_phone", 0, 96, -1f); On(0, 0, "stow"); On(0, 2, "phone_in"); On(0, 21, "phone_sound"); On(0, 60, "phone_sound");
                    for (int i = 1; i <= mids; i++) { AddSeg("antic_phone", 24, 96, 0.25f); On(i, 12, "phone_sound"); On(i, 36, "phone_sound"); }
                    AddSeg("antic_phone", 24, 120, 0.25f); On(mids + 1, 36, "phone_sound"); On(mids + 1, 94, "phone_out"); break;
                }
                case AnticKind.Drink:
                {   // longer drink (about 9-12 s): 3-4 sips, each with its glugs, then the crush and the throw
                    int sips = Random.Range(3, 5);
                    for (int i = 0; i < sips; i++) { AddSeg("drink", 0, 60, i == 0 ? -1f : 0.2f); On(i, 20, "glug"); On(i, 40, "glug"); }
                    Add("crush", 14); Add("toss", 16);
                    On(0, 0, "stow"); On(0, 0, "can_in"); On(sips, 7, "crush"); On(sips + 1, 9, "release"); break;
                }
                case AnticKind.Fish:
                    Add("fish_cast", 36); AddLoop("fish_hold", 48, Random.Range(12f, 22f)); Add("fish_bite", 18); AddLoop("fish_reel", 48, Random.Range(2.2f, 3.0f));
                    On(0, 0, "stow"); On(0, 0, "rod_in"); On(0, 13, "cast"); On(0, 15, "line_release");
                    On(2, 2, "bite"); On(2, 9, "hook_set"); On(3, 0, "reel"); OnT(3, steps[3].len - 0.95f, "catch"); break;
                case AnticKind.Sunbed:
                    Add("sunbed_down", 36); AddLoop("sunbed_lie", 48, Random.Range(30f, 50f)); Add("sunbed_up", 36);
                    On(0, 0, "stow"); On(0, 0, "bed_in"); On(0, 14, "creak"); On(0, 33, "creak2"); On(2, 2, "creak"); On(2, 20, "creak2"); break;
            }
            evs.Sort((a, b) => a.step != b.step ? a.step.CompareTo(b.step) : a.t.CompareTo(b.t));
            if (kind == AnticKind.Fish && spot != null) faceYaw = spot.yaw;
            if (kind == AnticKind.Sunbed && spot != null) faceYaw = spot.yaw;
        }

        void Add(string s, int frames) => steps.Add(new St { state = s, len = F(frames), fade = -1f });
        void AddLoop(string s, int frames, float hold) => steps.Add(new St { state = s, len = Mathf.Max(F(frames), hold), loop = true, fade = -1f });
        /// <summary>Plays frames from..to of a clip (re-enters the same state at that offset with its own cross-fade, so a short
        /// authored clip can repeat its middle part).</summary>
        void AddSeg(string s, int from, int to, float fade) => steps.Add(new St { state = s, len = F(to - from), off = F(from), fade = fade });
        void On(int s, int frame, string ev) => evs.Add(new Ev { step = s, t = F(frame), id = ev });
        void OnT(int s, float t, string ev) => evs.Add(new Ev { step = s, t = Mathf.Max(0f, t), id = ev });

        public IEnumerable<string> States() { foreach (var s in steps) yield return s.state; }

        // ------------------------------------------------------------------ per frame (UnitView.AnticAnim, before the Animator)
        /// <summary>Advances the chain; false when it has ended.</summary>
        public bool Tick(Animator anim, float dt)
        {
            if (Finished) return false;
            if (step < 0) { Enter(anim, 0, 0.12f); }
            else
            {
                stepT += dt;
                if (stepT >= steps[step].len)
                {
                    FireUpTo(step, float.MaxValue);
                    if (step + 1 >= steps.Count) { Finish(false, false, "done"); return false; }
                    Enter(anim, step + 1, steps[step + 1].fade >= 0f ? steps[step + 1].fade : steps[step + 1].loop || steps[step].loop ? 0.1f : 0.05f);
                }
            }
            FireUpTo(step, stepT);
            if (!float.IsNaN(faceYaw)) View.AnticFace(faceYaw, dt);
            return true;
        }

        void Enter(Animator anim, int s, float fade)
        {
            step = s; stepT = 0f;
            if (anim) anim.CrossFadeInFixedTime(steps[s].state, fade, 0, steps[s].off);
            Note("clip " + steps[s].state);
        }

        void FireUpTo(int s, float t)
        {
            while (evNext < evs.Count && (evs[evNext].step < s || (evs[evNext].step == s && evs[evNext].t <= t)))
            {
                var e = evs[evNext++];
                Fire(e.id);
            }
        }

        /// <summary>After the Animator and IK (AnticDirector.LateUpdate): props that follow the pose (line, bobber, reel, fish).</summary>
        public void Late(float dt)
        {
            if (Finished) return;
            if (can) CanLate();
            if (phone) PhoneLate(dt);
            if (Kind == AnticKind.Fish) FishLate(dt);
        }

        // ------------------------------------------------------------------ held props: upright in world space (PropUpright)
        Transform headBone; bool headSearched; Renderer phoneScreen; float phoneCallW; bool phoneCallShown;
        Transform chestBone, padBone; float rHead, rChest, rPad; bool phoneClearInit;
        Transform Head()
        {
            if (headSearched) return headBone;
            headSearched = true;
            var an = View ? View.GetComponentInChildren<Animator>() : null;
            if (an && an.isHuman) headBone = an.GetBoneTransform(HumanBodyBones.Head);
            return headBone;
        }
        Vector3 HeadPos => Head() ? headBone.position : Pos + Vector3.up * 1.55f * k;
        string Who => View && View.Obj != null ? View.Obj.Type : "robot";

        /// <summary>The can (full or crushed, until the toss release) stands upright in world space, the hand's twist kept; while it is
        /// near the mouth (a sip) its top tilts toward the mouth, at most PropUpright.CanSipMaxDeg. The socket's own rotation (the
        /// thumb axis, which turns with the wrist animation) only gives the twist.</summary>
        void CanLate()
        {
            if (!can.parent) return;
            can.localPosition = Vector3.zero; can.localRotation = AnticSockets.CanRot;                       // the animated hand pose (twist)
            var localTop = Vector3.up;                                                                       // the model's top (+Y) in the holder
            var mouth = HeadPos + Vector3.up * PropUpright.MouthUp * k + Front * PropUpright.MouthFwd * k;
            var center = can.position;
            float w = PropUpright.SipWeight(Vector3.Distance(center, mouth), k);
            var top = PropUpright.CanTop(center, mouth, Front, w, out _);
            PropUpright.AlignAxis(can, localTop, top);
            PropUpright.Note(id, Who, w < 0.2f ? "can_hold" : "can_sip", PropUpright.AxisAngle(can, localTop), w);
        }

        /// <summary>The phone in portrait: looking at it, the long side vertical with the top leaning back PropUpright.PhoneLookTiltDeg so
        /// the bright screen faces the eyes; at the ear (the fist beside the head), upright with the screen to the head and the green call
        /// screen. Its grip point stays in the fist.</summary>
        void PhoneLate(float dt)
        {
            var sock = phone.parent; if (!sock) return;
            var kit = AnticDirector.Kit;
            var size = kit ? kit.phoneSize : new Vector3(0.215f, 0.44f, 0.024f);
            var front = Front; var right = Vector3.Cross(Vector3.up, front).normalized;
            var head = HeadPos;
            var eyes = head + Vector3.up * PropUpright.EyesUp * k + front * 0.08f * k;
            var d = sock.position - head;
            float side = Vector3.Dot(d, right), fwd = Vector3.Dot(d, front);
            bool atEar = Mathf.Abs(side) > PropUpright.PhoneCallSide * k && Mathf.Abs(side) > fwd;
            phoneCallW = Mathf.MoveTowards(phoneCallW, atEar ? 1f : 0f, dt / Mathf.Max(0.01f, PropUpright.PhoneCallEase));
            float cw = Mathf.SmoothStep(0f, 1f, phoneCallW);
            // looking: the screen toward the eyes, the top leaning back (away from the face)
            var h = eyes - sock.position; h.y = 0f; if (h.sqrMagnitude < 1e-6f) h = -front; h.Normalize();
            float r = PropUpright.PhoneLookTiltDeg * Mathf.Deg2Rad;
            var lookUp = Vector3.up * Mathf.Cos(r) - h * Mathf.Sin(r);
            var lookN = Vector3.up * Mathf.Sin(r) + h * Mathf.Cos(r);
            // the call: upright beside the head, the screen to the head (the ear)
            var hc = head - sock.position; hc.y = 0f; if (hc.sqrMagnitude < 1e-6f) hc = side > 0f ? -right : right; hc.Normalize();
            float rc = PropUpright.PhoneCallTiltDeg * Mathf.Deg2Rad;
            var callUp = Vector3.up * Mathf.Cos(rc) - hc * Mathf.Sin(rc);
            var callN = Vector3.up * Mathf.Sin(rc) + hc * Mathf.Cos(rc);
            var rot = Quaternion.Slerp(Quaternion.LookRotation(lookN, lookUp), Quaternion.LookRotation(callN, callUp), cw);
            phone.rotation = rot;
            float grip = Mathf.Lerp(PropUpright.PhoneGripLook, PropUpright.PhoneGripCall, cw);
            var gripLocal = new Vector3(0f, (grip - 0.5f) * size.y, -size.z * 0.5f);
            phone.position = sock.position - phone.TransformVector(gripLocal);
            PhoneClear(size, sock.position, front, grip);                                                    // out of the head, the chest and the shoulder
            // the screen: home screen while looking, the green call screen at the ear
            bool call = cw > 0.5f;
            if (kit && kit.phoneOwnMats && call != phoneCallShown)
            {
                phoneCallShown = call;
                if (!phoneScreen) { var st = AnticFx.Find(phone, "Screen"); phoneScreen = st ? st.GetComponent<Renderer>() : null; }
                var m = call ? kit.phoneScreenCall : kit.phoneScreenHome;
                if (phoneScreen && m) phoneScreen.sharedMaterial = m;
            }
            PropUpright.Note(id, Who, call ? "phone_call" : "phone_look", PropUpright.AxisAngle(phone, Vector3.up), call ? cw : 1f - cw);
        }

        // ------------------------------------------------------------------ events
        /// <summary>Keeps the held phone out of the body without taking it out of the hand, by this robot type's own sizes (the ragdoll
        /// radii, RobotV3.RagdollRadius). Out of the chest and the left shoulder it slides UP through the fist along its own long side
        /// (the fist then holds it lower down, never below PropUpright.PhoneGripMin of its height); off the head it is pushed level,
        /// at most PropUpright.PhoneClearMax. Each with PropUpright.PhoneGap of air. `grip` = where along its height the fist holds it now.</summary>
        void PhoneClear(Vector3 size, Vector3 sockPos, Vector3 front, float grip)
        {
            if (!View || !phone) return;
            if (!phoneClearInit)
            {
                phoneClearInit = true;
                var an = View.GetComponentInChildren<Animator>(); var body = View.gameObject; float sc = Mathf.Abs(View.transform.lossyScale.y);
                if (an && an.isHuman)
                {
                    chestBone = an.GetBoneTransform(HumanBodyBones.Chest); if (!chestBone) chestBone = an.GetBoneTransform(HumanBodyBones.Spine);
                    padBone = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                }
                rHead = RobotV3.RagdollRadius(body, "Head", 0.2f) * sc; rChest = RobotV3.RagdollRadius(body, "Spine1", 0.26f) * sc; rPad = RobotV3.RagdollRadius(body, "Pad", 0f) * sc;
            }
            float gap = PropUpright.PhoneGap * k, max = PropUpright.PhoneClearMax * k;
            var half = size * 0.5f; var up = phone.up;
            // chest and shoulder: up through the fist, in steps of 1 cm, while it still touches one of them and the fist still holds it
            float room = Mathf.Max(0f, (grip - PropUpright.PhoneGripMin) * size.y * Mathf.Abs(phone.lossyScale.y)), slid = 0f, st = 0.01f * k; bool touching = false;
            for (int i = 0; i < 64; i++)
            {
                touching = (chestBone && PhoneDist(chestBone.position, half) < rChest + gap) || (padBone && rPad > 0f && PhoneDist(padBone.position, half) < rPad + gap);
                if (!touching || slid + st > room) break;
                phone.position += up * st; slid += st;
            }
            // the head: level, a little
            var start = phone.position;
            PhonePush(HeadPos + Vector3.up * (0.6f * rHead), rHead + gap, half, true, sockPos, front);
            var off = phone.position - start; bool capped = off.magnitude > max;
            if (capped) phone.position = start + off.normalized * max;
            PropUpright.NotePhoneFit(Who, (slid + Mathf.Min(off.magnitude, max)) / Mathf.Max(0.01f, k), capped || touching);
        }

        /// <summary>Distance from world point c to the phone's box (half extents `half`, phone space); 0 inside it.</summary>
        float PhoneDist(Vector3 c, Vector3 half)
        {
            var l = phone.InverseTransformPoint(c);
            var q = new Vector3(Mathf.Clamp(l.x, -half.x, half.x), Mathf.Clamp(l.y, -half.y, half.y), Mathf.Clamp(l.z, -half.z, half.z));
            return (phone.TransformPoint(q) - c).magnitude;
        }

        /// <summary>Moves the phone so that its box (half extents `half`, phone space) clears the ball (c, r): level when `level`,
        /// else out and up. False when it was clear already.</summary>
        bool PhonePush(Vector3 c, float r, Vector3 half, bool level, Vector3 sockPos, Vector3 front)
        {
            var l = phone.InverseTransformPoint(c);
            var q = new Vector3(Mathf.Clamp(l.x, -half.x, half.x), Mathf.Clamp(l.y, -half.y, half.y), Mathf.Clamp(l.z, -half.z, half.z));
            var d = phone.TransformPoint(q) - c; float dist = d.magnitude;
            if (dist >= r) return false;
            var dir = d; dir.y = level ? 0f : Mathf.Max(dir.y, 0f);
            if (dir.sqrMagnitude < 1e-6f) { dir = sockPos - c; dir.y = level ? 0f : Mathf.Max(dir.y, 0f); }
            if (dir.sqrMagnitude < 1e-6f) dir = level ? front : Vector3.up;
            phone.position += dir.normalized * (r - dist);
            return true;
        }

        Vector3 Pos => View ? View.transform.position : Vector3.zero;
        Vector3 Front => View ? View.AnticFront : Vector3.forward;

        void Fire(string e)
        {
            var p = Pos + Vector3.up * 1.4f * k;
            switch (e)
            {
                case "servo": AnticFx.Sound("rob_servo", p, id, 0.8f); break;
                case "scan_whirr": AnticFx.Sound("rob_antic_scan_whirr", p, id); break;
                case "scan_step": AnticFx.Sound(StepEvent(), Pos, id, 0.45f); break;
                case "neck": AnticFx.Sound("rob_antic_neck_servo", p, id); break;
                case "stow": Stow(); break;
                case "phone_in": phone = HandProp(AnticDirector.Kit ? AnticDirector.Kit.phone : null, "AnticPhone", true, AnticSockets.PhoneScale, true); if (phone) AnticFx.PopIn(phone, phone.localScale, true, "phone_pop_in", id); break;
                case "phone_sound": AnticFx.Sound("rob_antic_phone", phone ? phone.position : p, id); break;
                case "phone_out": if (phone) { AnticFx.PopOut(phone, true, true, "phone_pop_out", id); phone = null; } break;
                case "can_in":
                    can = HandProp(AnticDirector.Kit ? AnticDirector.Kit.can : null, "AnticFuelCan", true, AnticSockets.CanScale, false);
                    if (can) AnticFx.PopIn(can, can.localScale, true, "can_pop", id);
                    break;
                case "glug": AnticFx.Sound("rob_antic_can_glug", can ? can.position : p, id); break;
                case "crush":
                    if (can) can = SwapCan(can);
                    AnticFx.Sound("rob_antic_can_crush", can ? can.position : p, id);
                    break;
                case "release":
                    if (can)
                    {
                        var right = Vector3.Cross(Vector3.up, Front).normalized;
                        var v = (-right * AnticCan.TossVel.x + Vector3.up * AnticCan.TossVel.y + Front * AnticCan.TossVel.z) * Mathf.Sqrt(Mathf.Max(0.25f, k));
                        var at = can.position;
                        tossed = AnticCan.Throw(can, v, id, k);
                        can = null;
                        AnticFx.Sound("wpn_grenade_throw", at, id, AnticSockets.TossWhooshGain);                   // the throw whoosh
                    }
                    break;
                case "rod_in":
                    rod = RodProp();
                    if (rod) AnticFx.PopIn(rod, rod.localScale, true, "rod_pop_in", id);
                    break;
                case "cast": AnticFx.Sound("rob_antic_fish_cast", p, id); break;
                case "line_release": FixRodFront(); CastBobber(); break;
                case "bite":
                    dipT = 0f;
                    if (bobber) { AnticFx.Sound("rob_antic_fish_splash", bobber.position, id, 0.8f); Splash(bobber.position, 0.18f); }
                    break;
                case "hook_set": dipT = -1f; break;
                case "reel": AnticFx.Sound("rob_antic_fish_reel", rod ? rod.position : p, id); break;
                case "catch": Catch(); break;
                case "bed_in": PlaceBed(); break;
                case "creak": AnticFx.Sound("rob_antic_sunbed_creak", bed ? bed.position : Pos, id); break;
                case "creak2": AnticFx.Sound("rob_antic_sunbed_creak", bed ? bed.position : Pos, id + 1000000); break;   // the bed's own slot: 0.8 s after the first creak (1 s per-emitter cooldown)
            }
            Note(e);
        }

        string StepEvent()
        {
            var g = ZGame.I; if (!g) return "rob_step_sand";
            if (g.level == "L17") return "rob_step_concrete";
            return OpenZFxPlayer.ClassAt(Pos) == ZClass.Road ? "rob_step_concrete" : "rob_step_sand";
        }

        // ------------------------------------------------------------------ weapon
        void Stow()
        {
            if (weapon || !View) return;
            weapon = AnticSockets.Weapon(View);
            if (!weapon) return;
            weaponScale = weapon.localScale; weaponHidden = true;
            weaponOff.Clear(); foreach (var r in weapon.GetComponentsInChildren<Renderer>(true)) if (!r.enabled) weaponOff.Add(r);
            AnticFx.PopOut(weapon, false, true, "weapon_stow_pop", id);
        }

        void Unstow(bool instant)
        {
            if (!weapon || !weaponHidden) return;
            weaponHidden = false;
            if (instant) AnticFx.Snap(weapon, weaponScale, true);
            else AnticFx.PopIn(weapon, weaponScale, true, "weapon_back_pop", id);
            foreach (var r in weaponOff) if (r) r.enabled = false;                    // the pop shows every renderer: keep the ones that were off, off
            weaponOff.Clear();
        }

        // ------------------------------------------------------------------ hand props
        Transform HandProp(GameObject model, string name, bool left, float scale, bool phoneFit)
        {
            var kit = AnticDirector.Kit; if (!kit || !model || !View) return null;
            var socket = left ? AnticSockets.LeftProp(View, kit) : AnticSockets.RightWeapon(View);
            if (!socket) { Note("no socket for " + name); return null; }
            bool ownMats = phoneFit && kit.phoneOwnMats;
            var h = AnticFx.Make(model, name, model == kit.can || model == kit.canCrushed ? kit.canMat : ownMats ? null : kit.propMat);
            AnticFx.OnSocket(h, socket, ownMats ? AnticSockets.PhoneV2Scale * AnticSockets.PhoneBodyFor(View) : scale);
            if (model == kit.can || model == kit.canCrushed) h.localRotation = AnticSockets.CanRot;
            if (phoneFit && !ownMats)
            {   // the screen faces the socket's +X (authored), the phone top along the thumb axis; its GRIP point sits in the fist
                h.localRotation = AnticSockets.PhoneRot;
                var grip = AnticFx.Find(h, "GRIP");
                if (grip) h.position += socket.position - grip.position;
            }
            return h;
        }

        Transform SwapCan(Transform full)
        {
            var kit = AnticDirector.Kit; if (!kit || !kit.canCrushed) return full;
            var h = AnticFx.Make(kit.canCrushed, "AnticFuelCanCrushed", kit.canMat);
            h.SetParent(full.parent, false); h.localPosition = full.localPosition; h.localRotation = full.localRotation; h.localScale = full.localScale;
            h.gameObject.layer = full.gameObject.layer;
            Object.Destroy(full.gameObject);
            return h;
        }

        Transform RodProp()
        {
            var kit = AnticDirector.Kit; if (!kit || !kit.rod) return null;
            var socket = AnticSockets.RightWeapon(View); if (!socket) return null;
            var h = AnticFx.Make(kit.rod, "AnticRod", kit.propMat);
            AnticFx.OnSocket(h, socket, 1f);
            h.localRotation = AnticSockets.RodRot;
            rodTip = AnticFx.Find(h, "ROD_TIP"); reelHandle = AnticFx.Find(h, "REEL_HANDLE");
            if (reelHandle) reelRest = reelHandle.localRotation;
            return h;
        }

        // ------------------------------------------------------------------ fishing
        void CastBobber()
        {
            var kit = AnticDirector.Kit; if (!kit || !kit.bobber || spot == null) return;
            bobber = AnticFx.Make(kit.bobber, "AnticBobber", kit.propMat);
            bobber.localScale = Vector3.one * k;
            bobFrom = rodTip ? rodTip.position : Pos + Vector3.up * 2f * k;
            bobber.position = bobFrom; bobber.rotation = Quaternion.LookRotation(Front, Vector3.up);
            lineAttach = AnticFx.Find(bobber, "LINE_ATTACH");
            bobFlyT = 0f; bobPhase = Random.value * 6.28f;
            EnsureLine();
        }

        void EnsureLine()
        {
            if (line) return;
            var go = new GameObject("AnticLine");
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = LinePoints; line.widthMultiplier = AnticSockets.LineWidth * Mathf.Max(0.5f, k); line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            if (!lineMat)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit"); if (!sh) sh = Shader.Find("Sprites/Default");
                lineMat = new Material(sh) { name = "OZ_AnticLine" };
                var c = new Color(0.09f, 0.09f, 0.08f, 1f);
                if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", c);
                if (lineMat.HasProperty("_Color")) lineMat.SetColor("_Color", c);
            }
            line.sharedMaterial = lineMat; line.startColor = line.endColor = new Color(0.09f, 0.09f, 0.08f, 1f);
        }

        const int LinePoints = 14;

        /// <summary>The rod's tip must be in front of the angler, over the water (at the release and in the hold). RodRot was measured on
        /// the grunt's RightHandWeapon socket; the sniper's socket frame is turned round, so its rod pointed backward over the quay (tip
        /// 1.9 m behind him) with the line running back over his head. Once per antic, at the release and again at the first hold frame:
        /// a tip more than RodFrontTolerance deg off his facing turns the rod about the vertical through the grip to point straight out.</summary>
        void FixRodFront()
        {
            if (!rod || !rodTip || rodFixed >= 2) return;
            rodFixed++;
            var d = rodTip.position - rod.position; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            float turn = Vector3.SignedAngle(d, Front, Vector3.up);
            if (Mathf.Abs(turn) <= AnticSockets.RodFrontTolerance) return;
            var parentRot = rod.parent ? rod.parent.rotation : Quaternion.identity;
            rod.localRotation = Quaternion.Inverse(parentRot) * (Quaternion.AngleAxis(turn, Vector3.up) * rod.rotation);
            Note("rod turned " + turn.ToString("F0") + " deg to the front");
        }
        int rodFixed;

        void FishLate(float dt)
        {
            if (rodFixed < 2 && Step == "fish_hold") FixRodFront();
            float wy = spot != null ? spot.waterY : Pos.y;
            if (reelHandle && Step == "fish_reel") { reelAngle += 720f * dt; reelHandle.localRotation = reelRest * Quaternion.AngleAxis(reelAngle, Vector3.right); }
            if (bobber)
            {
                if (bobFlyT >= 0f)
                {   // the cast: the bobber flies from the rod tip to the landing point and plops (authored: lands ~0.9 s after release)
                    bobFlyT += dt; float u = Mathf.Clamp01(bobFlyT / AnticSockets.CastFlight);
                    var land = new Vector3(spot.land.x, wy, spot.land.z);
                    var q = Vector3.Lerp(bobFrom, land, u); q.y += Mathf.Sin(u * Mathf.PI) * 1.4f * k;
                    bobber.position = q;
                    if (u >= 1f)
                    {
                        bobFlyT = -1f; bobIn = true; bobPos = land; bobLandAt = Time.time;
                        AnticFx.Sound("rob_antic_fish_plop", land, id); Splash(land, 0.16f);
                    }
                }
                else if (bobIn)
                {
                    float bob = Mathf.Sin((Time.time - bobLandAt) * 4.5f + bobPhase) * 0.012f * k;                 // +-1.2 cm x scale: inside the +-2 cm acceptance
                    if (dipT >= 0f) { dipT += dt; bob -= 0.05f * k * Mathf.Clamp01(Mathf.Sin(Mathf.Min(1f, dipT / 0.35f) * Mathf.PI * 0.5f)); }
                    if (Step == "fish_reel" && spot != null)
                    {   // reeled in toward the bank, on the water surface
                        var shore = new Vector3(spot.edge.x, wy, spot.edge.z);
                        bobPos = Vector3.MoveTowards(bobPos, shore, AnticSockets.ReelSpeed * k * dt);
                    }
                    bobber.position = new Vector3(bobPos.x, wy + bob, bobPos.z);
                    if (dipT < 0f) AnticDirector.NoteFish(Mathf.Abs(bobber.position.y - wy));
                    if ((rippleT -= dt) <= 0f) { rippleT = 1.7f; var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled && WreckFx.OnScreen(bobPos)) fxp.Ripple(new Vector3(bobPos.x, wy, bobPos.z), 0.22f * k); }
                }
            }
            if (fish && fishOut)
            {   // the catch rises from the water to hang under the rod tip, flopping
                fishOutT += dt;
                var hang = rodTip ? rodTip.position + Vector3.down * 0.75f * k : Pos;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fishOutT / 0.45f));
                fish.position = Vector3.Lerp(new Vector3(bobPos.x, wy, bobPos.z), hang, u);
                fish.rotation = Quaternion.Euler(0f, Mathf.Sin(fishOutT * 14f) * 35f + View.AnticYaw, Mathf.Sin(fishOutT * 9f) * 12f);
            }
            if (line)
            {
                Vector3 a = rodTip ? rodTip.position : Pos + Vector3.up * 2f * k;
                Vector3 b = fish && fishOut ? (fishMouth ? fishMouth.position : fish.position) : bobber ? (lineAttach ? lineAttach.position : bobber.position) : a;
                float len = Vector3.Distance(a, b);
                float sag = bobFlyT >= 0f ? 0.01f : Step == "fish_reel" || (fish && fishOut) ? 0.015f : 0.07f;
                for (int i = 0; i < LinePoints; i++)
                {
                    float u = i / (float)(LinePoints - 1);
                    var q = Vector3.Lerp(a, b, u); q.y -= sag * len * 4f * u * (1f - u);
                    line.SetPosition(i, q);
                }
            }
        }

        void Catch()
        {
            var kit = AnticDirector.Kit; if (!kit || !kit.fish || !bobber) return;
            fish = AnticFx.Make(kit.fish, "AnticFish", kit.propMat);
            fish.position = new Vector3(bobPos.x, spot.waterY, bobPos.z);
            fishMouth = AnticFx.Find(fish, "MOUTH");
            AnticFx.PopIn(fish, Vector3.one * (k * AnticSockets.FishScale), false, "fish", id);
            AnticFx.Sound("rob_antic_fish_splash", fish.position, id); Splash(fish.position, 0.3f);
            AnticFx.PopOut(bobber, true, false, "bobber_pop_out", id); bobber = null; bobIn = false;
            fishOut = true; fishOutT = 0f;
        }

        static void Splash(Vector3 at, float s)
        {
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled && WreckFx.OnScreen(at)) { fxp.Splash(at, s); fxp.Ripple(at, s * 1.6f); }
        }

        // ------------------------------------------------------------------ sunbed
        void PlaceBed()
        {
            var kit = AnticDirector.Kit; if (!kit || spot == null) return;
            var beds = team == Team.Blue ? kit.sunbedsBlue : kit.sunbedsRed;
            GameObject model = null; float bestErr = float.MaxValue;
            if (beds != null)
                foreach (var b in beds)
                {   // the build whose seat matches the clip's 0.40 m seat top (SEAT_HIPS height in the model)
                    if (!b) continue;
                    var seat = AnticFx.Find(b.transform, "SEAT_HIPS");
                    float h = seat ? b.transform.InverseTransformPoint(seat.position).y : 0.5f, err = Mathf.Abs(h - AnticSockets.SeatTop);
                    if (err < bestErr) { bestErr = err; model = b; }
                }
            var pm = team == Team.Blue ? kit.parasolBlue : kit.parasolRed;
            bed = AnticFx.Make(model, "AnticSunbed", kit.propMat);
            if (bed)
            {
                bed.SetPositionAndRotation(spot.bed, spot.bedRot);
                AnticFx.PopIn(bed, new Vector3(k * spot.bedWidth, k, k), true, "sunbed_pop_in", id);    // wider for the wider robot types
            }
            parasol = AnticFx.Make(pm, "AnticParasol", kit.propMat);
            if (parasol)
            {
                parasol.SetPositionAndRotation(spot.parasol + Vector3.down * (0.05f * k), spot.parasolRot);    // leaning away from the camera, its foot planted
                AnticFx.PopIn(parasol, Vector3.one * k, true, "parasol_pop_in", id);
            }
        }

        // ------------------------------------------------------------------ end
        /// <summary>Ends the antic: every prop pops off (a tossed can keeps its own 15 s timer), the weapon pops back (at once when
        /// `instant`, e.g. on death), the line goes. `reason` is logged.</summary>
        public void Finish(bool cancelled, bool instant, string reason)
        {
            if (Finished) return;
            Finished = true;
            FireLeftoversQuietly();
            void Off(ref Transform t, string what) { if (t) { if (instant) Object.Destroy(t.gameObject); else AnticFx.PopOut(t, true, true, what, id); } t = null; }
            Off(ref phone, "phone_pop_out"); Off(ref can, "can_pop_out"); Off(ref rod, "rod_pop_out"); Off(ref bobber, "bobber_pop_out");
            Off(ref fish, "fish_pop_out"); Off(ref bed, "sunbed_pop_out"); Off(ref parasol, "parasol_pop_out");
            if (line) { Object.Destroy(line.gameObject); line = null; }
            Unstow(instant);
            Note((cancelled ? "cancel: " : "end: ") + reason);
        }

        void FireLeftoversQuietly() { evNext = evs.Count; }

        public void Note(string s) { if (Trace.Count < 64) Trace.Add($"{Age:F2}s {s}"); }
    }
}

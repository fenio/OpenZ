// OpenZ (GPL-3.0)
// View only: nothing here touches the sim.
//  - THE BOYS ARE ALAN AND BRAD: Resources/OpenZ/Zod/Boys/ZodAlan + ZodBrad (built by OpenZ > Zod Boys > 2 Build materials + prefabs).
//    Brad (the big one) stands at boyA (left), Alan at boyB (right edge, the same depth), Brad at boysModelScale (0.84 = the small
//    grunt's approved HQ scale), Alan at boysModelScale x alanScale: on one floor he is about 0.945 of Brad's height (their reference
//    sheet; as modelled he is 0.80). The opening backs: Alan on HandsClasped, Brad on
//    ArmsCrossed (their reference poses), random phase, darkened by boysShade like the grunts were. Without the prefabs the BS-01 grunts
//    stand there as before.
//  - THE FRONT SHOT (FrontShot, called by ZodBriefing on Alan's cues): a hard cut to the two of them standing, seen from Zod's side of the
//    room (the reverse of the opening, on the same side of the line, so Zod is off screen right and Alan screen left of Brad), one slow
//    eased push-in (sine in-out over the whole shot, dist + fov), a light hand-held (frontHandheld of the transmission shake), the back
//    wall of the room behind them (the same wall recipe as behind Zod, only while the shot is on), their own key / rim / fill / wall
//    wash. At the start Zod's shoulder edge sits at the right frame edge and slides out as the camera pushes (foreground parallax).
//    Composition: checked with a projection sim of the same maths (the numbers below).
//      Plan (briefing, "What's our plan of attack?"): Alan on Talk (the open-handed question), Brad on ArmsCrossed with a small nod
//      near the end of the question (servo). Celebrate (zod_win_02, "But sir... what about the celebration rocket fuel?"): both hold
//      their rocket-fuel can at the chest (Drink, Alan slowed to stay at the chest), Brad starts lifting his can to his face on
//      "celebration" (arm servo). Every visible action has its sound (the boys' step / servo set, Zod's voice gain, OzSoundLog).
//  - ALAN'S MOUTH: the MOUTH node (hinged under the Head bone) opens by alanJawDeg x the jaw value of his voice: the precomputed
//    curve Resources/OpenZ/Zod/Boys/alan_mouth_curves.json (syllable flaps from the approved clip, 60 fps, read at the voice source's
//    sample position, so it never drifts), or the live level of the voice source (GetOutputData) for a clip without a curve; only
//    inside his cue (Zod's words never move Alan's mouth). The opening sign is measured once per model (the jaw front must drop).
//  - BRAD'S MOUTH: the same drive on his own MOUTH node (the chin block, hinged by the ear discs) with his own 0..1 value and
//    bradJawDeg (10: beyond that his chin meets the collar). He has no words of his own in the current lines, so his cue is empty
//    and the value stays 0 (mouth shut); a line in which he speaks gives his cue with FrontBradCue after FrontShot.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        public const string AlanPrefabPath = "OpenZ/Zod/Boys/ZodAlan", BradPrefabPath = "OpenZ/Zod/Boys/ZodBrad", AlanCurvesPath = "OpenZ/Zod/Boys/alan_mouth_curves";
        public enum FrontKind { Plan, Celebrate }

        [Header("Alan + Brad")]
        [Tooltip("The two soldiers are Alan and Brad (Resources/OpenZ/Zod/Boys); off or missing = the BS-01 grunts")] public bool alanBradBoys = true;
        [Tooltip("Scale of both models (Alan 2.34 m, Brad 2.92 m at 1): 0.84 = the small grunt's approved HQ scale")] public float boysModelScale = 0.84f;
        [Tooltip("Front shot: Alan's spot, x = bearing from Zod's forward toward the boys' side (deg), y = distance from Zod (m)")] public Vector2 frontAlan = new Vector2(22f, 2.30f);
        [Tooltip("Front shot: Brad's spot (bearing deg, distance m)")] public Vector2 frontBrad = new Vector2(50f, 2.70f);
        [Tooltip("Front shot: where Alan stands (bearing deg, distance m): beside Brad, half a step (0.3 m) nearer to the camera, so the picture shows their real height ratio and his open-handed question passes in front of Brad, not into him. frontAlan stays the reference spot of the camera line, the wall and the lights (none of them moves)")] public Vector2 frontAlanStand = new Vector2(28.5f, 2.92f);
        [Tooltip("Front shot camera: turn off the boys-to-Zod line (deg; negative = the opening camera's side of the line)")] public float frontCamYaw = -33f;
        [Tooltip("Front shot camera height (m above the floor): Alan's eye line, looking up a little at Brad")] public float frontCamY = 1.80f;
        [Tooltip("Aim point between Alan's eyes (0) and Brad's eyes (1)")] [Range(0f, 1f)] public float frontAimAlan = 0.40f;
        [Tooltip("Aim point this much lower (m)")] public float frontAimDrop = 0.10f;
        [Tooltip("Plan-of-attack push: x = distance at the cut (m), y = distance at the end, z = fov at the cut, w = fov at the end")] public Vector4 frontPlanPush = new Vector4(3.50f, 2.90f, 29f, 27f);
        [Tooltip("Celebration push (the longer shot): start dist, end dist, start fov, end fov")] public Vector4 frontCelebratePush = new Vector4(3.60f, 3.00f, 30f, 28f);
        [Tooltip("Share of the transmission's hand-held shake kept in the front shot")] [Range(0f, 1f)] public float frontHandheld = 0.35f;
        [Tooltip("Alan's jaw at full voice (deg): 16 reads clearly in the feed")] public float alanJawDeg = 16f;
        [Tooltip("Brad's jaw at full voice (deg): his chin meets the collar beyond 10")] public float bradJawDeg = 10f;
        [Tooltip("Celebration: Alan's Drink speed (slow = the can stays at his chest while he asks)")] public float alanDrinkSpeed = 0.55f;
        [Tooltip("Celebration: Brad starts lifting his can at this share of Alan's question (0.6 = on 'celebration')")] [Range(0f, 1f)] public float bradLiftAtCue = 0.6f;
        [Tooltip("Plan: Brad's nod starts this long before the cut back (s)")] public float bradNodBeforeEnd = 0.65f;
        [Tooltip("Back wall behind the boys: distance behind their middle (m)")] public float frontWallBack = 2.6f;
        public float frontKeyIntensity = 10f, frontRimIntensity = 12f, frontFillIntensity = 2.2f, frontWashIntensity = 3.5f;
        [Tooltip("Alan's own scale on top of boysModelScale: standing on the same floor he is about 0.945 of Brad's height (the reference sheet of the two: the top of his head at Brad's brow), still the slimmer one. As modelled he is 0.80 of Brad")] public float alanScale = 1.19f;
        [Tooltip("Alan's hunch (his reference sheet: the upper back bent forward, the head dropped): forward lean on his Spine bone (deg)")] public float alanHunchDeg = 6f;
        [Tooltip("Alan's head dropped forward on top of the hunch (deg)")] public float alanHeadDropDeg = 5f;

        /// <summary>Clip facts of the models (measured on the exported clips): the Drink can leaves the chest at 2.13 s
        /// and is at the face from about 4 s; Talk lifts both hands from 0.5 s.</summary>
        const float BradLiftStart = 2.13f, AlanTalkLift = 0.5f;
        /// <summary>Bind pose heights at scale 1 (m): eyes, for the aim point.</summary>
        const float AlanEyesY = 2.09f, BradEyesY = 2.72f;
        /// <summary>Brad's lips from his MOUTH node (the jaw joint), model m: ahead of it and below it.</summary>
        const float BradLipsAhead = 0.23f, BradLipsBelow = 0.015f;

        Boy alanB, bradB;
        Boy TalkBoy => alanB ?? (boys.Count > 0 ? boys[0] : null);                                       // the BoysCut talk / shy overlay: Alan wherever he stands
        bool frontOn; FrontKind frontKind; float frontT0, frontHold = 1f, frontCueA, frontCueB, alanJaw, bradNodAt = -10f, alanServoAt = -10f, bradServoAt = -10f;
        float bradJaw, bradCueA = -1f, bradCueB = -2f;                                                    // Brad's own cue on the clip clock (B < A = none: his mouth stays shut)
        bool alanServoDone, bradServoDone; string bradServoKey = "";
        Vector3 frontAim, frontDir, frontAlanPos, frontBradPos;
        AudioSource frontVoice; readonly float[] jawBuf = new float[256];
        GameObject frontSet; Light frontKey, frontRim, frontFill, frontWash;
        static MaterialPropertyBlock shadeMpb;

        /// <summary>Alan and Brad are built (the front shot can play).</summary>
        public bool FrontReady => alanB != null && bradB != null && alanB.go && bradB.go;
        public bool FrontOn => frontOn;
        /// <summary>Alan's jaw 0..1 this frame (the comms VU follows it while he talks).</summary>
        public float AlanMouth01 { get; private set; }
        /// <summary>Brad's jaw 0..1 this frame (0 while he has no words of his own).</summary>
        public float BradMouth01 { get; private set; }
        /// <summary>Play check: 0..1 holds Brad's mouth at that value during a front shot; negative = off.</summary>
        public float BradMouthProof { get; set; } = -1f;
        /// <summary>Brad speaks inside the line of the running front shot: his cue on the clip clock (call after FrontShot).</summary>
        public void FrontBradCue(float cueA, float cueB) { bradCueA = cueA; bradCueB = cueB; }
        public int FrontShots { get; private set; }
        /// <summary>Play check: 0..1 holds the push at that point (the shot does not end while held); negative = off.</summary>
        public float FrontProofU { get; set; } = -1f;
        public float FrontU => frontOn ? Mathf.Clamp01((clock - frontT0) / Mathf.Max(0.1f, frontHold)) : -1f;

        // ------------------------------------------------------------------------------------------------ build
        /// <summary>BuildBoys: Alan + Brad from Resources (order = the grunt order: [0] the big one at boyA = Brad, [1] Alan at boyB).
        /// False = a prefab is missing (the grunts stand in).</summary>
        bool BuildAlanBrad()
        {
            if (!alanBradBoys) return false;
            var pa = Resources.Load<GameObject>(AlanPrefabPath); var pb = Resources.Load<GameObject>(BradPrefabPath);
            if (!pa || !pb)
            {
                Debug.Log("[ZodCameo] Alan / Brad prefabs not in Resources/OpenZ/Zod/Boys (OpenZ > Zod Boys > 2 Build materials + prefabs): the boys are the grunts");
                return false;
            }
            bradB = AddAlanBrad(pb, "ZodBoy_Brad", 2, "ArmsCrossed");
            alanB = AddAlanBrad(pa, "ZodBoy_Alan", 1, "HandsClasped");
            boysSource = "Resources/" + AlanPrefabPath + " + " + BradPrefabPath;
            return true;
        }

        Boy AddAlanBrad(GameObject prefab, string name, int who, string idle)
        {
            var go = Instantiate(prefab, transform);
            go.name = name;
            var b = new Boy { go = go, t = go.transform, seed = who * 7.31f + UnityEngine.Random.Range(0f, 3f), who = who, idleState = idle, scaleOverride = boysModelScale * (who == 1 ? alanScale : 1f) };
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = Layer;
            b.rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in b.rends)
            {
                r.renderingLayerMask = 1u << LightLayerBit;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;                   // nothing of theirs ever falls on Zod (as the grunts)
                r.lightProbeUsage = LightProbeUsage.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            b.anim = go.GetComponentInChildren<Animator>(true);
            if (b.anim) { b.anim.updateMode = AnimatorUpdateMode.UnscaledTime; b.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; b.anim.applyRootMotion = false; }
            b.head = Find(go.transform, "Head"); b.spine = Find(go.transform, "Spine");
            b.mouth = Find(go.transform, "MOUTH"); b.can = Find(go.transform, "FUEL_CAN"); b.canR = Find(go.transform, "FUEL_CAN_R");
            if (b.mouth)
            {   // which way opens the jaw: a point in front of the hinge must DROP for + (the README maths says +X; measured, never assumed)
                b.mouthRest = b.mouth.localRotation;
                Vector3 front = -go.transform.forward;                                                   // the robots face -Z
                Vector3 local = Quaternion.Inverse(b.mouth.rotation) * front * 0.1f;
                float y0 = b.mouth.TransformPoint(local).y;
                b.mouth.localRotation = b.mouthRest * Quaternion.Euler(10f, 0f, 0f);
                b.jawSign = b.mouth.TransformPoint(local).y < y0 ? 1f : -1f;
                b.mouth.localRotation = b.mouthRest;
                // Brad's MOUTH node is hinged at the jaw joint by the ear discs, well behind his lips: the can's sip is measured to the lips
                if (who == 2) b.lipsLocal = b.mouth.InverseTransformPoint(b.mouth.position + (front * BradLipsAhead + Vector3.down * BradLipsBelow) * Mathf.Abs(go.transform.lossyScale.y));
            }
            Cans(b, false);
            go.SetActive(false);
            boys.Add(b);
            if (!b.anim || !b.anim.runtimeAnimatorController || !b.head || !b.mouth)
                Debug.LogWarning($"[ZodCameo] {name}: animator {(b.anim && b.anim.runtimeAnimatorController ? "ok" : "MISSING")}, Head {(b.head ? "ok" : "MISSING")}, MOUTH {(b.mouth ? "ok" : "MISSING")} (rebuild: OpenZ > Zod Boys > 2)");
            return b;
        }

        static void Cans(Boy b, bool left, bool right = false)
        {
            if (b.can && b.can.gameObject.activeSelf != left) b.can.gameObject.SetActive(left);
            if (b.canR && b.canR.gameObject.activeSelf != right) b.canR.gameObject.SetActive(right);
        }

        /// <summary>Darkens Alan / Brad like TeamPaint.Charred darkened the grunts (k 0..0.8; 0 = their own colour). The can keeps its look.</summary>
        static void ShadeBoy(Boy b, float k)
        {
            if (b.rends == null) return;
            shadeMpb ??= new MaterialPropertyBlock();
            float v = 1f - Mathf.Clamp(k, 0f, 0.8f);
            foreach (var r in b.rends)
            {
                if (!r || r.name.StartsWith("FUEL_CAN")) continue;
                if (k <= 0.001f) { r.SetPropertyBlock(null); continue; }
                r.GetPropertyBlock(shadeMpb); shadeMpb.SetColor("_BaseColor", new Color(v, v, v, 1f)); r.SetPropertyBlock(shadeMpb);
            }
        }

        /// <summary>BeginBoys, per boy: the opening look of Alan / Brad (cans away, mouth shut, the boysShade darkening).</summary>
        void OpeningLook(Boy b)
        {
            if (b.who == 0) return;
            Cans(b, false); ShadeBoy(b, boysShade);
            if (b.mouth) b.mouth.localRotation = b.mouthRest;
        }

        // ------------------------------------------------------------------------------------------------ the front shot
        static Vector3 AtBearing(Vector2 bd) { float r = bd.x * Mathf.Deg2Rad; return new Vector3(bd.y * Mathf.Sin(r), 0f, bd.y * Mathf.Cos(r)); }

        /// <summary>Cut to Alan and Brad standing (hold s), one eased push-in. voice = Zod's voice source (Alan speaks inside his clip),
        /// cueA / cueB = Alan's cue on the clip clock, lineT = the clip clock now. False = not built (the caller keeps its fallback).</summary>
        public bool FrontShot(FrontKind kind, float hold, AudioSource voice, float cueA, float cueB, float lineT)
        {
            if (!FrontReady) return false;
            ResetBoys();                                                                                  // no opening / back cut under it
            frontKind = kind; frontHold = Mathf.Clamp(hold, 0.6f, 8f); frontT0 = clock; frontVoice = voice; frontCueA = cueA; frontCueB = cueB;
            alanJaw = 0f; AlanMouth01 = 0f; bradJaw = 0f; BradMouth01 = 0f; bradCueA = -1f; bradCueB = -2f;
            float s = boysModelScale;
            frontAlanPos = AtBearing(frontAlan); frontBradPos = AtBearing(frontBrad);
            Place(alanB, AtBearing(frontAlanStand)); Place(bradB, frontBradPos);
            // camera line (stage space; tools/front_shot_sim.py does the same). The aim keeps the approved framing: both eye lines at
            // boysModelScale (Alan's own scale makes him taller inside the same frame, the camera does not move for it)
            Vector3 aE = frontAlanPos + Vector3.up * AlanEyesY * s, bE = frontBradPos + Vector3.up * BradEyesY * s;
            frontAim = Vector3.Lerp(aE, bE, frontAimAlan) - Vector3.up * frontAimDrop;
            Vector3 axis = new Vector3(-frontAim.x, 0f, -frontAim.z).normalized;                         // from the boys toward Zod
            float yr = frontCamYaw * Mathf.Deg2Rad, c = Mathf.Cos(yr), sn = Mathf.Sin(yr);
            frontDir = new Vector3(axis.x * c - axis.z * sn, 0f, axis.x * sn + axis.z * c).normalized;
            // performance
            alanServoDone = bradServoDone = false; bradNodAt = -10f;
            if (kind == FrontKind.Plan)
            {
                Cans(alanB, false); Cans(bradB, false);
                PlayAt(alanB, "Talk", 0f, 1f);                                                            // the open-handed question
                PlayAt(bradB, "ArmsCrossed", UnityEngine.Random.value * ClipLen(bradB, "ArmsCrossed"), 1f);
                alanServoAt = clock + AlanTalkLift;                                                       // both hands come up
                bradNodAt = clock + Mathf.Max(0.3f, frontHold - bradNodBeforeEnd); bradServoAt = bradNodAt; bradServoKey = "brad_nod_servo";
            }
            else
            {
                Cans(alanB, true); Cans(bradB, true);
                PlayAt(alanB, "Drink", 0f, alanDrinkSpeed);                                               // the can at his chest while he asks
                float liftLine = cueA + bradLiftAtCue * Mathf.Max(0.2f, cueB - cueA);                     // Brad lifts on "celebration"
                float c0 = Mathf.Clamp(BradLiftStart - (liftLine - lineT), 0f, BradLiftStart - 0.2f);
                PlayAt(bradB, "Drink", c0, 1f);
                alanServoAt = -10f; alanServoDone = true;
                bradServoAt = clock + (BradLiftStart - c0); bradServoKey = "brad_can_lift_servo";
            }
            BuildFrontSet(); PlaceFrontSet(); SetFrontSet(true);
            frontOn = true; FrontShots++;
            Debug.Log($"[ZodCameo] front shot {kind}: Alan + Brad, {frontHold:F2} s, cue {cueA:F2}-{cueB:F2} at line t {lineT:F2}, push {(kind == FrontKind.Plan ? frontPlanPush : frontCelebratePush)}, jaw sign {alanB.jawSign:+0;-0}, curve {(AlanCurve(voice ? voice.clip : null) != null ? "yes" : "live level")}");
            return true;
        }

        /// <summary>The celebration cans stand upright in world space (long axis vertical, top up) the whole time they are held; only
        /// near the mouth (Brad's sip) does the top tilt toward it, at most PropUpright.CanSipMaxDeg. FUEL_CAN's mesh runs along its
        /// local +Z (top), seated on HAND_L / HAND_R, which turn with the wrist: rebuilt from world up after the Animator every frame.</summary>
        static readonly Dictionary<Transform, Quaternion> canRest = new Dictionary<Transform, Quaternion>();
        static readonly Dictionary<Transform, Vector3> canRestScale = new Dictionary<Transform, Vector3>();
        void UprightCan(Boy b, Transform c)
        {
            if (b == null || !c || !c.gameObject.activeInHierarchy) return;
            if (!canRest.TryGetValue(c, out var rest)) { canRest[c] = rest = c.localRotation; canRestScale[c] = c.localScale; }
            c.localRotation = rest;
            if (b.who == 1 && alanScale > 0.01f) c.localScale = canRestScale[c] / alanScale;                  // one standard can: Alan's own scale does not grow it (it stays the size of Brad's)
            float s = Mathf.Abs(b.t.lossyScale.y);
            var mouth = b.mouth ? b.mouth.TransformPoint(b.lipsLocal) : b.head ? b.head.position : c.position + Vector3.up * 0.5f * s;
            var front = b.t.rotation * Vector3.back;                                                      // the models face -Z
            var localTop = Vector3.forward;
            float w = PropUpright.SipWeight(Vector3.Distance(c.position, mouth), s);
            PropUpright.AlignAxis(c, localTop, PropUpright.CanTop(c.position, mouth, front, w, out _));
            PropUpright.Note(-1000 - b.who * 2 - (c == b.canR ? 1 : 0), b.who == 1 ? "alan" : b.who == 2 ? "brad" : "boy", w < 0.2f ? "can_hold" : "can_sip", PropUpright.AxisAngle(c, localTop), w);
        }

        void Place(Boy b, Vector3 pos)
        {
            Vector3 toZod = new Vector3(-pos.x, 0f, -pos.z).normalized;
            b.face = toZod; b.basePos = pos; b.baseRot = Quaternion.LookRotation(-toZod, Vector3.up);      // the models face -Z
            b.t.localPosition = pos; b.t.localRotation = b.baseRot; b.t.localScale = Vector3.one * (b.scaleOverride > 0f ? b.scaleOverride : boysModelScale);
            ShadeBoy(b, 0f);                                                                               // full colour: they are the subject now
            if (b.mouth) b.mouth.localRotation = b.mouthRest;
            b.go.SetActive(true);
        }

        void PlayAt(Boy b, string state, float clipSeconds, float speed)
        {
            if (!b.anim || !b.anim.runtimeAnimatorController) return;
            float len = ClipLen(b, state);
            b.speed = speed; b.anim.speed = frozen ? 0f : speed;
            b.anim.Play(state, 0, len > 0.01f ? Mathf.Repeat(clipSeconds, len) / len : 0f);
            b.anim.Update(0f);
        }

        static float ClipLen(Boy b, string clip)
        {
            if (!b.anim || !b.anim.runtimeAnimatorController) return 0f;
            foreach (var c in b.anim.runtimeAnimatorController.animationClips) if (c && c.name == clip) return c.length;
            return 0f;
        }

        /// <summary>The shot is over (its hold ran out, a new line, the window closed): the boys go, the set and lights go off.</summary>
        void EndFront()
        {
            if (!frontOn) return;
            frontOn = false; AlanMouth01 = 0f; alanJaw = 0f; BradMouth01 = 0f; bradJaw = 0f; frontVoice = null;
            foreach (var b in new[] { alanB, bradB })
            {
                if (b == null || !b.go) continue;
                if (b.mouth) b.mouth.localRotation = b.mouthRest;
                Cans(b, false);
                if (b.go.activeSelf) { PoseClean(b); b.go.SetActive(false); }
            }
            SetFrontSet(false);
            Debug.Log("[ZodCameo] front shot out: cut back to Zod");
        }

        /// <summary>LateUpdate after the Animator (and after TickBoys): the shot clock, Alan's mouth, Brad's nod, the sounds.</summary>
        void TickFront(float dt)
        {
            if (!frontOn) return;
            if (FrontProofU >= 0f) frontT0 += dt;                                                         // Play check: the frame is held
            float u = clock - frontT0;
            if (u >= frontHold) { EndFront(); return; }
            UprightCan(alanB, alanB != null ? alanB.can : null); UprightCan(alanB, alanB != null ? alanB.canR : null);
            UprightCan(bradB, bradB != null ? bradB.can : null); UprightCan(bradB, bradB != null ? bradB.canR : null);
            // Alan's jaw: open 25 ms, close 45 ms (as Zod's rig)
            float target = JawTarget(frontCueA, frontCueB);
            if (dt > 0f) alanJaw += (target - alanJaw) * (1f - Mathf.Exp(-dt / (target > alanJaw ? 0.025f : 0.045f)));
            AlanMouth01 = Mathf.Clamp01(alanJaw);
            if (alanB.mouth) alanB.mouth.localRotation = alanB.mouthRest * Quaternion.Euler(AlanMouth01 * alanJawDeg * alanB.jawSign, 0f, 0f);
            // Brad's jaw: the same drive inside his own cue (none in the current lines: the value stays 0 and the mouth shut)
            float bTarget = JawTarget(bradCueA, bradCueB);
            if (dt > 0f) bradJaw += (bTarget - bradJaw) * (1f - Mathf.Exp(-dt / (bTarget > bradJaw ? 0.025f : 0.045f)));
            BradMouth01 = BradMouthProof >= 0f ? Mathf.Clamp01(BradMouthProof) : Mathf.Clamp01(bradJaw);
            if (bradB.mouth) bradB.mouth.localRotation = bradB.mouthRest * Quaternion.Euler(BradMouth01 * bradJawDeg * bradB.jawSign, 0f, 0f);
            float t = clock;
            Vector3 aW = transform.TransformDirection(Vector3.Cross(Vector3.up, alanB.face)).normalized;    // + angle tips toward Zod
            Vector3 bW = transform.TransformDirection(Vector3.Cross(Vector3.up, bradB.face)).normalized;
            if (alanB.head)
            {   // talking: the chin lifts a little with each open syllable, a slow worried tilt (the Drink clip holds still)
                float talk = frontKind == FrontKind.Celebrate ? 1f : 0.5f;
                alanB.head.rotation = Quaternion.AngleAxis(-4f * AlanMouth01 * talk, aW) * Quaternion.AngleAxis(3f * talk * Mathf.Sin((t - frontT0) * 1.6f), transform.up) * alanB.head.rotation;
            }
            if (bradB.head && bradNodAt > 0f)                                                             // the nod: down and back up in 0.45 s
                bradB.head.rotation = Quaternion.AngleAxis(9f * Bump((t - bradNodAt) / 0.45f), bW) * bradB.head.rotation;
            if (!frozen)
            {   // every action has its sound
                if (!alanServoDone && t >= alanServoAt) { alanServoDone = true; BoysSound(ServoClips, boysServoDb - 3f, "alan_gesture_servo"); }
                if (!bradServoDone && t >= bradServoAt) { bradServoDone = true; BoysSound(ServoClips, boysServoDb + (frontKind == FrontKind.Celebrate ? 2f : 0f), bradServoKey); }
            }
        }

        /// <summary>
        /// Spine bone plus a dropped head, in the opening backs and the front shots (LateUpdate after TickBoys / TickFront).</summary>
        void AlanHunch()
        {
            if (alanB == null || !alanB.go || !alanB.go.activeSelf) return;
            Vector3 aW = transform.TransformDirection(Vector3.Cross(Vector3.up, alanB.face)).normalized;    // + angle tips toward Zod (his front)
            if (aW.sqrMagnitude < 0.5f) return;
            if (alanB.spine && Mathf.Abs(alanHunchDeg) > 0.01f) alanB.spine.rotation = Quaternion.AngleAxis(alanHunchDeg, aW) * alanB.spine.rotation;
            if (alanB.head && Mathf.Abs(alanHeadDropDeg) > 0.01f) alanB.head.rotation = Quaternion.AngleAxis(alanHeadDropDeg, aW) * alanB.head.rotation;
        }

        /// <summary>The jaw value 0..1 of the speaker whose cue runs from cueA to cueB on the clip clock (0 outside it, and for an empty cue).</summary>
        float JawTarget(float cueA, float cueB)
        {
            var v = frontVoice;
            if (cueB < cueA || !v || !v.clip || !v.isPlaying) return 0f;
            float t = v.timeSamples / (float)Mathf.Max(1, v.clip.frequency);
            if (t < cueA - 0.12f || t > cueB + 0.2f) return 0f;                                           // only his words
            var curve = AlanCurve(v.clip);
            if (curve != null)
            {
                float fi = t * alanCurveFps; int i0 = Mathf.FloorToInt(fi);
                if (i0 < 0 || i0 >= curve.Length) return 0f;
                int i1 = Mathf.Min(i0 + 1, curve.Length - 1);
                return Mathf.Lerp(curve[i0], curve[i1], fi - i0) / 100f;
            }
            v.GetOutputData(jawBuf, 0);                                                                     // no curve: the live level
            double sum = 0; for (int i = 0; i < jawBuf.Length; i++) sum += jawBuf[i] * jawBuf[i];
            float rms = Mathf.Sqrt((float)(sum / jawBuf.Length)) / Mathf.Max(0.05f, v.volume);
            float db = 20f * Mathf.Log10(rms + 1e-6f);
            float k = Mathf.Clamp01((db + 40f) / 24f);
            return k * k;
        }

        // ------------------------------------------------------------------------------------------------ Alan's mouth curves
        [System.Serializable] class AlanCurveLine { public string id; public int[] jaw; }
        [System.Serializable] class AlanCurveFile { public int fps = 60; public AlanCurveLine[] lines; }
        static Dictionary<string, int[]> alanCurves; static float alanCurveFps = 60f;
        static int[] AlanCurve(AudioClip clip)
        {
            if (!clip) return null;
            if (alanCurves == null)
            {
                alanCurves = new Dictionary<string, int[]>();
                var ta = Resources.Load<TextAsset>(AlanCurvesPath);
                if (ta)
                {
                    try
                    {
                        var f = JsonUtility.FromJson<AlanCurveFile>(ta.text);
                        alanCurveFps = Mathf.Max(1, f.fps);
                        if (f.lines != null) foreach (var l in f.lines) if (l != null && !string.IsNullOrEmpty(l.id) && l.jaw != null) alanCurves[l.id] = l.jaw;
                    }
                    catch (System.Exception e) { Debug.LogWarning("[ZodCameo] " + AlanCurvesPath + ": " + e.Message); }
                }
            }
            return alanCurves.TryGetValue(clip.name, out var j) ? j : null;
        }

        // ------------------------------------------------------------------------------------------------ camera (PlaceCamera hook)
        /// <summary>PlaceCamera: the front shot's camera (true = placed). One move: dist + fov eased (sine in-out) over the whole shot.</summary>
        bool PlaceFrontCamera(float pitch, float yaw, float roll, Vector3 off)
        {
            if (!frontOn) return false;
            float u = FrontProofU >= 0f ? Mathf.Clamp01(FrontProofU) : Mathf.Clamp01((clock - frontT0) / Mathf.Max(0.1f, frontHold));
            float e = 0.5f - 0.5f * Mathf.Cos(u * Mathf.PI);
            Vector4 p = frontKind == FrontKind.Celebrate ? frontCelebratePush : frontPlanPush;
            float d = Mathf.Lerp(p.x, p.y, e);
            Vector3 pos = frontAim + frontDir * d; pos.y = frontCamY;
            Vector3 wp = transform.TransformPoint(pos), wa = transform.TransformPoint(frontAim);
            var baseRot = Quaternion.LookRotation(wa - wp, transform.up);
            float h = frontHandheld;
            cam.transform.position = wp + baseRot * (off * h);
            cam.transform.rotation = baseRot * Quaternion.Euler(pitch * h, yaw * h, roll * h);
            cam.fieldOfView = Mathf.Lerp(p.z, p.w, e);
            if (dof != null)
            {   // Alan sharp, the back wall soft
                float dd = Vector3.Distance(pos, frontAlanPos + Vector3.up * 1.6f * boysModelScale);
                dof.gaussianStart.value = dd + 0.9f; dof.gaussianEnd.value = dd + 5f;
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------------ the room behind them
        /// <summary>The far wall of the HQ (the same recipe as the wall behind Zod: gunmetal wall, steel ribs, olive wainscot, console
        /// lip, amber strip) + a dark floor, and the shot's lights. Built once, on only while a front shot is on.</summary>
        void BuildFrontSet()
        {
            if (frontSet) return;
            frontSet = new GameObject("FrontSet") { layer = Layer };
            frontSet.transform.SetParent(transform, false);
            var gm = Lit(new Color32(40, 45, 42, 255), 0.32f, 0.35f);
            var rib = Lit(new Color32(58, 64, 60, 255), 0.45f, 0.6f);
            var olive = Lit(new Color32(66, 71, 52, 255), 0.25f, 0.2f);
            var dark = Lit(new Color32(20, 23, 21, 255), 0.4f, 0.4f);
            void Part(Renderer r) { if (r) r.transform.SetParent(frontSet.transform, false); }
            // local frame of the wall: x along it, y up, +z toward the camera (wall face at z = 0)
            Part(Box("FrontWall", new Vector3(0f, 2.1f, -0.1f), new Vector3(8f, 4.8f, 0.2f), gm));
            for (int i = -3; i <= 3; i++) Part(Box("FrontRib" + i, new Vector3(i * 1.3f + 0.35f, 2.1f, 0.04f), new Vector3(0.1f, 4.8f, 0.08f), rib));
            Part(Box("FrontWainscot", new Vector3(0f, 0.62f, 0.1f), new Vector3(8f, 1.24f, 0.22f), olive));
            Part(Box("FrontConsoleLip", new Vector3(0f, 1.25f, 0.18f), new Vector3(8f, 0.05f, 0.12f), dark));
            Part(Box("FrontAmberStrip", new Vector3(0f, 1.215f, 0.23f), new Vector3(8f, 0.022f, 0.02f), Glow(new Color32(60, 40, 10, 255), new Color(1f, 0.55f, 0.16f), 2.4f)));
            Part(Box("FrontFloor", new Vector3(0f, -0.01f, 4.5f), new Vector3(10f, 0.02f, 9f), dark));
            frontKey = AddLight("FrontKey", LightType.Spot, Vector3.zero, Vector3.forward, new Color(1f, 0.80f, 0.60f), frontKeyIntensity, 8f, 50f);
            frontRim = AddLight("FrontRim", LightType.Spot, Vector3.zero, Vector3.forward, new Color(1f, 0.74f, 0.52f), frontRimIntensity, 7f, 70f);
            frontFill = AddLight("FrontFill", LightType.Point, Vector3.zero, Vector3.zero, fillColor, frontFillIntensity, 6f);
            frontWash = AddLight("FrontWash", LightType.Point, Vector3.zero, Vector3.zero, wallColor, frontWashIntensity, 3.6f);
            SetFrontSet(false);
        }

        void PlaceFrontSet()
        {
            Vector3 mid = (frontAlanPos + frontBradPos) * 0.5f;
            Vector3 camFloor = frontAim + frontDir * frontPlanPush.x; camFloor.y = 0f;
            Vector3 fwd = mid - camFloor; fwd.y = 0f; fwd.Normalize();                                    // the view on the floor
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;                                    // screen right on the floor
            frontSet.transform.localPosition = mid + fwd * frontWallBack;
            frontSet.transform.localRotation = Quaternion.LookRotation(-fwd, Vector3.up);
            Vector3 head = mid + Vector3.up * 2.0f * boysModelScale;
            // key: high from screen right (Zod's side, the HQ lamp), in front of them; rim: high behind them from screen left;
            // fill: soft and cool from the camera's left; wash: warm on the wall between them
            Aim(frontKey, mid - fwd * 2.2f + right * 1.6f + Vector3.up * 3.2f, head - Vector3.up * 0.3f, frontKeyIntensity);
            Aim(frontRim, mid + fwd * 1.5f - right * 1.1f + Vector3.up * 3.3f, head, frontRimIntensity);
            Aim(frontFill, mid - fwd * 3.0f - right * 1.5f + Vector3.up * 1.5f, head, frontFillIntensity);
            Aim(frontWash, mid + fwd * (frontWallBack - 0.7f) + Vector3.up * 1.5f, mid + fwd * frontWallBack + Vector3.up * 1.5f, frontWashIntensity);
        }

        void Aim(Light l, Vector3 pos, Vector3 at, float intensity)
        {
            if (!l) return;
            l.transform.localPosition = pos;
            if (l.type == LightType.Spot) l.transform.rotation = Quaternion.LookRotation(transform.TransformPoint(at) - transform.TransformPoint(pos), transform.up);
            l.intensity = intensity;
        }

        void SetFrontSet(bool on)
        {
            if (frontSet && frontSet.activeSelf != on) frontSet.SetActive(on);
            foreach (var l in new[] { frontKey, frontRim, frontFill, frontWash }) if (l) l.enabled = on;
        }

        /// <summary>Play check: the front shot state in one line.</summary>
        public string FrontStatus()
        {
            if (!FrontReady) return "front shot: Alan / Brad not built (" + boysSource + ")";
            return $"front shot: {(frontOn ? frontKind + $" u {FrontU:F2} of {frontHold:F2} s, Alan jaw {AlanMouth01:F2} (sign {alanB.jawSign:+0;-0}), Brad jaw {BradMouth01:F2} x {bradJawDeg:F0} deg (sign {bradB.jawSign:+0;-0})" : "off")}, shots {FrontShots}, scale {boysModelScale:F2}, "
                 + $"Alan at {frontAlanPos.x:F2},{frontAlanPos.z:F2} Brad at {frontBradPos.x:F2},{frontBradPos.z:F2}, proof u {FrontProofU:F2}";
        }
    }
}

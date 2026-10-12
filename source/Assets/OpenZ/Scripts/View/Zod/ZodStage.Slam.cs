// OpenZ (GPL-3.0)
// The big red button.
//  - Slam() (beat fx "slam", anchored SlamHit s before the last word): the transmission CUTS to an insert of a big red launch
//    button on a small gunmetal console in front of Zod's belly (the cut hides in a signal burst, ZodBriefing), his right fist
//    winds up, slams down onto the cap exactly SlamHit s later (the cap sinks, the red lamp flashes, the hand-held camera jolts,
//    ZodBriefing plays the clunk and launches the strike), holds, lifts off, and the feed cuts back to his face.
//  - The console is built on the first slam and placed from Zod's own arm: the cap sits at 93 % of the arm's reach down-forward-
//    right of his shoulder, so the fist always lands on it (no clipping through the cap, no stretched arm), and it is hidden
//    whenever no slam is on screen (no prop in the other lines).
//  - The arm: the same two-bone IK as the v2 fist at the camera (elbow bends only around its own hinge, from-to rotations, straight
//    wrist, LateUpdate after the Animator), aimed at the button with the elbow out to his right and a little up.
// Knobs (inspector, ZodCameoStage): slamDuration, slamInsertFov, slamInsert (camera offset: forward / up / side), slamReach, slamDir.
using UnityEngine;


namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary>Seconds from Slam() to the fist hitting the button (the beat leads the stressed word by this much).</summary>
        public const float SlamHit = 0.36f;
        [Header("voice_lines_v3: red button slam")]
        [Tooltip("Seconds the insert (fist + button) stays on screen")] public float slamDuration = 1.15f;
        [Tooltip("Field of view of the insert shot")] public float slamInsertFov = 42f;
        [Tooltip("Insert camera from the cap: forward (toward the main camera), up, to his left (m)")] public Vector3 slamInsert = new Vector3(0.62f, 0.44f, 0.14f);
        [Tooltip("Cap distance from the shoulder as a share of the arm's reach")] [Range(0.7f, 0.98f)] public float slamReach = 0.93f;
        [Tooltip("Direction shoulder -> cap in (forward, his right, up) weights")] public Vector3 slamDir = new Vector3(0.72f, 0.30f, -0.62f);

        /// <summary></summary>
        public const int ConsoleLightBit = 6;                                                         // a defined, otherwise unused layer ("Light Layer 6"; only 0-7 exist)
        float slamAt = -10f, slamW; bool slamHitDone, buttonPlaced;
        /// <summary></summary>
        public System.Action SlamContact;
        public static int ContactFrame { get; private set; } = -1;
        Transform buttonRoot, buttonCap; Light buttonLight; Material capMat;
        Vector3 buttonTop, slamFwd, slamRight, capRest;

        /// <summary>True while the insert of the red button is on screen.</summary>
        public bool Slamming => buttonPlaced && clock - slamAt >= 0f && clock - slamAt < slamDuration;

        /// <summary>The fist slams the big red button (strength scales the camera jolt).</summary>
        public void Slam(float strength = 1f)
        {
            if (!uArm || !fArm || !hand || !cam) { Kick(0.8f); return; }
            EnsureButton();
            PlaceButton();
            slamAt = clock; slamHitDone = false; slamK = Mathf.Clamp(strength, 0.3f, 1.5f);
            fistWhoosh = false;
            thrustUntil = -1f;                                                                          // the slam owns the right arm
        }
        float slamK = 1f;

        void EnsureButton()
        {
            if (buttonRoot) return;
            var root = new GameObject("RedButtonConsole") { layer = Layer };
            root.transform.SetParent(transform, false);
            buttonRoot = root.transform;
            var gm = Lit(new Color32(52, 57, 54, 255), 0.4f, 0.55f);
            var dark = Lit(new Color32(22, 24, 23, 255), 0.35f, 0.4f);
            var yellow = Lit(new Color32(214, 170, 38, 255), 0.35f, 0.1f);
            capMat = Glow(new Color32(200, 24, 18, 255), new Color(1f, 0.08f, 0.04f), 0.6f);
            Part(PrimitiveType.Cube, "Pedestal", buttonRoot, new Vector3(0f, -0.5f, 0f), new Vector3(0.16f, 1f, 0.16f), dark);       // scaled to the floor in PlaceButton
            if (DeskV2)
            {
                if (capMat && capMat.HasProperty("_Cull")) capMat.SetFloat("_Cull", 0f);
                BuildDesk(gm, dark);
            }
            else
            {
                Part(PrimitiveType.Cube, "Console", buttonRoot, new Vector3(0f, -0.075f, 0f), new Vector3(0.30f, 0.09f, 0.26f), gm);
                Part(PrimitiveType.Cylinder, "Collar", buttonRoot, new Vector3(0f, -0.02f, 0f), new Vector3(0.17f, 0.012f, 0.17f), yellow);
                buttonCap = Part(PrimitiveType.Cylinder, "Cap", buttonRoot, new Vector3(0f, 0.005f, 0f), new Vector3(0.12f, 0.028f, 0.12f), capMat).transform;
            }
            capRest = buttonCap.localPosition;
            buttonLight = AddLight("ButtonLamp", LightType.Point, Vector3.zero, Vector3.zero, new Color(1f, 0.12f, 0.06f), 0f, 1.3f);   // stage light layer 7 only
            buttonLight.transform.SetParent(buttonRoot, false); buttonLight.transform.localPosition = new Vector3(0f, 0.12f, 0.05f);
            NoBezel(buttonLight);
            if (PropFist)
            {
                // (it washed the arm orange-red; moving the lamp in front of the cap, Play run 2, lit exactly the faces the camera sees)
                buttonLight.renderingLayerMask = 1 << ConsoleLightBit;
                var ld = buttonLight.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                if (ld) { ld.renderingLayers = 1u << ConsoleLightBit; ld.shadowRenderingLayers = 1u << ConsoleLightBit; }
                foreach (var r in buttonRoot.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= 1u << ConsoleLightBit;
            }
            foreach (var r in bezelRs) if (r) r.renderingLayerMask = 1u << BezelLightBit;
            if (bezelRs.Count > 0) Debug.Log($"[ZodCameo] desk: {bezelRs.Count} rubber bezels on rendering layer {BezelLightBit} only (the red lamp / alarm wash / beacons skip it), thin hinged safety lids: {LidCount}");
            buttonRoot.gameObject.SetActive(false);
        }

        Renderer Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            return Dress(go, m, true);
        }

        /// <summary>The cap goes where his fist can reach it: down-forward-right of the shoulder at slamReach of the arm.</summary>
        void PlaceButton()
        {
            Vector3 up = transform.up;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.position - (chest ? chest.position : uArm.position), up);
            slamFwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
            slamRight = model ? Vector3.ProjectOnPlane(model.right, up).normalized : transform.right;
            if (slamRight.sqrMagnitude < 1e-4f) slamRight = Vector3.Cross(up, slamFwd);
            float la = (fArm.position - uArm.position).magnitude, lb = (hand.position - fArm.position).magnitude;
            Vector3 dir = (slamFwd * slamDir.x + slamRight * slamDir.y + up * slamDir.z).normalized;
            buttonTop = uArm.position + dir * (la + lb) * slamReach - up * 0.09f;                    // the fist (knuckles) sits below the wrist
            if (PropFist) buttonTop += slamFwd * PropConsoleForward;
            buttonRoot.position = buttonTop;
            buttonRoot.rotation = Quaternion.LookRotation(slamFwd, up);
            float floor = transform.position.y, h = Mathf.Max(0.2f, buttonTop.y - 0.12f - floor);
            var ped = buttonRoot.Find("Pedestal");
            if (ped) { ped.localScale = DeskV2 ? new Vector3(0.30f, h, 0.18f) : new Vector3(0.16f, h, 0.16f); ped.localPosition = new Vector3(0f, -0.12f - h * 0.5f, DeskV2 ? -0.03f : 0f); }
            buttonRoot.gameObject.SetActive(true);
            buttonPlaced = true;
        }

        /// <summary>LateUpdate (after ApplyThrust): the fist path and the button (press, lamp); hides the console after the insert.</summary>
        void ApplySlam(float dt)
        {
            HideBody(PropFist && HideBodyInInsert && Slamming);
            if (!buttonPlaced) return;
            float u = clock - slamAt;
            bool on = u >= 0f && u < slamDuration;
            slamW = on ? Mathf.MoveTowards(slamW, 1f, dt / 0.06f) : Mathf.MoveTowards(slamW, 0f, dt / 0.2f);
            if (!on && slamW <= 0f)
            {
                if (buttonRoot && buttonRoot.gameObject.activeSelf) buttonRoot.gameObject.SetActive(false);
                HideFist(); buttonPlaced = false; return;
            }
            // the button: sinks on the hit, lamp flash, springs back when the fist lifts
            if (!slamHitDone && u >= SlamHit)
            {
                slamHitDone = true; Kick(0.9f * slamK); ContactFrame = Time.frameCount;
                var sc = SlamContact; SlamContact = null;
                try { sc?.Invoke(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            float press = PressV2 ? CapPress(u) : u < SlamHit ? 0f : u < 0.8f ? 1f : Mathf.Clamp01(1f - (u - 0.8f) / 0.12f);
            if (buttonCap) buttonCap.localPosition = capRest - Vector3.up * (PressV2 ? CapTravel : 0.018f) * press;
            if (buttonLight) buttonLight.intensity = u < SlamHit ? 0.4f : 0.4f + 5f * Mathf.Exp(-(u - SlamHit) / 0.18f);
            if (capMat) capMat.SetVector("_EmissionColor", (Vector4)(new Color(1f, 0.08f, 0.04f) * (u < SlamHit ? 0.6f : 0.6f + 5f * Mathf.Exp(-(u - SlamHit) / 0.2f))));
            if (DeskV2) TickDesk(u);
            if (PropFist) { LowerArm(Mathf.SmoothStep(0f, 1f, slamW)); ApplyFist(u, press, slamW); return; }
            if (!uArm || !fArm || !hand || slamW <= 0f) return;
            // the fist: wind-up to 0.34 m over the cap (0 -> 0.26 s), strike (accelerating, 0.26 -> SlamHit), pressed until 0.8 s, lift off
            float lift;
            if (u < 0.26f) lift = Mathf.SmoothStep(0.12f, 0.34f, u / 0.26f);
            else if (u < SlamHit) { float k = (u - 0.26f) / (SlamHit - 0.26f); lift = Mathf.Lerp(0.34f, 0f, k * k); }
            else if (u < SlamRelease) lift = 0f;
            else lift = Mathf.SmoothStep(0f, 0.22f, (u - SlamRelease) / 0.3f);
            Vector3 up = transform.up;
            float w = Mathf.SmoothStep(0f, 1f, slamW);
            if (chest)
            {   // the upper body leans into the slam (up to 8 deg forward-down on the hit)
                float lean = 8f * (u < SlamHit ? u / SlamHit : Mathf.Clamp01(1f - (u - SlamHit) / 0.6f));
                chest.rotation = Quaternion.AngleAxis(lean * w, slamRight) * chest.rotation;
            }
            if (PressV2) { PressArm(up, lift, press, w); return; }
            Vector3 T = buttonTop + up * (0.09f + 0.012f + lift) - slamFwd * (0.3f * lift);
            Vector3 A = uArm.position, B = fArm.position, C = hand.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Quaternion u0 = uArm.rotation, f0 = fArm.rotation, h0 = hand.localRotation;
            Quaternion rUp, r1;
            SolveArm(T, la, lb, out rUp, out r1);
            uArm.rotation = Quaternion.Slerp(u0, rUp * u0, w);
            fArm.rotation = Quaternion.Slerp(f0, rUp * r1 * f0, w);
            // 4. straight wrist: the fist continues the forearm down onto the cap (v1)
            hand.localRotation = Quaternion.Slerp(h0, handRest, w);
        }

        /// <summary>Two-bone IK from the current bones: r1 bends the elbow around its hinge (law of cosines), rUp aims the arm at
        /// T and swivels the elbow out to his right, a little up and back (a hammer blow, not a push).</summary>
        void SolveArm(Vector3 T, float la, float lb, out Quaternion rUp, out Quaternion r1)
        {
            Vector3 up = transform.up;
            Vector3 A = uArm.position, B = fArm.position, C = hand.position;
            float lat = Mathf.Clamp((T - A).magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.985f);
            Vector3 n = hingeOk ? (fArm.rotation * hingeLocal).normalized : slamRight, ua = A - B, va = C - B;
            Vector3 e1 = Vector3.ProjectOnPlane(ua, n).normalized, e2 = Vector3.Cross(n, e1).normalized;
            float phi = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - lat * lat) / (2f * la * lb), -1f, 1f));
            r1 = Quaternion.FromToRotation(va, (e1 * Mathf.Cos(phi) + e2 * Mathf.Sin(phi)) * lb);
            var r2 = Quaternion.FromToRotation(B + r1 * va - A, T - A);
            Vector3 ax = (T - A).normalized, b2 = r2 * (B - A);
            Vector3 e = Vector3.ProjectOnPlane(b2, ax), p = Vector3.ProjectOnPlane(slamRight * 0.8f + up * 0.3f - slamFwd * 0.2f, ax);
            var r3 = e.sqrMagnitude > 1e-8f && p.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(e, p) : Quaternion.identity;
            rUp = r3 * r2;
        }

        // was the prefab's import pose (hand local 3/45/12 deg), 30 deg of twist away from the mesh's bind pose (359/15/20), so the
        // fist was wrung against the forearm; (b) the wrist was aimed 9 cm over the cap centre, but this hand reaches 23 cm past
        // the wrist: the knuckles sank through the cap and the console. Press v2: the fist points down onto the cap (wrist swing
        // capped at MaxSwing off the forearm), the palm turns to face him (the turn is a forearm roll, pronation, capped at MaxRoll;
        // the wrist itself keeps the bind twist), and the fist's LOWEST point (a 45-point hull of the hand mesh in the hand bone's
        // bind space) lands exactly ON the cap top, which travels CapTravel down under it and springs back when he lifts.
        /// <summary>Press v2 on / off (the proof switches it to show the old press).</summary>
        public static bool PressV2 = true;
        /// <summary>Fist direction (down, toward the camera, to his right), the wrist swing limit, the palm target (back toward him,
        /// inward), an extra roll and the forearm roll limit (deg). Live knobs for the Play check.</summary>
        public static float PressDown = 1f, PressFwd = 0.32f, PressSide = 0.05f, MaxSwing = 30f, PalmBack = 1f, PalmIn = 0.35f, PressRoll = 0f, MaxRoll = 95f;
        /// <summary>The palm's normal in the hand bone's frame (bind pose).</summary>
        public static Vector3 PalmAxis = new Vector3(0f, 0f, -1f);   // Play check: -z = the palm (+z = the plated back of the fist faces the camera, forearm roll 46-71 deg)
        public const float SlamRelease = 0.8f, CapTop = 0.033f, CapTravel = 0.016f;
        /// <summary>Hand mesh bind pose relative to the forearm (Zod_Body bindposes; read at run time when possible).</summary>
        static Quaternion handBind = Quaternion.Euler(358.9f, 14.8f, 19.7f); static bool handBindRead;
        // support points of the right hand mesh (1513 vertices weighted >= .5 to mixamorigRightHand, bind space, Zod_Cameo.fbx)
        static readonly Vector3[] FistHull = {new Vector3(0.020f,0.233f,0.034f),new Vector3(-0.016f,0.230f,0.054f),new Vector3(-0.027f,0.228f,-0.055f),new Vector3(0.022f,0.231f,0.047f),new Vector3(-0.105f,0.205f,-0.036f),new Vector3(0.000f,0.233f,-0.006f),new Vector3(-0.026f,0.221f,0.068f),new Vector3(-0.033f,0.222f,-0.062f),new Vector3(-0.118f,0.195f,-0.025f),new Vector3(-0.013f,0.187f,0.113f),new Vector3(0.073f,0.171f,0.004f),new Vector3(-0.053f,0.181f,0.101f),new Vector3(0.023f,0.165f,-0.081f),new Vector3(-0.034f,0.172f,0.119f),new Vector3(-0.106f,0.188f,-0.054f),new Vector3(0.071f,0.167f,0.037f),new Vector3(-0.105f,0.178f,0.033f),new Vector3(-0.122f,0.177f,-0.041f),new Vector3(0.083f,0.121f,0.001f),new Vector3(-0.041f,0.168f,0.118f),new Vector3(-0.095f,0.174f,-0.066f),new Vector3(0.078f,0.149f,0.031f),new Vector3(-0.108f,0.160f,0.033f),new Vector3(0.023f,0.128f,-0.084f),new Vector3(-0.112f,0.161f,-0.055f),new Vector3(0.083f,0.097f,-0.004f),new Vector3(-0.049f,0.162f,0.112f),new Vector3(-0.003f,-0.009f,-0.070f),new Vector3(0.049f,0.063f,0.072f),new Vector3(-0.122f,0.159f,-0.034f),new Vector3(0.038f,-0.004f,-0.056f),new Vector3(-0.091f,0.107f,-0.063f),new Vector3(0.069f,0.013f,0.024f),new Vector3(-0.090f,0.091f,0.029f),new Vector3(0.032f,-0.004f,-0.063f),new Vector3(0.037f,-0.008f,0.063f),new Vector3(-0.101f,0.106f,-0.043f),new Vector3(0.072f,0.019f,0.003f),new Vector3(-0.013f,0.008f,0.069f),new Vector3(0.046f,-0.011f,0.054f),new Vector3(-0.049f,0.021f,0.025f),new Vector3(0.027f,-0.013f,-0.059f),new Vector3(0.024f,0.001f,0.072f),new Vector3(-0.017f,-0.003f,-0.070f),new Vector3(-0.028f,0.011f,0.058f)};
        // derived palm axis, and the table the running model uses (ZodStage.BuildModel sets it)
        static readonly Vector3[] FistHullV2 = {new Vector3(-0.018f,0.246f,-0.001f),new Vector3(0.056f,0.236f,0.026f),new Vector3(-0.031f,0.233f,-0.054f),new Vector3(0.084f,0.223f,0.005f),new Vector3(-0.058f,0.214f,-0.067f),new Vector3(0.007f,0.180f,0.105f),new Vector3(0.079f,0.207f,-0.062f),new Vector3(-0.073f,0.188f,0.050f),new Vector3(0.091f,0.216f,0.022f),new Vector3(0.109f,0.183f,-0.059f),new Vector3(-0.093f,0.172f,-0.035f),new Vector3(0.075f,0.165f,0.088f),new Vector3(0.038f,0.179f,-0.089f),new Vector3(-0.056f,0.188f,0.072f),new Vector3(0.131f,0.174f,-0.022f),new Vector3(-0.078f,0.168f,-0.063f),new Vector3(0.121f,0.165f,-0.053f),new Vector3(-0.093f,0.039f,0.023f),new Vector3(0.082f,0.164f,0.083f),new Vector3(-0.031f,0.192f,-0.084f),new Vector3(-0.064f,0.088f,0.076f),new Vector3(-0.073f,0.034f,-0.045f),new Vector3(0.036f,0.071f,0.094f),new Vector3(0.071f,0.174f,-0.078f),new Vector3(0.072f,0.037f,0.060f),new Vector3(-0.002f,0.043f,0.103f),new Vector3(0.111f,0.123f,-0.043f),new Vector3(-0.074f,0.046f,0.059f),new Vector3(0.070f,0.032f,0.014f),new Vector3(0.052f,0.037f,-0.028f),new Vector3(-0.059f,0.021f,0.013f)};
        public static Vector3 PalmAxisV2 = new Vector3(0.737f, -0.273f, 0.618f);
        static Vector3[] hullNow = FistHull;
        Vector3 PalmNow => ModelV2 ? PalmAxisV2 : PalmAxis;
        // proof readings (last press frame)
        float pGap, pSwing, pRoll, pWristTwist, pClear;

        /// <summary>Cap travel 0..1: pushed down in 0.05 s by the fist's contact, held, springs back (small overshoot) on the lift.</summary>
        static float CapPress(float u)
        {
            if (u < SlamHit) return 0f;
            if (u < SlamRelease) return Mathf.SmoothStep(0f, 1f, (u - SlamHit) / 0.05f);
            float r = (u - SlamRelease) / 0.12f;
            if (r >= 1f) return 0f;
            return Mathf.Lerp(1f, -0.12f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, r / 0.85f))) * (r > 0.85f ? (1f - r) / 0.15f : 1f);
        }

        void ReadHandBind()
        {
            if (handBindRead) return;
            handBindRead = true;
            try
            {
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = smr.sharedMesh; var bones = smr.bones; if (!m || bones == null) continue;
                    int hi = -1, fi = -1;
                    for (int i = 0; i < bones.Length; i++) { if (bones[i] == hand) hi = i; else if (bones[i] == fArm) fi = i; }
                    var bp = m.bindposes;
                    if (hi < 0 || fi < 0 || bp == null || bp.Length <= Mathf.Max(hi, fi)) continue;
                    handBind = Quaternion.Inverse(bp[fi].inverse.rotation) * bp[hi].inverse.rotation;
                    return;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[ZodCameo] hand bind pose not readable, using the measured one: " + e.Message); }
        }

        Quaternion HandRot(Vector3 d, Vector3 palmTarget)
        {
            Vector3 pp = Vector3.ProjectOnPlane(palmTarget, d);
            if (pp.sqrMagnitude < 1e-6f) pp = Vector3.ProjectOnPlane(-slamFwd, d);
            var L = Quaternion.LookRotation(pp.normalized, d);
            var pa = Vector3.ProjectOnPlane(PalmNow, Vector3.up); if (pa.sqrMagnitude < 1e-6f) pa = Vector3.forward;
            var B = Quaternion.LookRotation(pa.normalized, Vector3.up);
            return Quaternion.AngleAxis(PressRoll, d) * L * Quaternion.Inverse(B);
        }

        static Vector3 Support(Quaternion r, Vector3 dir)
        {
            var hull = hullNow;
            Vector3 best = hull[0]; float bd = float.NegativeInfinity;
            for (int i = 0; i < hull.Length; i++) { float s = Vector3.Dot(r * hull[i], dir); if (s > bd) { bd = s; best = hull[i]; } }
            return best;
        }

        static float TwistAbout(Quaternion q, Vector3 axis)
        {
            var v = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            var t = new Quaternion(v.x, v.y, v.z, q.w);
            float m = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (m < 1e-6f) return 0f;
            t = new Quaternion(t.x / m, t.y / m, t.z / m, t.w / m);
            float ang; Vector3 ax;
            t.ToAngleAxis(out ang, out ax);
            if (ang > 180f) ang -= 360f;
            return Vector3.Dot(ax, axis) < 0f ? -ang : ang;
        }

        /// <summary>Press v2: the arm IK (3 passes: the contact point depends on the fist's turn), the forearm roll, the wrist.</summary>
        void PressArm(Vector3 up, float lift, float press, float w)
        {
            ReadHandBind();
            Vector3 A = uArm.position, B = fArm.position, C = hand.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Quaternion u0 = uArm.rotation, f0 = fArm.rotation, h0 = hand.rotation;
            Vector3 capTopNow = buttonTop + up * (CapTop - CapTravel * Mathf.Max(0f, press));
            Vector3 liftOff = up * lift - slamFwd * (0.3f * lift);
            Vector3 dWant = (-up * PressDown + slamFwd * PressFwd + slamRight * PressSide).normalized;
            Vector3 palm = (-slamFwd * PalmBack - slamRight * PalmIn).normalized;
            Vector3 d = dWant; Quaternion Rh = HandRot(d, palm);
            for (int it = 0; it < 3; it++)
            {
                Rh = HandRot(d, palm);
                Vector3 c = Support(Rh, -up);
                Vector3 T = capTopNow + liftOff - Rh * c + up * 0.002f;                                  // 2 mm: touching, never inside
                uArm.rotation = u0; fArm.rotation = f0;
                Quaternion rUp, r1;
                SolveArm(T, la, lb, out rUp, out r1);
                uArm.rotation = rUp * u0; fArm.rotation = rUp * r1 * f0;
                Vector3 fa = (hand.position - fArm.position).normalized;
                float ang = Vector3.Angle(fa, dWant);
                d = ang > MaxSwing ? Vector3.Slerp(fa, dWant, MaxSwing / ang).normalized : dWant;          // the wrist bends at most MaxSwing
            }
            Rh = HandRot(d, palm);
            // the palm turn = a forearm roll (pronation / supination), capped; the wrist keeps the mesh's bind twist
            Vector3 fax = (hand.position - fArm.position).normalized;
            float tw = TwistAbout(Rh * Quaternion.Inverse(fArm.rotation * handBind), fax);
            float roll = Mathf.Clamp(tw, -MaxRoll, MaxRoll);
            fArm.rotation = Quaternion.AngleAxis(roll, fax) * fArm.rotation;
            Quaternion wristLocal = Quaternion.Inverse(fArm.rotation) * Rh;
            float wristTwist = TwistAbout(Quaternion.Inverse(handBind) * wristLocal, Vector3.up);
            if (Mathf.Abs(wristTwist) > 20f)
            {   // beyond the forearm's limit: the rest of the twist is dropped (the wrist twists at most 20 deg)
                wristLocal = wristLocal * Quaternion.AngleAxis(-(wristTwist - Mathf.Clamp(wristTwist, -20f, 20f)), Vector3.up);
                wristTwist = Mathf.Clamp(wristTwist, -20f, 20f);
            }
            Quaternion uS = uArm.rotation, fS = fArm.rotation;
            uArm.rotation = Quaternion.Slerp(u0, uS, w);
            fArm.rotation = Quaternion.Slerp(f0, fS, w);
            hand.rotation = Quaternion.Slerp(h0, fArm.rotation * wristLocal, w);
            // proof readings: contact gap (fist's lowest point - the cap top), wrist swing, forearm roll, wrist twist, clearance of
            // the rest of the fist over the console top (min height of the hull points outside the cap's 6 cm radius)
            float low = float.PositiveInfinity, clear = float.PositiveInfinity;
            for (int i = 0; i < hullNow.Length; i++)
            {
                Vector3 pw = hand.TransformPoint(hullNow[i]);
                float h = Vector3.Dot(pw - capTopNow, up); if (h < low) low = h;
                Vector3 lat = Vector3.ProjectOnPlane(pw - buttonTop, up);
                if (lat.magnitude > 0.065f) { float hc = Vector3.Dot(pw - (buttonTop - up * 0.03f), up); if (hc < clear) clear = hc; }
            }
            pGap = low; pClear = float.IsPositiveInfinity(clear) ? 1f : clear; pRoll = roll; pWristTwist = wristTwist;
            pSwing = Vector3.Angle((hand.position - fArm.position).normalized, hand.rotation * Vector3.up);
        }

        /// <summary>Proof: the press state now (stage seconds since Slam, cap travel, contact gap in mm, wrist / roll angles).</summary>
        public string PressStatus()
        {
            float u = clock - slamAt;
            return "press " + (PressV2 ? "v2" : "v1") + " u " + u.ToString("F2") + " s, cap travel " + (buttonCap ? (capRest.y - buttonCap.localPosition.y) * 1000f : 0f).ToString("F1") +
                   " mm, fist gap over cap top " + (pGap * 1000f).ToString("F1") + " mm, clearance over console " + (pClear * 1000f).ToString("F0") + " mm, wrist swing " + pSwing.ToString("F0") +
                   " deg, forearm roll " + pRoll.ToString("F0") + " deg, wrist twist " + pWristTwist.ToString("F0") + " deg";
        }
        public float PressU => clock - slamAt;
        public bool PressPlaced => buttonPlaced;

        /// <summary>Proof: renders the transmission camera into a w x h frame (the feed RenderTexture is put back).</summary>
        public Texture2D CaptureFeed(int w, int h)
        {
            if (!cam) return null;
            var prev = cam.targetTexture; float asp = cam.aspect;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt; cam.aspect = w / (float)h; cam.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; cam.targetTexture = prev; cam.aspect = asp; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary></summary>
        public void RenderFeedInto(RenderTexture rt)
        {
            if (!cam || !rt) return;
            var prev = cam.targetTexture; float asp = cam.aspect;
            cam.targetTexture = rt; cam.aspect = rt.width / (float)rt.height; cam.Render();
            cam.targetTexture = prev; cam.aspect = asp;
        }

        /// <summary>PlaceCamera: the insert shot while the slam is on (hand-held noise and kicks kept). True = the camera is placed.</summary>
        bool PlaceSlamCamera(float pitch, float yaw, float roll, Vector3 off)
        {
            if (!Slamming) return false;
            Vector3 up = transform.up;
            Vector3 ins = PropFist ? PropInsert : slamInsert;
            Vector3 ip = buttonTop + slamFwd * ins.x + up * ins.y - slamRight * ins.z;
            var ir = Quaternion.LookRotation(buttonTop + up * (PropFist ? PropInsertAim : 0.12f) - ip, up);
            cam.transform.position = ip + ir * off;
            cam.transform.rotation = ir * Quaternion.Euler(pitch, yaw, roll);
            cam.fieldOfView = PropFist ? PropInsertFov : slamInsertFov;
            return true;
        }

        /// <summary>SnapFraming / SetLive(false): a new line or a closed window never keeps the button or the insert.</summary>
        void ResetSlam()
        {
            slamAt = -10f; slamW = 0f; buttonPlaced = false;
            if (buttonRoot) buttonRoot.gameObject.SetActive(false);
            HideFist(); HideBody(false);
        }
    }
}

// OpenZ (GPL-3.0)
// The boys and the opening push-in.
//  - Two BS-01 grunts (ZGame's Z_bs01_grunt prefab; Resources/OpenZ/Zod/ZodBoys.asset where there is no ZGame, e.g. the menu),
//    team red, scales 0.92 and 0.84, stand in the left foreground of the opening camera with their backs to it, facing Zod. They
//    are built once with the stage (inactive), placed for each line from its framing, and hidden as soon as the push has passed
//    them (no pull-out, wide framing or fist can ever show them later in the line).
//  - The opening: BeginBoys(hold, push) starts the line `boysBack` m further back along the same line of sight, aimed `boysDrop` lower
//    and `boysAim` to screen left (Zod a little right of centre, the backs fill the left third), the camera `boysRise` m higher (over
//    their shoulders: their heads and backs read), field of view x `boysFov`; it holds
//    `hold` s (window preroll + his first word, "You two!"), then one eased push (smootherstep, `push` s, 0.8 by default) lands
//    exactly on the line's normal framing: PlaceCamera adds BoysLookOffset / BoysExtraDist / BoysFovMul, all 0 / 1 afterwards.
//    opening_sim.py, qc/opening_sim.json), so the camera never passes through them, and they are behind the lens or out of frame
//    at the end of the push (also with the 1.32 pull-out). Composition preview: preview/opening_sheet.png (Blender, same maths).
//  - Idle life on top of their Animator idle (LateUpdate, bones only): breathing, the big one shifts his weight (a small shuffle,
//    with a soft robot step) as the window opens, the small one nods ("sir!", with a small servo whirr) as Zod starts; random
//    start offset and speed, never in lockstep. Both sounds are logged in OzSoundLog ("ZodBoys").
//  - Look: their own TeamLit materials (red team paint on the shoulders), a warm rim spot on their shoulders from Zod's side and a
//    faint warm back light on the rendering layer of the stage; `boysShade` darkens them a little (TeamPaint.Charred) so the
//    foreground reads as depth and Zod stays the brightest thing in the shot. No shadows cast (nothing falls on Zod).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        [Header("zod_briefings_v3: the boys (opening shot)")]
        [Tooltip("Open the lines on the backs of two red grunts in the left foreground, then push in to Zod")] public bool boysOpening = true;
        [Tooltip("Metres further back along the same line of sight at the start of the line")] public float boysBack = 2.6f;
        [Tooltip("Camera this much higher at the start (m): over the boys' shoulders, their heads and backs in view")] public float boysRise = 0.55f;
        [Tooltip("Aim this much lower at the start (m)")] public float boysDrop = 0f;
        [Tooltip("Aim this far to screen left at the start (m): Zod sits between the big one (left) and the small one (right edge), his widest gesture clear of both. Was 0.32 while both stood on the left")] public float boysAim = 0.14f;
        [Tooltip("Field of view multiplier at the start (1.12 = 28 -> 31.4 deg)")] public float boysFov = 1.12f;
        [Tooltip("Big one (Brad): x = depth ahead of the start camera (m), y = lateral (m, - = screen left; 6 cm further left than before so Zod's widest gesture stays clear of him), z = scale, w = yaw (deg)")] public Vector4 boyA = new Vector4(1.95f, -0.92f, 0.92f, 10f);
        [Tooltip("Alan: depth (the same as Brad: side by side they show their real height ratio), lateral (+ = screen right: he stands at the right edge, clear of Zod and of Brad), scale (not used for Alan / Brad: boysModelScale and alanScale), yaw")] public Vector4 boyB = new Vector4(1.95f, 0.92f, 0.84f, -4f);
        [Tooltip("Foreground darkening (TeamPaint.Charred 0..0.8): the backs sit a little darker than Zod")] [Range(0f, 0.8f)] public float boysShade = 0.22f;
        [Tooltip("Warm rim spot on the boys' shoulders from Zod's side (URP intensity)")] public float boysRimIntensity = 9f;
        [Tooltip("Faint warm light on their backs from the camera side (URP intensity)")] public float boysBackIntensity = 1.4f;
        [Tooltip("The big one's shuffle step (Audio/Sfx/sd_step_robot_concrete_*), dB relative to Zod's voice gain")] public float boysStepDb = -12f;
        [Tooltip("The small one's nod servo (Audio/Sfx/sd_robot_servo_a/b/c), dB relative to Zod's voice gain")] public float boysServoDb = -22f;
        [Tooltip("Play check: hold the opening frame (no push) until released")] public bool boysHoldForProof;
        /// <summary>Zod's voice gain on the voice bus (ZodBriefing sets it for every line): the boys' step / servo follow it.</summary>
        public float BoysSfxGain { get; set; } = 1f;

        public const string GruntPrefabName = "Z_bs01_grunt";

        sealed class Boy
        {
            public GameObject go; public Transform t, head, spine; public Animator anim; public TeamPaint paint;
            public int who; public string idleState = "idle"; public float scaleOverride = -1f, jawSign = 1f;
            public Transform mouth, can, canR; public Quaternion mouthRest = Quaternion.identity; public Renderer[] rends;
            public Vector3 lipsLocal;                                                                 // the lips in the MOUTH node's space (zero = the node sits at them)
            public Vector3 basePos; public Quaternion baseRot; public Vector3 face; public float speed = 1f, seed, nodAt = -10f, shuffleAt = -10f, shuffleDir = 1f;
            public bool stepDone, servoDone;
            public Quaternion spineBase, spineOut, headBase, headOut; public bool poseOk;
        }

        // ------------------------------------------------------------------------------------------------ overlay base pose
        // Cause, measured in Play: the
        // Animator does not rewrite a bone whose clip curve is constant. Alan's HandsClasped and Brad's ArmsCrossed hold the Spine still, so
        // the bone overlays below (breathing, the nod, the BoysCut talk / shy beats, Alan's hunch) were applied on top of LAST frame's
        // result: Alan's spine turned ~7 deg per frame (a full turn every second), Brad's swung back and forth. The plan shot plays Talk,
        // which animates the spine, so it looked right there. Every overlay now starts from the bone's animated pose: a bone still
        // holding last frame's output gets last frame's animated pose back first (BoysPoseIn), and the output is remembered after the
        // last overlay (BoysPoseOut). A bone the Animator did write is taken as is.
        void BoysPoseIn()
        {
            foreach (var b in boys)
            {
                if (b == null || !b.go || !b.go.activeInHierarchy) { if (b != null) b.poseOk = false; continue; }
                PoseIn(b.spine, ref b.spineBase, b.spineOut, b.poseOk);
                PoseIn(b.head, ref b.headBase, b.headOut, b.poseOk);
                b.poseOk = true;
            }
        }

        static void PoseIn(Transform t, ref Quaternion basePose, Quaternion lastOut, bool ok)
        {
            if (!t) return;
            var q = t.localRotation;
            if (ok && q.x == lastOut.x && q.y == lastOut.y && q.z == lastOut.z && q.w == lastOut.w) t.localRotation = q = basePose;   // not rewritten this frame: undo last frame's overlay
            basePose = q;
        }

        void BoysPoseOut()
        {
            foreach (var b in boys)
            {
                if (b == null || !b.poseOk || !b.go || !b.go.activeInHierarchy) continue;
                if (b.spine) b.spineOut = b.spine.localRotation;
                if (b.head) b.headOut = b.head.localRotation;
            }
        }

        /// <summary>Before a boy is hidden: his overlaid bones go back to the animated pose (the next showing starts clean).</summary>
        static void PoseClean(Boy b)
        {
            if (b == null || !b.poseOk) return;
            if (b.spine) b.spine.localRotation = b.spineBase;
            if (b.head) b.head.localRotation = b.headBase;
            b.poseOk = false;
        }
        readonly List<Boy> boys = new List<Boy>();
        bool boysBuilt, boysOn; float boysT0, boysHold, boysPushS = 0.8f; string boysSource = "not built";
        Light boysRim, boysBackLight; AudioSource boysSfx;
        static readonly string[] StepClips = { "sd_step_robot_concrete_02", "sd_step_robot_concrete_05", "sd_step_robot_concrete_08", "sd_step_robot_concrete_11" };
        static readonly string[] ServoClips = { "sd_robot_servo_a", "sd_robot_servo_b", "sd_robot_servo_c" };
        static readonly Dictionary<string, AudioClip> boysClips = new Dictionary<string, AudioClip>();
        Vector3 boysRightH;                                                                          // screen-left/right on the floor (stage space)

        /// <summary>1 = the opening pose (wide, on the boys' backs), 0 = the line's own framing.</summary>
        public float BoysK { get; private set; }
        /// <summary>True while the two grunts are placed and shown for the current line.</summary>
        public bool BoysShown => boysOn;
        public int BoysCount => boys.Count;
        public string BoysSource => boysSource;

        // ------------------------------------------------------------------------------------------------ build
        static GameObject GruntPrefab(out string source)
        {
            var g = ZGame.I;
            var p = g ? g.Prefab(GruntPrefabName) : null;
            if (p) { source = "ZGame." + GruntPrefabName; return p; }
            var r = Resources.Load<ZodBoysRef>(ZodBoysRef.ResPath);
            if (r && r.grunt) { source = "Resources/" + ZodBoysRef.ResPath; return r.grunt; }
            source = "none (no ZGame prefab, no Resources/" + ZodBoysRef.ResPath + ".asset)";
            return null;
        }

        public static int BoysHatsHidden { get; private set; }
        void BuildBoys()
        {
            if (boysBuilt) return;
            bool ab = BuildAlanBrad();
            var prefab = ab ? null : GruntPrefab(out boysSource);
            if (!ab && !prefab) { Debug.LogWarning("[ZodCameo] the boys: no grunt prefab (" + boysSource + "): lines open on Zod as before"); return; }
            boysBuilt = true;
            for (int i = 0; i < (ab ? 0 : 2); i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = i == 0 ? "ZodBoy_Big" : "ZodBoy_Small";
                var b = new Boy { go = go, t = go.transform, seed = i * 7.31f + Random.Range(0f, 3f) };
                foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = Layer;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.renderingLayerMask = 1u << LightLayerBit;
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;       // nothing of theirs ever falls on Zod
                    r.lightProbeUsage = LightProbeUsage.Off;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                // scene (the grunt kit head = the domed helmet with the antenna); their bare BS-01 heads show
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.name.Contains("Kit") && r.name.Contains("Head")) { r.enabled = false; BoysHatsHidden++; }
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                b.anim = go.GetComponentInChildren<Animator>(true);
                if (b.anim)
                {
                    b.anim.updateMode = AnimatorUpdateMode.UnscaledTime; b.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    b.anim.applyRootMotion = false;
                }
                b.head = Find(go.transform, "mixamorigHead"); b.spine = Find(go.transform, "mixamorigSpine2");
                b.paint = go.GetComponent<TeamPaint>();
                if (!b.paint) b.paint = go.AddComponent<TeamPaint>();
                b.paint.PlainPaint = true;                                                              // this room keeps its own look: no field gain
                b.paint.Team = Team.Red;                                                                // the player's side: red, like Z
                b.paint.Charred = boysShade;
                go.SetActive(false);
                boys.Add(b);
            }
            boysRim = AddLight("BoysRim", LightType.Spot, new Vector3(1f, 3f, 1.5f), new Vector3(1.2f, 1.7f, 3f), new Color(1f, 0.74f, 0.5f), boysRimIntensity, 5f, 60f);
            boysBackLight = AddLight("BoysBack", LightType.Point, new Vector3(1.5f, 2.2f, 4.6f), new Vector3(1.5f, 2.2f, 4.6f), new Color(1f, 0.78f, 0.6f), boysBackIntensity, 2.6f);
            boysRim.enabled = boysBackLight.enabled = false;
            boysSfx = gameObject.AddComponent<AudioSource>();                                           // 2D, like Zod's voice and the static
            boysSfx.playOnAwake = false; boysSfx.loop = false; boysSfx.spatialBlend = 0f; boysSfx.priority = 60; boysSfx.dopplerLevel = 0f;
            boysSfx.bypassReverbZones = true; boysSfx.ignoreListenerPause = false;
            Debug.Log(ab ? $"[ZodCameo] the boys: Alan + Brad from {boysSource}, scale {boysModelScale:F2}" : $"[ZodCameo] the boys: 2 grunts from {boysSource}, team red, scales {boyA.z:F2} / {boyB.z:F2}");
        }

        // ------------------------------------------------------------------------------------------------ opening
        /// <summary>zod_briefings_v3: open the line on the boys' backs. hold = seconds on the wide frame (window preroll + his first
        /// word), push = the eased push-in to the line's framing. Call right after SetFraming + SnapFraming. False = no grunts
        /// (the line opens on Zod as before).</summary>
        public bool BeginBoys(float hold, float push)
        {
            ResetBoys();
            alanOutEnd = -10f; alanOutSteps = 0;
            if (!boysOpening) return false;
            BuildBoys();
            if (boys.Count < 2) return false;
            boysHold = Mathf.Max(0f, hold); boysPushS = Mathf.Clamp(push, 0.2f, 2f); boysT0 = clock;
            // the line's own framing, whatever clip played last: the boys are placed for it, and a gesture's wider framing
            // (fist_pump, victory_cheer ... ClipFraming) waits until the push is done (UpdateWantFraming, ResetBoys)
            boysOn = true;
            var fr = Framings.TryGetValue(lineFraming, out var lf) ? lf : Framings["medium"];
            wantDist = dist = fr.dist; wantLook = lookY = fr.look; distVel = lookVel = 0f;
            // the opening camera (PlaceCamera's base pose + the opening offsets, no hand-held noise): stage space
            Vector3 dir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward; dir.y = elevation; dir.Normalize();
            Vector3 fh = new Vector3(-dir.x, 0f, -dir.z).normalized;                                    // the line of sight on the floor
            boysRightH = Vector3.Cross(Vector3.up, fh).normalized;                                     // screen right on the floor
            Vector3 look0 = new Vector3(0f, wantLook - boysDrop, 0.02f) - boysRightH * boysAim;
            Vector3 c0 = look0 + dir * (wantDist + boysBack) + Vector3.up * boysRise;
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                var b = boys[i]; var p = i == 0 ? boyA : boyB;
                Vector3 pos = c0 + fh * p.x + boysRightH * p.y; pos.y = 0f;
                Vector3 toZod = new Vector3(-pos.x, 0f, -pos.z).normalized;
                b.face = Quaternion.Euler(0f, p.w, 0f) * toZod;
                b.basePos = pos; b.baseRot = Quaternion.LookRotation(-b.face, Vector3.up);          // BS-01 models face -Z
                b.t.localPosition = pos; b.t.localRotation = b.baseRot; b.t.localScale = Vector3.one * (b.scaleOverride > 0f ? b.scaleOverride : Mathf.Clamp(p.z, 0.5f, 1.3f));
                if (b.paint) b.paint.Charred = boysShade;
                OpeningLook(b);
                b.go.SetActive(true);
                if (b.anim && b.anim.runtimeAnimatorController)
                {   // their idle, desynced (random phase, +-4 % speed)
                    b.speed = Random.Range(0.96f, 1.04f); b.anim.speed = frozen ? 0f : b.speed;
                    b.anim.Play(b.idleState, 0, Random.value);
                    b.anim.Update(0f);
                }
                // big one shifts his weight as the window opens, the small one nods as Zod starts ("You two!")
                b.shuffleAt = i == 0 ? clock + 0.12f : -10f; b.shuffleDir = Random.value < 0.5f ? -1f : 1f;
                b.stepDone = i != 0; b.servoDone = false;
                b.nodAt = i == 1 ? clock + Mathf.Max(0.2f, boysHold - 0.15f) : clock + boysHold + 0.25f;
                mid += pos * 0.5f;
            }
            // lights: a warm rim on their shoulders from Zod's side (high, in front of them), a faint warm fill on their backs
            Vector3 toZ = new Vector3(-mid.x, 0f, -mid.z).normalized;
            Vector3 at = mid + Vector3.up * 1.65f;
            boysRim.transform.localPosition = at + toZ * 1.3f + Vector3.up * 1.2f;
            boysRim.transform.rotation = Quaternion.LookRotation(transform.TransformPoint(at) - transform.TransformPoint(boysRim.transform.localPosition));
            boysRim.intensity = boysRimIntensity; boysRim.enabled = true;
            boysBackLight.transform.localPosition = c0 + Vector3.up * 0.7f - fh * 0.3f;
            boysBackLight.intensity = boysBackIntensity; boysBackLight.enabled = true;
            BoysK = 1f;
            return true;
        }

        /// <summary>
        /// cut back to the over-the-shoulder shot on the boys for Alan's line (hold s), then the push in to Zod again. talk: Alan (the big
        /// one) bobs his head as he speaks; shy: he hangs his head (looks down, shoulders a little in) for the whole beat.</summary>
        public bool BoysCut(float hold, bool shy)
        {
            if (!BeginBoys(Mathf.Max(0.3f, hold), 0.45f)) return false;
            boysTalkT0 = clock; boysTalkUntil = clock + hold; boysShy = shy;
            foreach (var b in boys) { b.stepDone = true; b.servoDone = true; b.nodAt = -10f; b.shuffleAt = clock + 9999f; }
            BoysCuts++;
            return true;
        }
        float boysTalkT0 = -10f, boysTalkUntil = -10f; bool boysShy;

        [Tooltip("Alan's way out of the opening picture, to the right (m): past the frame edge")] public float alanOutDist = 0.6f;
        /// <summary>Seconds Alan takes to step out of the opening picture.</summary>
        public const float AlanOutSeconds = 0.9f;
        float alanOutEnd = -10f; int alanOutSteps;
        /// <summary>A briefing's opening: Alan is out of the picture `seconds` from now (before the first word). A long opening: he
        /// stands at the right and steps out in time. An opening too short for that: he is not in it at all. Call after BeginBoys.</summary>
        public void AlanOutBefore(float seconds)
        {
            if (alanB == null || !alanB.go || !boysOn) return;
            if (seconds >= AlanOutSeconds + 0.6f) { alanOutEnd = clock + seconds; alanOutSteps = 0; }
            else if (alanB.go.activeSelf) { PoseClean(alanB); alanB.go.SetActive(false); }
        }
        public int BoysCuts { get; private set; }

        /// <summary>No opening (a new line, the stage going dark): grunts hidden, camera offsets off.</summary>
        void ResetBoys()
        {
            bool was = boysOn;
            boysOn = false; BoysK = 0f;
            foreach (var b in boys) if (b.go && b.go.activeSelf) { PoseClean(b); b.go.SetActive(false); }
            if (boysRim) boysRim.enabled = false;
            if (boysBackLight) boysBackLight.enabled = false;
            EndFront();
            if (was) UpdateWantFraming();                                                              // a gesture's wider framing waited for the push
        }

        /// <summary>The game's pause: their Animator stops (speed 0, it still writes the pose, so the bone overlays never pile up).</summary>
        void FreezeBoys(bool f) { foreach (var b in boys) if (b.anim) b.anim.speed = f ? 0f : b.speed; }

        static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static float Bump(float x) => x <= 0f || x >= 1f ? 0f : Mathf.Sin(x * Mathf.PI);

        /// <summary>LateUpdate (after the Animator, before PlaceCamera): the opening curve and the boys' idle life.</summary>
        void TickBoys(float dt)
        {
            if (!boysOn) { BoysK = 0f; return; }
            if (boysHoldForProof) boysT0 += dt;                                                      // Play check: freeze the opening frame
            float u = clock - boysT0;
            BoysK = u < boysHold ? 1f : 1f - Smoother((u - boysHold) / boysPushS);
            if (u > boysHold + boysPushS + 0.05f) { ResetBoys(); return; }                           // the push has passed them: gone for the line
            float t = clock;
            foreach (var b in boys)
            {
                if (!b.go || !b.go.activeSelf) continue;
                if (!frozen && !b.stepDone && t >= b.shuffleAt) { b.stepDone = true; BoysSound(StepClips, boysStepDb, "boys_step"); }        // every action has its sound
                if (!frozen && !b.servoDone && t >= b.nodAt) { b.servoDone = true; BoysSound(ServoClips, boysServoDb, "boys_nod_servo"); }
                Vector3 right = Vector3.Cross(Vector3.up, b.face).normalized;                         // his right, stage space
                // weight shift: a 0.5 s shuffle to one side (4 cm, 3 deg) that stays, plus a slow sway
                float sh = Smoother((t - b.shuffleAt) / 0.5f) * (t >= b.shuffleAt ? 1f : 0f);
                float sway = 0.006f * Mathf.Sin((t + b.seed) * 2f * Mathf.PI * 0.23f);
                b.t.localPosition = b.basePos + right * (b.shuffleDir * 0.04f * sh + sway);
                if (b == alanB && alanOutEnd > 0f)
                {   // Alan leaves to the right before the first word: two side steps, eased, each with its step sound; then he is gone for the line
                    float ko = (t - (alanOutEnd - AlanOutSeconds)) / AlanOutSeconds;
                    if (ko >= 1f) { PoseClean(b); b.go.SetActive(false); alanOutEnd = -10f; continue; }
                    if (ko > 0f)
                    {
                        if (!frozen && alanOutSteps == 0) { alanOutSteps = 1; BoysSound(StepClips, boysStepDb, "alan_step_out"); }
                        if (!frozen && alanOutSteps == 1 && ko >= 0.5f) { alanOutSteps = 2; BoysSound(StepClips, boysStepDb, "alan_step_out"); }
                        b.t.localPosition += boysRightH * (alanOutDist * Smoother(ko)) + Vector3.up * (0.025f * Mathf.Abs(Mathf.Sin(ko * 2f * Mathf.PI)));
                    }
                }
                b.t.localRotation = Quaternion.AngleAxis(b.shuffleDir * 3f * sh + 0.8f * Mathf.Sin((t + b.seed) * 1.3f), Vector3.up) * b.baseRot;
                Vector3 axisW = transform.TransformDirection(Vector3.Cross(Vector3.up, b.face)).normalized;   // + angle tips toward Zod
                if (b.spine)                                                                          // breathing
                    b.spine.rotation = Quaternion.AngleAxis(1.1f * Mathf.Sin((t + b.seed) * 2f * Mathf.PI * 0.3f), axisW) * b.spine.rotation;
                if (b.head)                                                                           // the nod: down and back up in 0.42 s
                    b.head.rotation = Quaternion.AngleAxis(11f * Bump((t - b.nodAt) / 0.42f), axisW) * b.head.rotation;
                if (b == TalkBoy && t >= boysTalkT0 && t < boysTalkUntil + 0.4f)
                {
                    float on = Smoother((t - boysTalkT0) / 0.3f) * (1f - Smoother((t - boysTalkUntil) / 0.4f));
                    if (boysShy)
                    {   // shy: the head hangs (28 deg down) and turns a little away to his side so it reads from behind, the back curls
                        if (b.head) b.head.rotation = Quaternion.AngleAxis(28f * on, axisW) * Quaternion.AngleAxis(-14f * on, transform.up) * b.head.rotation;
                        if (b.spine) b.spine.rotation = Quaternion.AngleAxis(9f * on, axisW) * b.spine.rotation;
                    }
                    else
                    {   // talking: quick head bobs + a small turn to Brad and back, the shoulders lift on the question
                        float bob = Mathf.Abs(Mathf.Sin((t - boysTalkT0) * Mathf.PI * 2.6f));
                        if (b.head) b.head.rotation = Quaternion.AngleAxis(6f * bob * on, axisW) * Quaternion.AngleAxis(7f * Mathf.Sin((t - boysTalkT0) * 1.7f) * on, transform.up) * b.head.rotation;
                        if (b.spine) b.spine.rotation = Quaternion.AngleAxis(-3f * on, axisW) * b.spine.rotation;
                    }
                }
            }
        }

        /// <summary>The boys' shuffle step / nod servo: a random clip of the set, 2D, at Zod's voice gain + db, logged in OzSoundLog.</summary>
        void BoysSound(string[] names, float db, string key)
        {
            if (!boysSfx) return;
            string n = names[Random.Range(0, names.Length)];
            if (!boysClips.TryGetValue(n, out var c)) boysClips[n] = c = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/" + n);
            float vol = Mathf.Clamp01(BoysSfxGain * Mathf.Pow(10f, db / 20f));
            if (!c) { OzSoundLog.Add("ZodBoys", key, n, "missing clip"); return; }
            boysSfx.pitch = Random.Range(0.94f, 1.06f);
            boysSfx.PlayOneShot(c, vol);
            OzSoundLog.Add("ZodBoys", key, c.name, "played", vol);
        }

        // ------------------------------------------------------------------------------------------------ PlaceCamera hooks
        /// <summary>Opening offset of the aim point (stage space): lower and to screen left, fading out with the push.</summary>
        Vector3 BoysLookOffset() => BoysK <= 0f ? Vector3.zero : (Vector3.down * boysDrop - boysRightH * boysAim) * BoysK;
        /// <summary>Opening extra distance along the line of sight (m).</summary>
        float BoysExtraDist() => BoysK <= 0f ? 0f : boysBack * BoysK;
        /// <summary>Opening lift of the camera (m, stage up): over the boys' shoulders.</summary>
        float BoysRise() => BoysK <= 0f ? 0f : boysRise * BoysK;
        /// <summary>Opening field of view multiplier.</summary>
        float BoysFovMul() => BoysK <= 0f ? 1f : Mathf.Lerp(1f, boysFov, BoysK);

        /// <summary>Play check: the boys' state in one line.</summary>
        public string BoysStatus()
        {
            var s = $"boys: {boys.Count} built from {boysSource}, shown {boysOn}, k {BoysK:F2}, hold {boysHold:F2} s, push {boysPushS:F2} s";
            foreach (var b in boys)
                if (b.go) s += $" | {b.go.name} active {b.go.activeSelf} at {b.t.localPosition.x:F2},{b.t.localPosition.z:F2} scale {b.t.localScale.x:F2} team {(b.paint ? b.paint.Team.ToString() : "?")}";
            return s;
        }
    }
}

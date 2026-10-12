// OpenZ (GPL-3.0) - Commander Zod cameo: talking jaw, head bob and cigar ember for the Zod_Cameo model.
// Put it on the root of the Zod_Cameo instance (the object with the Animator). Runs in LateUpdate after the Animator,
// so it layers on top of any clip (idle, talk_*, point_map, salute ...).
//  - Jaw: the model's "Jaw" bone slides down along the face like a ventriloquist dummy (the "JawOpen" socket marks the
//    fully open position, 22 mm) and tilts a few degrees around the "JawHingeRight" axis. The cigar rides on the jaw.
//  - Mouth signal: the precomputed curve of the playing AudioClip from zod_mouth_curves.json (one flap per syllable,
//    matched by clip name = line id, e.g. "zod_brief_L02"); otherwise the live RMS of the AudioSource.
//  - Head: a quick nod on every stressed word (curve "nods" or live onsets), the head lifts a little as the jaw opens,
//    and a slow sway while talking. Procedural fallback: without a Jaw bone only the head bob runs.
//  - Cigar: the ember (material with "Cigar" in its name, Zod_Cigar_Emission map) flickers; Puff() / the AnimationEvent
//    "ZodPuff" makes it glow up (the cigar_puff clip draws at about 0.5-1.5 s).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class ZodTalkRig : MonoBehaviour
    {
        [Header("Voice")]
        [Tooltip("2D AudioSource that plays the Zod lines (Materials/zod/voice/*.wav)")]
        public AudioSource voice;
        [Tooltip("zod_mouth_curves.json (TextAsset)")]
        public TextAsset mouthCurves;
        [Tooltip("Use the live RMS of the AudioSource when the playing clip has no precomputed curve")]
        public bool liveFallback = true;
        [Tooltip("Live RMS (dBFS) that counts as a fully open mouth")]
        public float liveFullDb = -16f;
        [Tooltip("Live RMS (dBFS) below which the mouth is closed")]
        public float liveClosedDb = -36f;

        [Header("Jaw (ventriloquist dummy)")]
        [Range(0f, 2f)] [Tooltip("1 = the model's JawOpen socket (22 mm drop)")]
        public float jawGain = 1.3f;
        [Range(0f, 10f)] public float jawTiltDeg = 4f;
        [Range(0f, 1f)] [Tooltip("Small cigar chews while silent")]
        public float idleChew = 0.25f;

        [Header("Head bob")]
        [Range(0f, 2f)] public float headBob = 1f;
        [Tooltip("Chin-down nod on a stressed word (deg)")]
        public float nodDeg = 4f;
        [Tooltip("Head lifts back this much at a fully open jaw (deg)")]
        public float talkLiftDeg = 1.5f;
        [Tooltip("Slow yaw / roll sway while talking (deg)")]
        public float swayDeg = 1.5f;

        [Header("Cigar ember")]
        public Color emberColor = new Color(1f, 0.38f, 0.06f);
        public float emberIntensity = 2.5f;
        [Range(0f, 1f)] public float emberFlicker = 0.25f;
        public float puffIntensity = 7f;

        /// <summary>Current mouth opening 0..1 (before jawGain).</summary>
        public float Mouth01 { get; private set; }
        /// <summary>Extra mouth opening 0..1 set by a director (the laugh's "HAR HAR" when a Humanoid import drops the keyed jaw).</summary>
        [NonSerialized] public float extraMouth;
        /// <summary></summary>
        public static float DebugMouth = -1f;
        /// <summary>Holds jaw, nods and sway where they are (the game is paused; the voice is paused with the listener).</summary>
        [NonSerialized] public bool frozen;
        public bool IsTalking => voice != null && voice.isPlaying;
        public bool HasJaw => jaw != null;

        /// <summary>The clips inside Zod_Cameo.fbx.</summary>
        public static readonly string[] Clips =
        {
            "idle", "idle_hand_on_hip", "idle_look_around", "talk_passionate", "talk_hands_open", "talk_right_hand",
            "talk_left_hand", "talk_angry", "point_map", "salute", "cigar_puff", "hat_tip", "laugh", "fist_pump",
            "victory_cheer", "chest_pound", "facepalm", "angry_stomp", "finger_wag", "scheming", "crouch_gun_gesture",
            "jaw_chatter", "walk", "run"
        };

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] class CurveLine { public string id; public float duration; public int[] jaw; public float[] nods; }
        [Serializable] class CurveFile { public int fps = 60; public CurveLine[] lines; }
#pragma warning restore 0649

        Transform jaw, head, jawOpen, jawHinge, cigarTip;
        Vector3 jawRestPos, dropLocal, hingeAxisLocal;
        Quaternion jawRestRot;
        float tiltSign = 1f;
        readonly Dictionary<string, CurveLine> curves = new Dictionary<string, CurveLine>();
        int curveFps = 60;

        // "did the Animator write this bone this frame?" bookkeeping (keeps the rig additive with or without an Animator)
        Vector3 jawLastWrittenPos, jawLastBasePos; Quaternion jawLastWrittenRot, jawLastBaseRot; bool jawWritten;
        Quaternion headLastWrittenRot, headLastBaseRot; bool headWritten;

        AudioClip lastClip; float lastTime; float nodT = 99f; float nodStrength = 1f;
        float mouthSmooth, talkEnv, swayPhase, chewT, chewNext = 3f, chewAmt, puffT, lastLive;
        readonly float[] rmsBuf = new float[1024];
        Material cigarMat; static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            jaw = Find(transform, "Jaw");
            head = Find(transform, "mixamorigHead");
            jawOpen = Find(transform, "JawOpen");
            jawHinge = Find(transform, "JawHingeRight");
            cigarTip = Find(transform, "CigarTip");
            if (jaw != null)
            {
                jawRestPos = jaw.localPosition; jawRestRot = jaw.localRotation;
                // drop and hinge axis in the jaw's parent (head) space; the model's defaults if a socket is missing
                dropLocal = jawOpen != null && jawOpen.parent == jaw.parent ? jawOpen.localPosition - jawRestPos : new Vector3(0f, -0.022f, 0.003f);
                hingeAxisLocal = jawHinge != null && jawHinge.parent == jaw.parent ? (jawHinge.localPosition - jawRestPos).normalized : Vector3.right;
                if (hingeAxisLocal.sqrMagnitude < 1e-6f) hingeAxisLocal = Vector3.right;
                // pick the tilt direction that moves the front of the jaw (the cigar) DOWN, like the slide
                Vector3 tip = cigarTip != null && cigarTip.parent == jaw ? jawRestRot * cigarTip.localPosition : dropLocal.normalized * 0.05f;
                Vector3 moved = Quaternion.AngleAxis(2f, hingeAxisLocal) * tip - tip;
                tiltSign = Vector3.Dot(moved, dropLocal) >= 0f ? 1f : -1f;
            }
            if (head != null) { skullLocal = head.InverseTransformDirection(transform.up).normalized; skullOk = true; }
            BuildMouthPlug();
            LoadCurves();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.IndexOf("Cigar", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cigarMat = mats[i]; cigarMat.EnableKeyword("_EMISSION");
                        cigarMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                if (cigarMat != null) break;
            }
            BuildSideCigar();
        }

        void LoadCurves()
        {
            curves.Clear();
            if (mouthCurves == null) return;
            try
            {
                var f = JsonUtility.FromJson<CurveFile>(mouthCurves.text);
                if (f == null || f.lines == null) return;
                curveFps = Mathf.Max(1, f.fps);
                foreach (var l in f.lines) if (l != null && !string.IsNullOrEmpty(l.id)) curves[l.id] = l;
            }
            catch (Exception e) { Debug.LogWarning("[ZodTalkRig] mouth curves not readable: " + e.Message); }
        }

        /// <summary>Assigns the mouth curves after Awake (a rig added at run time) and reads them.</summary>
        public void SetCurves(TextAsset curvesJson) { mouthCurves = curvesJson; LoadCurves(); }

        /// <summary>Plays a Zod line on the voice source; returns its length in seconds.</summary>
        public float Say(AudioClip clip)
        {
            if (voice == null || clip == null) return 0f;
            voice.clip = clip; voice.time = 0f; voice.Play();
            return clip.length;
        }

        /// <summary>Makes the cigar ember glow up for a while (cigar_puff).</summary>
        public void Puff(float seconds = 1.0f) { puffT = Mathf.Max(puffT, seconds); }
        /// <summary>AnimationEvent receiver.</summary>
        public void ZodPuff() { Puff(1.0f); }

        void LateUpdate()
        {
            float dt = frozen ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float target = 0f; bool beat = false;
            bool playing = voice != null && voice.isPlaying && voice.clip != null;
            if (playing)
            {
                var clip = voice.clip;
                float t = voice.timeSamples / (float)Mathf.Max(1, clip.frequency);
                if (clip != lastClip || t < lastTime) { lastTime = t - 1e-4f; lastClip = clip; }
                if (curves.TryGetValue(clip.name, out var cl) && cl.jaw != null && cl.jaw.Length > 0)
                {
                    float fi = t * curveFps; int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 0, cl.jaw.Length - 1);
                    int i1 = Mathf.Min(i0 + 1, cl.jaw.Length - 1);
                    target = Mathf.Lerp(cl.jaw[i0], cl.jaw[i1], fi - Mathf.Floor(fi)) / 100f;
                    if (cl.nods != null)
                        foreach (float n in cl.nods) if (n > lastTime && n <= t) { beat = true; break; }
                }
                else if (liveFallback)
                {
                    voice.GetOutputData(rmsBuf, 0);
                    double s = 0; for (int i = 0; i < rmsBuf.Length; i++) s += rmsBuf[i] * rmsBuf[i];
                    float db = 10f * Mathf.Log10((float)(s / rmsBuf.Length) + 1e-10f);
                    target = Mathf.Clamp01((db - liveClosedDb) / Mathf.Max(1f, liveFullDb - liveClosedDb));
                    target *= target;
                    if (target - lastLive > 0.35f && nodT > 0.45f) beat = true;
                    lastLive = target;
                }
                lastTime = t;
            }
            else { lastClip = null; lastLive = 0f; }

            // idle chew while silent (tiny, now and then)
            if (!playing && idleChew > 0f)
            {
                chewT += dt;
                if (chewT > chewNext) { chewT = 0f; chewNext = UnityEngine.Random.Range(2.5f, 7f); chewAmt = UnityEngine.Random.Range(0.4f, 1f); }
                float c = chewT < 0.5f ? Mathf.Sin(chewT / 0.5f * Mathf.PI * 2f) : 0f;
                target = Mathf.Max(target, idleChew * chewAmt * Mathf.Max(0f, c) * 0.5f);
            }

            if (extraMouth > 0f) target = Mathf.Max(target, Mathf.Clamp01(extraMouth));
            if (DebugMouth >= 0f) target = Mathf.Clamp01(DebugMouth);                                      // Play check: a fixed jaw state
            float k = 1f - Mathf.Exp(-dt / (target > mouthSmooth ? 0.025f : 0.045f));
            mouthSmooth += (target - mouthSmooth) * k;
            Mouth01 = Mathf.Clamp01(mouthSmooth);
            talkEnv += ((playing ? 1f : 0f) - talkEnv) * (1f - Mathf.Exp(-dt / 0.35f));
            if (beat) { nodT = 0f; nodStrength = UnityEngine.Random.Range(0.7f, 1.1f); }
            nodT += dt;
            swayPhase += dt;

            ApplyJaw();
            ApplyHead();
            ApplyEmber(dt);
        }

        void ApplyJaw()
        {
            if (jaw == null) return;
            Vector3 basePos = jawWritten && jaw.localPosition == jawLastWrittenPos ? jawLastBasePos : jaw.localPosition;
            Quaternion baseRot = jawWritten && jaw.localRotation == jawLastWrittenRot ? jawLastBaseRot : jaw.localRotation;
            float a = Mathf.Min(1.6f, Mouth01 * jawGain);
            Vector3 p = basePos + dropLocal * a;
            Quaternion r = Quaternion.AngleAxis(tiltSign * jawTiltDeg * a, hingeAxisLocal) * baseRot;
            jaw.localPosition = p; jaw.localRotation = r;
            jawLastBasePos = basePos; jawLastBaseRot = baseRot; jawLastWrittenPos = p; jawLastWrittenRot = r; jawWritten = true;
        }

        /// <summary>
        /// inside this rig's own base-pose cache (a bone write from outside would accumulate when the clip leaves the head unkeyed).</summary>
        [System.NonSerialized] public float gazeYaw, gazePitch;

        void ApplyHead()
        {
            if (head == null || headBob <= 0f) return;
            Quaternion baseRot = headWritten && head.localRotation == headLastWrittenRot ? headLastBaseRot : head.localRotation;
            head.localRotation = baseRot;
            // nod: quick chin-down and back (0.38 s); lift with the jaw; slow sway while talking
            float nod = nodT < 0.38f ? Mathf.Sin(nodT / 0.38f * Mathf.PI) * nodDeg * nodStrength : 0f;
            float pitch = (nod - talkLiftDeg * Mouth01) * headBob;              // + = chin down (Unity: +X looks down)
            float yaw = Mathf.Sin(swayPhase * 1.3f) * swayDeg * talkEnv * headBob;
            float roll = Mathf.Sin(swayPhase * 0.9f + 1.1f) * swayDeg * 0.5f * talkEnv * headBob;
            Transform root = transform;
            Quaternion w = Quaternion.AngleAxis(pitch + gazePitch, root.right) * Quaternion.AngleAxis(yaw + gazeYaw, root.up) * Quaternion.AngleAxis(roll, root.forward);
            head.rotation = w * head.rotation;
            ClampChinDown(root);
            headLastBaseRot = baseRot; headLastWrittenRot = head.localRotation; headWritten = true;
        }

        /// <summary>QC (win 22.2 s, loss facepalm): a clip bow + the gaze down at the boys + a nod tipped his head until the hat brim
        /// filled the feed (and his hat read as gone in the facepalm). The skull's tilt toward the lens (clip + nods + gaze, measured from
        /// the bind pose = upright) is soft-clamped at MaxChinDownDeg: beyond it only SoftKeep of the extra survives.</summary>
        public static float MaxChinDownDeg = 16f, MaxRollDeg = 14f, SoftKeep = 0.12f;   // first Play check: the finger_wag clip also ROLLS the head ~30 deg (brim over the face), the facepalm ~35 deg (the hat read as gone): roll clamped too
        /// <summary>
        /// the roll clamp keeps SoftKeep.</summary>
        public static float ChinSoftKeep = 0.12f;
        public static float ChinDownPeak { get; private set; }
        public static float ChinDownOutPeak { get; private set; }
        public static int ChinClamps { get; private set; }
        Vector3 skullLocal; bool skullOk;
        void ClampChinDown(Transform root)
        {
            if (!skullOk || MaxChinDownDeg <= 0f) return;
            Vector3 up = head.TransformDirection(skullLocal);
            float pd = Mathf.Atan2(Vector3.Dot(up, root.forward), Vector3.Dot(up, root.up)) * Mathf.Rad2Deg;   // + = chin down (the skull tips toward the lens)
            if (pd > ChinDownPeak) ChinDownPeak = pd;
            if (pd <= MaxChinDownDeg) { if (pd > ChinDownOutPeak) ChinDownOutPeak = pd; }
            else
            {
                float over = pd - MaxChinDownDeg, back = over * (1f - ChinSoftKeep);
                head.rotation = Quaternion.AngleAxis(-back, root.right) * head.rotation;
                ChinClamps++;
                float outPd = MaxChinDownDeg + over * ChinSoftKeep; if (outPd > ChinDownOutPeak) ChinDownOutPeak = outPd;
            }
            if (MaxRollDeg <= 0f) return;
            up = head.TransformDirection(skullLocal);
            float roll = Mathf.Atan2(Vector3.Dot(up, root.right), Vector3.Dot(up, root.up)) * Mathf.Rad2Deg;      // + = the skull tips to the rig's +X
            if (Mathf.Abs(roll) > RollPeak) RollPeak = Mathf.Abs(roll);
            if (Mathf.Abs(roll) <= MaxRollDeg) return;
            float rBack = (Mathf.Abs(roll) - MaxRollDeg) * (1f - SoftKeep) * Mathf.Sign(roll);
            head.rotation = Quaternion.AngleAxis(rBack, root.forward) * head.rotation;                               // +angle about forward tips up toward -X
            RollClamps++;
        }
        public static float RollPeak { get; private set; }
        public static int RollClamps { get; private set; }
        /// <summary>Play proof: reset the peaks.</summary>
        public static void ResetChinStats() { ChinDownPeak = ChinDownOutPeak = RollPeak = 0f; ChinClamps = RollClamps = 0; }

        /// <summary>QC (alarm_feed_2_9s.png: a blue patch inside his open mouth = the tactical board seen through the open back of the
        /// mouth): a dark mouth interior on the head (not the jaw), filling the gap the sliding jaw opens. Tunable live, then Rebuild.</summary>
        public static float PlugDown = 1.15f, PlugBack = -2.3f, PlugW = 2.6f, PlugH = 2.3f, PlugD = 2.6f;   // tuned live in the Play checks (the jaw bone sits ~15 cm behind the lips: PlugBack < 0 = forward; lower + taller so the inner lower jaw reads dark too)
        public static Color PlugColor = new Color32(20, 8, 7, 255);
        Transform mouthPlug;
        public bool HasMouthPlug => mouthPlug != null;
        /// <summary></summary>
        public static bool MouthPlug = true;
        public void BuildMouthPlug()
        {
            if (mouthPlug) { Destroy(mouthPlug.gameObject); mouthPlug = null; }
            if (!MouthPlug || jaw == null || jaw.parent == null) return;
            var par = jaw.parent; float d = Mathf.Max(0.005f, dropLocal.magnitude);
            Vector3 faceL = par.InverseTransformDirection(transform.forward).normalized, upL = par.InverseTransformDirection(transform.up).normalized;
            var holder = new GameObject("MouthInterior") { layer = jaw.gameObject.layer };
            holder.transform.SetParent(par, false);
            holder.transform.localPosition = jawRestPos + dropLocal * PlugDown - faceL * d * PlugBack;
            holder.transform.localRotation = Quaternion.LookRotation(faceL, upL);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "MouthInteriorShell"; go.layer = jaw.gameObject.layer;
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(holder.transform, false); go.transform.localScale = new Vector3(PlugW * d, PlugH * d, PlugD * d);
            var mr = go.GetComponent<MeshRenderer>();
            var m = StageMat("ZodStage_Lit", "ZodMouthInterior (runtime)");
            if (m) { m.SetColor("_BaseColor", PlugColor); m.SetFloat("_Smoothness", 0.15f); m.SetFloat("_Metallic", 0f); mr.sharedMaterial = m; }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var skin = GetComponentInChildren<SkinnedMeshRenderer>(true); if (skin) mr.renderingLayerMask = skin.renderingLayerMask;
            mouthPlug = holder.transform;
        }

        /// <summary>A copy of a stage template material from Resources (their shader variants ship in the player build).</summary>
        static Material StageMat(string template, string name)
        {
            var t = Resources.Load<Material>("OpenZ/Zod/Materials/" + template);
            if (t) return new Material(t) { name = name };
            var sh = Shader.Find("Universal Render Pipeline/Lit"); return sh ? new Material(sh) { name = name } : null;
        }

        // The cigar is in every shot or in none (no pop-in across the insert cut): the mesh cigar points straight down the lens in
        // every square-on shot (only its ember showed, a glowing blob) and read as a cigar only in the three-quarter shots. The mesh
        // cigar is hidden (its submesh gets a fully clipped cutout material) and a cigar of the same size sticks out of the mouth
        // corner, angled out to his side and a little down, so it reads in profile from the front and the side. It rides the jaw;
        // the CigarTip socket (smoke, ember light) moves to its tip; its ember disc takes over the flicker / puff glow.
        public static bool SideCigar = true;
        public static float CigarLen = 0.11f, CigarDia = 0.022f, CigarOut = 0.62f, CigarSide = 0.72f, CigarDown = 0.18f;
        public static Color CigarColor = new Color32(92, 52, 28, 255);
        Transform sideCigar;
        public bool HasSideCigar => sideCigar != null;
        void BuildSideCigar()
        {
            if (!SideCigar || jaw == null || cigarTip == null || sideCigar) return;
            float L = CigarLen * transform.lossyScale.y, D = CigarDia * transform.lossyScale.y;
            Vector3 baseW = cigarTip.position - cigarTip.forward * L;
            Vector3 dir = (transform.forward * CigarOut - transform.right * CigarSide - transform.up * CigarDown).normalized;
            var g = new GameObject("SideCigar") { layer = jaw.gameObject.layer };
            g.transform.SetParent(jaw, true);
            g.transform.position = baseW + dir * (L * 0.5f); g.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            var ls = g.transform.lossyScale; float inv = 1f / Mathf.Max(1e-4f, ls.y);
            var body = StageMat("ZodStage_Lit", "ZodSideCigar (runtime)"); if (body) { body.SetColor("_BaseColor", CigarColor); body.SetFloat("_Smoothness", 0.25f); body.SetFloat("_Metallic", 0f); }
            var band = StageMat("ZodStage_Lit", "ZodSideCigarBand (runtime)"); if (band) { band.SetColor("_BaseColor", new Color32(176, 38, 26, 255)); band.SetFloat("_Smoothness", 0.5f); }
            var ember = StageMat("ZodStage_Glow", "ZodSideCigarEmber (runtime)"); if (ember) { ember.SetColor("_BaseColor", new Color32(60, 20, 10, 255)); ember.EnableKeyword("_EMISSION"); ember.SetVector("_EmissionColor", new Vector4(4f, 0.9f, 0.15f, 1f)); }
            var ash = StageMat("ZodStage_Lit", "ZodSideCigarAsh (runtime)"); if (ash) { ash.SetColor("_BaseColor", new Color32(120, 116, 110, 255)); ash.SetFloat("_Smoothness", 0.1f); }
            Cyl(g.transform, "Body", Vector3.zero, new Vector3(D, L * 0.5f, D) * inv, body);
            Cyl(g.transform, "Band", new Vector3(0f, -L * 0.28f, 0f) * inv, new Vector3(D * 1.06f, L * 0.035f, D * 1.06f) * inv, band);
            Cyl(g.transform, "Ash", new Vector3(0f, L * 0.5f + D * 0.12f, 0f) * inv, new Vector3(D * 0.97f, D * 0.12f, D * 0.97f) * inv, ash);
            Cyl(g.transform, "Ember", new Vector3(0f, L * 0.5f + D * 0.26f, 0f) * inv, new Vector3(D * 0.9f, D * 0.04f, D * 0.9f) * inv, ember);
            // hide the mesh cigar: a fully clipped cutout material on its submesh (no transparent variant needed in the build)
            var clip = StageMat("ZodStage_Cutout", "ZodCigarHidden (runtime)");
            if (clip) { clip.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f)); clip.SetFloat("_Cutoff", 0.99f); clip.EnableKeyword("_ALPHATEST_ON"); }
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.materials; bool any = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.IndexOf("Cigar", StringComparison.OrdinalIgnoreCase) >= 0 && clip) { mats[i] = clip; any = true; }
                if (any) r.materials = mats;
            }
            if (ember) cigarMat = ember;                                                                  // the flicker / puff now drive this ember
            cigarTip.position = baseW + dir * (L + D * 0.3f); cigarTip.rotation = Quaternion.LookRotation(dir, transform.up);
            sideCigar = g.transform;
        }

        static void Cyl(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); c.name = name; c.layer = parent.gameObject.layer;
            var col = c.GetComponent<Collider>(); if (col) Destroy(col);
            c.transform.SetParent(parent, false); c.transform.localPosition = pos; c.transform.localScale = scale;
            var mr = c.GetComponent<MeshRenderer>(); if (m) mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ApplyEmber(float dt)
        {
            if (cigarMat == null) return;
            puffT = Mathf.Max(0f, puffT - dt);
            float flick = 1f + emberFlicker * (Mathf.PerlinNoise(Time.unscaledTime * 3.1f, 0.37f) - 0.5f) * 2f;
            float inten = Mathf.Lerp(emberIntensity, puffIntensity, Mathf.Clamp01(puffT / 0.3f)) * flick;
            cigarMat.SetColor(EmissionColorId, emberColor.linear * inten);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        void OnDisable()
        {
            // leave the bones where the Animator puts them
            jawWritten = false; headWritten = false;
            if (jaw != null && GetComponentInChildren<Animator>() == null) { jaw.localPosition = jawRestPos; jaw.localRotation = jawRestRot; }
        }
    }
}

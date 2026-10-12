// OpenZ (GPL-3.0) - intro cutscene: the talking jaw + head bob of Alan / Brad (IC_Alan.fbx / IC_Brad.fbx, mouths pass v2, 5 Oct 2026).
// Plays models\export\data\ic_mouth_curves.json. Put it on the robot's model root (the object OzIntroPlayer poses: its `alan` /
// `bradGo` GameObject) and assign `curves` = ic_mouth_curves.json (TextAsset) and `who` = alan / brad.
//
// TWO WAYS TO DRIVE IT
//  1. CLOCK (the intro). OzIntroPlayer plays ONE master mix (or the pre-rendered openz_intro.mp4) and poses the robots with
//     clip.SampleAnimation in PlaceActor, so no AudioSource ever plays a robot's own line. The intro calls, in OzIntroPlayer.PlaceActor,
//     right AFTER clip.SampleAnimation(go, ct) (and before the panel cameras render this frame):
//         var jaw = go.GetComponent<OpenZ.Intro.IC_RobotJaw>(); if (jaw) jaw.ApplyAt(Clock());
//     ApplyAt samples the curve file's `timeline.<who>` at Clock() x fps (the mix time: live, the frame-exact pre-render and Play all use
//     Clock()), writes the jaw and adds the head bob on top of the pose SampleAnimation just wrote (so nothing accumulates). Once
//     ApplyAt has been called, LateUpdate does nothing. The timeline is at the absolute times of audio\cue_sheet.csv: import the mix
//     and the timeline that go with it (see README_IMPORT.md section 9).
//  2. AUDIO SOURCE (any other use): `voice` = an AudioSource that plays this robot's own line files (AudioClip name = the line's
//     "clip", e.g. ic09_alan_let_me_drive). LateUpdate (after the Animator) plays that line's curve synced to voice.timeSamples.
// `overrideOpen` >= 0 lets a director hold the jaw at an opening instead.
//
// THE HINGE comes from the model's own sockets, so no axis convention can flip it: pivot = IC_Jaw, axis = IC_JawHingeR - pivot (both in
// the head's space), full opening = the signed turn about that axis that carries IC_JawLip onto IC_JawLipOpen (v2: about 8.2 deg Alan,
// 7.0 deg Brad; the hinge sits 30 / 40 cm behind the face, so the jaw slides 4.5 / 5.2 cm down along the face).
// THE HEAD BOB (as ZodTalkRig): a quick chin-down nod on every stressed onset (the curve file's `nods`), the head lifting back a little
// as the jaw opens, and a slow sway while the robot talks. All deterministic in CLOCK mode (the pre-render is repeatable).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ.Intro
{
    [DefaultExecutionOrder(200)]
    public class IC_RobotJaw : MonoBehaviour
    {
        public TextAsset curves;
        [Tooltip("alan or brad: only this robot's lines drive this jaw")]
        public string who = "alan";
        [Tooltip("AUDIO SOURCE mode only: the source that plays this robot's own line files")]
        public AudioSource voice;
        [Range(0f, 1f)] public float gain = 1f;
        [Tooltip(">= 0: a director sets the opening (0..1) instead of the voice / clock; -1 = off")]
        public float overrideOpen = -1f;
        [Tooltip("AUDIO SOURCE mode: how fast the jaw shuts when no line plays (1/s)")]
        public float closeSpeed = 14f;
        [Tooltip("CLOCK mode: seconds added to the clock before sampling (0 when the mix and the timeline are the matching pair)")]
        public float clockOffset = 0f;

        [Header("Head bob (0 = off)")]
        [Range(0f, 2f)] public float headBob = 1f;
        [Tooltip("chin-down nod on a stressed onset (deg)")] public float nodDeg = 3.5f;
        [Tooltip("the head lifts back this much at a fully open jaw (deg)")] public float talkLiftDeg = 1.5f;
        [Tooltip("slow yaw / roll sway while talking (deg)")] public float swayDeg = 1.0f;
        public float nodSeconds = 0.38f;

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] class Line { public string id; public string clip; public string who; public float start_abs_s; public float duration; public int[] jaw; public float[] nods; }
        [Serializable] class Timeline { public int fps; public int[] alan; public int[] brad; }
        [Serializable] class Root { public int fps; public Line[] lines; public Timeline timeline; }
#pragma warning restore 0649

        Transform jaw, head;
        Vector3 axisLocal;
        float openDeg;
        Quaternion restLocal;
        bool ready, clockDriven;
        int fps = 60, tlFps = 60;
        int[] tl;
        float current, nodAt = -99f, swayT;
        readonly Dictionary<string, Line> byClip = new Dictionary<string, Line>();
        readonly List<Line> mine = new List<Line>();
        readonly List<float> nodTimes = new List<float>();          // absolute (CLOCK mode)
        AudioClip lastClip; float lastVoiceT;
        Quaternion headLastWritten, headLastBase; bool headWritten;  // AUDIO SOURCE mode: stay additive without a per-frame re-pose

        /// <summary>The jaw opening this frame, 0..1.</summary>
        public float Open01 => current;
        /// <summary>The model's full opening in degrees (from the sockets).</summary>
        public float OpenDegrees => openDeg;
        /// <summary>True once the intro drives it through ApplyAt.</summary>
        public bool ClockDriven => clockDriven;

        void Awake()
        {
            jaw = Find(transform, "IC_Jaw");
            Transform hinge = Find(transform, "IC_JawHingeR"), lip = Find(transform, "IC_JawLip"), lipOpen = Find(transform, "IC_JawLipOpen");
            if (jaw == null || jaw.parent == null || hinge == null || lip == null || lipOpen == null)
            {
                Debug.LogWarning($"IC_RobotJaw on {name}: IC_Jaw or its sockets (IC_JawHingeR, IC_JawLip, IC_JawLipOpen) not found", this);
                return;
            }
            head = jaw.parent;                                          // mixamorigHead
            restLocal = jaw.localRotation;
            Vector3 pivot = jaw.localPosition;
            Vector3 hingeL = head.InverseTransformPoint(hinge.position);
            Vector3 lipL = head.InverseTransformPoint(lip.position) - pivot;
            Vector3 openL = head.InverseTransformPoint(lipOpen.position) - pivot;
            axisLocal = (hingeL - pivot).normalized;
            openDeg = Vector3.SignedAngle(Vector3.ProjectOnPlane(lipL, axisLocal), Vector3.ProjectOnPlane(openL, axisLocal), axisLocal);
            // self-check: keep the sign that really lands the lip on IC_JawLipOpen
            float ePlus = (Quaternion.AngleAxis(openDeg, axisLocal) * lipL - openL).sqrMagnitude;
            float eMinus = (Quaternion.AngleAxis(-openDeg, axisLocal) * lipL - openL).sqrMagnitude;
            if (eMinus < ePlus) openDeg = -openDeg;
            ready = true;
            if (curves == null) return;
            Root r = JsonUtility.FromJson<Root>(curves.text);
            if (r == null || r.lines == null) return;
            if (r.fps > 0) fps = r.fps;
            foreach (Line l in r.lines)
            {
                if (l == null || l.jaw == null || !string.Equals(l.who, who, StringComparison.OrdinalIgnoreCase)) continue;
                mine.Add(l);
                if (!string.IsNullOrEmpty(l.clip)) byClip[l.clip] = l;
                if (!string.IsNullOrEmpty(l.id) && !byClip.ContainsKey(l.id)) byClip[l.id] = l;
                if (l.nods != null) foreach (float n in l.nods) nodTimes.Add(l.start_abs_s + n);
            }
            nodTimes.Sort();
            if (r.timeline != null)
            {
                if (r.timeline.fps > 0) tlFps = r.timeline.fps;
                tl = string.Equals(who, "brad", StringComparison.OrdinalIgnoreCase) ? r.timeline.brad : r.timeline.alan;
            }
            if (tl == null) Debug.LogWarning($"IC_RobotJaw on {name}: the curve file has no timeline for '{who}' (CLOCK mode needs it)", this);
        }

        // ------------------------------------------------------------------------------------------------ CLOCK mode (the intro)
        /// <summary>Call right after the robot's pose is sampled this frame (OzIntroPlayer.PlaceActor, after clip.SampleAnimation), with
        /// the intro clock (OzIntroPlayer.Clock(), the mix time in seconds).</summary>
        public void ApplyAt(float clockSeconds)
        {
            if (!ready) return;
            clockDriven = true;
            float t = clockSeconds + clockOffset;
            float open = 0f;
            if (overrideOpen >= 0f) open = Mathf.Clamp01(overrideOpen);
            else if (tl != null && tl.Length > 0)
            {
                float fi = t * tlFps;
                if (fi >= 0f && fi < tl.Length - 1)
                {
                    int i0 = Mathf.FloorToInt(fi);
                    open = Mathf.Lerp(tl[i0], tl[i0 + 1], fi - i0) / 100f;
                }
            }
            current = open;
            WriteJaw();
            // head bob: nod on the last onset within nodSeconds, lift with the jaw, sway inside this robot's lines (no randomness)
            float nod = 0f;
            for (int i = nodTimes.Count - 1; i >= 0; i--)
            {
                float d = t - nodTimes[i];
                if (d < 0f) continue;
                if (d < nodSeconds) nod = Mathf.Sin(d / nodSeconds * Mathf.PI);
                break;
            }
            float env = 0f;
            foreach (Line l in mine)
            {
                float a = t - l.start_abs_s, b = l.start_abs_s + l.duration - t;
                env = Mathf.Max(env, Mathf.Clamp01(Mathf.Min(a, b) / 0.25f));
            }
            AddHead(nod * nodDeg - talkLiftDeg * current, Mathf.Sin(t * 1.3f) * swayDeg * env, Mathf.Sin(t * 0.9f + 1.1f) * swayDeg * 0.5f * env, false);
        }

        // ------------------------------------------------------------------------------------------------ AUDIO SOURCE mode
        void LateUpdate()
        {
            if (!ready || clockDriven) return;
            float target = 0f; bool talking = false, beat = false;
            if (overrideOpen >= 0f) { target = Mathf.Clamp01(overrideOpen); talking = true; }
            else if (voice != null && voice.isPlaying && voice.clip != null && byClip.TryGetValue(voice.clip.name, out Line l))
            {
                float vt = voice.timeSamples / (float)Mathf.Max(1, voice.clip.frequency);
                if (voice.clip != lastClip || vt < lastVoiceT) { lastVoiceT = vt - 1e-4f; lastClip = voice.clip; }
                float fi = vt * fps;
                int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 0, l.jaw.Length - 1), i1 = Mathf.Min(i0 + 1, l.jaw.Length - 1);
                target = Mathf.Lerp(l.jaw[i0], l.jaw[i1], fi - Mathf.Floor(fi)) / 100f;
                if (l.nods != null) foreach (float n in l.nods) if (n > lastVoiceT && n <= vt) { beat = true; break; }
                lastVoiceT = vt; talking = true;
            }
            else lastClip = null;
            current = talking ? target : Mathf.MoveTowards(current, 0f, closeSpeed * Time.deltaTime);
            WriteJaw();
            float now = Time.time;
            if (beat) nodAt = now;
            swayT += talking ? Time.deltaTime : 0f;
            float dn = now - nodAt;
            float nod = dn >= 0f && dn < nodSeconds ? Mathf.Sin(dn / nodSeconds * Mathf.PI) : 0f;
            float env = talking ? 1f : 0f;
            AddHead(nod * nodDeg - talkLiftDeg * current, Mathf.Sin(swayT * 1.3f) * swayDeg * env, Mathf.Sin(swayT * 0.9f + 1.1f) * swayDeg * 0.5f * env, true);
        }

        void WriteJaw() { jaw.localRotation = Quaternion.AngleAxis(openDeg * current * gain, axisLocal) * restLocal; }

        /// <summary>pitch + = chin down, about the jaw's own hinge axis (the robot's left-right, convention-free: the same turn sense that
        /// opens the jaw moves the face down); yaw about world up, roll about the face direction. keepBase: re-use the last base pose
        /// when nothing re-posed the head since our last write (AUDIO SOURCE mode without an Animator).</summary>
        void AddHead(float pitch, float yaw, float roll, bool keepBase)
        {
            if (head == null || headBob <= 0f) return;
            if (keepBase && headWritten && head.localRotation == headLastWritten) head.localRotation = headLastBase;
            Quaternion baseRot = head.localRotation;
            Vector3 side = head.TransformDirection(axisLocal).normalized, face = Vector3.Cross(side, Vector3.up);
            if (face.sqrMagnitude < 1e-6f) face = transform.forward;
            Quaternion w = Quaternion.AngleAxis(Mathf.Sign(openDeg) * pitch * headBob, side) * Quaternion.AngleAxis(yaw * headBob, Vector3.up)
                         * Quaternion.AngleAxis(roll * headBob, face.normalized);
            head.rotation = w * head.rotation;
            headLastBase = baseRot; headLastWritten = head.localRotation; headWritten = true;
        }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = Find(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }
    }
}

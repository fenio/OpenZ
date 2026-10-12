using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Robot antics: the pop effect for props and the stowed weapon (scale pop + golden puff + the crunchy pop sound), prop
    /// instancing on the robot's sockets and in the world, and the sound helper that logs every antic sound.
    /// Pop in = the placement curve (0 -> 1.15 -> 0.95 -> 1, PlacementPop.Curve) over PopInTime; pop out = up to 1.12 then to 0
    /// (the debris pop curve) over PopOutTime, then the prop is destroyed (or, for the weapon, hidden). View only.
    /// </summary>
    public static class AnticFx
    {
        public static float PopInTime = 0.3f, PopOutTime = 0.26f, PopGain = 0.32f, PopPitch = 1.28f;
        public static int Pops { get; private set; }

        sealed class Job { public Transform t; public Vector3 baseScale; public float t0, dur; public bool outPop, destroy; public Action done; }
        static readonly List<Job> jobs = new List<Job>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { jobs.Clear(); Pops = 0; Puffs = 0; }

        static void Drop(Transform t) { for (int i = jobs.Count - 1; i >= 0; i--) if (jobs[i].t == t) jobs.RemoveAt(i); }

        /// <summary>Pops `t` in at `baseScale` (its local scale at full size). `fx` = puff + pop sound.</summary>
        public static void PopIn(Transform t, Vector3 baseScale, bool fx, string what, int emitter)
        {
            if (!t) return;
            Drop(t);
            t.localScale = baseScale * 0.001f;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            jobs.Add(new Job { t = t, baseScale = baseScale, t0 = Time.time, dur = PopInTime });
            if (fx) Fx(t.position, what, emitter, 1f);
        }

        /// <summary>Pops `t` out from its current scale; destroys it at the end (or hides its renderers when destroy is false).</summary>
        public static void PopOut(Transform t, bool destroy, bool fx, string what, int emitter, Action done = null)
        {
            if (!t) { done?.Invoke(); return; }
            Drop(t);
            jobs.Add(new Job { t = t, baseScale = t.localScale, t0 = Time.time, dur = PopOutTime, outPop = true, destroy = destroy, done = done });
            if (fx) Fx(t.position, what, emitter, 0.9f);
        }

        /// <summary>Ends any pop on `t` and puts it at `scale` at once (cancel on death: the weapon is back for the death pop / ragdoll).</summary>
        public static void Snap(Transform t, Vector3 scale, bool visible)
        {
            if (!t) return;
            Drop(t); t.localScale = scale;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        public static bool Busy(Transform t) { foreach (var j in jobs) if (j.t == t) return true; return false; }

        /// <summary>The soft pop puff: a few small white billows at the prop (no sparks), their size (m), life (s) and opacity.</summary>
        public static int PuffCount = 6;
        public static float PuffMin = 0.24f, PuffMax = 0.36f, PuffLife = 0.55f, PuffAlpha = 0.5f, PuffSpread = 0.45f, PuffRise = 0.35f;
        public static Color PuffColor = new Color(0.96f, 0.96f, 0.94f, 1f);
        public static int Puffs { get; private set; }

        /// <summary>A small soft white puff at `at` (the hand or the prop): short billows drifting out and up, no spark streaks.
        /// The shared OpenZFxPlayer.Pop draws nine blue spark streaks, which read as an electric fault on a robot opening a can.</summary>
        public static void SoftPuff(Vector3 at, float gain)
        {
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
            Puffs++;
            float a0 = UnityEngine.Random.value * 6.2831853f;
            for (int j = 0; j < PuffCount; j++)
            {
                float a = a0 + j * 2.399f + (UnityEngine.Random.value - 0.5f) * 0.6f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var v = d * (PuffSpread * UnityEngine.Random.Range(0.75f, 1.2f)) + Vector3.up * (PuffRise * UnityEngine.Random.Range(0.7f, 1.3f));
                fxp.EmitChannel("m12_dust", at + d * 0.05f, v, PuffLife * UnityEngine.Random.Range(0.85f, 1.15f),
                    UnityEngine.Random.Range(PuffMin, PuffMax), PuffColor, PuffAlpha * Mathf.Clamp(gain, 0.5f, 1f));
            }
        }

        /// <summary>The soft pop puff + the crunchy pop (small, higher pitch), only near the screen; logged as `what`.</summary>
        public static void Fx(Vector3 at, string what, int emitter, float gain)
        {
            Pops++;
            if (!WreckFx.OnScreen(at, 0.15f)) { OzSoundLog.Add("AN", what, "rp_pop", "skip off screen", 0f, at); return; }
            SoftPuff(at, gain);
            var au = CombatAudio.I;
            if (au) au.PlayPitched(RobotPopDeath.SoundKey, at, PopGain * gain, 0f, emitter, PopPitch * UnityEngine.Random.Range(0.94f, 1.06f), RobotPopDeath.Bank);
            OzSoundLog.Add("AN", what, "rp_pop", au ? "PLAY pop" : "DROP no CombatAudio", PopGain * gain, at);
        }

        /// <summary>One antic sound event (AudioDirector logs it in OzSoundLog with its outcome).</summary>
        public static void Sound(string id, Vector3 at, int emitter, float volMul = 1f) => AudioDirector.Fire(id, at, emitter, volMul);

        /// <summary>Runs the pops (AnticDirector.LateUpdate, after the Animator and UnitView).</summary>
        public static void Step()
        {
            float now = Time.time;
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                if (!j.t) { jobs.RemoveAt(i); j.done?.Invoke(); continue; }
                float u = Mathf.Clamp01((now - j.t0) / Mathf.Max(0.01f, j.dur));
                if (!j.outPop)
                {
                    j.t.localScale = j.baseScale * Mathf.Max(0.001f, PlacementPop.Curve(u, 0f, PlacementPop.PeakScale, PlacementPop.Undershoot));
                    if (u >= 1f) { j.t.localScale = j.baseScale; jobs.RemoveAt(i); j.done?.Invoke(); }
                }
                else
                {
                    float s = u < 0.3f ? 1f + 0.12f * OpenZ.PopOut.Smooth(u, 0f, 0.3f) : Mathf.Max(0.001f, 1.12f * (1f - OpenZ.PopOut.Smooth(u, 0.3f, 1f)));
                    j.t.localScale = j.baseScale * s;
                    if (u >= 1f)
                    {
                        jobs.RemoveAt(i);
                        if (j.destroy) UnityEngine.Object.Destroy(j.t.gameObject);
                        else { foreach (var r in j.t.GetComponentsInChildren<Renderer>(true)) r.enabled = false; j.t.localScale = j.baseScale; }
                        j.done?.Invoke();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ prop instancing
        /// <summary>Instantiates a prop model under a new holder. The holder is the "weapon convention" root: put it on a socket with a zero
        /// local transform (the model keeps its own import root transform inside the holder). Every renderer gets `mat` when given.</summary>
        public static Transform Make(GameObject model, string name, Material mat)
        {
            if (!model) return null;
            var holder = new GameObject(name).transform;
            var m = UnityEngine.Object.Instantiate(model, holder, false);
            m.name = model.name;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                if (mat) { var ms = r.sharedMaterials; for (int k = 0; k < ms.Length; k++) ms[k] = mat; r.sharedMaterials = ms; }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            return holder;
        }

        /// <summary>The holder on a socket (zero local transform, local scale `scale`).</summary>
        public static Transform OnSocket(Transform holder, Transform socket, float scale)
        {
            if (!holder || !socket) return holder;
            holder.SetParent(socket, false);
            holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one * scale;
            holder.gameObject.layer = socket.gameObject.layer;
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = socket.gameObject.layer;
            return holder;
        }

        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}

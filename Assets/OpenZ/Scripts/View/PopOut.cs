using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// turrets, rock chunks and rubble scale out around their ground point, then the callback hides / destroys them.
    ///   Corpse: over .5 s, t in 0..1: a small swell
    ///     1 + .08 sin(t / .24 pi) until t = .24, then 1 - smoothstep(.24, 1, t); fx.pop(position, .5) when it starts.
    ///   Debris (debrisPopScale): over the last .5 s: smoothstep up to 1.12 in .14 s, then
    ///     1.12 x (1 - smoothstep) down to 0 in .36 s.
    /// Scale is applied around the object's bottom centre (renderer bounds), so a body shrinks into the ground where it
    /// lies instead of sliding toward its transform pivot (ragdoll bones, fractured cells and wrecks all keep their spot).
    /// One manager, LateUpdate order 95: after UnitView (60) and UnitAim (62), before FxManager (100).
    /// </summary>
    [DefaultExecutionOrder(95)]
    public class PopOut : MonoBehaviour
    {
        public enum Style { Corpse, Debris }
        /// <summary></summary>
        public const float Duration = 0.5f;

        /// <summary>three.js MathUtils.smoothstep(x, min, max): 0 below min, 1 above max, x^2 (3 - 2x) between.</summary>
        public static float Smooth(float x, float min, float max)
        {
            if (x <= min) return 0f;
            if (x >= max) return 1f;
            x = (x - min) / (max - min);
            return x * x * (3f - 2f * x);
        }

        /// <summary>debrisPopScale(age, end): 1 until end - .5, +12 % by end - .36, 0 at end.</summary>
        public static float DebrisScale(float age, float end)
        {
            if (age <= end - 0.5f) return 1f;
            if (age < end - 0.36f) return 1f + 0.12f * Smooth(age, end - 0.5f, end - 0.36f);
            return Mathf.Max(0.001f, 1.12f * (1f - Smooth(age, end - 0.36f, end)));
        }

        /// <summary>t = pop progress 0..1 (over .5 s).</summary>
        public static float CorpseScale(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.24f ? 1f + 0.08f * Mathf.Sin(t / 0.24f * Mathf.PI) : Mathf.Max(0.001f, 1f - Smooth(t, 0.24f, 1f));
        }

        class Job
        {
            public Transform t; public Vector3 baseScale, basePos, pivot; public Style style; public float age;
            public Action done; public bool destroy;
        }

        static PopOut inst;
        public static int Popped { get; private set; }
        readonly List<Job> jobs = new List<Job>();

        static PopOut Instance
        {
            get
            {
                if (inst) return inst;
                inst = FindFirstObjectByType<PopOut>();
                if (!inst) inst = new GameObject("PopOut").AddComponent<PopOut>();
                return inst;
            }
        }

        public static bool IsPopping(Transform t)
        {
            if (!inst || !t) return false;
            foreach (var j in inst.jobs) if (j.t == t) return true;
            return false;
        }

        /// <summary>
        /// </summary>
        public static void Begin(Transform t, Style style, Action done = null, bool destroy = false, bool puff = true, Vector3? groundPoint = null)
        {
            if (!t) { done?.Invoke(); return; }
            var p = Instance;
            foreach (var j in p.jobs) if (j.t == t) return;
            var pivot = groundPoint ?? BottomCentre(t);
            p.jobs.Add(new Job { t = t, baseScale = t.localScale, basePos = t.position, pivot = pivot, style = style, done = done, destroy = destroy });
            Popped++;
            if (puff && style == Style.Corpse && FxManager.I) FxManager.I.Pop(pivot + Vector3.up * 0.4f, 0.5f);
        }

        /// <summary>Bottom centre of the visible renderers (world AABB), or the transform position without renderers.</summary>
        public static Vector3 BottomCentre(Transform t)
        {
            bool any = false; var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? new Vector3(b.center.x, b.min.y, b.center.z) : t.position;
        }

        void Awake() { if (inst && inst != this) { Destroy(this); return; } inst = this; }
        void OnDestroy() { if (inst == this) inst = null; }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                if (!j.t) { jobs.RemoveAt(i); j.done?.Invoke(); continue; }
                j.age += dt;
                float k = j.style == Style.Corpse ? CorpseScale(j.age / Duration) : DebrisScale(j.age, Duration);
                j.t.localScale = j.baseScale * k;
                j.t.position = j.pivot + (j.basePos - j.pivot) * k;                                   // uniform scale about the ground point
                if (j.age < Duration) continue;
                jobs.RemoveAt(i);
                if (j.destroy) Destroy(j.t.gameObject);
                j.done?.Invoke();
            }
        }
    }
}

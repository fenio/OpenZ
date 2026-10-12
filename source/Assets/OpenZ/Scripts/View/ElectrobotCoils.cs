using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// children of mixamorigSpine2, not humanoid). In a tight seat BoardingView asks for them to be stowed: each mast eases down to
    /// <see cref="StowScale"/> of its size about its base (0.3 s, never a pop) and back up when he stands; the spark balls, their
    /// glow sockets and ElectrobotView's arcs follow the bones. Runs after the Animator (LateUpdate, order 95, before ElectrobotView 96
    /// reads the coil tops), so a clip's own CoilMast keys never undo it. On the Z_bs01_electrobot prefab (OpenZElectrobot menu 1).
    /// </summary>
    [DefaultExecutionOrder(95)]
    public sealed class ElectrobotCoils : MonoBehaviour
    {
        [Tooltip("mast scale when fully stowed (measured offline: 0.5 clears the jeep's gun post, no contact with his own body)")]
        [Range(0.3f, 1f)] public float StowScale = 0.5f;
        [Tooltip("seconds to stow / unstow")] public float EaseTime = 0.3f;
        [Tooltip("0 = upright, 1 = stowed (set by BoardingView)")] [Range(0f, 1f)] public float stow;
        Transform l, r; float shown; bool bound;

        /// <summary>BoardingView: the stow wanted for this figure (no-op on every other robot).</summary>
        public static void Set(GameObject figure, float amount)
        {
            if (!figure) return;
            var c = figure.GetComponent<ElectrobotCoils>();
            if (c) c.stow = Mathf.Clamp01(amount);
        }

        void Bind()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "CoilMast_L") l = t;
                else if (t.name == "CoilMast_R") r = t;
            }
            bound = true;
        }

        void OnEnable() { shown = stow; }

        void LateUpdate()
        {
            if (!bound) Bind();
            if (!l && !r) return;
            float dt = Time.deltaTime;
            shown = Mathf.MoveTowards(shown, stow, dt / Mathf.Max(0.01f, EaseTime));
            float k = Mathf.Lerp(1f, StowScale, Mathf.SmoothStep(0f, 1f, shown));
            if (l) l.localScale = Vector3.one * k;
            if (r) r.localScale = Vector3.one * k;
        }
    }
}

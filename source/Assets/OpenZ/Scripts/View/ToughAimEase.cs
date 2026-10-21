using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// (ToughKick reads the tube's heading), no longer jump from target to target in one frame. VIEW ONLY: the simulation, the rockets'
    /// timing, damage and targets are untouched. The rules are in <see cref="ToughAimEaseCore"/> (plain C#, run as they are by the
    /// off-engine replay); this component feeds it once a frame from WeaponHold (its Tough shoulder hold, after weapons_v2's ballistic
    /// line and before the carry pitch and the fire climb, which stay as they were) and gives UnitAim the fire gate
    /// (<see cref="HoldsFire"/>, through UnitView.StanceHoldsFire): a rocket waits, within UnitAim's own cap, until the drawn tube is on it.
    /// Added to a Tough by WeaponHold on its first frame. <see cref="On"/> = false: exactly the drawing of before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToughAimEase : MonoBehaviour
    {
        /// <summary>Off = the raw aim of before (the tube and chest follow every target change at once).</summary>
        public static bool On = true;
        /// <summary>Rockets seen leaving a Tough this component draws, and the drawn tube's angle off the aim then (deg).</summary>
        public static int Launches; public static float MaxLaunchLag, SumLaunchLag;
        public static void ResetCounters() { Launches = 0; MaxLaunchLag = SumLaunchLag = 0f; ToughAimEaseCore.ResetCounters(); }

        UnitView view; bool ok; int lastFrame = -10; float lastFire = -100f;
        readonly ToughAimEaseCore core = new ToughAimEaseCore();

        /// <summary>WeaponHold (the Tough's shoulder hold): before = the aim it had before weapons_v2 (UnitAim's, or the carry line),
        /// after = with weapons_v2's ballistic line. Returns the aim to lay the tube on.</summary>
        public static Vector3 Ease(WeaponHold h, UnitAim ua, Vector3 before, Vector3 after)
        {
            if (!On || !h) return after;
            if (!h.TryGetComponent(out ToughAimEase e)) e = h.gameObject.AddComponent<ToughAimEase>();
            return e.Step(ua, before, after);
        }

        /// <summary>UnitView.StanceHoldsFire: this Tough's drawn tube is still turning onto its aim (a rocket waits for it).</summary>
        public static bool HoldsFire(UnitView v) =>
            On && v && v.TryGetComponent(out ToughAimEase e) && e.ok && Time.frameCount - e.lastFrame <= 2 && e.core.HoldsFire;

        void Awake()
        {
            view = GetComponent<UnitView>();
            ok = view && view.Obj != null && view.Obj.Kind == ZKind.Robot && view.Obj.Type == "tough";
        }

        void OnDisable() { lastFrame = -10; }

        Vector3 Step(UnitAim ua, Vector3 before, Vector3 after)
        {
            if (!ok || view.Obj == null || view.Dying || view.Reacting || after.sqrMagnitude < 1e-8f) { lastFrame = -10; return after; }
            if (Time.frameCount == lastFrame) return Dir(core.Yaw, core.Elev) * after.magnitude;
            if (Time.frameCount - lastFrame > 2) core.Reset();                                       // first frame, not drawn, knocked down: from the raw aim
            lastFrame = Time.frameCount;
            bool onTarget = (ua && ua.TryAimDirection(out _)) || Vector3.Angle(before, after) > 0.01f;   // UnitAim's target, or the bazooka's ballistic line on one
            var f = -transform.forward;                                                                // robot models face -z
            float bodyYaw = f.x * f.x + f.z * f.z > 1e-8f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            float rawYaw = Mathf.Atan2(after.x, after.z) * Mathf.Rad2Deg;
            float rawElev = Mathf.Asin(Mathf.Clamp(after.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            var at = view.Obj.AttackObject; bool engaged = at != null && !at.Destroyed && at.KillAt < 0;                   // the sim's own live target (else the views' guess)
            core.Step(Time.time, Time.deltaTime, rawYaw, rawElev, onTarget, engaged, Sent(view.Obj), bodyYaw, ua && ua.HeldShots > 0);
            if (view.LastFireAt > lastFire + 1e-5f)
            {   // a rocket left since the last frame (WeaponsV2Runner launches after this): how far the drawn tube was off its aim
                lastFire = view.LastFireAt;
                if (Time.time - lastFire < 0.1f) { float lag = (float)core.Lag; Launches++; SumLaunchLag += lag; if (lag > MaxLaunchLag) MaxLaunchLag = lag; }
            }
            return Dir(core.Yaw, core.Elev) * after.magnitude;
        }

        /// <summary>He walks on an order to go elsewhere (no aim is held then): a move, a force move, entering, a fort, a crate, a repair.</summary>
        public static bool Sent(ZObject o)
        {
            if (o == null || !o.IsMoving || o.Waypoints.Count == 0) return false;
            var m = o.Waypoints[0].mode;
            return m == WpMode.Move || m == WpMode.ForceMove || m == WpMode.Enter || m == WpMode.EnterFort || m == WpMode.PickupGrenades || m == WpMode.UnitRepair || m == WpMode.CraneRepair;
        }

        static Vector3 Dir(double yaw, double elev)
        {
            float y = (float)yaw * Mathf.Deg2Rad, e = (float)elev * Mathf.Deg2Rad, c = Mathf.Cos(e);
            return new Vector3(Mathf.Sin(y) * c, Mathf.Sin(e), Mathf.Cos(y) * c);
        }

        /// <summary>The numbers and counters (the Play check reads them in the console).</summary>
        public static string Report() =>
            $"ToughAimEase on {On}: follow {ToughAimEaseCore.SmoothTime:F2} s / {ToughAimEaseCore.MaxSpeed:F0} deg/s, hold {ToughAimEaseCore.HoldSecs:F1} s (twist <= {ToughAimEaseCore.TwistMax:F0} deg), " +
            $"switch after {ToughAimEaseCore.SwitchHold:F2} s / a guess after {ToughAimEaseCore.PredictHold:F2} s (jump > {ToughAimEaseCore.SwitchDeg:F0} deg), fire gate {ToughAimEaseCore.LagTol:F0} deg; frames {ToughAimEaseCore.Frames}, holds {ToughAimEaseCore.Holds}, " +
            $"switches followed {ToughAimEaseCore.Switches}, jumps never followed {ToughAimEaseCore.JumpsIgnored}, frames a rocket waited for the tube {ToughAimEaseCore.GateFrames}; " +
            $"largest one-frame step raw {ToughAimEaseCore.MaxStepRaw:F1} deg -> drawn {ToughAimEaseCore.MaxStepDrawn:F1} deg; rockets {Launches}, tube off its aim at launch mean {(Launches > 0 ? SumLaunchLag / Launches : 0f):F1} / max {MaxLaunchLag:F1} deg";
    }
}

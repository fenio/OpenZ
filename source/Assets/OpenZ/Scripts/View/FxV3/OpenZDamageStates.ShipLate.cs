using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// untouched.
    /// <list type="bullet">
    /// <item><b>Why:</b> OpenZDamageStates ticks in Update at order 70. The shuttle's UnitView places its root at order 60 the vehicles'
    /// way: on the rules' spot ON THE GROUND, turned to the hull's steering yaw. The shuttle's own view (OzPlayableShuttle, order 200)
    /// lifts it to where it is drawn (up to 12 m, banked, bobbing) only after that. So K393's emitter points (the pod tops, the top vent,
    /// the small fire's pod) were read off that ground pose: every beat puff, the trail, the flame tongues, the embers and the heat shimmer
    /// of a flying damaged shuttle were born about 4.7 m over the ground under it (two lines of puffs when battered, three when burning),
    /// and the clank, the ignite and a repair's last puff about 5.4 m over the ground.</item>
    /// <item><b>Now:</b> TickUnits hands every living shuttle to <see cref="ShipDefer"/> (one hook); its whole step (the health stage with
    /// the others' thresholds and hysteresis, the stage sounds, the char, the stillness the oil stain needs) and K393's own ShipTick run in
    /// <see cref="OzShipSmokeLate"/> (LateUpdate at order 205: after the shuttle's pose in Update 200 and its LateUpdate 200, so with the
    /// bank, the bob, the crash pose and the kickflip's drawn turn). ShipTick itself is K393's, unchanged: its points, spacing, beat,
    /// scales and LOW thinning, now on the hull where it is drawn. The small pyro fire on the pod keeps its spot: OpenZPyroFire stores it
    /// ship-local and draws it in its LateUpdate (it was right before and stays right). The oil stain stays K393's (only when the rules
    /// have it landed and it stands still).</item>
    /// <item><b>Test switch:</b> <see cref="ShipDrawnPose"/> = false = the old path (K393's tick inside Update 70), for the Play check's
    /// BEFORE run.</item>
    /// </list>
    /// Play proof: View/OzShipSmokeHeightProof.cs (every puff's emitter height against the drawn hull and the ground).
    /// </summary>
    public partial class OpenZDamageStates
    {
        /// <summary>On: a living shuttle's damage step runs after its drawn pose (OzShipSmokeLate). Off: the old path (a test switch).</summary>
        public static bool ShipDrawnPose = true;
        /// <summary>Proof: shuttle steps run after the pose, and the frames that had one.</summary>
        public static int ShipLateSteps, ShipLateFrames;

        readonly List<int> shipLate = new List<int>(4);
        OzShipSmokeLate shipLateRunner;

        /// <summary>TickUnits hook (a living shuttle, after its hit flash and gear check): true = its step waits for the drawn pose.</summary>
        bool ShipDefer(int id)
        {
            if (!ShipDrawnPose) return false;
            if (!shipLateRunner)
            {
                shipLateRunner = GetComponent<OzShipSmokeLate>();
                if (!shipLateRunner) shipLateRunner = gameObject.AddComponent<OzShipSmokeLate>();
            }
            if (!shipLateRunner.enabled) shipLateRunner.enabled = true;
            if (!shipLate.Contains(id)) shipLate.Add(id);
            return true;
        }

        /// <summary>OzShipSmokeLate (LateUpdate 205): the deferred shuttles, each on its drawn pose.</summary>
        public void ShipLate()
        {
            if (shipLate.Count == 0) return;
            if (!Enabled || !isActiveAndEnabled || game == null || game.World == null) { shipLate.Clear(); return; }
            float now = Time.time; var fx = OpenZFxPlayer.I; bool fxOn = fx && fx.isActiveAndEnabled;
            ShipLateFrames++;
            for (int i = 0; i < shipLate.Count; i++)
            {
                int id = shipLate[i];
                if (!game.Views.TryGetValue(id, out var v) || !v || !units.TryGetValue(id, out var u)) continue;
                var o = v.Obj;
                if (o == null || !o.IsShuttle || o.MaxHealth <= 0 || o.Destroyed || v.Dying || o.KillAt >= 0) continue;   // killed this frame: TickUnits takes it next frame
                ShipStep(id, v, o, u, now, fx, fxOn);
            }
            shipLate.Clear();
        }

        /// <summary>TickUnits' own lines for a living vehicle (the stage, the stillness, the char; then K393's tick), on the drawn ship.</summary>
        void ShipStep(int id, UnitView v, ZObject o, U u, float now, OpenZFxPlayer fx, bool fxOn)
        {
            ShipLateSteps++;
            float k = o.Health / (float)o.MaxHealth;
            var pos = v.transform.position;                                                             // the drawn root (the pads' soles), at its height
            float moved = (pos - u.lastPos).sqrMagnitude; u.lastPos = pos;
            u.still = moved < 0.0004f ? u.still + Time.deltaTime : 0f;
            int want = k <= BurnAt ? 2 : k <= SmokeAt ? 1 : 0;
            if (u.stage == 2 && k <= OutAt) want = 2;                                                   // the others' hysteresis: lit until repaired past 40 %
            if (want != u.stage) SetUnitStage(id, v, o, u, want, pos);
            if (now >= u.nextUnchar) UncharLiving(v, u, now);
            if (u.stage == 0 || !v.gameObject.activeInHierarchy || ZodBriefing.EndingHides(v)) return;
            ShipTick(id, v, o, u, now, fx, fxOn);
        }
    }
}

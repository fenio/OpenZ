using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Pose() runs in Update at order 200, after UnitView (order 60) has put the root on the rules' spot ON THE GROUND (the vehicles'
    /// placement); until its own transform.SetPositionAndRotation the transform is still that ground pose. Two effects laid from inside
    /// Pose() read their ship-local points off it:
    /// <list type="bullet">
    /// <item>K408's engine cut (0.8 s before a crash landing's contact, the ship still up to 7 m over its pad): its two pod puffs were
    /// born 2 m over the GROUND under the ship, its sputter and whoosh 1.5 m over the ground;</item>
    /// <item>K256e's crash wisp (after the contact, while it rests crooked): the right height, but turned to the hull's steering yaw and
    /// without the crash's slide and tilt, so not on the drawn low pod.</item>
    /// </list>
    /// Now both wait for the pose: they are queued and laid by <see cref="AfterPose"/> (one hook right after SetPositionAndRotation), at
    /// the same ship-local points, sizes and sounds as before, on the ship where it is drawn. Test switch: <see cref="PosedPuffsOn"/> =
    /// false = the old way. The Play check (View/OzShipSmokeHeightProof.cs) reads <see cref="PosedRows"/>.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>On: the puffs laid inside Pose() come out after the pose, on the drawn ship. Off: the old way (a test switch).</summary>
        public static bool PosedPuffsOn = true;
        /// <summary>Proof: posed puffs laid, engine cuts laid after the pose, the lowest posed puff against the drawn hull's belly (m, + = over
        /// it) and over the ground (m) since <see cref="PosedReset"/>, and one row per puff (what, its height over the ground, the belly's and
        /// the hull top's over the ground).</summary>
        public static int PosedPuffs, PosedCuts;
        public static float PosedLowestOverBellyM = float.MaxValue, PosedLowestOverGroundM = float.MaxValue;
        public static readonly List<string> PosedRows = new List<string>();
        public static void PosedReset() { PosedPuffs = PosedCuts = 0; PosedLowestOverBellyM = PosedLowestOverGroundM = float.MaxValue; PosedRows.Clear(); }

        struct PosedPuff { public Vector3 local; public float scale; public bool burning; }
        readonly List<PosedPuff> posedQ = new List<PosedPuff>(4);
        bool posedCutDue;

        /// <summary>CrashWisp: a puff at this ship-local point once the ship is posed (true); false = the switch is off (lay it now).</summary>
        bool PuffOnShip(Vector3 local, float scale, bool burning)
        {
            if (!PosedPuffsOn) return false;
            if (posedQ.Count < 16) posedQ.Add(new PosedPuff { local = local, scale = scale, burning = burning });
            return true;
        }

        /// <summary>CrashDropCut: the engines' cough once the ship is posed (true); false = the switch is off (as before, now).</summary>
        bool CrashCutAfterPose()
        {
            if (!PosedPuffsOn) return false;
            posedCutDue = true; return true;
        }

        /// <summary>Pose(), right after the ship is placed where it is drawn this frame.</summary>
        void AfterPose()
        {
            if (posedCutDue) { posedCutDue = false; CrashDropCutPosed(); }
            if (posedQ.Count == 0) return;
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled)
                for (int i = 0; i < posedQ.Count; i++)
                {
                    var q = posedQ[i]; var at = transform.TransformPoint(q.local);
                    fx.WreckSmoke(at, q.scale, q.burning);
                    NotePosed(at, "wisp");
                }
            posedQ.Clear();
        }

        /// <summary>K408's engine cut (CrashDropCut's own lines), on the drawn ship: the sputter, the falling whoosh, a puff from each pod.</summary>
        void CrashDropCutPosed()
        {
            PosedCuts++;
            var at = transform.position + Vector3.up * 1.5f;
            if (AudioDirector.Has("veh_engine_shutdown")) AudioDirector.Fire("veh_engine_shutdown", at, Obj.Id + 11, 0.9f, 1.25f);
            if (AudioDirector.Has("shuttle_strafe_whoosh")) AudioDirector.Fire("shuttle_strafe_whoosh", at, Obj.Id + 12, 0.8f, 0.8f);
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled)
            {
                var a = transform.TransformPoint(new Vector3(2.47f, 2.0f, -1.6f)); var b = transform.TransformPoint(new Vector3(-2.47f, 2.0f, -1.6f));
                fx.WreckSmoke(a, 0.4f, false);
                fx.WreckSmoke(b, 0.4f, false);
                CrashDropPuffs += 2;
                NotePosed(a, "cut"); NotePosed(b, "cut");
            }
        }

        void NotePosed(Vector3 at, string what)
        {
            PosedPuffs++;
            float gy = game ? game.GroundY(at.x, at.z) : 0f;
            float belly = transform.TransformPoint(new Vector3(0f, OzShuttleAim.HullMin.y, 0f)).y, top = transform.TransformPoint(new Vector3(0f, OzShuttleAim.HullMax.y, 0f)).y;
            PosedLowestOverBellyM = Mathf.Min(PosedLowestOverBellyM, at.y - belly);
            PosedLowestOverGroundM = Mathf.Min(PosedLowestOverGroundM, at.y - gy);
            if (PosedRows.Count < 2000)
                PosedRows.Add($"{Time.time:F3},{what},{at.y - gy:F2},{belly - gy:F2},{top - gy:F2},{at.y - belly:F2}");
        }
    }
}

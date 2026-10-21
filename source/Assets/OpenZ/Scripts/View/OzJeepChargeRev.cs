using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV121 (Sim/ZObject.JeepCharge.cs), view only: a jeep that begins a charge at a robot guns its engine, with the same engine rev a
    /// player's order plays (EngineRev: the jeep's engine clip, the pitch rising to pull-away, a swell then a fade, at most once every
    /// EngineRev.Gap s per vehicle), for the jeeps of every team, heard where the jeep is. One rev per charge (a squad run over one after
    /// the other is one charge); the drive back is a plain move and makes no rev of its own. The run-over itself already sounds (the hit,
    /// the squash). Starts by itself at the first scene and does nothing while no game runs or the rule is off.
    /// </summary>
    public sealed class OzJeepChargeRev : MonoBehaviour
    {
        static OzJeepChargeRev inst;
        readonly HashSet<int> charging = new HashSet<int>(), seen = new HashSet<int>();
        readonly List<ZObject> one = new List<ZObject>(1);
        /// <summary>Charges that asked for a rev since the start (the checks in Play compare it with EngineRev.Played).</summary>
        public static int Revs { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst) return;
            var go = new GameObject("OZ_JeepChargeRev");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<OzJeepChargeRev>();
        }

        void Update()
        {
            var g = ZGame.I;
            var w = g ? g.World : null;
            if (w == null || !ZWorld.JeepChargeDeviation) { charging.Clear(); return; }
            seen.Clear();
            foreach (var o in w.Objects)
            {
                if (o.Kind != ZKind.Vehicle || o.Type != "jeep" || o.Destroyed || o.ChargeTargetId < 0) continue;
                seen.Add(o.Id);
                if (!charging.Add(o.Id)) continue;                                           // already revved for this charge
                one.Clear(); one.Add(o);
                EngineRev.Ordered(one);
                Revs++;
            }
            charging.IntersectWith(seen);                                                     // a charge that is over: its next one revs again
        }
    }
}

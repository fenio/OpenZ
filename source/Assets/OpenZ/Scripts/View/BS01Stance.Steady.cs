using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K248b: the sniper's stance holds through a fight (the rules: SniperStanceGate.cs). Drive() hands the sniper's frame to
    /// <see cref="SteadySniper"/>, which feeds the gate and passes DriveSniper a "walking" that is true only when the stance has to
    /// change for it: standing, "not ready to go down yet" (it has not stood still with a target for DownStill s, or it got up for a
    /// walk in this fight); kneeling or lying, "a real walk" (the sim moves it for RealWalkTime s, or the drawn body is RealWalkDist m
    /// from where it went down; at once when the sim sets off on an order or a chase). It stays down until the fight is over (<see cref="SteadyLinger"/>). Everything else of DriveSniper
    /// (clips, water, grenade throw) is as it was. Counters for the Play check: SniperStanceGate's, and per
    /// sniper the lie-downs and get-ups (<see cref="SteadyCounts"/>).
    /// </summary>
    public partial class BS01Stance
    {
        readonly SniperStanceGate steady = new SniperStanceGate();
        bool steadyHeldNudge;

        /// <summary>Per sniper (unit id): lie-downs, get-ups for a walk, get-ups after the last target (Play check).</summary>
        public static readonly Dictionary<int, Vector3Int> SteadyCounts = new Dictionary<int, Vector3Int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SteadyStatics() { SteadyCounts.Clear(); SniperStanceGate.ResetCounters(); SniperStanceGate.On = true; }   // every Play start: the new rules (an A/B look ends with the session)

        /// <summary>DriveSniper: how long it stays down after its last target (SniperStanceGate.Linger).</summary>
        float SteadyLinger => SniperStanceGate.Linger(linger);

        bool SteadySniper(bool engaged, bool walking, bool throwing, ref string viewState, float animMul, float dt)
        {
            var p = transform.position;
            steady.Step(dt, engaged, walking, obj != null && obj.IsMoving, SniperStanceGate.OrderWalk(obj), p.x, p.z);
            steady.Look(obj);                                                               // K248d: down only where the fight needs no more walking
            bool gated = st == St.Up ? walking || !steady.MayGoDown : steady.MustGetUp(walking);
            bool held = st != St.Up && st != St.GetUp && walking && !gated;
            if (held && !steadyHeldNudge) SniperStanceGate.NudgesHeld++;
            steadyHeldNudge = held;
            var before = st;
            bool own = DriveSniper(engaged, gated, throwing, ref viewState, animMul);
            if (obj != null && before != st)
            {
                SteadyCounts.TryGetValue(obj.Id, out var c);
                if (before == St.Up && st == St.Down) { steady.WentDown(); SniperStanceGate.LieDowns++; c.x++; }
                else if (st == St.GetUp)
                {
                    if (throwing) SniperStanceGate.GetUpsThrow++;
                    else if (gated) { steady.GotUpForWalk(); SniperStanceGate.GetUpsWalk++; c.y++; if (SteadyEnemyInRange()) SniperStanceGate.GetUpsWalkInRange++; }
                    else { SniperStanceGate.GetUpsLinger++; c.z++; }
                }
                SteadyCounts[obj.Id] = c;
            }
            return own;
        }

        /// <summary>An enemy unit inside this sniper's range right now (for the counter of get-ups in the middle of a fight).</summary>
        bool SteadyEnemyInRange()
        {
            var w = ZGame.I ? ZGame.I.World : null;
            if (w == null || obj == null) return false;
            foreach (var o in w.Objects)
                if (o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != obj.Owner && obj.CanAttackObject(o) && obj.WithinAttackRadius(o)) return true;
            return false;
        }
    }
}

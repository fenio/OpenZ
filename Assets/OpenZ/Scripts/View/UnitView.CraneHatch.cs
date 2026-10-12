using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The crane's rifle shots (sim DV31) leave the hatch driver's muzzle (BoardingView) and never jolt
    /// the crane's hull (UnitView.OnFire).
    /// </summary>
    public partial class UnitView
    {
        bool CraneMuzzle(out Vector3 p)
        {
            p = default;
            return Obj != null && Obj.Type == "crane" && Obj.Kind == ZKind.Vehicle && BoardingView.TryCrewMuzzle(Obj.Id, out p);
        }
    }
}

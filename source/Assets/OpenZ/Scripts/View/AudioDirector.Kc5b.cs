using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>Version 1.1 item 202: a vehicle / gun whose death waits for the drawn shot that kills it (VehicleDeathSync) gets its
    /// death sounds (the turret toss, the wreck fire loop) when it explodes, not on the sim's frame.</summary>
    public partial class AudioDirector
    {
        /// <summary>OnDeath hook, vehicle / gun: true = the sounds were put off with the held explosion.</summary>
        bool VehicleDeathLater(ZEvent e, ZObject o, Vector3 p)
        {
            float wait = VehicleDeathSync.Delay(ZGame.I, e, o);
            if (wait <= 0.015f) return false;
            StartCoroutine(VehicleDeathSync.After(wait, () =>
            {
                if (o.Kind == ZKind.Vehicle)
                {
                    Owned("exp_vehicle_death");
                    if (o.Type == "light" || o.Type == "medium" || o.Type == "heavy") Play("dst_turret_toss", p + Vector3.up * 2f, o.Id);
                }
                else Owned("exp_cannon_death");
                wrecks[o.Id] = (p, Time.time);
            }));
            return true;
        }
    }
}

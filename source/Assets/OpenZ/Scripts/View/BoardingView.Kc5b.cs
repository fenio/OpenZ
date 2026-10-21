using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>Version 1.1 item 202: the crew of a vehicle / gun whose death waits for the drawn shot that kills it (VehicleDeathSync)
    /// is flung out when it explodes, not on the sim's frame.</summary>
    public partial class BoardingView
    {
        /// <summary>Death hook, vehicle / gun: true = the crew's death was put off with the held explosion.</summary>
        bool CrewDiesLater(ZEvent e, UnitView v, Rig r)
        {
            float wait = VehicleDeathSync.Delay(game ? game : ZGame.I, e, v.Obj);
            if (wait <= 0.015f) return false;
            StartCoroutine(VehicleDeathSync.After(wait, () => { if (r != null) CrewDies(r); }));
            return true;
        }
    }
}

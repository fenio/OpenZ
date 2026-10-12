using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>On the DetailsV2_&lt;L&gt; root: level DEC_ decor the Editor hid because it stood in front of a detail (the cave
    /// mouth); OpenZ/Details v2 re-enables them before it rebuilds or removes the root.
    /// matches the class. Declared inside DetailProp.cs it had none, so L02_Desert.unity embedded a MonoScript (!u!115) and
    /// the Windows player crashed loading L02 ("OpenZ_Data/level1 is corrupted! Position out of bounds").</summary>
    public class DetailsV2Hidden : MonoBehaviour
    {
        public List<GameObject> hidden = new List<GameObject>();
    }
}

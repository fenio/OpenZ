using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The fort_v2 gun material (m01_fort_v2_gun_mat) carries war paint A (the live FortV2_Gun_Albedo);
    /// Resources/OpenZ/FortV2/m01_fort_v2_gun_mat_B carries B (WarPaint~/B_ClawSlashes). Every frame the gun's renderers (yaw, pitch,
    /// recoil nodes under the fort view) show A while the fort is red (or neutral) and B while it is blue. View only.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class FortGunWarPaint : MonoBehaviour
    {
        static Material matB;
        readonly List<Renderer> gun = new List<Renderer>();
        Material matA; Team shown = (Team)(-1); ZObject fort; bool bound;

        /// <summary>FortGunView calls this with its fort object and view root.</summary>
        public static void Attach(GameObject fortView, ZObject fort)
        {
            if (!fortView) return;
            var w = fortView.GetComponent<FortGunWarPaint>(); if (!w) w = fortView.AddComponent<FortGunWarPaint>();
            w.fort = fort;
        }

        void Bind()
        {
            bound = true;
            if (!matB) matB = Resources.Load<Material>("OpenZ/FortV2/m01_fort_v2_gun_mat_B");
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial; if (!m) continue;
                if (m.name.StartsWith("m01_fort_v2_gun_mat")) { gun.Add(r); if (!matA && !m.name.EndsWith("_B")) matA = m; }
            }
        }

        void LateUpdate()
        {
            if (!bound) Bind();
            if (fort == null || !matA || !matB || gun.Count == 0) return;
            if (fort.Owner == shown) return;
            shown = fort.Owner;
            var m = TeamColors.Shown(shown) == Team.Blue ? matB : matA;
            foreach (var r in gun) if (r) r.sharedMaterial = m;
        }

        /// <summary>Checks: the paint a fort gun shows now ("A", "B" or "-").</summary>
        public string Shown => gun.Count == 0 ? "-" : gun[0].sharedMaterial == matB ? "B" : "A";
    }
}

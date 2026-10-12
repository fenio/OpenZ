using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// its gunner is hidden whenever the gun faces the camera. This hangs a raised perch on the gun's CannonTurretYaw: a bucket seat on
    /// a deck standing on the chair, two control levers on an arm from the right-hand cheek, a grab pole with a team cap
    /// (Resources/OpenZ/GunnerSeats/gun_perch: the same kit as the imported guns' modelled seats, CannonTurretYaw-local vertices,
    /// gun_perch_mat = the gatling's TeamLit toy paint). It turns with the turret; TeamPaint (team, neutral grey, the fort palette)
    /// paints it like the gun. BoardingView seats the gunner on it (LegacyTowerPerch). View only.
    /// </summary>
    public static class GunnerPerch
    {
        public const string NodeName = "OZ_GunnerPerch";
        /// <summary>
        /// The perch is stretched up from its chair foot (y 0.45) so the seat (hips) rides `Raise` m higher than the
        /// modelled 1.90 m: head, shoulders and hands clear over the 1.79 m housing. BoardingView seats him at BaseSeat + Raise.</summary>
        public static float Raise = 0.32f;
        public const float Foot = 0.45f, BaseSeatY = 1.90f;
        public static Vector3 Seat => new Vector3(0f, BaseSeatY + Raise, 1.56f);
        static Mesh mesh; static Material mat; static bool loaded; static int enabledState = -1;

        public static bool Enabled
        {
            get { if (enabledState < 0) enabledState = PlayerPrefs.GetInt("oz_tower_perch", 1) != 0 ? 1 : 0; return enabledState == 1; }
        }

        /// <summary>Builds the perch under `turret` once (true when it is there).</summary>
        public static bool Build(Transform turret, Component view)
        {
            if (!turret || !Enabled) return false;
            if (turret.Find(NodeName)) return true;
            if (!loaded)
            {
                loaded = true;
                mesh = Resources.Load<Mesh>("OpenZ/GunnerSeats/gun_perch");
                mat = Resources.Load<Material>("OpenZ/GunnerSeats/gun_perch_mat");
                if (!mesh || !mat) Debug.LogWarning("[OpenZ] GunnerPerch: Resources/OpenZ/GunnerSeats/gun_perch(_mat) missing: tower legacy guns keep their chair");
            }
            if (!mesh || !mat) return false;
            var go = new GameObject(NodeName);
            go.transform.SetParent(turret, false);
            float k = 1f + Raise / (BaseSeatY - Foot);                                                     // stretch about the chair foot
            go.transform.localPosition = new Vector3(0f, Foot * (1f - k), 0f); go.transform.localScale = new Vector3(1f, k, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
            var tp = view ? view.GetComponent<TeamPaint>() : null;
            if (tp) tp.Apply();                                                                              // team / neutral / fort palette like the gun
            return true;
        }
    }
}

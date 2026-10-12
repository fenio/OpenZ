using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Robot antics: the asset kit the runtime loads (Resources/OpenZ/Antics/AnticKit.asset, written by the menu
    /// OpenZ > Antics > 1 Import props + kit + controller states). It only references assets; nothing here is sim state.
    ///  * hand props: phone (left fist), fishing rod (right fist, weapon convention), bobber + toy fish (line end);
    ///  * the rocket-fuel can of the intro (full / crushed meshes), scaled to the fist;
    ///  * team sunbeds (two builds per team) and a striped parasol per team;
    ///  * the left-fist prop socket pose (PropSocket_LeftHand under LeftHandGrip, read from the clips model);
    ///  * one prop material (the details kit look: atlas x baked vertex AO + the phone screen emission) and the can material.
    /// </summary>
    public class AnticKit : ScriptableObject
    {
        public const string ResPath = "OpenZ/Antics/AnticKit";

        public GameObject phone, rod, bobber, fish, can, canCrushed;
        public GameObject[] sunbedsRed = new GameObject[0], sunbedsBlue = new GameObject[0];
        public GameObject parasolRed, parasolBlue;
        public Material propMat, canMat;
        [Tooltip("The modelled smartphone (OpenZ > Antics > 3 Build smartphone): keeps its own materials; screen materials for the home and the call screen")]
        public bool phoneOwnMats;
        public Material phoneScreenHome, phoneScreenCall;
        [Tooltip("Smartphone size in metres at scale 1: width, height, thickness (portrait, top on +Y, screen on +Z)")] public Vector3 phoneSize = new Vector3(0.215f, 0.44f, 0.024f);
        [Tooltip("PropSocket_LeftHand local pose under LeftHandGrip (from the clips model)")] public Vector3 socketPos;
        public Quaternion socketRot = Quaternion.identity;
        public Vector3 socketScale = Vector3.one;
        public bool socketOk;

        static AnticKit cached; static bool tried;
        public static AnticKit Get()
        {
            if (cached || tried) return cached;
            tried = true; cached = Resources.Load<AnticKit>(ResPath);
            if (!cached) Debug.LogWarning("[OpenZ Antics] no AnticKit in Resources/" + ResPath + ": run OpenZ > Antics > 1 Import props + kit + controller states; prop antics are skipped");
            return cached;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cached = null; tried = false; }

        public bool HasHandProps => phone && can && canCrushed;
        public bool HasFishing => rod && bobber && fish;
        public bool HasSunbed => sunbedsRed != null && sunbedsRed.Length > 0 && sunbedsBlue != null && sunbedsBlue.Length > 0 && parasolRed && parasolBlue;
    }
}

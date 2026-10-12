// OpenZ (GPL-3.0) - Commander Zod v3 (zod_briefings_v3): Resources/OpenZ/Zod/ZodBoys.asset points at the BS-01 grunt prefab, so the
// two red soldiers Zod talks to also stand in his HQ where no ZGame hands out Z_bs01_grunt (the main menu greeting).
using UnityEngine;

namespace OpenZ
{
    public sealed class ZodBoysRef : ScriptableObject
    {
        public const string ResPath = "OpenZ/Zod/ZodBoys";
        [Tooltip("Assets/OpenZ/Prefabs/Z_bs01_grunt.prefab (the same prefab ZGame spawns)")] public GameObject grunt;
    }
}

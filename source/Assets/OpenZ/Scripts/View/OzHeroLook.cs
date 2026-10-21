using UnityEngine;

namespace OpenZ
{
    /// <summary>DV108: the hero's materials per team colour (filled by the editor menu OpenZ/Hero Squad/1 Build hero prefabs): the red
    /// set is the robots' own paint, the blue set the same textures with the red paint turned to the blue team's.</summary>
    public sealed class OzHeroLook : MonoBehaviour
    {
        public Renderer[] renderers;
        public Material[] red, blue;

        public void Apply(Team team)
        {
            var set = TeamColors.Key(team) == "blue" ? blue : red;
            if (renderers == null || set == null) return;
            int k = 0;
            foreach (var r in renderers)
            {
                if (!r) { k++; continue; }
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length && k < set.Length; i++, k++) if (set[k]) ms[i] = set[k];
                r.sharedMaterials = ms;
            }
        }
    }
}

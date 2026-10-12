using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// soak_hitch_fix: the wreck_teeth cut (WreckPieces.ClipSet: the whole fragment set re-cut at the real bin walls, then squashed
    /// toward its core) made ONCE in the editor (OpenZ/Wreck/Rebake clipped fragments) and stored on the OZ_WreckFragments holder of
    /// the unit prefab. The frag_ children keep their original fragment meshes (TeamPaint measures its accent bands on them at spawn,
    /// exactly as before); at the break WreckPieces.Chunkify takes the stored piece instead of cutting the set on the main thread.
    /// Pure data: no Awake / Update. A set baked with another SquashOuter, a fragment whose mesh was swapped (wheel strip), or a
    /// parent renderer whose mesh was swapped (its bounds place the bin walls) is ignored and takes the runtime cut as before.
    /// </summary>
    [DisallowMultipleComponent]
    public class WreckClipBaked : MonoBehaviour
    {
        [Tooltip("WreckPieces.SquashOuter the bake used")] public float squashOuter = -1f;
        [Tooltip("the frag_ meshes the cut was made from")] public Mesh[] src;
        [Tooltip("the cut + squashed piece of each (null = the cut left nothing there)")] public Mesh[] cut;
        [Tooltip("the core box of each (the cut piece's bounds before the squash)")] public Bounds[] box;
        [Tooltip("the parent renderer's mesh at the bake (ClipSet takes the bin walls from its bounds)")] public Mesh model;
        [Tooltip("bake note")] public string note;

        public bool Matches(float squash) =>
            src != null && cut != null && box != null && src.Length > 0 && src.Length == cut.Length && src.Length == box.Length &&
            Mathf.Abs(squashOuter - squash) <= 1e-4f;

        /// <summary>The holder's parent still shows the mesh the bins were measured on (a run-time swap of that mesh, e.g. a wheel cut,
        /// sends the set back to the runtime cut, as before).</summary>
        public bool SameModel(Transform holder)
        {
            var p = holder ? holder.parent : null;
            var mf = p ? p.GetComponent<MeshFilter>() : null;
            Mesh cur = mf ? mf.sharedMesh : null;
            return cur == model;
        }

        public bool Has(Mesh m)
        {
            if (!m || src == null) return false;
            for (int i = 0; i < src.Length; i++) if (src[i] == m) return true;
            return false;
        }

        public bool TryGet(Mesh m, out (Mesh m, Bounds box) e)
        {
            e = default;
            if (!m || src == null) return false;
            for (int i = 0; i < src.Length; i++)
                if (src[i] == m) { e = cut[i] ? (cut[i], box[i]) : ((Mesh)null, default(Bounds)); return true; }
            return false;
        }
    }
}

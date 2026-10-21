// OpenZ (GPL-3.0) - Level 16, version 1.1 item 360 (rule DV124): the trees a big faller comes down on (View/OzTreeCrush.cs).
// What OzTreeCrush needs of the level's trees: where the drawn trunk of a tree of the rules stands and whether it is still up, the
// level's other tall plants near a point (the flora's tree cells, as the fort's blast reads them), and a plant of the flora felled by
// a big piece that touched it: the same steps as a tree the rules destroy (the break, the stump cut from its own mesh, the fall away
// from the piece with its creak, the crown's thud, leaves and dust or the swamp's splash), plus splinters where it was hit.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public partial class OzTreeFall
    {
        /// <summary>Checks: plants of the flora felled by a big piece, splinter bursts made.</summary>
        public static int CrushFloraFelled, CrushSplinters;

        /// <summary>A tall plant of the flora near a point (no rules: the view fells it).</summary>
        internal struct NearTree { public bool rules; public Transform plant; public Vector3 foot; public float height, radius; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCrush() { CrushFloraFelled = CrushSplinters = 0; }

        /// <summary>The drawn trunk of the rules' tree on this tile: its foot, its height, and whether it is down (falling or fallen).</summary>
        internal static bool RulesTree(int tx, int ty, out Vector3 foot, out float height, out bool down)
        {
            foot = Vector3.zero; height = 0f; down = true;
            if (!I || !I.byTile.TryGetValue(Key(tx, ty), out var t) || !t.holder) return false;
            foot = t.holder.position; height = t.height; down = t.fallen; return true;
        }

        /// <summary>The flora's tall plants standing within r (flat) of p (the trees of the rules are not among them).</summary>
        internal static void FloraNear(Vector3 p, float r, List<NearTree> into)
        {
            if (!I) return;
            float r2 = r * r;
            foreach (var f in I.floraTrees)
            {   // the distance first (the level's ~5800 plants): the engine calls and the name below only for the few near
                float dx = f.foot.x - p.x, dz = f.foot.z - p.z;
                if (dx * dx + dz * dz > r2) continue;
                if (!f.plant || !f.plant.gameObject.activeInHierarchy || f.plant.parent == null || !f.plant.parent.name.StartsWith("T_")) continue;
                into.Add(new NearTree { rules = false, plant = f.plant, foot = f.foot, height = f.height, radius = f.radius });
            }
        }

        /// <summary>A big piece touched this plant of the flora: it breaks and falls away from `from` (view only: the rules have no such tree).</summary>
        internal static bool CrushFlora(Transform plant, Vector3 foot, float height, float radius, Vector3 from, Vector3 hit)
        {
            if (!I || !plant || !plant.gameObject.activeInHierarchy || plant.parent == null || !plant.parent.name.StartsWith("T_")) return false;
            var t = I.FloraStand(plant, foot, height, radius);
            t.fallen = true; Falls++; CrushFloraFelled++;
            var away = Away(t, foot, from);
            float push = Mathf.Clamp01(1.5f - (foot - from).magnitude / 8f);
            DetailProp.Play("dtl_tree_break", Crack, foot + Vector3.up * (t.height * 0.3f), 0.8f, Random.Range(0.92f, 1.08f), -1, 0f);
            var rng = new System.Random(t.tx * 131 + t.ty * 17 + Falls);
            if (I.bits != null) { I.bits.Burst(foot + Vector3.up * (t.height * 0.6f), 10, 2, 3.2f, 0.24f, rng); I.bits.Burst(foot + Vector3.up * (t.height * 0.25f), 8, 0, 2.4f, 0.2f, rng); }
            Splinters(hit, rng.Next());
            for (int i = 0; i < t.plants.Count; i++) if (t.plants[i]) I.HideInFar(t, t.plants[i], t.boxes[i]);
            OzFloraWind.Still(t.holder.gameObject);
            float cut = Mathf.Clamp(t.height * 0.08f, 0.35f, 0.6f);
            var top = I.CutStump(t, foot.y + cut);
            I.StartCoroutine(I.Fall(t, away, push, top ? cut : 0f, top));
            Log(t, "falls under a big piece");
            return true;
        }

        /// <summary>Splinters and leaves where a big piece hit a trunk (bark, dark bark, two leaf greens).</summary>
        internal static void Splinters(Vector3 at, int seed)
        {
            if (!I || I.bits == null) return;
            var rng = new System.Random(seed);
            I.bits.Burst(at, 12, 0, 3.4f, 0.11f, rng); I.bits.Burst(at, 8, 1, 2.8f, 0.09f, rng);
            I.bits.Burst(at + Vector3.up * 1.5f, 9, 2, 2.4f, 0.2f, rng); I.bits.Burst(at + Vector3.up * 2.2f, 7, 3, 2.2f, 0.2f, rng);
            CrushSplinters++;
        }
    }
}

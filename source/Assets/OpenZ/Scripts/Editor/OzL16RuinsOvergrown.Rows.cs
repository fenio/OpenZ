using System.Collections.Generic;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The plant rows of the overgrown ruins (written from the kit's plan; see OzL16RuinsOvergrown.cs).
    /// Row: name, the plant kit's shape, kind, the ruin it stands on or hangs from, x, z (world; on a ruin: in the ruin's own
    /// frame), turn (degrees about up), size (vines: across, length), and for a ruin's rows the kit's table: the foot's height
    /// over the ruin's place (top) or the vine's place in the ruin's frame (hang), used only when the ruin cannot be probed.
    /// To make a plant bigger or smaller, turn it or move it: change its numbers here, run menu 2 again (it replaces the group).
    /// </summary>
    public static partial class OzL16RuinsOvergrown
    {
        static readonly Row[] Rows =
        {
            new Row("C45_top_bush", "FL_BushBroadleafA", "top", "RUIN_45_ruin_column_standing", -0.05f, 0.1f, 37f, 0.68f, 0f, 0f, 0f, 4.0085f, 0f),
            new Row("C45_face_banana", "FL_BananaPlantA", "ground", "", 49.25f, -158.2f, 330f, 0.62f, 0f, 0f, 0f, 0f, 0f),
            new Row("C45_front_fern", "FL_FernClusterA", "ground", "", 48.55f, -158.4f, 200f, 0.78f, 0f, 0f, 0f, 0f, 0f),
            new Row("C45_left_bush", "FL_BushBroadleafA", "ground", "", 47.35f, -157.7f, 290f, 0.66f, 0f, 0f, 0f, 0f, 0f),
            new Row("C45_right_fern", "FL_FernClusterA", "ground", "", 50.25f, -157.75f, 115f, 0.6f, 0f, 0f, 0f, 0f, 0f),
            new Row("C43_top_fern", "FL_FernClusterA", "top", "RUIN_43_ruin_column_standing", -0.05f, -0.25f, 75f, 0.82f, 0f, 0f, 0f, 4.0095f, 0f),
            new Row("C43_front_bush", "FL_BushBroadleafA", "ground", "", 52.75f, -158.2f, 150f, 0.74f, 0f, 0f, 0f, 0f, 0f),
            new Row("C43_side_palm", "FL_PalmYoungA", "ground", "", 54.75f, -157.65f, 20f, 0.62f, 0f, 0f, 0f, 0f, 0f),
            new Row("Mid_fern", "FL_FernClusterA_001", "ground", "", 50.7f, -158.1f, 310f, 0.42f, 0f, 0f, 0f, 0f, 0f),
            new Row("Mid_fern_back", "FL_FernClusterA", "ground", "", 51.2f, -156.7f, 205f, 0.62f, 0f, 0f, 0f, 0f, 0f),
            new Row("Crest_fern_a", "FL_FernClusterA_001", "ground", "", 56.35f, -158.15f, 140f, 0.44f, 0f, 0f, 0f, 0f, 0f),
            new Row("Crest_bush", "FL_BushBroadleafA", "ground", "", 58f, -158.3f, 15f, 0.6f, 0f, 0f, 0f, 0f, 0f),
            new Row("Crest_banana", "FL_BananaPlantA", "ground", "", 60.9f, -158.4f, 230f, 0.5f, 0f, 0f, 0f, 0f, 0f),
            new Row("Crest_fern_b", "FL_FernClusterA", "ground", "", 62.7f, -158.55f, 320f, 0.6f, 0f, 0f, 0f, 0f, 0f),
            new Row("W_vine_a", "FL_VineCurtainA", "hangT", "", 48.1f, 0f, 4f, 0f, 0.8f, 0.66f, 0f, 0f, 0f),
            new Row("G_vine_left", "FL_VineCurtainA", "hang", "RUIN_42_ruin_gateway", -0.7f, 0f, -1f, 0f, 0.8f, 0.72f, -0.7f, 4.555f, -0.3625f),
            new Row("G_vine_door", "FL_VineCurtainA", "hang", "RUIN_42_ruin_gateway", -0.05f, 0f, -2f, 0f, 0.8f, 0.58f, -0.05f, 4.5639f, -0.3419f),
            new Row("G_top_fern", "FL_FernClusterA", "top", "RUIN_42_ruin_gateway", -1.75f, 0f, 100f, 0.52f, 0f, 0f, 0f, 4.5773f, 0f),
            new Row("G_crown_bush", "FL_BushBroadleafA", "top", "RUIN_42_ruin_gateway", -0.1f, 0.05f, 300f, 0.45f, 0f, 0f, 0f, 5.483f, 0f),
            new Row("G_top_fern_r", "FL_FernClusterA_001", "top", "RUIN_42_ruin_gateway", 1.55f, 0f, 35f, 0.4f, 0f, 0f, 0f, 4.5559f, 0f),
            new Row("G_opening_bush", "FL_BushBroadleafA", "ground", "", 58.9f, -160.7f, 45f, 0.5f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_left_bush", "FL_BushBroadleafA", "ground", "", 55.55f, -160.75f, 170f, 0.7f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_left_fern", "FL_FernClusterA", "ground", "", 54.55f, -161.05f, 65f, 0.72f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_right_bush", "FL_BushBroadleafA", "ground", "", 62.35f, -160.7f, 80f, 0.65f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_foot_fern_l", "FL_FernClusterA", "ground", "", 57.2f, -161.95f, 335f, 0.55f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_front_fern", "FL_FernClusterA", "ground", "", 58.7f, -162.15f, 245f, 0.52f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_foot_fern_r", "FL_FernClusterA_001", "ground", "", 60.95f, -161.55f, 260f, 0.45f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_reeds_l", "FL_ReedsB", "water", "", 59.65f, -162.6f, 50f, 0.75f, 0f, 0f, 0f, 0f, 0f),
            new Row("G_reeds_m", "FL_ReedsA", "water", "", 60f, -162.4f, 125f, 0.7f, 0f, 0f, 0f, 0f, 0f),
        };

        /// <summary>The ruins the rows use and the places they had when the kit was made (the report names a ruin moved since).</summary>
        static readonly Dictionary<string, Vector3> RuinAt = new Dictionary<string, Vector3>
        {
            { "RUIN_42_ruin_gateway", new Vector3(59f, -0.769f, -160.9f) },
            { "RUIN_43_ruin_column_standing", new Vector3(53f, 2.197f, -157f) },
            { "RUIN_44_ruin_column_broken", new Vector3(61f, -0.392f, -155f) },
            { "RUIN_45_ruin_column_standing", new Vector3(49f, 2.408f, -157f) },
            { "RUIN_46_ruin_column_broken", new Vector3(43f, -0.508f, -159f) },
        };
        static IEnumerable<string> RuinNames => RuinAt.Keys;

        /// <summary>Per shape the smallest and largest size the level gives it (vines: across); a vine's length range below.</summary>
        static readonly Dictionary<string, Vector2> LevelSizes = new Dictionary<string, Vector2>
        {
            { "FL_BananaPlantA", new Vector2(0.4435f, 0.678f) },
            { "FL_BushBroadleafA", new Vector2(0.4311f, 0.7484f) },
            { "FL_FernClusterA", new Vector2(0.5042f, 0.8496f) },
            { "FL_FernClusterA_001", new Vector2(0.3554f, 0.4739f) },
            { "FL_PalmYoungA", new Vector2(0.5152f, 0.7087f) },
            { "FL_ReedsA", new Vector2(0.6039f, 0.8493f) },
            { "FL_ReedsB", new Vector2(0.6001f, 0.8493f) },
            { "FL_VineCurtainA", new Vector2(0.8f, 0.8f) },
        };
        static readonly Vector2 VineLength = new Vector2(0.5534f, 1f);
    }
}

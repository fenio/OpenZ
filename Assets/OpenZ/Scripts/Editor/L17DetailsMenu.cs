using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>l17_details_v1 test menu (Play mode): status logs and the destruction / behaviour tests of HarbourDetails (L17)
    /// and DesertCritters (L02), plus an edit-mode check that the kit and sounds are imported where the code loads them.</summary>
    public static class L17DetailsMenu
    {
        const string M = "OpenZ/L17 Details + Critters/";

        /// <summary>After apply.py: re-import the kit and the sounds now that L17DetailsImport is compiled (a first import during the
        /// same refresh may have run before the postprocessor existed), then check.</summary>
        [MenuItem(M + "Reimport kit + Check")]
        public static void ReimportAndCheck()
        {
            const string kit = "Assets/OpenZ/Resources/OpenZ/L17Details";
            if (AssetDatabase.IsValidFolder(kit)) AssetDatabase.ImportAsset(kit, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            foreach (var guid in AssetDatabase.FindAssets("dtl_ t:AudioClip", new[] { "Assets/OpenZ/Resources/OpenZ/Sfx" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Check();
        }

        [MenuItem(M + "Check import (edit mode)")]
        public static void Check()
        {
            string[] models = { "L17D_FloorLight", "L17D_Grate", "L17D_Hatch", "L17D_Outfall", "L17D_Wreck_A", "L17D_Wreck_B", "L17D_Wreck_C", "L02C_Rabbit", "L02C_Burrow" };
            string[] sfx = { "dtl_outfall_pour_loop", "dtl_snake_hiss_a", "dtl_snake_hiss_b", "dtl_rabbit_thump_a", "dtl_rabbit_thump_b", "dtl_rabbit_hop_a", "dtl_rabbit_hop_b" };
            int bad = 0; var sb = new System.Text.StringBuilder("[OpenZ] L17 details / critters import check\n");
            foreach (var m in models)
            {
                var go = Resources.Load<GameObject>(DetailKit.Res + m);
                bool ok = go != null;
                if (ok)
                {
                    var f = DetailKit.Find(go.transform, "FWD");
                    var d = f ? go.transform.InverseTransformPoint(f.position) : Vector3.zero;
                    bool fwd = f && d.z > 0.9f && Mathf.Abs(d.x) < 0.1f;
                    int mf = go.GetComponentsInChildren<MeshFilter>(true).Length;
                    sb.Append($"  {m}: {mf} parts, FWD {(f ? d.ToString("F2") : "missing")} {(fwd ? "OK" : "(corrected at run time)")}\n");
                }
                else { sb.Append($"  {m}: MISSING (Resources/OpenZ/L17Details)\n"); bad++; }
            }
            foreach (var s in sfx)
            {
                bool ok = Resources.Load<AudioClip>("OpenZ/Sfx/" + s) != null;
                sb.Append($"  sfx {s}: {(ok ? "OK" : "MISSING")}\n"); if (!ok) bad++;
            }
            sb.Append(bad == 0 ? "CHECK OK" : $"CHECK: {bad} missing");
            if (bad == 0) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
        }

        [MenuItem(M + "Status (play)")]
        public static void Status() { if (HarbourDetails.I) HarbourDetails.I.Status(); if (DesertCritters.I) DesertCritters.I.Status(); }

        [MenuItem(M + "L17: blast every detail in view (play)")]
        public static void Blast() { if (HarbourDetails.I) HarbourDetails.I.TestBlastInView(); }

        /// <summary>Channel wrecks take a blast; harbour wrecks (DV39) lose 40 % of their sim hit points (test-only sim damage):
        /// the third call destroys them and their tiles open.</summary>
        [MenuItem(M + "L17: hit every wreck once (play)")]
        public static void HitWrecks() { if (HarbourDetails.I) HarbourDetails.I.TestHitWrecks(); }

        // ------------------------------------------------------------------ DV39 switch (same pattern as the other deviations)
        const string WrecksMenu = "OpenZ/Deviations/L17 Harbour Wrecks Block Tiles (oz_l17_wrecks)";

        /// <summary>DV39: the L17 harbour wrecks are sim objects whose tiles robots path round until they are destroyed.
        /// PlayerPrefs oz_l17_wrecks (1 = on, default); off in 'Zod exact' (oz_zod_exact = 1). Takes effect at the next Play.</summary>
        [MenuItem(WrecksMenu)]
        static void ToggleWrecks()
        {
            bool on = PlayerPrefs.GetInt("oz_l17_wrecks", 1) != 1;
            PlayerPrefs.SetInt("oz_l17_wrecks", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ L17 harbour wrecks (deviation DV39) from the next Play: {(on ? "ON: 3 wrecks in the harbour block their tiles until destroyed" : "OFF: Zod's open harbour, no harbour wreck drawn")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }

        [MenuItem(WrecksMenu, true)]
        static bool ToggleWrecksCheck() { Menu.SetChecked(WrecksMenu, PlayerPrefs.GetInt("oz_l17_wrecks", 1) == 1); return true; }

        [MenuItem(M + "L17: hatches peek now (play)")]
        public static void Peek() { if (HarbourDetails.I) HarbourDetails.I.TestHatches(); }

        [MenuItem(M + "L02: everyone out now (play)")]
        public static void Out() { if (DesertCritters.I) DesertCritters.I.TestOut(); }

        [MenuItem(M + "L02: scare everyone (play)")]
        public static void Scare() { if (DesertCritters.I) DesertCritters.I.TestScare(); }

        [MenuItem(M + "L02: kill every critter that is out (play)")]
        public static void Kill() { if (DesertCritters.I) DesertCritters.I.TestKill(); }

        [MenuItem(M + "Status (play)", true)] [MenuItem(M + "L17: blast every detail in view (play)", true)] [MenuItem(M + "L17: hit every wreck once (play)", true)]
        [MenuItem(M + "L17: hatches peek now (play)", true)] [MenuItem(M + "L02: everyone out now (play)", true)] [MenuItem(M + "L02: scare everyone (play)", true)]
        [MenuItem(M + "L02: kill every critter that is out (play)", true)]
        static bool InPlay() => Application.isPlaying;
    }
}

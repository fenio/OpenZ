#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K350: the grenade look switch (<see cref="OzGrenadeLook.Variant"/>: 1 pineapple, 2 round band, 0 the old grenade) and the shading
    /// of the new models. Works in Play (the next frame draws the chosen look) and before Play: the choice is kept for this editor session
    /// (SessionState: it survives the domain reload on entering Play, and a new editor session starts again from the code's default, the
    /// look the built game draws). Report: the drawn look, its mesh, triangles and drawn size, in the Console.
    /// </summary>
    [InitializeOnLoad]
    public static class OzGrenadeLookMenu
    {
        const string Root = "OpenZ/V1.1/Grenade look/", Key = "OpenZ.K350.GrenadeLook", KeySmooth = "OpenZ.K350.GrenadeLookSmooth";

        static OzGrenadeLookMenu()
        {
            OzGrenadeLook.Variant = Mathf.Clamp(SessionState.GetInt(Key, OzGrenadeLook.Variant), 0, 2);
            OzGrenadeLook.Smooth = SessionState.GetBool(KeySmooth, OzGrenadeLook.Smooth);
        }

        static void Set(int v)
        {
            OzGrenadeLook.Variant = v; SessionState.SetInt(Key, v);
            Debug.Log($"[OpenZ] K350 grenade look -> {v} ({OzGrenadeLook.Names[v]})" + (Application.isPlaying ? "; drawn from the next frame" : "; drawn when Play starts"));
        }

        [MenuItem(Root + "1 Pineapple", false, 1)] static void Pineapple() => Set(OzGrenadeLook.Pineapple);
        [MenuItem(Root + "2 Round band", false, 2)] static void Round() => Set(OzGrenadeLook.RoundBand);
        [MenuItem(Root + "0 Old grenade (kept)", false, 3)] static void Old() => Set(OzGrenadeLook.Old);
        [MenuItem(Root + "1 Pineapple", true)] static bool PineappleCheck() { Menu.SetChecked(Root + "1 Pineapple", OzGrenadeLook.Variant == 1); return true; }
        [MenuItem(Root + "2 Round band", true)] static bool RoundCheck() { Menu.SetChecked(Root + "2 Round band", OzGrenadeLook.Variant == 2); return true; }
        [MenuItem(Root + "0 Old grenade (kept)", true)] static bool OldCheck() { Menu.SetChecked(Root + "0 Old grenade (kept)", OzGrenadeLook.Variant == 0); return true; }

        [MenuItem(Root + "Flat facets on the new models", false, 20)]
        static void Facets()
        {
            OzGrenadeLook.Smooth = !OzGrenadeLook.Smooth; SessionState.SetBool(KeySmooth, OzGrenadeLook.Smooth);
            Debug.Log("[OpenZ] K350 grenade shading -> " + (OzGrenadeLook.Smooth ? "smooth" : "flat facets"));
        }
        [MenuItem(Root + "Flat facets on the new models", true)] static bool FacetsCheck() { Menu.SetChecked(Root + "Flat facets on the new models", !OzGrenadeLook.Smooth); return true; }

        [MenuItem(Root + "Report (Play)", false, 40)]
        static void Report()
        {
            if (!Application.isPlaying) { Debug.Log($"[OpenZ] K350 grenade look chosen: {OzGrenadeLook.Variant} ({OzGrenadeLook.Names[Mathf.Clamp(OzGrenadeLook.Variant, 0, 2)]}); enter Play and throw a grenade for the drawn one"); return; }
            Debug.Log($"[OpenZ] K350 chosen {OzGrenadeLook.Variant}, drawn {OzGrenadeLook.Drawn}, look switches {OzGrenadeLook.Switches}, grenades thrown {GrenadeFx.Thrown}, in the air {(GrenadeFx.I ? GrenadeFx.I.Active : 0)}: " +
                      (OzGrenadeLook.Report == "" ? "no grenade view yet (throw one)" : OzGrenadeLook.Report));
        }
    }
}
#endif

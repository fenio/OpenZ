using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// The firing range of a gun on the ground = the units' range ring (AttackRangeRings), nothing of its own: the very same
    /// ring, code, material, dots, colours, lift and motion; only the radius is the gun's own range.
    /// A selected gun (own or not): AttackRangeRings plays the ring with the selection, as it does for every unit.
    /// The gun being placed (PlayerInput.Placing / PlaceTile): this component tells AttackRangeRings every frame which gun and
    /// where (AttackRangeRings.Placement): the spot's centre (where the rule will measure from) and the weapon table's
    /// attack_radius (the value ZWorld.CreateObject gives the gun). The ring bursts out of the spot, follows it in tile steps,
    /// holds while the placement lasts and fades when it ends. Under a spot that takes no gun its dots are dimmed (invalidDim),
    /// not hidden, so they do not blink while the pointer crosses rocks.
    /// View only. No new sound: the ring appears with the placement mode (ui_cannon_place_arm) or with a selection (its click).
    /// </summary>
    [DefaultExecutionOrder(77)]
    public class OzGunRange : MonoBehaviour   // the ring itself: AttackRangeRings (the units' ring)
    {
        public static OzGunRange I { get; private set; }
        /// <summary>Adds itself next to PlayerInput in every loaded scene (set false to add it by hand).</summary>
        public static bool AutoAttach = true;

        public PlayerInput input;

        [Tooltip("the ring's dots under a spot that takes no gun")] [Range(0f, 1f)] public float invalidDim = 0.4f;
        [Tooltip("seconds for the dots' dim to follow the spot (no blink)")] public float dimSeconds = 0.18f;

        /// <summary>The gun being placed shows its ring this frame (1) or not (0).</summary>
        public int RingsShown { get; private set; }

        string placeType; int placingFrames; float dim = 1f;
        ZGame game;

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var pi = Object.FindFirstObjectByType<PlayerInput>();
            if (pi && !pi.GetComponent<OzGunRange>()) pi.gameObject.AddComponent<OzGunRange>();
        }

        void Awake()
        {
            I = this;
            if (!input) input = GetComponent<PlayerInput>();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ public
        /// <summary>The weapon table's range of a gun type in px (0 = no such unit).</summary>
        public static int RadiusPx(ZWorld w, string type)
        {
            var u = w != null && w.S != null ? w.S.Unit(type) : null;
            return u != null ? u.attack_radius : 0;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!game) game = input && input.game ? input.game : ZGame.I;
            var rr = AttackRangeRings.I;
            if (!input || !game || game.World == null || !rr) { RingsShown = 0; return; }
            float dt = Time.unscaledDeltaTime;

            // ---- the gun being placed (from the second frame of the mode on: PlaceTile is this placement's own by then)
            var b = input.Placing;
            bool on = false; string placing = null; int rpx = 0; var spot = Vector3.zero;
            if (b != null && b.StoredCannons.Count > 0)
            {
                placingFrames++;
                string type = b.StoredCannons[Mathf.Clamp(input.PlaceIndex, 0, b.StoredCannons.Count - 1)];   // k252 (UI/OpenZProductionWindow.Buttons.cs, UI/OzReadyGuns.cs): the gun being placed
                rpx = RadiusPx(game.World, type);
                if (placingFrames >= 2 && rpx > 0)
                {
                    var t = input.PlaceTile;
                    float target = input.PlaceValid ? 1f : invalidDim;
                    dim = placingFrames == 2 || type != placeType ? target : Mathf.MoveTowards(dim, target, dt / Mathf.Max(0.01f, dimSeconds));
                    placeType = type;
                    spot = ZGame.PxToWorld(t.x * 16 + 16, t.y * 16 + 16);
                    placing = type; on = true;
                }
            }
            else placingFrames = 0;
            rr.Placement(on, game.player, placing, rpx, spot, dim);                                       // the units' ring (AttackRangeRings.Placement.cs)
            RingsShown = rr.PlacementShown ? 1 : 0;
        }

        // ------------------------------------------------------------------ audit (Play mode, for the proof steps)
        /// <summary>For the overlay audit (OzGunPlacementCheck): every gun on the map with the units' ring built for it and
        /// measured (AttackRangeRings.AuditGuns). violations = the number of lines that must be 0.</summary>
        public static string Audit(out int violations) => AttackRangeRings.AuditGuns(out violations);
    }
}

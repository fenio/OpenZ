using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: trodden grass round the robots' start camp (the campfire, the two loungers) and round their shuttle. Drawn by the
    /// ground shader (Shaders/JungleGround.shader, OZJLayers) from one small picture over that part of the map,
    /// Resources/OpenZ/Ground/L16_trodden_camp.png (a window of 32 x 24 m at 16 points per metre, west x 113, south z -248):
    ///   R the grass flattened into the transitional grass: that share of the grass weight goes to the verge layer (x Share), so the
    ///     ground's own height blend shows it in small leafy blotches of the drier grass (a sprinkle in the camp's middles),
    ///   G worn: the grass a little paler and browner, its leaves evened out (x Worn): soft patches over the camp's area and round
    ///     the ship, faint trails from the ship's flank to the fire and to the loungers, a slightly worn ring round the fire, and the
    ///     steps themselves (soft dabs along the trails).
    /// Only the grass and the verge change (never road, water, rock or mud); no earth, no gravel, no mud, no hard edge. Nothing on
    /// the low grass's rain hollows (the puddles stay as they are). Outside the window and on every other level: nothing (the
    /// globals are 0). Like the rest of the layers it gives way to the far picture when zoomed far out.
    /// Statics: Play can tune them (Apply() after a change).
    /// </summary>
    public static class OzL16TroddenCamp
    {
        public const string PictureRes = "OpenZ/Ground/L16_trodden_camp";
        /// <summary>On (1) or off (0); the share of the grass handed to the transitional grass where R is 1; how much of the worn look
        /// where G is 1; how far the ground's own height moves the worn edge (leafy, not a line).</summary>
        public static float Amount = 1f, Share = 0.40f, Worn = 0.75f, Edge = 0.90f;
        /// <summary>The worn grass: a tint on its stored colour (sRGB numbers, as the ground's own tints) and the share of it that goes
        /// to the flattened, drier colour <see cref="Flat"/> (the leaves' light and dark evened out).</summary>
        public static Vector3 Tint = new Vector3(1.16f, 1.08f, 0.86f);
        public static float FlatShare = 0.40f;
        public static Vector3 Flat = new Vector3(0.56f, 0.52f, 0.33f);
        /// <summary>The picture's window on the map: west x, south z, width, depth (m).</summary>
        public static Vector4 Window = new Vector4(113f, -248f, 32f, 24f);
        public static bool On { get; private set; }
        static Texture2D pic;
        static readonly int IdMask = Shader.PropertyToID("_OzTrodMask"), IdRule = Shader.PropertyToID("_OzTrodRule"), IdRect = Shader.PropertyToID("_OzTrodRect"),
            IdTint = Shader.PropertyToID("_OzTrodTint"), IdFlat = Shader.PropertyToID("_OzTrodFlat");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded;
            Apply();
        }

        static void Loaded(Scene s, LoadSceneMode m) => Apply();

        /// <summary>On level 16 the trodden grass at the camp and the shuttle, elsewhere none.</summary>
        public static void Apply()
        {
            bool l16 = ZGame.I && ZGame.I.level == "L16";
            if (l16 && !pic) pic = Resources.Load<Texture2D>(PictureRes);
            On = l16 && pic;
            Shader.SetGlobalTexture(IdMask, On ? pic : Texture2D.blackTexture);
            Shader.SetGlobalVector(IdRule, On ? new Vector4(Amount, Share, Worn, Edge) : Vector4.zero);
            Shader.SetGlobalVector(IdRect, new Vector4(Window.x, Window.y, 1f / Mathf.Max(Window.z, 0.01f), 1f / Mathf.Max(Window.w, 0.01f)));
            Shader.SetGlobalVector(IdTint, new Vector4(Tint.x, Tint.y, Tint.z, FlatShare));
            Shader.SetGlobalVector(IdFlat, new Vector4(Flat.x, Flat.y, Flat.z, 1f));
            if (l16) Debug.Log(On ? $"[OpenZ] L16 trodden grass at the camp on: picture {pic.width}x{pic.height}, amount {Amount}, share {Share}, worn {Worn}"
                                  : "[OpenZ] L16 trodden grass at the camp: the picture is missing (" + PictureRes + "), the grass stays as it is");
        }

        public static string Status() => $"[OzL16TroddenCamp] {(On ? "ON" : "off")} amount {Amount} share {Share} worn {Worn} edge {Edge} tint {Tint} flat {Flat} x{FlatShare} window {Window}";
    }
}

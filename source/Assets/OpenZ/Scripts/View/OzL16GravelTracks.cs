using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the gravel paths from the doors (every factory, the repair bays, the radars, both forts' gates) to the nearest
    /// main road. They are drawn by the ground shader (Shaders/JungleGround.shader, OZJLayers, after the height blend): a gravel
    /// picture of its own (grey pebbles of mixed sizes on pale earth, 160 points per metre like the ground's layers) is laid over
    /// the grass and the verge where the path picture says so, softly (the grass shows through a little), in loose drifts with
    /// pale bare-earth lanes between them, ragged overgrown edges with a brownish bare-earth margin, pebbles thinning out one by
    /// one into the grass and a few grass tufts inside. No wheel ruts, no specks. No road tile,
    /// no studs, no kerb, no rule: units, paths and the map are untouched.
    /// This sets the shader's globals on level 16 and takes them away on every other level:
    ///   the path picture (Resources/OpenZ/Ground/L16_gravel_tracks.png, 8 points per metre over the map's tiles: R the path,
    ///   G the pebble density, B the bare-earth margin), the gravel pictures (L16_gravel_stones.png: pebble colour, A = each pebble's rank;
    ///   L16_gravel_stones_aux.png: pebble cover, contact shadow, earth detail), the strength, the earth and verge colours (stored
    ///   values, as the ground's own tints) and the fade with distance (full under 0.075, none over 0.1 ground metres per screen
    ///   point, as the road wear: the standard zoom is fully in, nothing pops).
    /// Statics: Play can tune them (Apply() after a change).
    /// </summary>
    public static class OzL16GravelTracks
    {
        public const string PictureRes = "OpenZ/Ground/L16_gravel_tracks";
        public const string GravelRes = "OpenZ/Ground/L16_gravel_stones";
        public const string GravelAuxRes = "OpenZ/Ground/L16_gravel_stones_aux";
        /// <summary>Amount (0 = none), the path's strength in its middle (the rest is the grass showing through), the pebbles'
        /// strength, their contact shadow.</summary>
        public static float Amount = 1f, Strength = 0.72f, Pebbles = 0.80f, Shadow = 0.50f;
        public static Vector2 Far = new Vector2(0.075f, 0.1f);
        /// <summary>The path's edge: sharpness of the picture's soft ramp, how far the ground's own height moves it (leafy, not a line).</summary>
        public static Vector2 Edge = new Vector2(1.45f, 0.90f);
        /// <summary>Stored colours (the ground's own tint numbers, sRGB): the pale earth, the darker earth patches, the brownish bare-earth margin
        /// (VergeAmount: how much of it shows).</summary>
        public static Vector3 EarthLight = new Vector3(0.580f, 0.490f, 0.440f), EarthDark = new Vector3(0.470f, 0.390f, 0.340f), Verge = new Vector3(0.460f, 0.350f, 0.240f);
        public static float VergeAmount = 0.90f, GravelMetres = 12.8f;
        public static bool On { get; private set; }
        static Texture2D pic, gravel, gravelAux;
        static readonly int IdMask = Shader.PropertyToID("_OzTrackMask"), IdRule = Shader.PropertyToID("_OzTrackRule"), IdFar = Shader.PropertyToID("_OzTrackFar"),
            IdTex = Shader.PropertyToID("_OzGravelTex"), IdAux = Shader.PropertyToID("_OzGravelAux"), IdEarth = Shader.PropertyToID("_OzGravelEarth"),
            IdEarth2 = Shader.PropertyToID("_OzGravelEarth2"), IdVerge = Shader.PropertyToID("_OzGravelVerge");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded;
            Apply();
        }

        static void Loaded(Scene s, LoadSceneMode m) => Apply();

        /// <summary>On level 16 the gravel paths, elsewhere none.</summary>
        public static void Apply()
        {
            bool l16 = ZGame.I && ZGame.I.level == "L16";
            if (l16 && !pic) pic = Resources.Load<Texture2D>(PictureRes);
            if (l16 && !gravel) gravel = Resources.Load<Texture2D>(GravelRes);
            if (l16 && !gravelAux) gravelAux = Resources.Load<Texture2D>(GravelAuxRes);
            On = l16 && pic && gravel && gravelAux;
            Shader.SetGlobalTexture(IdMask, On ? pic : Texture2D.blackTexture);
            Shader.SetGlobalTexture(IdTex, On ? gravel : Texture2D.grayTexture);
            Shader.SetGlobalTexture(IdAux, On ? gravelAux : Texture2D.blackTexture);
            Shader.SetGlobalVector(IdRule, On ? new Vector4(Amount, Strength, Pebbles, Shadow) : Vector4.zero);
            Shader.SetGlobalVector(IdFar, new Vector4(Far.x, Far.y, Edge.x, Edge.y));
            Shader.SetGlobalVector(IdEarth, new Vector4(EarthLight.x, EarthLight.y, EarthLight.z, Mathf.Max(GravelMetres, 0.5f)));
            Shader.SetGlobalVector(IdEarth2, new Vector4(EarthDark.x, EarthDark.y, EarthDark.z, 0f));
            Shader.SetGlobalVector(IdVerge, new Vector4(Verge.x, Verge.y, Verge.z, VergeAmount));
            if (l16) Debug.Log(On ? $"[OpenZ] L16 gravel paths on: picture {pic.width}x{pic.height}, gravel {gravel.width}x{gravel.height} over {GravelMetres} m, amount {Amount}, strength {Strength}, pebbles {Pebbles}, shadow {Shadow}"
                                  : "[OpenZ] L16 gravel paths: a picture is missing (" + PictureRes + ", " + GravelRes + ", " + GravelAuxRes + "), none drawn");
        }

        public static string Status() => $"[OzL16GravelTracks] {(On ? "ON" : "off")} amount {Amount} strength {Strength} pebbles {Pebbles} shadow {Shadow} edge {Edge.x}/{Edge.y} earth {EarthLight} / {EarthDark} verge {Verge} x{VergeAmount} far {Far.x}-{Far.y}";
    }
}

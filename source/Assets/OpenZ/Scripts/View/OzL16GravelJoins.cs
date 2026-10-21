using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: where the gravel paths (OzL16GravelTracks) meet the roads. The ground shader (Shaders/JungleGround.shader: the gravel
    /// block in OZJLayers, and OZJStuds) reads one picture, Resources/OpenZ/Ground/L16_gravel_joins.png (8 points per metre over the
    /// map's tiles, exactly over the path picture):
    ///   R the spill of gravel and pale earth over the road's edge across a path's mouth (a ragged fan, thick at the edge, thinner
    ///     to its end, 0.25 to 1.1 m onto the asphalt) and the trodden band beside it on the grass side (no straight line at the edge),
    ///   G the pebbles on the asphalt (the path's own drifts in the spill, a few loose ones beyond it; never the big stones),
    ///   B the road's white studs taken off across the mouth (whole studs, plus a little margin each side), only there,
    ///   A the mouth zone on the asphalt (there R and G stand for the path picture).
    /// Everything else of the roads is unchanged; no rule, no road tile, the map file is untouched. This sets the shader's globals
    /// on level 16 and takes them away on every other level. The mouths are drawn only while the gravel paths are (their amount and
    /// their fade with distance); with the paths off, or this Amount 0, the studs come back too.
    /// Statics: Play can tune them (Apply() after a change).
    /// </summary>
    public static class OzL16GravelJoins
    {
        public const string PictureRes = "OpenZ/Ground/L16_gravel_joins";
        /// <summary>On (1) or off (0) for the whole thing: the spill AND the studs taken off (0 = the roads exactly as without it); the
        /// strength of the earth at a mouth, on both sides of the road's edge (the path's own middle is OzL16GravelTracks.Strength); the
        /// studs taken off while on (1 = all the picture says, 0 = every stud stays, the spill stays).</summary>
        public static float Amount = 1f, Strength = 0.85f, StudsOff = 1f;
        /// <summary>Stored colour (sRGB, as the ground's own tints) of the pale earth over the asphalt (the path's earth colours are
        /// set against the grass showing through; over the grey asphalt this one gives the same tone).</summary>
        public static Vector3 Dust = new Vector3(0.490f, 0.445f, 0.335f);
        public static bool On { get; private set; }
        static Texture2D pic;
        static readonly int IdMask = Shader.PropertyToID("_OzJoinMask"), IdRule = Shader.PropertyToID("_OzJoinRule"), IdEarth = Shader.PropertyToID("_OzJoinEarth");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded;
            Apply();
        }

        static void Loaded(Scene s, LoadSceneMode m) => Apply();

        /// <summary>On level 16 the path mouths, elsewhere none.</summary>
        public static void Apply()
        {
            bool l16 = ZGame.I && ZGame.I.level == "L16";
            if (l16 && !pic) pic = Resources.Load<Texture2D>(PictureRes);
            On = l16 && pic;
            Shader.SetGlobalTexture(IdMask, On ? pic : Texture2D.blackTexture);
            Shader.SetGlobalVector(IdRule, On ? new Vector4(Amount, Strength, 0f, StudsOff) : Vector4.zero);
            Shader.SetGlobalVector(IdEarth, new Vector4(Dust.x, Dust.y, Dust.z, 1f));
            if (l16) Debug.Log(On ? $"[OpenZ] L16 gravel path mouths on: picture {pic.width}x{pic.height}, amount {Amount}, strength {Strength}, studs off {StudsOff}"
                                  : "[OpenZ] L16 gravel path mouths: the picture is missing (" + PictureRes + "), the roads stay as they are");
        }

        public static string Status() => $"[OzL16GravelJoins] {(On ? "ON" : "off")} amount {Amount} strength {Strength} studs off {StudsOff} dust {Dust}"
            + $" (the gravel paths: {(OzL16GravelTracks.On ? "on" : "off")}, amount {OzL16GravelTracks.Amount})";
    }
}

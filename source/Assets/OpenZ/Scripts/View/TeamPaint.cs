using UnityEngine;

namespace OpenZ
{
    /// <summary>Z teams: the player is RED, the CPU is BLUE, neutral is GREY (there is never a green team).</summary>
    public enum Team { Neutral = 0, Red = 1, Blue = 2 }

    public static class TeamColors
    {
        // with highlights ~99,115,251): a deep warm brick red and a deep royal blue, lit naturally by TeamLit, and a slightly
        // warm grey for neutral. The same values everywhere: units, buildings, flags, zone markers and the UI.
        // (Zod's raw sprite keys 223,0,0 / 19,55,251 / 115,115,115 read neon on lit 3D models.)
        public static readonly Color Red = new Color32(176, 40, 30, 255);
        public static readonly Color Blue = new Color32(47, 79, 168, 255);
        public static readonly Color Neutral = new Color32(122, 116, 106, 255);

        // default 0 = red). VIEW ONLY: the player's side (sim Red) is drawn blue and the CPU (sim Blue) red on every unit,
        // building, flag, zone, minimap mark, portrait, HUD bar and team name; the sim, fort positions, parity and bot never
        // see it. Every team colour / sprite key / name goes through Shown(t); read once, set from the menu (next level load).
        static int swap = -1;
        public static bool Swap
        {
            get { if (swap < 0) swap = PlayerPrefs.GetInt("oz_team_blue", 0) != 0 ? 1 : 0; return swap == 1; }
            set { swap = value ? 1 : 0; PlayerPrefs.SetInt("oz_team_blue", swap); PlayerPrefs.Save(); }
        }
        /// <summary>The team whose COLOUR is drawn for sim team t (Red and Blue trade places when Swap is on).</summary>
        public static Team Shown(Team t) => !Swap || t == Team.Neutral ? t : t == Team.Red ? Team.Blue : Team.Red;
        /// <summary>"RED" / "BLUE" as displayed (neutral: "NEUTRAL").</summary>
        public static string Name(Team t) { var s = Shown(t); return s == Team.Red ? "RED" : s == Team.Blue ? "BLUE" : "NEUTRAL"; }
        /// <summary>Kit / portrait key of the displayed colour: "red" / "blue" / "neutral".</summary>
        public static string Key(Team t) { var s = Shown(t); return s == Team.Red ? "red" : s == Team.Blue ? "blue" : "neutral"; }
        static readonly System.Text.RegularExpressions.Regex ColourWord = new System.Text.RegularExpressions.Regex(@"\b(red|blue)(s?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>True when a voice / text line names a team colour (skipped or swapped while Swap is on).</summary>
        public static bool NamesColour(string line) => !string.IsNullOrEmpty(line) && ColourWord.IsMatch(line);
        /// <summary>Static UI text naming a colour ("BREAK INTO THE BLUE FORT"): RED and BLUE trade places while Swap is on.</summary>
        public static string Words(string s) => !Swap || string.IsNullOrEmpty(s) ? s : ColourWord.Replace(s, m =>
        {
            string w = m.Groups[1].Value, o = w.ToLowerInvariant() == "red" ? "blue" : "red";
            o = w == w.ToUpperInvariant() ? o.ToUpperInvariant() : char.IsUpper(w[0]) ? char.ToUpperInvariant(o[0]) + o.Substring(1) : o;
            return o + m.Groups[2].Value;
        });

        /// <summary>A copy of a kit sprite with its baked team art swapped (the top bar's RED FORCE / BLUE FORCE labels, emblems and
        /// edge strips): strongly red or blue pixels trade their R and B channels, greys / amber stay. Made once per sprite while Swap is on.</summary>
        public static Sprite SwapHues(Sprite s, System.Action<Color32[], Color32[], int, int> post = null)
        {
            if (!s || !Swap) return s;
            if (swapped.TryGetValue(s, out var done) && done) return done;
            var src = s.texture; int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active; Graphics.Blit(src, rt); RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = src.name + "_teamswap", filterMode = src.filterMode, wrapMode = src.wrapMode };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32(); var orig = post != null ? (Color32[])px.Clone() : null; int n = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; if (c.a < 8) continue;
                bool red = c.r > c.g * 1.5f + 8 && c.r > c.b * 1.5f + 8, blue = c.b > c.r * 1.3f + 8 && c.b > c.g * 1.15f + 6;
                if (!red && !blue) continue;
                px[i] = new Color32(c.b, c.g, c.r, c.a); n++;
            }
            post?.Invoke(orig, px, w, h);                                                                   // e.g. the top bar moves its baked words
            tex.SetPixels32(px); tex.Apply(false, true);
            var sp = Sprite.Create(tex, s.rect, new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height), s.pixelsPerUnit, 0, SpriteMeshType.FullRect, s.border);
            sp.name = s.name + "_teamswap"; swapped[s] = sp;
            Debug.Log($"[TeamColors] team colour swap: {s.name} {n} team pixels swapped");
            return sp;
        }
        static readonly System.Collections.Generic.Dictionary<Sprite, Sprite> swapped = new System.Collections.Generic.Dictionary<Sprite, Sprite>();

        public static Color Of(Team t) { t = Shown(t); return t == Team.Red ? Red : t == Team.Blue ? Blue : Neutral; }
        public static Color Paint(Team t) => Of(t);

        // rims, LINEAR (TeamPaint sets them with SetVector, no second sRGB -> linear step). Same hues as above (brick red,
        // royal blue), lighter so they read at 60-70 m. w of VividLinear: 1 = neutral, 2 = owned (0 = not set = v1 look).
        public static Vector4 VividLinear(Team t) => (t = Shown(t)) == Team.Red ? new Vector4(0.45f, 0.038f, 0.024f, 2f)
                                                    : t == Team.Blue ? new Vector4(0.035f, 0.14f, 0.60f, 2f) : new Vector4(0f, 0f, 0f, 1f);
        /// <summary>Team rim by day / night and the faint night glow of the panels (linear; red lifted: dark red vanishes at night).</summary>
        public static Vector4 GlowLinear(Team t) => (t = Shown(t)) == Team.Red ? new Vector4(0.80f, 0.075f, 0.045f, 1f)
                                                   : t == Team.Blue ? new Vector4(0.06f, 0.22f, 0.85f, 1f) : Vector4.zero;
        /// <summary>Ground team discs (TeamReadability, sRGB): brighter than the paint so the ring reads on sand and concrete.</summary>
        static readonly Color discRed = new Color32(228, 62, 44, 255), discBlue = new Color32(60, 128, 245, 255);
        /// <summary>Disc colour of sim team Red / Blue as displayed (Swap trades them).</summary>
        public static Color DiscRed => Swap ? discBlue : discRed;
        public static Color DiscBlue => Swap ? discRed : discBlue;
        public static readonly Color DiscEmpty = new Color(0.86f, 0.86f, 0.84f, 1f);

        public static Team Parse(string owner) =>
            owner == "red" ? Team.Red : owner == "blue" ? Team.Blue : Team.Neutral;
    }

    /// <summary>
    /// Sets the owner colour on every renderer below this object (OpenZ/TeamLit reads _TeamColor, linear).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class TeamPaint : MonoBehaviour
    {
        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        /// <summary>Robots only: their team paint is this much brighter, so an all-plate robot reads as its team colour by day
        /// instead of a dark shape (measured by day: the plates come out about halfway between the old plate and a tank's plate of
        /// the same team, never brighter than the tank). One number for every robot type and both teams; the hue is kept
        /// (the shader's lift toward white would grey it). Vehicles, guns and buildings are not touched.</summary>
        public static float RobotTeamGain = 1.55f;
        static readonly int TeamModeId = Shader.PropertyToID("_TeamMode");
        int robotBody = -1;                                                           // -1 = not looked at yet, 0 = no, 1 = yes
        bool plainPaint;
        float glowLevel = 1f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        /// <summary>1 = alive. A dead robot's glows fade through this (OzGlowFade): its glow parts, its emissive parts and the
        /// team glow of its paint are drawn at this share, and stay there through every later paint step of the death.</summary>
        public float GlowLevel { get => glowLevel; set { value = Mathf.Clamp01(value); if (Mathf.Approximately(glowLevel, value)) return; glowLevel = value; Apply(); } }
        /// <summary>A stage that shows robot models under its own lighting keeps the plain paint (no robot gain).</summary>
        public bool PlainPaint { get => plainPaint; set { if (plainPaint == value) return; plainPaint = value; Apply(); } }
        /// <summary>True when this paint belongs to a robot body: a bound robot unit, or any figure made from a robot model that is
        /// no unit (a driver in a hatch, a gunner, a figure boarding or leaving, a thrown crew, a body). Such a figure is known by
        /// what it paints: an animated skin whose material masks its armour plates (team modes 2 and 3). Vehicles, guns and
        /// buildings have no such skin.</summary>
        public bool IsRobotBody
        {
            get
            {
                if (plainPaint) return false;
                var unit = GetComponent<UnitView>();
                if (unit && unit.Obj != null) return unit.Obj.IsRobot;
                if (robotBody < 0)
                {
                    robotBody = 0;
                    foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (s.GetComponentInParent<TeamPaint>(true) != this) continue;
                        foreach (var m in s.sharedMaterials)
                            if (m && m.HasProperty(TeamModeId) && m.GetFloat(TeamModeId) > 1.5f) { robotBody = 1; break; }
                        if (robotBody == 1) break;
                    }
                }
                return robotBody == 1;
            }
        }
        [SerializeField] Team team = Team.Neutral;
        /// <summary>
        /// the khaki / orange field paint. TeamLit _OZFortPaint: the fort cream (linear) + w = the paint's reference luminance.</summary>
        [SerializeField] bool fortPalette;
        public static readonly Color FortCream = new Color32(214, 204, 184, 255);
        public const float FortCreamRefLum = 0.34f;
        public bool FortPalette { get => fortPalette; set { if (fortPalette == value) return; fortPalette = value; Apply(); } }
        MaterialPropertyBlock block;

        public Team Team
        {
            get => team;
            set { team = value; Apply(); }
        }

        void OnEnable() { robotBody = -1; Apply(); }
        void OnValidate() => Apply();

        public void Apply()
        {
            block ??= new MaterialPropertyBlock();
            TeamReadability.EnsureGlobals();                                          // teamcolor_v2
            Color c = TeamColors.Paint(team).linear;
            // Z's colours come from unlit 2D sprites; under 3D lighting the neutral 115 grey reads as charcoal, so the
            // neutral paint albedo is lifted to read as the same grey on screen (red/blue are saturated enough).
            if (team == Team.Neutral) { c *= 3.0f; c.a = 1; }   // x2.2 read khaki-brown under the warm key; toy-clean light warm grey
            float k = 1f - 0.78f * charred;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var host = r.GetComponentInParent<TeamPaint>(true);
                bool robot = (host ? host : this).IsRobotBody;
                if (robot && host != this && !IsRobotBody) continue;                   // a robot figure riding on this object (a gunner, a driver) keeps its own paint: never the gun's colour or burn
                for (int i = 0; i < mats.Length; i++)
                {
                    r.GetPropertyBlock(block, i);
                    block.SetColor(TeamColorId, robot ? new Color(c.r * k * RobotTeamGain, c.g * k * RobotTeamGain, c.b * k * RobotTeamGain, c.a * k) : c * k);
                    // teamcolor_v2: vivid accent colour + neutral / owned flag, team rim / night glow, this part's team panel
                    var viv = TeamColors.VividLinear(team);
                    block.SetVector(VividId, new Vector4(viv.x * k, viv.y * k, viv.z * k, viv.w));
                    block.SetVector(GlowId, TeamColors.GlowLinear(team) * (k * glowLevel));
                    PanelFor(r, mats[i], out var panelRect, out var panelY);
                    block.SetVector(PanelId, panelRect); block.SetVector(PanelYId, panelY);
                    var fc = FortCream.linear; bool mine = host == this;   /* not the gunner under the gun */
                    var fp = fortPalette && mine ? new Vector4(fc.r, fc.g, fc.b, FortCreamRefLum) : Vector4.zero;   // TG2
                    if (!fortPalette && mine && FieldKhaki(mats[i]))
                    {
                        var kc = PaintKhaki.TargetLinear; if (kc.a > 0f) fp = new Vector4(kc.r, kc.g, kc.b, FortCreamRefLum);
                    }
                    block.SetVector(FortPaintId, fp);
                    // a band at a fraction of their own mesh height (material _AccentFrom/_AccentTo)
                    var mat = mats[i];
                    if (mat && mat.HasProperty(AccentFromId))
                    {
                        bool owner = r.name.ToLower().Contains("owner") || mat.name.Contains("redGlow") || mat.name.Contains("OwnerGlow");
                        block.SetFloat(OwnerPartId, owner ? 1f : 0f);
                        var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null;
                        float from = mat.GetFloat(AccentFromId), to = mat.GetFloat(AccentToId);
                        if (mesh && from < 1.5f) { var b = mesh.bounds; block.SetFloat(AccentLoId, b.min.y + from * b.size.y); block.SetFloat(AccentHiId, b.min.y + to * b.size.y); }
                        else { block.SetFloat(AccentLoId, 1e6f); block.SetFloat(AccentHiId, -1e6f); }
                    }
                    if (mats[i] && mats[i].HasProperty(BaseColorId))
                    {
                        float kg = glowLevel < 1f && OzGlowFade.IsGlow(mats[i]) ? k * glowLevel : k;                // a dead robot's glow parts go dark
                        block.SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * new Color(kg, kg, kg, 1));
                    }
                    if (glowLevel < 1f && robot && mats[i] && mats[i].HasProperty(EmissionId)) block.SetColor(EmissionId, mats[i].GetColor(EmissionId) * glowLevel);   // robots only: a vehicle's lamps belong to its lights
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        /// <summary></summary>
        static bool FieldKhaki(Material m) => m && m.name.StartsWith("cannon_DV3_VehiclePalette");

        // ---------------------------------------------------------------- teamcolor_v2 team panels
        static readonly int VividId = Shader.PropertyToID("_OZTeamVivid"), GlowId = Shader.PropertyToID("_OZTeamGlow"),
                            PanelId = Shader.PropertyToID("_OZPanel"), PanelYId = Shader.PropertyToID("_OZPanelY"),
                            FortPaintId = Shader.PropertyToID("_OZFortPaint");
        /// <summary>Renderer (or material) name -> a team panel where the RTS camera looks: the part's mesh-space xz bounds inset by
        /// (ix, iz), from yFrom of its mesh height, edge softness (fraction of the mesh height), corner radius (fraction of the
        /// smaller half size), mode: 1 = a surface panel on the up-facing metal (turret tops, roofs of vehicles / cannons), -1 =
        /// paint mode (buildings: the model's own paint panels inside the rect above yFrom take the team colour: roof trim, fan
        /// ring, crane beam; neutral keeps them flat grey). Tuned on the real models seen from the game camera
        /// The jeep keeps its owner stripes, the radar its dish band, the fort its gun band + flag (forts are never neutral).</summary>
        static readonly (string key, float ix, float iz, float yFrom, float soft, float corner, float mode)[] PanelRules =
        {
            ("Tank_turret", 0.16f, 0.16f, 0.66f, 0.03f, 0.35f, 1f),
            ("HeavyTankTurret", 0.16f, 0.16f, 0.62f, 0.03f, 0.35f, 1f),
            ("Mech_body", 0.22f, 0.22f, 0.80f, 0.03f, 0.35f, 1f),
            ("MechRocketHousing_L", 0.08f, 0.08f, 0.50f, 0.04f, 0.25f, 1f),
            ("MechRocketHousing_R", 0.08f, 0.08f, 0.50f, 0.04f, 0.25f, 1f),
            ("MLRS_rack", 0.18f, 0.18f, 0.78f, 0.03f, 0.30f, 1f),
            ("CannonTurretYaw_Surface", 0.18f, 0.18f, 0.66f, 0.03f, 0.40f, 1f),
            ("m06_apc_mat", 0.60f, 0.45f, 0.80f, 0.02f, 0.30f, 1f),
            ("m07_crane_mat", 0.62f, 0.34f, 0.78f, 0.02f, 0.30f, 1f),
            ("m09_gatling_mat", 0.52f, 0.52f, 0.78f, 0.02f, 0.30f, 1f),          // split cannons: every part carries the whole model's
            ("m11_missile_cannon_mat", 0.42f, 0.42f, 0.72f, 0.02f, 0.30f, 1f),   // bounds (OpenZTurretSplit), so the rect is the same place
            ("m02_robot_factory_mat", 0.0f, 0.0f, 0.62f, 0.02f, 0.20f, -1f),     // buildings, paint mode: roof trim around the vents
            ("m03_vehicle_factory_mat", 0.0f, 0.0f, 0.62f, 0.01f, 0.20f, -1f),   // fan ring + roof edge strips
            ("m04_repair_station_mat", 0.0f, 0.0f, 0.72f, 0.02f, 0.20f, -1f),    // crane beam + its posts
        };

        static void PanelFor(Renderer r, Material m, out Vector4 rect, out Vector4 y)
        {
            rect = Vector4.zero; y = Vector4.zero;
            string rn = r.name, mn = m ? m.name : null;
            foreach (var p in PanelRules)
            {
                if (p.key != rn && p.key != mn) continue;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (!mesh) return;
                var b = mesh.bounds; var s = b.size;
                rect = new Vector4(b.center.x, b.center.z, s.x * 0.5f * (1f - p.ix), s.z * 0.5f * (1f - p.iz));
                y = new Vector4(b.min.y + p.yFrom * s.y, Mathf.Max(1e-4f, p.soft * s.y), p.corner, p.mode);
                return;
            }
        }
        static readonly int AccentFromId = Shader.PropertyToID("_AccentFrom"), AccentToId = Shader.PropertyToID("_AccentTo"),
                            OwnerPartId = Shader.PropertyToID("_OwnerPart"), AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi");
        float charred;
        /// <summary>0 = intact, 1 = burnt-out wreck / ruin (vehicle wrecks, destroyed buildings).</summary>
        public float Charred { get => charred; set { charred = Mathf.Clamp01(value); Apply(); } }
    }
}

// OpenZ (GPL-3.0)
// "COMMANDER ZOD" shows as usual; ~2 s after the window opens a small cartoon robot hand (the real BS-01 grunt glove in team red,
// holding an amber marker: antics_props_v1 sprites, 3 poses) slides in, scribbles a strike through COMMANDER (stroke by stroke,
// fill mask from strike_order.png, marker scribble), writes a hand-lettered GENERAL above it (11 strokes in writing order from
// general_order.png, a marker squeak on every pen-down after a hop), slides out; then Zod clears his throat ("Ahem.", zod_ahem,
// on his own voice source: his mouth curve plays, ducked like his lines) in a gap before his first word (the briefing's preroll
// is extended by exactly the joke's length, so nothing talks over anything). From then on this mission every plate reads
// GENERAL ZOD and every speaker label GEN ZOD (ZodComms.General); later transmissions never replay the joke.
// Play check: return OpenZ.ZodBriefing.DebugJoke();  (the timeline of the last joke, window-open relative)
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        const string JokeRes = "OpenZ/Zod/Joke/";
        public const string AhemId = "zod_ahem";
        // timeline (s from the window opening): glove in, strike, hop, GENERAL, out, Ahem
        public static float JokeGloveIn = 2.0f, JokeEnterS = 0.3f, JokeHopS = 0.15f, JokeExitS = 0.3f, JokeAhemGap = 0.12f, JokeAfterAhem = 0.2f;
        public static bool JokeEnabled = true;
        /// <summary>
        /// zigzag height as a share of its sprite's aspect height (it spans the middle of the caps, so COMMANDER still reads, crossed out);
        /// GENERAL's lowest ink row as a share of its sprite height (general_ink.png alpha bbox: y 48..240 of 288) and the clear gap (u)
        /// between that ink and the top of COMMANDER's caps.</summary>
        public static float JokeStrikeSquash = 0.6f, JokeGeneralInkBottom = 0.84f, JokeGeneralGap = 2.5f;

        [System.Serializable] class PathPt { public float t, x, y; public bool down; }
        [System.Serializable] class InkDef { public int[] size; public float duration_s; public float cap_height_px; public PathPt[] glove_path; }
        [System.Serializable] class Lettering { public InkDef general, strike; }
        [System.Serializable] class Pose { public string file; public float[] tip_px; }
        [System.Serializable] class Poses { public Pose enter, strike, write; }
        [System.Serializable] class GloveDef { public int[] size; public Poses poses; }

        sealed class Joke
        {
            public float start, strikeAt, generalAt, outAt, ahemAt, end; public bool ahemOn, ahemDone, written, finished, scribbled, firstSqueak;
            public Image glove, strike, general; public Material strikeMat, generalMat; public Sprite[] poses; public Vector2[] tips;
            public Rect strikeR, generalR; public float gloveSize; public Vector2 offRight; public InkDef sDef, gDef; public AudioClip ahem;
            public bool lastDown = true; public float lastSqueak = -9f; public readonly StringBuilder log = new StringBuilder();
        }
        Joke joke;
        static string lastJokeLog = "no joke yet";
        static readonly int ProgressId = Shader.PropertyToID("_Progress"), OrderId = Shader.PropertyToID("_OrderTex");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void JokeBoot() { SceneManager.sceneLoaded -= JokeScene; SceneManager.sceneLoaded += JokeScene; }
        static void JokeScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) ZodComms.General = false; }   // a new mission: he is a commander again until the joke

        /// <summary>Extra preroll for the start-of-mission briefing (0 when the joke cannot or should not play).</summary>
        float JokeLead(Kind kind)
        {
            if (kind != Kind.Briefing || !JokeEnabled || ZodComms.General) return 0f;
            var ahem = lib != null ? lib.Clip(AhemId) : null;
            if (!ahem || !Resources.Load<TextAsset>(JokeRes + "lettering") || !Resources.Load<Shader>(JokeRes + "OzInkReveal"))
            { Debug.LogWarning("[ZodCameo] GENERAL joke skipped: zod_ahem (sound batch) or the joke sprites / shader (idle antics) are not imported"); return 0f; }
            var L = JsonUtility.FromJson<Lettering>(Resources.Load<TextAsset>(JokeRes + "lettering").text);
            float ahemAt = JokeGloveIn + JokeEnterS + L.strike.duration_s + JokeHopS + L.general.duration_s + JokeExitS + JokeAhemGap;
            return ahemAt + ahem.length + JokeAfterAhem;
        }

        /// <summary>Builds the overlay on the open Zod window (the Name plate) and starts the clock (the perf's preroll clock).</summary>
        void JokeBegin(Perf p, float lead)
        {
            joke = null;
            if (lead <= 0f || comms == null || comms.Window == null || comms.NameLabel == null) return;
            var L = JsonUtility.FromJson<Lettering>(Resources.Load<TextAsset>(JokeRes + "lettering").text);
            var G = JsonUtility.FromJson<GloveDef>(Resources.Load<TextAsset>(JokeRes + "glove_marker").text);
            var sh = Resources.Load<Shader>(JokeRes + "OzInkReveal");
            var j = new Joke { start = p.t, sDef = L.strike, gDef = L.general, ahem = lib.Clip(AhemId) };
            j.strikeAt = JokeGloveIn + JokeEnterS; j.generalAt = j.strikeAt + L.strike.duration_s + JokeHopS;
            j.outAt = j.generalAt + L.general.duration_s; j.ahemAt = j.outAt + JokeExitS + JokeAhemGap; j.end = j.ahemAt + j.ahem.length;
            var win = comms.Window; var np = comms.NamePos;                                            // caps centre-left of "COMMANDER ZOD"
            float cap = 0.727f * comms.NameLabel.FontSize;
            var meas = Hud.Label(win, "_JokeMeasure", "COMMANDER", comms.NameLabel.FontSize, Color.clear, 0.1f);
            float wordW = meas.preferredWidth; Object.Destroy(meas.rectTransform.gameObject);
            // strike: stretched over COMMANDER with 4 % overhang each side, aspect kept, centred on the caps
            float sw = wordW * 1.08f, shh = sw * L.strike.size[1] / L.strike.size[0] * JokeStrikeSquash;
            j.strikeR = new Rect(np.x - wordW * 0.04f, np.y - shh * 0.5f, sw, shh);
            // GENERAL: caps 1.5x the label's, left edge 4 u left of the word, its lowest ink JokeGeneralGap above the word's caps top
            // (was: sprite top 0.8 x its height above the word top, so the ink bottom sat on COMMANDER's caps: text on text)
            float gs = 1.5f * cap / Mathf.Max(1f, L.general.cap_height_px), gw = L.general.size[0] * gs, gh = L.general.size[1] * gs;
            j.generalR = new Rect(np.x - 4f, np.y - cap * 0.5f - JokeGeneralGap - JokeGeneralInkBottom * gh, gw, gh);
            j.strike = InkImage(win, "JokeStrike", JokeRes + "strike_ink", JokeRes + "strike_order", sh, j.strikeR, out j.strikeMat);
            j.general = InkImage(win, "JokeGeneral", JokeRes + "general_ink", JokeRes + "general_order", sh, j.generalR, out j.generalMat);
            // the glove: 84 u (0.11 of its 768 px), tip on the pen point, in front of the ink
            j.gloveSize = 84f;
            j.poses = new[] { Resources.Load<Sprite>(JokeRes + "glove_marker_enter"), Resources.Load<Sprite>(JokeRes + "glove_marker_strike"), Resources.Load<Sprite>(JokeRes + "glove_marker_write") };
            float k = j.gloveSize / Mathf.Max(1, G.size[0]);
            j.tips = new[] { Tip(G.poses.enter, k), Tip(G.poses.strike, k), Tip(G.poses.write, k) };
            var grt = Hud.Node("JokeGlove", win); grt.Place(new OzRect(0, 0, j.gloveSize, j.gloveSize));
            j.glove = grt.gameObject.AddComponent<Image>(); j.glove.raycastTarget = false; j.glove.preserveAspect = true; j.glove.sprite = j.poses[0]; j.glove.enabled = false;
            j.offRight = new Vector2(comms.Layout.W + 30f, j.strikeR.y + j.strikeR.height * 0.5f);
            joke = j;
            j.log.Append($"window open = 0.00 s; glove in {JokeGloveIn:0.00}, strike {j.strikeAt:0.00}-{j.strikeAt + L.strike.duration_s:0.00}, GENERAL {j.generalAt:0.00}-{j.outAt:0.00}, out {j.outAt:0.00}-{j.outAt + JokeExitS:0.00}, ahem {j.ahemAt:0.00}-{j.end:0.00}, first word {lead + briefingPreroll:0.00} s");
            Debug.Log("[ZodCameo] GENERAL joke (start-of-mission briefing): " + j.log);
        }

        static Vector2 Tip(Pose p, float k) => p != null && p.tip_px != null && p.tip_px.Length >= 2 ? new Vector2(p.tip_px[0] * k, p.tip_px[1] * k) : Vector2.zero;

        static Image InkImage(RectTransform win, string name, string ink, string order, Shader sh, Rect r, out Material mat)
        {
            var rt = Hud.Node(name, win); rt.Place(new OzRect(r.x, r.y, r.width, r.height));
            var img = rt.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.sprite = Resources.Load<Sprite>(ink);
            mat = new Material(sh) { name = name + " (reveal)" };
            mat.SetTexture(OrderId, Resources.Load<Texture2D>(order)); mat.SetFloat(ProgressId, 0f);
            img.material = mat;
            return img;
        }

        /// <summary>Per frame from TickPerf, before the line clock moves (paused = frozen, like the rest of the transmission).</summary>
        void JokeTick(Perf p)
        {
            var j = joke; if (j == null || j.finished) return;
            if (p != perf || p.closing || p.fadeSpeed > 0f) { JokeFinish(p, "skipped / closed"); return; }
            float t = p.t - j.start;                                                                         // s since the window opened
            float sDur = j.sDef.duration_s, gDur = j.gDef.duration_s;
            j.strikeMat.SetFloat(ProgressId, Mathf.Clamp01((t - j.strikeAt) / sDur));
            j.generalMat.SetFloat(ProgressId, Mathf.Clamp01((t - j.generalAt) / gDur));
            // glove
            Vector2 tip; int pose;
            if (t < JokeGloveIn) { j.glove.enabled = false; tip = j.offRight; pose = 0; }
            else if (t < j.strikeAt) { float u = Ease((t - JokeGloveIn) / JokeEnterS, true); tip = Vector2.Lerp(j.offRight, PathAt(j.sDef, j.strikeR, 0f, out _), u); pose = 0; j.glove.enabled = true; }
            else if (t < j.strikeAt + sDur) { tip = PathAt(j.sDef, j.strikeR, t - j.strikeAt, out bool d); pose = 1; Squeak(j, t, d, false); }
            else if (t < j.generalAt)
            {   // hop: pen lifted from the end of the strike to the first letter
                float u = (t - j.strikeAt - sDur) / JokeHopS;
                tip = Vector2.Lerp(PathAt(j.sDef, j.strikeR, sDur, out _), PathAt(j.gDef, j.generalR, 0f, out _), Ease(u, false)) + Vector2.down * (-6f * Mathf.Sin(u * Mathf.PI));
                pose = 2; j.lastDown = false;
            }
            else if (t < j.outAt) { tip = PathAt(j.gDef, j.generalR, t - j.generalAt, out bool d); pose = 2; Squeak(j, t, d, true); }
            else if (t < j.outAt + JokeExitS)
            {
                if (!j.written) { j.written = true; ZodComms.General = true; comms.ApplyRank(); j.log.Append($" | GENERAL written at {t:0.00}"); }
                tip = Vector2.Lerp(PathAt(j.gDef, j.generalR, gDur, out _), j.offRight, Ease((t - j.outAt) / JokeExitS, false)); pose = 0;
            }
            else { j.glove.enabled = false; tip = j.offRight; pose = 0; if (!j.written) { j.written = true; ZodComms.General = true; comms.ApplyRank(); } }
            if (j.glove.enabled)
            {
                j.glove.sprite = j.poses[pose] ? j.poses[pose] : j.poses[0];
                var tl = tip - j.tips[pose];
                j.glove.rectTransform.anchoredPosition = new Vector2(tl.x, -tl.y);
            }
            // Ahem: on Zod's own voice source (his mouth curve "zod_ahem"), then the line's clip goes back before his first word
            if (!j.ahemOn && t >= j.ahemAt && j.ahem)
            {
                j.ahemOn = true; voice.Stop(); voice.clip = j.ahem; voice.volume = VoiceGain(); voice.Play();
                if (stage && stage.Rig) stage.Rig.voice = voice;
                OzSoundLog.Add("zod", AhemId, j.ahem.name, "GENERAL joke ahem", voice.volume);
                j.log.Append($" | ahem {t:0.00}");
            }
            if (j.ahemOn && !j.ahemDone && (t >= j.end + 0.02f || !voice.isPlaying))
            {
                j.ahemDone = true; voice.Stop(); voice.clip = p.clip; voice.time = 0f;
                j.log.Append($" | ahem ends {t:0.00}, first word due {-j.start:0.00}");
                JokeFinish(p, "complete");
            }
        }

        void Squeak(Joke j, float t, bool down, bool lettering)
        {
            if (!lettering && !j.scribbled)
            { j.scribbled = true; AudioDirector.UI("ui_zod_marker_scribble"); OzSoundLog.Add("zod", "ui_zod_marker_scribble", "sd_marker_scribble_*", "GENERAL joke strike"); j.log.Append($" | scribble {t:0.00}"); }
            if (lettering && down && !j.lastDown && t - j.lastSqueak > 0.3f)
            { AudioDirector.UI("ui_zod_marker_squeak"); OzSoundLog.Add("zod", "ui_zod_marker_squeak", "sd_marker_squeak_*", "GENERAL joke letter"); j.lastSqueak = t; j.log.Append($" | squeak {t:0.00}"); }
            if (lettering && !j.firstSqueak)
            { j.firstSqueak = true; AudioDirector.UI("ui_zod_marker_squeak"); OzSoundLog.Add("zod", "ui_zod_marker_squeak", "sd_marker_squeak_*", "GENERAL joke first letter"); j.lastSqueak = t; j.log.Append($" | squeak {t:0.00}"); }
            j.lastDown = down;
        }

        static Vector2 PathAt(InkDef d, Rect r, float t, out bool down)
        {
            var path = d.glove_path; down = false;
            if (path == null || path.Length == 0) return r.center;
            int i = 0; while (i < path.Length - 2 && path[i + 1].t <= t) i++;
            var a = path[i]; var b = path[Mathf.Min(i + 1, path.Length - 1)];
            float u = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 0f;
            down = a.down;
            float x = Mathf.Lerp(a.x, b.x, u) / d.size[0], y = Mathf.Lerp(a.y, b.y, u) / d.size[1];
            return new Vector2(r.x + x * r.width, r.y + y * r.height);
        }

        static float Ease(float u, bool outQuad) { u = Mathf.Clamp01(u); return outQuad ? 1f - (1f - u) * (1f - u) : u * u * (3f - 2f * u); }

        void JokeFinish(Perf p, string why)
        {
            var j = joke; if (j == null || j.finished) return;
            j.finished = true;
            if (j.glove) j.glove.enabled = false;
            if (!j.written) { j.written = true; ZodComms.General = true; comms?.ApplyRank(); }
            if (j.ahemOn && !j.ahemDone && voice) { voice.Stop(); voice.clip = p.clip; voice.time = 0f; }
            j.strikeMat.SetFloat(ProgressId, 1f); j.generalMat.SetFloat(ProgressId, 1f);              // the correction stays on the plate for this transmission
            lastJokeLog = j.log + $" | {why}";
            Debug.Log("[ZodCameo] GENERAL joke " + why + ": " + j.log);
        }

        /// <summary>Play check: the last joke's timeline (s from the window opening) and the current rank.</summary>
        public static string DebugJoke() => $"rank {(ZodComms.General ? "GENERAL" : "COMMANDER")}; {lastJokeLog}";
    }
}

// OpenZ (GPL-3.0) - the one-time plate for a slow computer.
// When a level has run under 45 frames a second for 20 s of real play, one message plate tells the player that lower settings
// exist: "RUNNING SLOW?  LOWER GRAPHICS QUALITY OR RESOLUTION IN OPTIONS." A click on it pauses the game and opens OPTIONS on the
// GRAPHICS tab. Rules: only frames in which the player has control are counted (no briefing, no ending, no pause, no menu, no
// window without focus; a frame longer than 0.25 s is left out); never in the first 2 minutes of control; once per install
// (oz_slow_tip_shown); not at all when GRAPHICS QUALITY is LOW and RESOLUTION is at its lowest step. A frame rate limit the
// player chose himself (or a test cap, or the heat guard's) is not a slow computer: the line is three quarters of that limit
// when that is lower than 45. The plate is one of the message plates: it is quiet while the build menu is open, hidden in
// MINIMAL UI, steps aside from the unit panel, and it is held back (not used up) while it could not be seen.
using UnityEngine;

namespace OpenZ
{
    public partial class OpenZHud
    {
        public const string SlowTipPref = "oz_slow_tip_shown";
        public const string SlowTipText = "RUNNING SLOW?  LOWER GRAPHICS QUALITY OR RESOLUTION IN OPTIONS.", SlowTipText2 = "CLICK TO OPEN GRAPHICS OPTIONS";
        /// <summary>Real seconds of control before the plate may come; the length of the measured stretch; the frame rate under which
        /// a computer counts as slow; how long the plate stays; the longest frame that is counted.</summary>
        public const float SlowTipQuietStart = 120f, SlowTipWindow = 20f, SlowTipFps = 45f, SlowTipHold = 12f, SlowTipHitch = 0.25f;
        public static readonly OzRect SlowTipRect = new OzRect(OzAnchor.TC, 620, 118, 680, 54);

        /// <summary>Checks: plates shown since the program started; the last measured average; why the plate was last held back.</summary>
        public static int SlowTipShown; public static float SlowTipAverage; public static string SlowTipHeldBy = "";
        /// <summary>Checks: real seconds of control counted in this level; the plate is on the screen; its middle in screen pixels.</summary>
        public float SlowTipControl => slowControl;
        public bool SlowTipShowing => slowTip != null && slowTip.used && slowTip.rt.gameObject.activeInHierarchy && slowTip.cg.alpha > 0.5f;
        public bool SlowTipHovered => slowTip != null && slowTip.used && slowTip.hot.Hovered;
        public Vector2 SlowTipPoint { get { var c = new Vector3[4]; slowTip.rt.GetWorldCorners(c); return (c[0] + c[2]) * 0.5f; } }
        /// <summary>Checks only: the two quiet minutes count as over (the measured stretch still has to be filled with real frames).</summary>
        public void SlowTipProofSkipQuietStart() { slowControl = Mathf.Max(slowControl, SlowTipQuietStart); }
        /// <summary>Checks only: this level may show the plate again (the saved mark is the check's own business).</summary>
        public void SlowTipProofReset() { slowDone = false; slowFilled = 0; slowIdx = 0; slowBucketT = 0f; slowBucketN = 0; slowControl = 0f; slowHeldLogged = ""; if (slowTip != null) { slowTip.used = false; slowTip.rt.gameObject.SetActive(false); } }

        ToastView slowTip; OzText slowTipSub;
        const int SlowBuckets = 20;
        readonly float[] slowT = new float[SlowBuckets]; readonly int[] slowN = new int[SlowBuckets];
        int slowIdx, slowFilled, slowBucketN; float slowBucketT, slowControl; bool slowDone; string slowHeldLogged = "";

        /// <summary>One call a frame from the message plates' refresh: measures, shows the plate when its rules allow, draws it.
        /// Returns the room the plate takes above the message plates (0 when it is not there).</summary>
        float SlowTipTick(float now, float y0)
        {
            if (!slowDone) SlowTipMeasure();
            if (slowTip == null || !slowTip.used) return 0f;
            if (!hudVisible) { slowTip.t0 += Time.unscaledDeltaTime; return 0f; }                           // hidden with the HUD (a pause): its 12 s wait
            float age = now - slowTip.t0;
            if (age > SlowTipHold + 0.4f || exited) { slowTip.used = false; slowTip.rt.gameObject.SetActive(false); return 0f; }
            float kIn = OzEase.OutCubic(OzMotion.T(slowTip.t0, OzMotion.Dur(OzMotion.Short)));
            float kOut = age > SlowTipHold ? OzMotion.T(slowTip.t0 + SlowTipHold, OzMotion.Dur(0.4f, true)) : 0f;
            slowTip.cg.alpha = kIn * (1f - kOut) * minK * (1f - plateQuiet);
            slowTip.rt.gameObject.SetActiveFast(minK > 0.001f);
            var r = SlowTipRect;
            slowTip.rt.Place(ClearOfUnitPanel(OzHudLayout.At(OzPanel.Messages, new OzRect(OzAnchor.TC, r.x, OzLayout.Toast.y + y0 - 12f * (1f - kIn), r.w, r.h))));
            bool hover = slowTip.hot.Hovered;                                                               // it lights under the pointer: it can be clicked
            slowTip.text.color = hover ? (Color)OzUi.AmberHi : (Color)OzUi.Ink;
            if (slowTipSub != null) slowTipSub.color = hover ? (Color)OzUi.Ink : (Color)OzUi.Amber;
            if (slowTip.bar && slowTip.bar.enabled != hover) slowTip.bar.enabled = hover;
            return r.h + 8f;
        }

        void SlowTipMeasure()
        {
            float dt = Time.unscaledDeltaTime;
            bool control = hudVisible && !exited && introDone && OzMotion.Now >= introAt && !W.GameOver && game != null && !game.paused && Time.timeScale > 0f
                           && !(Pause && Pause.IsOpen) && !Modal && !ZodBriefing.BriefingActive && !OpenZEnding.Running && !CaptureKit.CineActive && !OzSettings.TakeArg
                           && (Application.isFocused || Application.isEditor);                             // the editor's Game view runs without the focus
            if (!control || dt > SlowTipHitch || dt <= 0f) return;
            slowControl += dt; slowBucketT += dt; slowBucketN++;
            if (slowBucketT < 1f) return;
            slowT[slowIdx] = slowBucketT; slowN[slowIdx] = slowBucketN; slowIdx = (slowIdx + 1) % SlowBuckets; if (slowFilled < SlowBuckets) slowFilled++;
            slowBucketT = 0f; slowBucketN = 0;
            if (slowFilled < SlowBuckets || slowControl < SlowTipQuietStart) return;
            float t = 0f; int n = 0; for (int i = 0; i < SlowBuckets; i++) { t += slowT[i]; n += slowN[i]; }
            float avg = n / Mathf.Max(0.001f, t); SlowTipAverage = avg;
            int cap = Application.targetFrameRate;                                                          // the player's own limit, a test cap or the heat guard's is not a slow computer
            float line = cap > 0 ? Mathf.Min(SlowTipFps, cap * 0.75f) : SlowTipFps;
            if (avg >= line) return;
            string held = PlayerPrefs.GetInt(SlowTipPref, 0) == 1 ? "shown before on this computer"
                        : SlowTipNothingLeft() ? "GRAPHICS QUALITY is LOW and RESOLUTION is at its lowest step: nothing left to suggest" : null;
            if (held != null)
            {
                slowDone = true; SlowTipHeldBy = held;
                Debug.Log($"[OpenZ] slow computer tip held back ({held}): {avg:0.0} frames a second over the last {SlowTipWindow:0} s of play (line {line:0.0})");
                return;
            }
            string wait = BuildMenuShowing ? "the build menu is open" : minK < 0.999f ? "MINIMAL UI is on" : GameBoot.TestMode ? "test mode" : null;
            if (wait != null)
            {   // it could not be seen now: it waits, the mark is not set
                if (slowHeldLogged != wait) { slowHeldLogged = wait; SlowTipHeldBy = wait; Debug.Log($"[OpenZ] slow computer tip waits ({wait}): {avg:0.0} frames a second (line {line:0.0})"); }
                return;
            }
            SlowTipShow(avg, line);
        }

        static bool SlowTipNothingLeft()
        {
            if (OzQuality.Level != 2) return false;
            var nat = OzSettings.NativeResolution(); var steps = OzSettings.ResolutionSteps(nat.y);
            return steps.Length <= 1 || OzSettings.ResolutionHeight(nat.y) == steps[steps.Length - 1];
        }

        void SlowTipShow(float avg, float line)
        {
            if (slowTip == null)
            {
                slowTip = MakeToast("SlowTip", SlowTipRect, false);
                slowTip.text.At(OzLayout.ToastTextX, 19f, OzH.L, true, SlowTipRect.w - OzLayout.ToastTextX - 18f);
                slowTipSub = Hud.Label(slowTip.rt, "Sub", SlowTipText2, 10, OzUi.Amber, 0.2f, OzStyle.Plain).At(OzLayout.ToastTextX, 39f, OzH.L, true, SlowTipRect.w - OzLayout.ToastTextX - 18f);
                if (slowTip.key) slowTip.key.gameObject.SetActive(false);                                   // a click, not the last-message key
                if (slowTip.icon) slowTip.icon.rectTransform.anchoredPosition += new Vector2(0f, -(SlowTipRect.h - OzLayout.Toast.h) * 0.5f);
                slowTip.action = SlowTipClicked;
                slowTip.hot.Tooltip = "RUNNING SLOW?|Click: the game pauses and OPTIONS opens on the GRAPHICS tab.|";
            }
            slowDone = true; SlowTipShown++; SlowTipHeldBy = "";
            PlayerPrefs.SetInt(SlowTipPref, 1); PlayerPrefs.Save();                                          // once per install
            slowTip.used = true; slowTip.t0 = OzMotion.Now; slowTip.hold = SlowTipHold; slowTip.baseText = SlowTipText; slowTip.count = 1; slowTip.keep = false;
            Hud.Dress(slowTip.bg, "Panels/toast", null);
            slowTip.text.Set(SlowTipText);
            Hud.SetIcon(slowTip.icon, "alert", OzLayout.ToastIconSize); slowTip.icon.color = OzUi.Amber;
            slowTip.hot.Interactable = true;
            slowTip.cg.alpha = 0f; slowTip.rt.gameObject.SetActive(true); slowTip.rt.SetAsLastSibling();
            OzSfx.Ui("ui_toast");
            OzSoundLog.Add("hud", "toast", SlowTipText, "shown (slow computer, once)");
            Debug.Log($"[OpenZ] slow computer tip shown: {avg:0.0} frames a second over the last {SlowTipWindow:0} s of play (line {line:0.0}), {slowControl:0} s after the player got control; {OzQuality.Describe()}, resolution {OzSettings.ResolutionWord}");
        }

        void SlowTipClicked()
        {
            if (slowTip == null || !slowTip.used) return;
            slowTip.used = false; slowTip.rt.gameObject.SetActive(false);
            Debug.Log("[OpenZ] slow computer tip clicked: the game pauses, OPTIONS opens on the GRAPHICS tab");
            if (Pause) Pause.OpenOptions(OzSettingTips.TabGraphics);
        }
    }
}

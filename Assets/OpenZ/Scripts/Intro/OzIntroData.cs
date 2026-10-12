using System;
using UnityEngine;

namespace OpenZ.Intro
{
    /// <summary>
    /// storyboard page (y down); camera values are already Unity space.</summary>
    [Serializable] public sealed class IntroCam { public float[] pos, fwd, up; public float fov; }
    [Serializable] public sealed class IntroActor
    {
        public string robot, clip, prop; public float[] pos; public float yaw, clipTime, clipStart, emis; public string[] bsN; public float[] bsV;
        /// <summary>The talking loop after the one-shot (storyboard actor "then": P05C ic_seat_lean_talk from 0.55 s, blend 0.2 s).</summary>
        public string thenClip; public float thenStart = -1f, thenBlend = 0.2f;
    }
    [Serializable] public sealed class IntroBg { public string style, corner; public float[] c1, c2, centre, vanish; public float rays, dot; }
    [Serializable] public sealed class IntroSticker { public string kind, text, fill; public float at, rot, size; public float[] uv, tail; }
    [Serializable] public sealed class IntroSpace
    {
        public float[] planetC, planetEuler, heroCans, shStart, shEnd, shNose, shUp, cloudBase, cloudUp;
        public float planetR, ringTilt, heroScale, shScale, cloudAt;
    }
    [Serializable] public sealed class IntroPanel
    {
        public string id, render, trans, ease, screen, screenText;
        public float t0, dur, transDur, shCanopy, shLever, shPitch;
        public float[] poly, rect, shPos;
        public bool hasCam, alarm, flames, gearUp, cabinGreen, space, mound3;
        public int pileAlan, pileBrad;
        public IntroCam c0, c1;
        public IntroActor[] actors;
        public IntroSticker[] stickers;
        public string[] captions;
        public IntroBg bg;
        public IntroSpace spaceData;
        /// <summary>The panel's sfx cues (name, panel time): effects key to them (lift-off ignition / blast, the can burst).</summary>
        public string[] sfxN; public float[] sfxT;
        /// <summary>Sun discs in the sky for this panel: az, elev, size (deg), Blender convention (P01A).</summary>
        public float[] sunDiscs;
        public float T1 => t0 + dur;
        /// <summary>Panel time of the n-th cue with this name (0-based), or fallback.</summary>
        public float Cue(string name, int n, float fallback)
        {
            if (sfxN != null) for (int i = 0; i < sfxN.Length; i++) if (sfxN[i] == name && n-- == 0) return sfxT[i];
            return fallback;
        }
    }
    [Serializable] public sealed class IntroPage { public string id, kind; public float t0, t1; public IntroPanel[] panels; }
    [Serializable] public sealed class IntroHole { public float at, r; public float[] uv; }
    [Serializable] public sealed class IntroTimes { public float type, cps, type2, fly0, slam, creak, tip, land, fade, end, dt_anniversary; }
    [Serializable] public sealed class IntroEnding
    {
        public IntroTimes times; public float[] shots, logoSize, logoCentre; public string caption, captionAlt, caption2; public IntroHole[] holes;
    }
    [Serializable] public sealed class IntroTimeline
    {
        public float total, skipAt; public IntroPage[] pages; public IntroEnding ending; public string[] palNames; public float[] palCols;
        public Color Pal(string name, Color fallback)
        {
            if (palNames != null) for (int i = 0; i < palNames.Length; i++) if (palNames[i] == name) return new Color(palCols[i * 3] / 255f, palCols[i * 3 + 1] / 255f, palCols[i * 3 + 2] / 255f);
            return fallback;
        }
    }

    [Serializable] public sealed class IntroInst { public int[] v; public float[] p, f, u; public int Count => v == null ? 0 : v.Length; }
    [Serializable] public sealed class IntroPlace { public string type, prefab; public float[] pos; public float yaw, scale; }
    [Serializable] public sealed class IntroSun { public float[] dir, col; public float intensity, disc; }
    [Serializable] public sealed class IntroSet
    {
        public float[] shuttlePos, alanPile, bradPile, skyZenith, skyHorizon, skyHaze, moonPos;
        public float shuttleYaw, alanYaw, bradYaw, moonR;
        public IntroInst pileA, pileB, mS, mM, mL, loose;
        public IntroPlace[] mounds, rocks;
        public IntroSun[] suns;
    }

    public static class IntroMath
    {
        public static Vector3 V3(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
        public static Vector2 V2(float[] a) => a == null || a.Length < 2 ? Vector2.zero : new Vector2(a[0], a[1]);
        public static Color C255(float[] a, float alpha = 1f) => a == null || a.Length < 3 ? Color.white : new Color(a[0] / 255f, a[1] / 255f, a[2] / 255f, alpha);
        public static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
        public static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x; }
        public static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        /// <summary>Storyboard page px (1920 x 1080, y down) to a UI local position on a centred 1920 x 1080 page.</summary>
        public static Vector2 Page(float x, float y) => new Vector2(x - 960f, 540f - y);
    }

    /// <summary>
    /// </summary>
    public static class OzIntro
    {
        public const string Pref = "oz_intro";
        public const string SceneName = "Intro";
        public const string MenuScene = "MainMenu";
        /// <summary>The one switch for the parked intro: false = no PLAY INTRO row in OPTIONS and the intro never plays in a build,
        /// whatever oz_intro was saved as. Set it to true to bring the option back.</summary>
        public static readonly bool Offered = false;
        public static bool PlayedThisLaunch;
        public static bool Running;
        public static bool Enabled
        {
            get => Offered && PlayerPrefs.GetInt(Pref, 0) == 1;
            set { PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save(); Debug.Log($"[OpenZ Intro] INTRO CUTSCENE {(value ? "ON" : "OFF")} (oz_intro saved)"); }
        }
        public static bool CommandLineOff
        {
            get { foreach (var a in Environment.GetCommandLineArgs()) if (string.Equals(a, "-nointro", StringComparison.OrdinalIgnoreCase)) return true; return false; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PlayedThisLaunch = false; Running = false; }
    }
}

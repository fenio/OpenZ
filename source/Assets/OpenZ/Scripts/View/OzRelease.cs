using System;
using System.IO;

namespace OpenZ
{
    /// <summary>
    /// the scripting define OZ_RELEASE (BuildPlayerOptions.extraScriptingDefines: build only, never written into the project
    /// settings), and every dev path is compiled out with it: the -ozdev take flags (-cap72, -ozend, -ozfast, -ozautopilot) and the
    /// Ctrl+Alt+W / L ending keys (GameBoot.DevArg), the capture kit (-showcase,
    /// -demo, -cinematic, -maptop, -level, Ctrl+Shift+F12 and its F7-F11 keys), the -take session (OzSettings, HeatGuardRuntime)
    /// and the -audiometer / -audioprobe meters. The editor and the normal "OpenZ/Build Windows" exes keep all of them.
    /// The testing overlay on P (OzCheatSpawner) is NOT compiled out: it stays in the release exe for every player.
    /// DevRoot replaces the absolute job-root paths the proof scripts had hard-coded (they were compiled into the player dll).
    /// </summary>
    public static class OzRelease
    {
#if OZ_RELEASE
        public const bool On = true;
#else
        public const bool On = false;
#endif
        /// <summary>On as a non-constant read: the dev gates use this, so no "unreachable code" warnings in either flavour.</summary>
        public static bool IsRelease => On;
        static string devRoot;

        /// <summary>
        /// in: editor = three levels above the project folder (the editor's working directory), a dev exe = two levels above the exe
        /// folder (builds\&lt;name&gt;\OpenZ.exe), the release exe = a temp folder (its proof hooks never run).</summary>
        public static string DevRoot
        {
            get
            {
                if (devRoot != null) return devRoot;
                string r = null;
                try
                {
#if OZ_RELEASE
                    r = Path.Combine(Path.GetTempPath(), "OpenZ");
#elif UNITY_EDITOR
                    r = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".."));
#else
                    string exe = null;
                    try { exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; } catch (Exception) { }
                    if (string.IsNullOrEmpty(exe)) exe = Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
                    r = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe), "..", ".."));
#endif
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(r)) r = Path.Combine(Path.GetTempPath(), "OpenZ");
                return devRoot = r.Replace('\\', '/');
            }
        }

        /// <summary></summary>
        public static string Milestones(string sub) => DevRoot + "/work/records/milestones/" + sub;
    }

    /// <summary>
    /// build only (restored afterwards), so Application.version, the release folder release\OpenZ_&lt;Tag&gt;, SHA256SUMS.txt and the
    /// player README all come from Number + the build date.
    /// </summary>
    public static class OzVersion
    {
        public const string Product = "OpenZ";
        public const string Number = "1.0";

        /// <summary>"1.0_20261006": folder / file safe (release\OpenZ_1.0_20261006).</summary>
        public static string Tag(DateTime d) => Number + "_" + d.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>"OpenZ 1.0 (6 Oct 2026)".</summary>
        public static string Display(DateTime d) => Product + " " + Number + " (" + d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + ")";

        /// <summary>The running build's version text: "OpenZ 1.0 (6 Oct 2026)" in a release exe, "OpenZ 1.0 (dev)" elsewhere.</summary>
        public static string Current
        {
            get
            {
                string v = UnityEngine.Application.version ?? "";
                int u = v.IndexOf('_');
                if (u > 0 && DateTime.TryParseExact(v.Substring(u + 1), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var d))
                    return Display(d);
                return Product + " " + Number + " (dev)";
            }
        }

        // The menu label R<build>.<DDMM>. <build> = a 5-digit
        // TextAsset Resources/OpenZ/Menu/build_info.json, stamped by OzBuildStampHook (Editor) before every player build. The editor
        // shows the stored build number with today's DDMM. No plain date text anywhere in the label.
        [Serializable] sealed class BuildInfo { public int build = 61320; public string ddmm = ""; }
        static BuildInfo info;
        public const string BuildInfoResource = "OpenZ/Menu/build_info";

        static BuildInfo Info
        {
            get
            {
                if (info != null) return info;
                BuildInfo bi = null;
                try
                {
                    var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(BuildInfoResource);
                    if (ta != null) bi = UnityEngine.JsonUtility.FromJson<BuildInfo>(ta.text);
                }
                catch (Exception) { }
                return info = bi ?? new BuildInfo();
            }
        }

        /// <summary>The build number (5 digits) of the last stamped player build.</summary>
        public static int Build => Info.build;

        /// <summary>"R61321.0610": the main menu's version label (editor: the stored build + today's DDMM).</summary>
        public static string Label
        {
            get
            {
                string ddmm = Info.ddmm;
#if UNITY_EDITOR
                ddmm = DateTime.Now.ToString("ddMM", System.Globalization.CultureInfo.InvariantCulture);
#endif
                if (string.IsNullOrEmpty(ddmm) || ddmm.Length != 4) ddmm = DateTime.Now.ToString("ddMM", System.Globalization.CultureInfo.InvariantCulture);
                return "R" + Math.Max(0, Info.build).ToString("00000", System.Globalization.CultureInfo.InvariantCulture) + "." + ddmm;
            }
        }

        /// <summary>Editor: forget the cached build info (after the stamp hook rewrote it).</summary>
        public static void ResetInfo() { info = null; }
    }
}

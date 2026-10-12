using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// any BuildPlayer call) the build number in Resources/OpenZ/Menu/build_info.json goes up by one and the build day is stamped as
    /// The file is the ONE source; nothing else carries the build number.
    /// </summary>
    public sealed class OzBuildStampHook : IPreprocessBuildWithReport
    {
        public const string Path = "Assets/OpenZ/Resources/OpenZ/Menu/build_info.json";
        public int callbackOrder => 1;

        [Serializable] sealed class Info { public int build = 61320; public string ddmm = ""; }

        public void OnPreprocessBuild(BuildReport report) => Stamp("player build");

        /// <summary>Raises the build number by one and writes today's DDMM; returns the new label.</summary>
        public static string Stamp(string why)
        {
            Info i = null;
            try { if (File.Exists(Path)) i = JsonUtility.FromJson<Info>(File.ReadAllText(Path)); } catch (Exception) { }
            if (i == null) i = new Info();
            i.build = Mathf.Clamp(i.build + 1, 10000, 99999);
            i.ddmm = DateTime.Now.ToString("ddMM", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(Path, "{\"build\": " + i.build + ", \"ddmm\": \"" + i.ddmm + "\"}\n");
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            OzVersion.ResetInfo();
            string label = "R" + i.build.ToString("00000") + "." + i.ddmm;
            Debug.Log($"[OpenZ Version] {why}: build info stamped {label} ({Path})");
            return label;
        }
    }
}

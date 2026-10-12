#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// palm_wind (PalmWind.cs): Play-check helpers under OpenZ > Palms v3.
    ///   Play: wind status : one log line (palms hooked, wind materials, wind direction / strength / gust, the look values).
    ///   Play: frame the nearest palm (game camera h 60 / close h 22) : the RTS camera flies to the standing palm nearest the view
    ///       centre at the usual game height or at the closest RTS height, for the true-60 clips of the README.
    /// </summary>
    public static class OpenZPalmWindMenu
    {
        [MenuItem("OpenZ/Palms v3/Play: wind status")]
        public static void Status() { Debug.Log("[OpenZ PalmWind] " + PalmWind.Status()); }

        [MenuItem("OpenZ/Palms v3/Play: frame the nearest palm (game camera h 60)")]
        public static void FrameGame() { Frame(60f); }

        [MenuItem("OpenZ/Palms v3/Play: frame the nearest palm (close, h 22)")]
        public static void FrameClose() { Frame(22f); }

        static void Frame(float h)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ PalmWind] enter Play first"); return; }
            var cam = Camera.main; var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (!cam || !rts) { Debug.LogWarning("[OpenZ PalmWind] no main camera / RtsCamera"); return; }
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            DetailProp best = null; float bd = float.MaxValue;
            foreach (var p in DetailProp.All)
            {
                if (!p || p.Broken || p.kind != "palm") continue;
                float d = Vector3.Cross(ray.direction, p.transform.position - ray.origin).magnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (!best) { Debug.LogWarning("[OpenZ PalmWind] no standing palm"); return; }
            rts.FlyTo(best.transform.position); rts.ZoomTo(h);
            Debug.Log("[OpenZ PalmWind] RTS camera to " + best.id + " (" + best.name + ") at " + best.transform.position.ToString("F1") + ", h " + h + "; " + PalmWind.Status());
        }
    }
}
#endif

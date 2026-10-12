#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Begin() hands the view to the capture kit's cinematic mode (RTS camera off, HUD and cursor hidden, painted flyby sky on, the kit's
    /// own camera moves held), Pose() places the camera (position, look point, vertical fov, roll) and keeps it there every frame,
    /// Shoot() renders the main camera into a RenderTexture of the asked size (3840x2400 = 16:10 like the 2560x1600 loading screen,
    /// MSAA 4x + the camera's SMAA / post stack) after a settle of a few frames and writes a PNG. Freeze() pauses the world (timeScale 0) on a hero moment so the 4K render and its temporal AA
    /// settle on one instant; ArmFreeze() does it a set delay after the next sim event of a type (a tank shell fired, a bolt) seen on
    /// screen. End() gives the camera back. Session-only: nothing is saved to the player's settings.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class OzStillCam : MonoBehaviour
    {
        public static OzStillCam I { get; private set; }
        public static string Status = "idle";
        public static string LastFile = "";

        Vector3 pos, look; float fov = 40f, roll; bool posed;
        float fov0 = -1f; float ts0 = 1f; bool frozen;
        string armType; float armDelay; int armSeen; float armAt = -1f; bool armOnScreenOnly = true;

        static FieldInfo F(string n) => typeof(CaptureKit).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo SetModeM => typeof(CaptureKit).GetMethod("SetMode", BindingFlags.NonPublic | BindingFlags.Instance);
        static Camera Cam { get { var r = FindFirstObjectByType<RtsCamera>(); return r ? r.GetComponent<Camera>() : Camera.main; } }

        public static string Begin()
        {
            if (!Application.isPlaying) return "not in Play";
            if (!I) { var go = new GameObject("OzStillCam"); DontDestroyOnLoad(go); I = go.AddComponent<OzStillCam>(); }
            var ck = CaptureKit.I; if (!ck) return "no CaptureKit";
            var cineType = typeof(CaptureKit).GetNestedType("Cine", BindingFlags.NonPublic);
            SetModeM.Invoke(ck, new[] { System.Enum.Parse(cineType, "Flyover") });               // RTS camera off, HUD + cursor hidden, CineActive (flyby sky)
            F("hold").SetValue(ck, true);                                                          // the kit's own flyover path does not move the camera
            var pi = FindFirstObjectByType<PlayerInput>(); if (pi) pi.Selection.Clear();           // no selection rings / brackets in a still
            var cam = Cam; if (cam && I.fov0 < 0f) I.fov0 = cam.fieldOfView;
            if (cam) { I.pos = cam.transform.position; I.look = cam.transform.position + cam.transform.forward * 50f; }
            Status = "begun"; return "still cam on: RTS camera off, HUD hidden, CineActive " + CaptureKit.CineActive + ", fov0 " + I.fov0.ToString("0.0");
        }

        public static string Pose(Vector3 p, Vector3 lookAt, float vfov = 40f, float rollDeg = 0f)
        {
            if (!I) return "Begin() first";
            I.pos = p; I.look = lookAt; I.fov = vfov; I.roll = rollDeg; I.posed = true; I.Apply();
            var d = lookAt - p; float pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return $"pose {p.ToString("F1")} -> {lookAt.ToString("F1")}: pitch {pitch:0.0} deg down, yaw {yaw:0.0}, fov {vfov:0.0}, roll {rollDeg:0.0}";
        }

        void Apply()
        {
            var cam = Cam; if (!cam || !posed) return;
            var fwd = look - pos; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
            cam.fieldOfView = fov;
        }

        void LateUpdate()
        {
            Apply();
            if (armType != null && ZGame.I && ZGame.I.World != null)
            {
                var cam = Cam; var ev = ZGame.I.World.Events;
                for (int i = 0; i < ev.Count && armAt < 0f; i++)
                {
                    if (ev[i].type.ToString() != armType) continue;
                    var p = ZGame.PxToWorld(ev[i].x, ev[i].y);
                    try { var a = ZGame.I.World.GetAny(ev[i].objId); if (a != null) p = ZGame.I.CenterWorld(a); } catch { }
                    bool on = true;
                    if (cam && armOnScreenOnly) { var vp = cam.WorldToViewportPoint(p); on = vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f; }
                    if (!on) continue;
                    armSeen++; armAt = Time.unscaledTime + armDelay;
                }
                if (armAt >= 0f && Time.unscaledTime >= armAt) { armType = null; armAt = -1f; Freeze(true); Status = "frozen on event"; }
            }
        }

        /// <summary>Pause the world (true) or let it run again (false). Particles, units and shells hold still while frozen.</summary>
        public static string Freeze(bool on)
        {
            if (!I) return "Begin() first";
            if (on && !I.frozen) { I.ts0 = Time.timeScale; Time.timeScale = 0f; I.frozen = true; }
            else if (!on && I.frozen) { Time.timeScale = I.ts0 > 0f ? I.ts0 : 1f; I.frozen = false; }
            return "frozen " + I.frozen + ", timeScale " + Time.timeScale;
        }

        /// <summary>Freeze `delay` real seconds after the next on-screen sim event of this type (ZEventType name, e.g. MissileFired, Hitscan).</summary>
        public static string ArmFreeze(string evType, float delay, bool onScreenOnly = true)
        {
            if (!I) return "Begin() first";
            I.armType = evType; I.armDelay = delay; I.armAt = -1f; I.armOnScreenOnly = onScreenOnly; Status = "armed " + evType;
            return "armed: freeze " + delay.ToString("0.00") + " s after the next " + evType + (onScreenOnly ? " on screen" : "");
        }

        /// <summary>Render the camera at w x h into a PNG at `path` (the scaled in-game copies are made offline with a Lanczos filter).
        /// Runs over a few frames: poll Status ("shot OK ..." / "shot FAILED ...").</summary>
        public static string Shoot(string path, int w, int h, int settleFrames = 12)
        {
            if (!I) return "Begin() first";
            Status = "shooting " + Path.GetFileName(path);
            I.StartCoroutine(I.ShootCo(path, w, h, settleFrames));
            return "started " + path;
        }

        IEnumerator ShootCo(string path, int w, int h, int settle)
        {
            var cam = Cam; if (!cam) { Status = "shot FAILED: no camera"; yield break; }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "OpenZ_StillCapture" };
            rt.Create();
            var prevT = cam.targetTexture; cam.targetTexture = rt;
            for (int i = 0; i < Mathf.Max(2, settle); i++) yield return null;                    // temporal AA / auto exposure / volumetrics settle at this size
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); tex.Apply(false);
            RenderTexture.active = prev;
            cam.targetTexture = prevT; rt.Release(); Destroy(rt);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                LastFile = path;
                Status = $"shot OK {Path.GetFileName(path)} {w}x{h}";
            }
            catch (System.Exception e) { Status = "shot FAILED: " + e.Message; }
            finally { Destroy(tex); }
        }

        public static string End()
        {
            if (!I) return "not begun";
            Freeze(false);
            var cam = Cam; if (cam && I.fov0 > 0f) cam.fieldOfView = I.fov0;
            var ck = CaptureKit.I;
            if (ck)
            {
                F("hold").SetValue(ck, false);
                var cineType = typeof(CaptureKit).GetNestedType("Cine", BindingFlags.NonPublic);
                SetModeM.Invoke(ck, new[] { System.Enum.Parse(cineType, "Rts") });
            }
            I.posed = false; I.armType = null; Status = "ended";
            return "still cam off: RTS camera back, fov " + (cam ? cam.fieldOfView.ToString("0.0") : "-");
        }
    }
}
#endif

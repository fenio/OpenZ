// OpenZ (GPL-3.0) - Level 16: the commander's window stands in the jungle, not inside the base.
// Only the SETTING changes: the figures, their poses, the cameras, the cuts and the timing are the stage's own. On level 16 the
// room's walls (the wall behind the commander with its ribs, board and banner, and the far wall behind the two robots) are not
// drawn; round the stage stands one round picture of the level itself (taken on the swamp bank by the lounger set: the pool with
// its ruin, the fall, the far bank and the trees), far enough behind every camera of the stage that nothing of it can cut through a
// figure. It is a picture of light (no lamp of the stage reaches it), dimmed and cooled with the level's own night share, so the
// window's plate and the subtitles keep their contrast. The room's floor is drawn in a grass tone, the alarm beacons hang on posts.
// Every other level keeps the base interior; a stage that is used for another level afterwards gets its room back.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        public const string JungleLevel = "L16", JunglePicture = "OpenZ/Zod/textures/l16_window_jungle";
        /// <summary>Tuning: the picture's distance (m), the height of its horizon over the floor, the level bearing that stands
        /// behind the commander (degrees east of north), its strength by day and at full night, the night's colour.</summary>
        public static float JungleRadius = 14f, JungleEye = 1.75f, JungleHeading = 12f, JungleDay = 0.60f, JungleNight = 0.35f;   // night 0.13 -> 0.35 (run 15: at 0.13 the night window read as static)
        public static Color JungleNightTint = new Color(0.55f, 0.70f, 1f);
        const float JungleHalf = 0.5176f;                                   // the picture's half height on a cylinder of radius 1
        static readonly string[] JungleRoomParts =
        {
            "Wall", "Wainscot", "ConsoleLip", "AmberStrip", "BoardFrame", "BoardScreen", "BoardLampBar", "BannerRod", "Banner", "BannerEmblem",
            "FrontWall", "FrontWainscot", "FrontConsoleLip", "FrontAmberStrip",
        };

        GameObject jungleRoot; Material jungleMat, jungleFloorMat, jungleFloorWas, junglePostMat; Renderer jungleFloor;
        readonly List<GameObject> jungleHid = new List<GameObject>(), junglePosts = new List<GameObject>();
        bool jungleOn; float jungleNext;
        /// <summary>True while the stage shows the jungle round it (level 16).</summary>
        public bool JungleSettingOn => jungleOn;
        /// <summary>Counters for a check: parts of the room not drawn, posts under the alarm beacons.</summary>
        public int JungleHiddenParts => jungleHid.Count;
        public int JunglePostCount => junglePosts.Count;

        sealed class JungleTicker : MonoBehaviour { public ZodStage stage; void LateUpdate() { if (stage) stage.JungleTick(); } }

        /// <summary>Called whenever the stage is told its level.</summary>
        void JungleSetting(string level)
        {
            level = JungleLevelFor(level);                                                                // round 4: while level 16 is played, always the jungle (ZodStage.JungleHold.cs)
            bool want = level == JungleLevel;
            if (want == jungleOn) return;
            if (want)
            {
                var tex = Resources.Load<Texture2D>(JunglePicture);
                if (!tex) { Debug.LogWarning("[ZodCameo] the jungle picture is missing (Resources/" + JunglePicture + "): the base interior stays"); return; }
                jungleMat = Glow(new Color32(0, 0, 0, 255), Color.white, JungleDay);
                if (!jungleMat) return;
                jungleMat.SetTexture("_EmissionMap", tex); jungleMat.SetFloat("_Smoothness", 0f); jungleMat.SetFloat("_Cull", 0f);
                jungleRoot = new GameObject("JungleSetting") { layer = Layer };
                jungleRoot.transform.SetParent(transform, false);
                var mf = jungleRoot.AddComponent<MeshFilter>(); mf.sharedMesh = JungleMesh(JungleRadius, JungleEye, JungleHeading);
                var mr = jungleRoot.AddComponent<MeshRenderer>(); mr.sharedMaterial = jungleMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                mr.renderingLayerMask = 1u << LightLayerBit;                 // the stage's own drawing layer (without it the stage's camera does not draw it); it is a picture of light, beyond every lamp's reach
                jungleRoot.AddComponent<JungleTicker>().stage = this;
                jungleOn = true; jungleNext = 0f;
                JungleTick();
                Debug.Log($"[ZodCameo] level 16: the window stands in the jungle (the round picture {tex.width} x {tex.height} at {JungleRadius:0.#} m, the level's bearing {JungleHeading:0} behind the commander); {jungleHid.Count} parts of the room are not drawn");
            }
            else
            {
                foreach (var g in jungleHid) if (g) g.SetActive(true);
                jungleHid.Clear();
                foreach (var g in junglePosts) if (g) Destroy(g);
                junglePosts.Clear();
                if (jungleFloor && jungleFloorWas) jungleFloor.sharedMaterial = jungleFloorWas;
                jungleFloor = null; jungleFloorWas = null;
                if (jungleRoot)
                {
                    var mf = jungleRoot.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) Destroy(mf.sharedMesh);
                    Destroy(jungleRoot);
                }
                jungleRoot = null; jungleOn = false;
            }
        }

        /// <summary>Four times a second while the jungle is on: parts of the room that were built since (the far wall is built with
        /// the first shot of the two robots, the beacons with the first alarm) are taken out, the picture follows the level's hour.</summary>
        void JungleTick()
        {
            if (!jungleOn) return;
            if (jungleMat)
            {
                float n = Mathf.Clamp01(DetailKit.Night);
                var c = Color.Lerp(Color.white * JungleDay, JungleNightTint * JungleNight, n);
                jungleMat.SetVector("_EmissionColor", new Vector4(c.r, c.g, c.b, 1f));
            }
            JungleEveryFrame();                                                                           // round 4: the late parts built at once, the room looked for every frame while the window shows
            if (Time.unscaledTime < jungleNext) return;
            jungleNext = Time.unscaledTime + 0.25f;
            JungleHide(transform);
            var fs = transform.Find("FrontSet");
            if (fs)
            {
                JungleHide(fs);
                if (!jungleFloor)
                {
                    var fl = fs.Find("FrontFloor"); var r = fl ? fl.GetComponent<Renderer>() : null;
                    if (r)
                    {
                        if (!jungleFloorMat) jungleFloorMat = Lit(new Color32(44, 56, 30, 255), 0.08f, 0f);
                        if (jungleFloorMat) { jungleFloor = r; jungleFloorWas = r.sharedMaterial; r.sharedMaterial = jungleFloorMat; }
                    }
                }
            }
            var al = transform.Find("HqAlarm");
            if (al)
                for (int i = 0; i < al.childCount; i++)
                {
                    var b = al.GetChild(i);
                    if (!b.name.StartsWith("AlarmBeacon") || b.Find("JunglePost")) continue;
                    // the beacon's arm ran to the wall (0.35 behind it): in the jungle it hangs on a post that stands there
                    if (!junglePostMat) junglePostMat = Lit(new Color32(58, 44, 30, 255), 0.12f, 0f);
                    float top = b.localPosition.y + al.localPosition.y + 0.12f;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "JunglePost"; Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(b, false);
                    go.transform.localPosition = new Vector3(0f, 0.12f - top * 0.5f, -0.37f); go.transform.localScale = new Vector3(0.09f, Mathf.Max(0.5f, top), 0.09f);
                    Dress(go, junglePostMat, false);
                    junglePosts.Add(go);
                }
        }

        void JungleHide(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                string n = c.name;
                if (n.StartsWith("Rib") || n.StartsWith("FrontRib") || System.Array.IndexOf(JungleRoomParts, n) >= 0) { c.gameObject.SetActive(false); jungleHid.Add(c.gameObject); }
            }
        }

        /// <summary>The round picture's wall, seen from inside: bearing 0 of the stage is its +z, the commander's back wall lies at
        /// bearing 180; the picture's first column lies 15 degrees left of the level's north. Above and below the picture its top
        /// and bottom rows run on (the sky, the ground), so no camera of the stage can look past it.</summary>
        static Mesh JungleMesh(float R, float eye, float heading)
        {
            const int N = 96;
            var v = new Vector3[(N + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[N * 6];
            float hh = JungleHalf * R;
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                float u = (a * Mathf.Rad2Deg - 180f + heading + 15f) / 360f;
                var p = new Vector3(Mathf.Sin(a) * R, eye, Mathf.Cos(a) * R);
                v[i * 2] = p + Vector3.down * (hh * 3f); v[i * 2 + 1] = p + Vector3.up * (hh * 3f);
                uv[i * 2] = new Vector2(u, -1f); uv[i * 2 + 1] = new Vector2(u, 2f);
            }
            for (int i = 0; i < N; i++)
            {
                int a0 = i * 2, k = i * 6;
                t[k] = a0; t[k + 1] = a0 + 1; t[k + 2] = a0 + 2; t[k + 3] = a0 + 2; t[k + 4] = a0 + 1; t[k + 5] = a0 + 3;
            }
            var m = new Mesh { name = "ZodStage jungle setting", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = uv; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}

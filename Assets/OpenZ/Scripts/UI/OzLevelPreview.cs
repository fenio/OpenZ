using System;
using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Video;

namespace OpenZ
{
    /// <summary>One playable level as the front end shows it (guide 9.15 card data).</summary>
    public sealed class OzLevelDef
    {
        public readonly string Id, Scene, Number, Name, Mood, Wordmark, Objective, Goal;
        public readonly string[] FallbackChips;
        public OzLevelDef(string id, string scene, string number, string name, string mood, string wordmark, string objective, string goal, string[] chips)
        { Id = id; Scene = scene; Number = number; Name = name; Mood = mood; Wordmark = wordmark; Objective = objective; Goal = goal; FallbackChips = chips; }

        /// <summary>OpenZ ships exactly two levels: Z level 2 (desert) and Z level 17 "Car Park" (city).</summary>
        public static readonly OzLevelDef[] All =
        {
            new OzLevelDef("L02", "L02_Desert", "02", "DESERT", "DAY · DUST HAZE", "Wordmarks/mask_desert",
                "CAPTURE THE FLAGS TO TAKE TERRITORY. BREAK INTO THE BLUE FORT TO WIN.", "Capture the flags. Break into the blue fort.",
                new[] { "9 ZONES", "7 FLAGS", "2 FORTS", "72×90 TILES" }),
            new OzLevelDef("L17", "L17_CarPark", "17", "CAR PARK", "NIGHT · CANAL FOG", "Wordmarks/mask_carpark",
                "CAPTURE THE FLAGS TO TAKE TERRITORY. BREAK INTO THE BLUE FORT TO WIN.", "Capture the flags. Break into the blue fort.",
                new[] { "16 ZONES", "14 FLAGS", "2 FORTS", "110×128 TILES" }),
        };
    }

    /// <summary>
    /// Card facts and the minimap inset, read from the level's own Zod map (Resources/OpenZ/Maps/&lt;id&gt;_grid): zone,
    /// flag and fort counts, map size, and a small tactical map at the start ownership (terrain palette and zone tint of
    /// OpenZMinimap, forts / buildings as footprints, flags as dots). Built once per level; any failure keeps the fallback chips.
    /// </summary>
    public sealed class OzLevelInfo
    {
        public int Zones = -1, Flags = -1, Forts = -1, Width, Height;
        public Texture2D Mini;
        public bool Ok => Zones >= 0;

        public string[] Chips(OzLevelDef d) => !Ok ? d.FallbackChips : new[] { $"{Zones} ZONES", $"{Flags} FLAGS", $"{Forts} FORTS", $"{Width}×{Height} TILES" };

        public static OzLevelInfo Load(string level)
        {
            var info = new OzLevelInfo();
            try
            {
                var st = Resources.Load<TextAsset>("OpenZ/default_settings");
                var map = Resources.Load<TextAsset>($"OpenZ/Maps/{level}_grid");
                if (st == null || map == null) { Debug.LogWarning($"[OpenZ Menu] {level}: map or settings missing in Resources, card uses its fallback facts"); return info; }
                var w = ZWorld.Create(st.text, map.text, 1);
                info.Width = w.Map.width; info.Height = w.Map.height;
                info.Zones = w.Zones.Count; info.Flags = w.Flags.Count;
                int forts = 0;
                foreach (var o in w.Objects) if (o.Kind == ZKind.Building && (o.Type == "fort_front" || o.Type == "fort_back")) forts++;
                info.Forts = forts;
                info.Mini = Bake(w, level);
            }
            catch (Exception e) { Debug.LogWarning($"[OpenZ Menu] {level}: could not read the map ({e.Message}), card uses its fallback facts"); info.Zones = -1; }
            return info;
        }

        static Color32 Mul(Color32 c, float f) => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);
        static Color32 Mix(Color32 a, Color c, float t) => new Color32((byte)Mathf.Lerp(a.r, c.r * 255f, t), (byte)Mathf.Lerp(a.g, c.g * 255f, t), (byte)Mathf.Lerp(a.b, c.b * 255f, t), 255);
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 1023) / 1023f; } }

        /// <summary>Same palette and zone tint as the in-game minimap (OpenZMinimap.Palette / RebuildZones), 3 texels per tile.</summary>
        static Texture2D Bake(ZWorld W, string level)
        {
            const int k = 3; const float zoneTint = 0.3f;
            int mw = W.Map.width, mh = W.Map.height, tw = mw * k, th = mh * k;
            bool city = level == "L17";
            Color32 ground = city ? new Color32(86, 86, 84, 255) : new Color32(158, 120, 80, 255), road = city ? new Color32(118, 116, 110, 255) : new Color32(126, 124, 118, 255),
                    water = city ? new Color32(92, 104, 58, 255) : new Color32(78, 168, 160, 255), deep = city ? new Color32(70, 82, 44, 255) : new Color32(56, 128, 128, 255),
                    cliff = city ? new Color32(172, 168, 158, 255) : new Color32(190, 136, 86, 255), cliffDark = city ? new Color32(138, 134, 126, 255) : new Color32(158, 106, 64, 255);
            var px = new Color32[tw * th];
            int Idx(int x, int yTop) => (th - 1 - yTop) * tw + x;
            var zoneOf = new int[mw * mh];
            for (int ty = 0; ty < mh; ty++) for (int tx = 0; tx < mw; tx++) zoneOf[ty * mw + tx] = W.GetZoneIndex(tx * 16 + 8, ty * 16 + 8);
            int ZoneAt(int tx, int ty) => tx < 0 || ty < 0 || tx >= mw || ty >= mh ? -1 : zoneOf[ty * mw + tx];
            bool Solid(int tx, int ty) => tx < 0 || ty < 0 || tx >= mw || ty >= mh || W.Map.ClassAt(tx, ty) == ZClass.Impassable;
            for (int ty = 0; ty < mh; ty++)
                for (int tx = 0; tx < mw; tx++)
                {
                    var cls = W.Map.ClassAt(tx, ty); bool solid = cls == ZClass.Impassable;
                    Color32 c = cls == ZClass.Road ? road : cls == ZClass.Water ? water : cls == ZClass.DeepWater ? deep : solid ? cliff : ground;
                    float n = 0.95f + 0.1f * Hash(tx, ty);
                    bool eN = solid && !Solid(tx, ty - 1), eS = solid && !Solid(tx, ty + 1), eW = solid && !Solid(tx - 1, ty), eE = solid && !Solid(tx + 1, ty);
                    int z = zoneOf[ty * mw + tx]; var owner = z >= 0 ? W.Zones[z].Owner : Team.Neutral;
                    bool zN = ZoneAt(tx, ty - 1) != z, zS = ZoneAt(tx, ty + 1) != z, zW = ZoneAt(tx - 1, ty) != z, zE = ZoneAt(tx + 1, ty) != z;
                    for (int sy = 0; sy < k; sy++)
                        for (int sx = 0; sx < k; sx++)
                        {
                            Color32 q = c;
                            if (solid && !city && (ty * k + sy) % 3 == 0) q = cliffDark;
                            if (city && (sx == 0 || sy == 0) && cls == ZClass.Ground) q = Mul(q, 0.9f);
                            float f = n;
                            if ((eN && sy == 0) || (eS && sy == k - 1) || (eW && sx == 0) || (eE && sx == k - 1)) f *= 0.62f;
                            q = Mul(q, f);
                            if (owner != Team.Neutral)
                            {
                                bool border = (zN && sy == 0) || (zS && sy == k - 1) || (zW && sx == 0) || (zE && sx == k - 1);
                                q = border ? Mix(q, OzUi.Mark(owner), 0.85f) : Mix(q, TeamColors.Of(owner), zoneTint);
                            }
                            px[Idx(tx * k + sx, ty * k + sy)] = q;
                        }
                }
            foreach (var o in W.Objects)                                                                  // building footprints: forts / factories / radar / repair
            {
                if (o.Kind != ZKind.Building || o.Type.StartsWith("bridge")) continue;
                Color32 bc = o.Owner == Team.Neutral ? new Color32(200, 194, 180, 255) : (Color32)OzUi.Mark(o.Owner);
                int x0 = o.LocX * k / 16, y0 = o.LocY * k / 16, x1 = (o.LocX + o.WidthPix) * k / 16, y1 = (o.LocY + o.HeightPix) * k / 16;
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(th, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(tw, x1); x++)
                        px[Idx(x, y)] = (x == x0 || y == y0 || x == x1 - 1 || y == y1 - 1) ? Mul(bc, 0.55f) : bc;
            }
            foreach (var f in W.Flags)                                                                    // flags: INK dot with a dark rim (owned: team mark)
            {
                int cx = (f.LocX + 8) * k / 16, cy = (f.LocY + 8) * k / 16;
                Color32 fc = f.Owner == Team.Neutral ? (Color32)OzUi.Ink : (Color32)OzUi.Mark(f.Owner);
                for (int y = cy - 2; y <= cy + 2; y++)
                    for (int x = cx - 2; x <= cx + 2; x++)
                        if (x >= 0 && y >= 0 && x < tw && y < th) px[Idx(x, y)] = Mathf.Abs(x - cx) == 2 || Mathf.Abs(y - cy) == 2 ? new Color32(11, 13, 12, 255) : fc;
            }
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, name = "OpenZ_MenuMini_" + level };
            tex.SetPixels32(px); tex.Apply(true, false);
            return tex;
        }
    }

    /// <summary>
    /// Animated level preview (guide 9.14 / 9.15): a muted VideoPlayer rendering into a RenderTexture, from
    /// Resources/OpenZ/Menu/&lt;id&gt;_preview (VideoClip) or StreamingAssets/menu_&lt;id&gt;.mp4; the still
    /// Resources/OpenZ/Menu/&lt;id&gt;_preview (Texture2D) is the poster frame and the fallback (slow Ken Burns pan, see
    /// <see cref="KenBurns"/>). A cheap two-step downsample of the current frame gives the blurred level-select backdrop.
    /// </summary>
    public sealed class OzLevelPreview
    {
        /// <summary>Where the poster sits in the clip; hovering a card starts the video from here.</summary>
        public const double PosterTime = 5.0;
        public readonly string Level;
        public VideoPlayer Player { get; private set; }
        public RenderTexture Frame { get; private set; }
        public Texture Poster { get; private set; }
        public bool HasVideo { get; private set; }
        public bool Prepared { get; private set; }
        public bool Failed { get; private set; }
        RenderTexture blurA, blurB; long blurredFrame = -2; bool posterBlurred;
        float playCalledAt = -10f;
        public string LastError { get; private set; }
        float prepareStartAt = -1f;
        public string StillReason()
        {
            if (!HasVideo) return "no video clip (Resources OpenZ/Menu/" + Level + "_preview*, StreamingAssets menu_" + Level + ".mp4)";
            if (Failed) return "VideoPlayer error: " + LastError;
            if (Player == null) return "no player";
            string c = Player.clip ? Player.clip.name + " " + Player.clip.width + "x" + Player.clip.height + " " + Player.clip.frameRate.ToString("0.##") + " fps" : Player.url;
            if (!Prepared) return $"{c}: not prepared {Time.unscaledTime - prepareStartAt:0.0} s after Prepare()";
            if (!Player.isPlaying) return $"{c}: prepared but not playing";
            if (Player.frame < 0) return $"{c}: playing but no frame decoded {Time.unscaledTime - playCalledAt:0.0} s after Play (decoder / codec / H.264 level?)";
            return $"{c}: frame {Player.frame}, within the 0.12 s grace after a seek";
        }
        readonly List<VideoClip> clips = new List<VideoClip>(); int clipIx;
        static readonly System.Random Rng = new System.Random();                                          // seeded from the clock: a new order at every start
        /// <summary>The flybys in their play order (names), for the log.</summary>
        public string ClipOrder { get { var sb = new System.Text.StringBuilder(); foreach (var c in clips) { if (sb.Length > 0) sb.Append(" > "); sb.Append(c.name); } return sb.ToString(); } }
        /// <summary>A new random play order; notFirst (the flyby that just played) never opens it.</summary>
        void Shuffle(VideoClip notFirst)
        {
            for (int i = clips.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); var c = clips[i]; clips[i] = clips[j]; clips[j] = c; }
            if (clips.Count > 1 && clips[0] == notFirst) { clips[0] = clips[clips.Count - 1]; clips[clips.Count - 1] = notFirst; }
        }
        public int ClipCount => clips.Count;
        public int ClipIndex => clipIx;

        /// <summary>Loads the level's next flyby into the (hidden, paused) player, prepared for its next turn in the menu.</summary>
        public void NextClip()
        {
            if (!VideoOk || clips.Count < 2) { Pause(); return; }
            if (++clipIx >= clips.Count) { Shuffle(clips[clips.Count - 1]); clipIx = 0; }   // a new random order every round, never the same flyby twice in a row
            Player.Stop(); Player.clip = clips[clipIx]; ApplyAudio(); Prepared = false; prepareStartAt = Time.unscaledTime; Player.Prepare();
            Debug.Log($"[OpenZ Menu] flyby {Level} next: #{clipIx + 1}/{clips.Count} {clips[clipIx].name} ({clips[clipIx].width}x{clips[clipIx].height})");
        }

        /// <summary>How loud a flyby's own sound track plays in the menu (1 = the clip's full level), before the effects volume setting.</summary>
        public const float MenuFlybyVolume = 0.22f;
        /// <summary>The flybys are heard quietly in the menu: the clip's own sound track (battle and world sounds) at a low level,
        /// scaled by the effects volume setting. Silent when the clip has no sound track. Call before Prepare.</summary>
        void ApplyAudio()
        {
            if (!Player) return;
            var c = Player.clip;
            if (c == null || c.audioTrackCount == 0) { Player.audioOutputMode = VideoAudioOutputMode.None; return; }
            Player.audioOutputMode = VideoAudioOutputMode.Direct;
            Player.controlledAudioTrackCount = 1;
            Player.EnableAudioTrack(0, true);
            Player.SetDirectAudioVolume(0, Mathf.Clamp01(MenuFlybyVolume * OzAudio.EffectsLin(PlayerPrefs.GetFloat("oz_vol_effects", 1f))));
        }

        OzLevelPreview(string level) { Level = level; }

        public static OzLevelPreview Create(Transform host, string level)
        {
            var p = new OzLevelPreview(level);
            // the level card / select still = the first of the level's chosen loading stills (L02: sunrise canyon red fort, L17: car park
            // war machines at dusk), which replaced the old posters; the old poster is the fallback
            p.Poster = Resources.Load<Texture2D>($"OpenZ/Loading/{level}_loading_1") ?? Resources.Load<Texture2D>($"OpenZ/Menu/{level}_preview") ?? Resources.Load<Texture2D>($"OpenZ/Menu/{level}_poster");
            var clip = Resources.Load<VideoClip>($"OpenZ/Menu/{level}_preview");
            if (clip != null) { p.clips.Add(clip); for (int k = 2; k <= 9; k++) { var more = Resources.Load<VideoClip>($"OpenZ/Menu/{level}_preview_{k}"); if (more == null) break; p.clips.Add(more); } p.Shuffle(null); clip = p.clips[0]; }
            string url = null;
            if (clip == null)
            {
                var sa = Path.Combine(Application.streamingAssetsPath, $"menu_{level}.mp4");
                if (File.Exists(sa)) url = sa;
            }
            if (clip != null || url != null)
            {
                var go = new GameObject("Preview_" + level);
                go.transform.SetParent(host, false);
                var vp = go.AddComponent<VideoPlayer>();
                vp.playOnAwake = false; vp.waitForFirstFrame = true; vp.skipOnDrop = true; vp.isLooping = true;
                vp.audioOutputMode = VideoAudioOutputMode.None;                                          // previews are muted
                vp.renderMode = VideoRenderMode.RenderTexture; vp.aspectRatio = VideoAspectRatio.FitOutside;
                int w = clip != null && clip.width > 0 ? (int)clip.width : 1280, h = clip != null && clip.height > 0 ? (int)clip.height : 720;
                p.Frame = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "OpenZ_Preview_" + level, useMipMap = false, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                p.Frame.Create();
                vp.targetTexture = p.Frame;
                if (clip != null) { vp.source = VideoSource.VideoClip; vp.clip = clip; }
                else { vp.source = VideoSource.Url; vp.url = url; }
                vp.prepareCompleted += v => { p.Prepared = true; Debug.Log($"[OpenZ Menu] preview {level}: prepared {(v.clip ? v.clip.name : v.url)} in {Time.unscaledTime - p.prepareStartAt:0.00} s ({v.width}x{v.height}, {v.frameRate:0.##} fps, {v.length:0.0} s)"); };
                vp.errorReceived += (v, msg) => { p.Failed = true; p.LastError = msg; Debug.LogWarning($"[OpenZ Menu] preview video {level}: {msg} (clip {(v.clip ? v.clip.name : v.url)}; using the still)"); };
                p.Player = vp; p.HasVideo = true; p.ApplyAudio();
                p.prepareStartAt = Time.unscaledTime; vp.Prepare();
            }
            Debug.Log($"[OpenZ Menu] preview {level}: video {(clip != null ? "Resources clip x" + p.clips.Count + " in the order " + p.ClipOrder + " (" + clip.width + "x" + clip.height + ")" : url != null ? "StreamingAssets" : "none")}, still {(p.Poster != null ? p.Poster.width + "x" + p.Poster.height : "none")}");
            return p;
        }

        /// <summary>The video can be shown (prepared, no error).</summary>
        public bool VideoOk => HasVideo && !Failed && Player != null;
        public bool IsPlaying => VideoOk && Player.isPlaying;
        /// <summary>A real frame of the running clip is in <see cref="Frame"/> (a short grace after Play for the first decode).</summary>
        public bool ShowingVideo => IsPlaying && Player.frame >= 0 && Time.unscaledTime - playCalledAt > 0.12f;
        public double Length => VideoOk && Player.length > 0 ? Player.length : VideoOk && Player.clip != null ? Player.clip.length : 0;
        public float Progress => Length > 0 ? Mathf.Clamp01((float)(Player.time / Length)) : 0f;
        public double TimeLeft => Length > 0 ? Length - Player.time : double.MaxValue;
        /// <summary>The texture to show sharp: the video when it has a frame, else the still (null = neither).</summary>
        public Texture Sharp => ShowingVideo ? Frame : Poster != null ? Poster : VideoOk && Player.frame >= 0 ? Frame : null;
        public bool IsStill => !ShowingVideo;

        public void PlayFrom(double t, bool loop)
        {
            if (!VideoOk) return;
            Player.isLooping = loop;
            if (t >= 0 && Player.canSetTime) Player.time = Length > 0 ? Math.Min(t, Math.Max(0, Length - 0.5)) : t;
            Player.Play();
            if (t >= 0) playCalledAt = Time.unscaledTime;                                                   // a seek may show one stale frame; a resume does not
        }

        public void Pause() { if (VideoOk && Player.isPlaying) Player.Pause(); }

        /// <summary>Blurred copy (320x180 then 160x90, bilinear): the level-select backdrop. Refreshes only when a new frame
        /// arrived; the still is blurred once.</summary>
        public Texture Blurred
        {
            get
            {
                var src = ShowingVideo ? (Texture)Frame : Poster;
                if (src == null) return null;
                if (blurA == null)
                {
                    blurA = new RenderTexture(320, 180, 0, RenderTextureFormat.ARGB32) { name = "OpenZ_PreviewBlurA_" + Level, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    blurB = new RenderTexture(160, 90, 0, RenderTextureFormat.ARGB32) { name = "OpenZ_PreviewBlurB_" + Level, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                }
                bool video = ShowingVideo;
                long f = video ? Player.frame : -1;
                if ((video && f != blurredFrame) || (!video && !posterBlurred))
                {
                    Graphics.Blit(src, blurA); Graphics.Blit(blurA, blurB);
                    blurredFrame = f; posterBlurred = !video;
                    if (video) posterBlurred = false;
                }
                return blurB;
            }
        }

        public void Release()
        {
            if (Player) { Player.Stop(); Player.targetTexture = null; }
            if (Frame) { Frame.Release(); UnityEngine.Object.Destroy(Frame); }
            if (blurA) { blurA.Release(); UnityEngine.Object.Destroy(blurA); }
            if (blurB) { blurB.Release(); UnityEngine.Object.Destroy(blurB); }
        }

        /// <summary>uvRect that covers a rect of aspect `rect` with a texture of aspect `tex` (crop, never stretch).</summary>
        public static Rect Cover(float tex, float rect)
        {
            if (tex <= 0f || rect <= 0f) return new Rect(0, 0, 1, 1);
            if (tex > rect) { float w = rect / tex; return new Rect((1f - w) * 0.5f, 0, w, 1); }
            float h = tex / rect; return new Rect(0, (1f - h) * 0.5f, 1, h);
        }

        /// <summary>Slow Ken Burns move inside a cover rect: zoom 1.05..1.13 and a drifting centre (periods 28 / 37 / 41 s,
        /// unscaled time), so a still reads as a live camera. Reduced motion: static 1.05 crop.</summary>
        public static Rect KenBurns(Rect cover, float t, float seed)
        {
            float z = OzMotion.Reduced ? 1.05f : 1.09f + 0.04f * Mathf.Sin(t * 2f * Mathf.PI / 28f + seed);
            float w = cover.width / z, h = cover.height / z;
            float cx = cover.center.x + (OzMotion.Reduced ? 0f : (cover.width - w) * 0.45f * Mathf.Sin(t * 2f * Mathf.PI / 37f + seed * 1.7f));
            float cy = cover.center.y + (OzMotion.Reduced ? 0f : (cover.height - h) * 0.45f * Mathf.Cos(t * 2f * Mathf.PI / 41f + seed * 0.6f));
            return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }
    }
}

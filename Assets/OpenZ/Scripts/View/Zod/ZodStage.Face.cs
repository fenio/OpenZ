// OpenZ (GPL-3.0)
//      cigar) ran on every model at v1's offsets: on v2 the interior shell sat in front of his lips = the black ball, with the side
//      cigar sticking out of it. They are now PER MODEL, data-driven like zod_react_cams.json: Resources/OpenZ/Zod/zod_face_rigs.json,
//      "models" keyed by the Resources prefab name (then "v<version>"); a model without an entry gets v1's values (v1 unchanged).
//  (2) The HQ face light: the warm key 2.7 m from his face blew the face out to flat yellow (v1 very orange). The "light" block of
//      the same file places key / fill / rim / bounce: key further back and a little up, lower, near-neutral, softer cone; a cooler
//      dim fill from the other side; a subtle rim per model (cool grey on v2's olive metal, bronze on v1). Live-tunable in Play:
//      edit the file, then ZodStage.ReloadFace() + RebuildFaceLights() on a stage.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        [Serializable]
        public class FaceRig
        {
            /// <summary>The Resources prefab name ("ZodCameo", "ZodCameo_v2") or "v&lt;version&gt;".</summary>
            public string key = "";
            /// <summary>v1: the mesh cigar points down the lens, so a side cigar replaces it. false = the model's own mesh cigar + ember.</summary>
            public bool sideCigar = true;
            public float cigarLen = 0.11f, cigarDia = 0.022f, cigarOut = 0.62f, cigarSide = 0.72f, cigarDown = 0.18f;
            /// <summary>The dark mouth interior (v1's open mouth showed the board through the back of the head). false = none.</summary>
            public bool mouthPlug = true;
            public float plugDown = 1.15f, plugBack = -2.3f, plugW = 2.6f, plugH = 2.3f, plugD = 2.6f;
            /// <summary>Face light multipliers of this model (bronze v1 vs olive v2).</summary>
            public float keyMul = 1f, fillMul = 1f, rimMul = 1f, bounceMul = 1f;
            /// <summary>Rim colour of this model (alpha 0 = the shared rim colour).</summary>
            public Color rimColor = new Color(0f, 0f, 0f, 0f);
            /// <summary>
            /// the hat hid his whole face, the side cigar rested on the shells). Empty = the model's own clamps on every clip.</summary>
            public ClipHead[] clipHeads = new ClipHead[0];
            public override string ToString() => $"{key}: side cigar {sideCigar}, mouth plug {mouthPlug} (down {plugDown:0.##}, back {plugBack:0.##}, {plugW:0.##}x{plugH:0.##}x{plugD:0.##}), light x{keyMul:0.##}/{fillMul:0.##}/{rimMul:0.##}/{bounceMul:0.##}";
        }

        /// <summary>A head clamp while one clip plays (eased in / out; only ever tighter than the model's own clamp).</summary>
        [Serializable]
        public class ClipHead
        {
            public string clip = "";
            /// <summary>Max chin-down (deg from upright, toward the lens) and the share of the tilt past it that still shows.</summary>
            public float chinDown = 16f, softKeep = 0.12f;
            public float easeIn = 0.25f, easeOut = 0.35f;
            public override string ToString() => $"{clip} chin {chinDown:0.#} deg keep {softKeep:0.###}";
        }

        float baseChinDown = 16f, clipHeadW; ClipHead clipHeadNow;
        /// <summary>Play proof: the clip clamp now (weight 0..1) and the chin limit the rig runs this frame.</summary>
        public string ClipHeadStatus => $"clip head {(clipHeadNow != null ? clipHeadNow.ToString() : "none")} w {clipHeadW:F2}, chin limit {ZodTalkRig.MaxChinDownDeg:F1} keep {ZodTalkRig.ChinSoftKeep:F3}";

        /// <summary>Eases the rig's chin clamp to the playing clip's ClipHead (face rig data) and back to the model's own.</summary>
        void TickClipHead(float dt)
        {
            if (!rig) return;
            ClipHead want = null;
            var list = Face != null ? Face.clipHeads : null;
            if (list != null && CurrentClip != null) foreach (var c in list) if (c != null && c.clip == CurrentClip) { want = c; break; }
            if (want != null) clipHeadNow = want;
            if (clipHeadNow == null) { ZodTalkRig.MaxChinDownDeg = baseChinDown; ZodTalkRig.ChinSoftKeep = ZodTalkRig.SoftKeep; return; }
            float ease = want != null ? clipHeadNow.easeIn : clipHeadNow.easeOut;
            clipHeadW = Mathf.MoveTowards(clipHeadW, want != null ? 1f : 0f, dt / Mathf.Max(0.05f, ease));
            float w = Mathf.SmoothStep(0f, 1f, clipHeadW);
            ZodTalkRig.MaxChinDownDeg = Mathf.Lerp(baseChinDown, Mathf.Min(baseChinDown, clipHeadNow.chinDown), w);
            ZodTalkRig.ChinSoftKeep = Mathf.Lerp(ZodTalkRig.SoftKeep, Mathf.Min(ZodTalkRig.SoftKeep, clipHeadNow.softKeep), w);
            if (clipHeadW <= 0f && want == null) clipHeadNow = null;
        }

        [Serializable]
        public class FaceLight
        {
            public Vector3 keyPos = new Vector3(-1.5f, 2.6f, 3.3f), keyAim = new Vector3(0f, 1.97f, 0f);
            public Color keyColor = new Color(1f, 0.95f, 0.9f);
            public float keyIntensity = 7.5f, keyRange = 14f, keyAngle = 48f, keyInner = 0.2f;
            public Vector3 fillPos = new Vector3(2.4f, 2.0f, 2.6f);
            public Color fillColor = new Color(0.66f, 0.76f, 1f);
            public float fillIntensity = 3.2f, fillRange = 9f;
            public Vector3 rimPos = new Vector3(1.35f, 2.9f, -1.7f), rimAim = new Vector3(0f, 1.85f, 0.1f);
            public Color rimColor = new Color(0.85f, 0.9f, 1f);
            public float rimIntensity = 10f, rimRange = 7f, rimAngle = 60f;
            public Vector3 bouncePos = new Vector3(0.4f, 1.1f, 1.9f);
            public Color bounceColor = new Color(1f, 0.9f, 0.8f);
            public float bounceIntensity = 0.5f, bounceRange = 3.4f;
            /// <summary>
            /// Small and away from the face so it does not over-light it: face luma p50 136 -> 52 and bright-yellow pixels 23 % -> 0 % on v2 with it this small.
            /// The ember itself still glows (emission + bloom) and still flares on a puff.</summary>
            public float emberIntensity = 0.06f, emberRange = 0.12f;
        }

        [Serializable] class FaceRigFile { public FaceLight light = new FaceLight(); public FaceRig[] models = new FaceRig[0]; }

        public const string FaceRigsPath = "OpenZ/Zod/zod_face_rigs";
        public static FaceLight FaceLights = new FaceLight();
        public static readonly Dictionary<string, FaceRig> FaceRigs = new Dictionary<string, FaceRig>();
        static bool faceLoaded;
        /// <summary>The face rig this stage's model runs.</summary>
        public FaceRig Face { get; private set; } = new FaceRig();

        public static void ReloadFace() { faceLoaded = false; LoadFace(); }
        static void LoadFace()
        {
            if (faceLoaded) return;
            faceLoaded = true; FaceRigs.Clear(); FaceLights = new FaceLight();
            var ta = Resources.Load<TextAsset>(FaceRigsPath);
            if (!ta) { Debug.Log("[ZodCameo] no " + FaceRigsPath + ".json: v1 face parts + the default HQ face light on every model"); return; }
            try
            {
                var f = JsonUtility.FromJson<FaceRigFile>(ta.text);
                if (f != null && f.light != null) FaceLights = f.light;
                if (f != null && f.models != null) foreach (var m in f.models) if (m != null && !string.IsNullOrEmpty(m.key)) FaceRigs[m.key] = m;
            }
            catch (Exception e) { Debug.LogWarning("[ZodCameo] " + FaceRigsPath + ".json: " + e.Message); }
        }

        /// <summary>Picks this model's face rig and sets the ZodTalkRig statics BEFORE the model is instantiated (its Awake builds
        /// the side cigar / mouth interior from them). Every static is set, so a v2 stage never leaks into a later v1 stage.</summary>
        void ApplyFaceRig(string prefabName)
        {
            LoadFace();
            FaceRig f;
            if (!FaceRigs.TryGetValue(prefabName ?? "", out f) && !FaceRigs.TryGetValue("v" + ModelVersion, out f)) f = new FaceRig { key = "(v1 default)" };
            Face = f;
            ZodTalkRig.SideCigar = f.sideCigar;
            ZodTalkRig.CigarLen = f.cigarLen; ZodTalkRig.CigarDia = f.cigarDia; ZodTalkRig.CigarOut = f.cigarOut; ZodTalkRig.CigarSide = f.cigarSide; ZodTalkRig.CigarDown = f.cigarDown;
            ZodTalkRig.MouthPlug = f.mouthPlug;
            ZodTalkRig.PlugDown = f.plugDown; ZodTalkRig.PlugBack = f.plugBack; ZodTalkRig.PlugW = f.plugW; ZodTalkRig.PlugH = f.plugH; ZodTalkRig.PlugD = f.plugD;
            Debug.Log("[ZodCameo] face rig " + f + (f.clipHeads != null && f.clipHeads.Length > 0 ? ", clip heads: " + string.Join(", ", Array.ConvertAll(f.clipHeads, c => c != null ? c.ToString() : "-")) : ""));
        }

        void BuildFaceLights()
        {
            var L = FaceLights; var f = Face ?? new FaceRig();
            keyIntensity = L.keyIntensity * f.keyMul; fillIntensity = L.fillIntensity * f.fillMul; rimIntensity = L.rimIntensity * f.rimMul; bounceIntensity = L.bounceIntensity * f.bounceMul;
            keyColor = L.keyColor; fillColor = L.fillColor; rimColor = f.rimColor.a > 0.01f ? new Color(f.rimColor.r, f.rimColor.g, f.rimColor.b) : L.rimColor;
            key = AddLight("Key", LightType.Spot, L.keyPos, L.keyAim, keyColor, keyIntensity, L.keyRange, L.keyAngle, true);
            key.innerSpotAngle = L.keyAngle * Mathf.Clamp01(L.keyInner);                                  // a long soft cone edge
            var kd = key.GetUniversalAdditionalLightData(); if (kd) kd.softShadowQuality = SoftShadowQuality.High;
            fill = AddLight("Fill", LightType.Point, L.fillPos, L.fillPos, fillColor, fillIntensity, L.fillRange);
            rim = AddLight("Rim", LightType.Spot, L.rimPos, L.rimAim, rimColor, rimIntensity, L.rimRange, L.rimAngle);
            bounce = AddLight("Bounce", LightType.Point, L.bouncePos, L.bouncePos, L.bounceColor, bounceIntensity, L.bounceRange);   // warm lift under the brim
            emberIntensity = L.emberIntensity;                                                            // LateUpdate flickers the ember light from this
            if (ember) ember.range = L.emberRange;
        }

        /// <summary>Play tuning: rebuild key / fill / rim / bounce from FaceLights (after ReloadFace()).</summary>
        public void RebuildFaceLights()
        {
            foreach (var l in new[] { key, fill, rim, bounce }) if (l) Destroy(l.gameObject);
            BuildFaceLights();
        }

        public string FaceStatus() => $"model v{ModelVersion} ({modelKey}), face rig {Face}, plug {(rig && rig.HasMouthPlug ? "built" : "none")}, side cigar {(rig && rig.HasSideCigar ? "built" : "none")}, "
                                      + $"key {keyIntensity:0.##} at {(key ? key.transform.localPosition.ToString("F2") : "-")}, fill {fillIntensity:0.##}, rim {rimIntensity:0.##}, bounce {bounceIntensity:0.##}";
    }
}

using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The cloth life of the two
    /// ROCKET FUEL banners (fuel_banner_v1 kit, props C11 / C12, kind "banner"), next to the prop's DetailProp, which still owns the
    /// frame, the lamps, the night light, the hit knock and the break (DV67: the banner is a sim map item like every details_v2 prop,
    /// Zod map-object HP, attacked by explosives only; its death in the sim breaks it).
    ///   D0 (as placed): the weathered print, 7 baked holes. Every shot segment that really crosses the print stamps a new hole
    ///      (holes atlas cell into the per-banner mask: a REAL cut, shadows too; view only, never sim HP); the knock plays the
    ///      canvas hit (kit hitEvent / hitClips).
    ///   D1 (torn): after <see cref="shotsToTear"/> holes, a splash in reach, or the sim HP at 2/3: the damaged print, and the
    ///      bottom-right flap tears off (short rip) and flutters down; the flap-fall clip is picked first and the fall lasts exactly
    ///      until its slap (a 1.123 s, b 0.702 s, measured in sfx_batch_v1), so the slap lands on the landing frame.
    ///   D2 (shredded): a blast in full reach or the sim HP at 1/3: the print becomes 10 printed strips hanging from the top rod
    ///      (sway as pendulums in the shader), the double rip + a pole clang, a few cloth scraps; a still-attached flap flies off.
    ///   Break (the sim death, DetailProp.Break): D2 first (so the strips are thrown with the frame), clang + sparks at SPARK_;
    ///      DetailProp throws every LOOSE_ part, plays the rip, kills the lamps and shows the RUIN_ stubs.
    /// Every step is in OzSoundLog (src DetailsV2) and in <see cref="Log"/>.
    /// OVERHEAD (fuel_banner_v1_street, set by itself at Start when the print's bottom edge is > 2.5 m above the root): the print hangs
    /// over a street between two banner masts clamped to street-lamp posts; view only (no sim item, no tiles). Only rounds that really
    /// cross the print hit it (DetailProp.ShotTest), a share of the rounds passing UNDER it go high and hole it (strayHole), a blast is
    /// measured to the print (DetailProp.BlastDist), and this component owns the ladder (DetailProp.HpOwned: Damage only knocks):
    /// holes -> D1 after shotsToTear; each blast in reach one step D0 -> D1 -> D2 -> break (full reach: two steps). The rig never rocks.
    /// </summary>
    [DefaultExecutionOrder(75)]                                                         // after DetailProp (74): its Start has run
    public class FuelBanner : MonoBehaviour
    {
        [Header("Sounds (sfx_batch_v1 Foley; pole clang = the details_v2 sign-fall bank)")]
        public string ripEvent = "dtl_banner_rip";
        public string[] ripClips = { "Foley/sd_fabric_rip_a", "Foley/sd_fabric_rip_b", "Foley/sd_fabric_rip_c" };
        public string flapEvent = "dtl_banner_flap_fall";
        public string[] flapClips = { "Foley/sd_cloth_flap_fall_a", "Foley/sd_cloth_flap_fall_b" };
        [Tooltip("slap onset (s) of each flap clip, same order (measured: a 1.123, b 0.702)")] public float[] flapSlap = { 1.123f, 0.702f };
        public string[] clangClips = { "DetailsV2/dv2_sign_fall_a", "DetailsV2/dv2_sign_fall_b" };
        public string[] sparkClips = { "s3_dtl_neon_spark", "sd_lamp_spark_a", "sd_lamp_spark_b" };
        [Header("Damage")]
        [Tooltip("holes atlas (fuel_banner_v1 art/decals, 4 x 2 cells of 256: a = 0 cut, rgb = scorch); Read/Write on")] public Texture2D holesAtlas;
        public int shotsToTear = 6;
        [Range(0f, 1f)] public float tearAt = 0.67f, shredAt = 0.34f;
        [Tooltip("hole stamp size in mask pixels (mask 256 x 512 over the print: ~1.2 cm per pixel at placement scale 1.2)")] public int stampPx = 22;

        [Header("Overhead street banner (fuel_banner_v1_street)")]
        [Tooltip("set at Start: the print's bottom edge is more than 2.5 m above the prop root (the rig hangs over a street)")] public bool overhead;
        [Tooltip("overhead: share of the rounds passing UNDER the print (across its ground strip) that go high and hole it (view only, no HP)")]
        [Range(0f, 1f)] public float strayHole = 0.3f;

        [Header("Lamp-post banner (fuel_banner_v1_pole)")]
        [Tooltip("the banner's own lamp: cone (deg) of the spot made at the BANNER_light_ empty on the top arm, aimed at the print; on at night like every detail lamp (prop.Lights)")]
        public float spotAngle = 120f;
        [Tooltip("unit obstacle disc (m) at the banner pole's foot, as the street lamp it replaces (NightLights: 0.35)")]
        public float poleObstacle = 0.35f;
        DetailLight spot; Vector3 poleDisc; float nextObstacleCheck;

        public int State { get; private set; }
        public int Holes { get; private set; }
        public static readonly List<string> Log = new List<string>();

        DetailProp prop;
        Transform d0, d1, d2, flap, spark;
        readonly List<Transform> strips = new List<Transform>();
        readonly List<Renderer> cloth = new List<Renderer>();
        Texture2D mask; Color32[] maskPx; MaterialPropertyBlock mpb; bool maskDirty;
        Vector3 rectO, rectU, rectV; bool rectOk; Vector3 lastFrom;
        static Color32[] atlasPx; static int atlasW, atlasH; static Texture2D atlasFor;
        static readonly int HoleMaskId = Shader.PropertyToID("_HoleMask"), SwayScaleId = Shader.PropertyToID("_SwayScale");
        const int MaskW = 256, MaskH = 512;

        void Start()
        {
            prop = GetComponent<DetailProp>();
            if (!prop) { enabled = false; return; }
            d0 = prop.FindPrefix("BANNER_D0_"); d1 = prop.FindPrefix("BANNER_D1_"); d2 = prop.FindPrefix("BANNER_D2_");
            flap = prop.FindPrefix("LOOSE_Flap_"); spark = prop.FindPrefix("SPARK_");
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (DetailProp.Base(t.name).StartsWith("LOOSE_Strip")) strips.Add(t);
            foreach (var t in new[] { d0, d1, d2, flap }) if (t) cloth.AddRange(t.GetComponentsInChildren<Renderer>(true));
            foreach (var t in strips) cloth.AddRange(t.GetComponentsInChildren<Renderer>(true));
            Show(d1, false); Show(d2, false); Show(flap, false); foreach (var s in strips) Show(s, false);
            Show(d0, true);
            prop.knockWobble = Mathf.Min(prop.knockWobble, 0.15f);                     // a ballasted frame shudders, it does not rock
            // the run-time hole mask (white = keep), shared by every cloth piece through a property block
            mask = new Texture2D(MaskW, MaskH, TextureFormat.RGBA32, false, true) { name = "FuelBannerHoles_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            maskPx = new Color32[MaskW * MaskH];
            for (int i = 0; i < maskPx.Length; i++) maskPx[i] = new Color32(255, 255, 255, 255);
            mask.SetPixels32(maskPx); mask.Apply(false);
            mpb = new MaterialPropertyBlock();
            foreach (var r in cloth) SetMpb(r, 1f);
            rectOk = PrintRect();
            overhead = rectOk && rectO.y > 2.5f;
            if (overhead)
            {
                prop.knockWobble = 0f;                                                     // masts bolted on the kerb: the rig does not rock
                prop.ShotTest = (a, b) => ShotUv(a, b, out _);                             // only rounds that really cross the print
                prop.BlastDist = PrintDist;                                                // splash reach measured to the print, not the street axis
                prop.HpOwned = true;                                                       // OnDamaged runs the ladder; DetailProp only knocks
            }
            // fuel_banner_v1_pole: the banner's own lamp on the top arm. A night-budget DetailLight at the BANNER_light_ empty, made a spot
            // aimed at the print; in prop.Lights, so DetailProp drives it by DetailKit.Night like the other lamps and kills it on the break.
            var bl = prop.FindPrefix("BANNER_light_");
            if (bl && rectOk)
            {
                spot = new DetailLight(bl, prop.lightColor, prop.lightRange, prop.lightIntensity, prop.lightPriority, (string.IsNullOrEmpty(prop.id) ? "dtl" : prop.id) + "_banner_spot");
                spot.Light.type = LightType.Spot; spot.Light.spotAngle = spotAngle; spot.Light.innerSpotAngle = spotAngle * 0.5f;
                spot.Light.transform.rotation = Quaternion.LookRotation(PrintPoint(new Vector2(0.55f, 0.5f)) - bl.position, Vector3.up);
                spot.Fix.Angle = spotAngle;
                prop.Lights.Add(spot);
            }
            if (prop.FindPrefix("BODY_Pole_")) poleDisc = new Vector3(transform.position.x, transform.position.z, poleObstacle);   // the pole stands at the root
            prop.Damaged += OnDamaged;
            prop.Breaking += OnBreaking;
            DetailPropHub.Shot += OnShot;
        }

        void OnDestroy()
        {
            if (prop) { prop.Damaged -= OnDamaged; prop.Breaking -= OnBreaking; }
            DetailPropHub.Shot -= OnShot;
            if (mask) Destroy(mask);
        }

        static void Show(Transform t, bool on) { if (t && t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        void SetMpb(Renderer r, float sway)
        {
            if (!r) return;
            r.GetPropertyBlock(mpb); mpb.SetTexture(HoleMaskId, mask); mpb.SetFloat(SwayScaleId, sway); r.SetPropertyBlock(mpb);
        }

        /// <summary>The print rectangle in the prop's local space from the D0 cloth mesh bounds: the kit leans the print back (top farther
        /// from FRONT_, i.e. +z after the facing fix), so the bounds' bottom-front edge to top-back edge is the print plane (the 6 cm
        /// belly is the only error). u runs along +x (the viewer's right looking at the front), v up.</summary>
        bool PrintRect()
        {
            var mf = d0 ? d0.GetComponentInChildren<MeshFilter>(true) : null;
            if (!mf || !mf.sharedMesh) return false;
            var b = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                var c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                var l = transform.InverseTransformPoint(m.MultiplyPoint3x4(c));
                lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
            }
            // which way it leans: the D2 sleeve band is the top hem on the rod (kit: z 5.49-5.60 m); its depth says where the top edge is
            bool topBack = true;
            var band = d2 ? d2.GetComponentInChildren<MeshFilter>(true) : null;
            if (band && band.sharedMesh)
            {
                var bc = transform.InverseTransformPoint(band.transform.localToWorldMatrix.MultiplyPoint3x4(band.sharedMesh.bounds.center));
                topBack = Mathf.Abs(bc.z - hi.z) <= Mathf.Abs(bc.z - lo.z);
            }
            rectO = new Vector3(lo.x, lo.y, topBack ? lo.z : hi.z);
            rectU = new Vector3(hi.x - lo.x, 0f, 0f);
            rectV = new Vector3(0f, hi.y - lo.y, topBack ? hi.z - lo.z : lo.z - hi.z);
            return rectU.x > 0.5f && rectV.y > 0.5f;
        }

        /// <summary>Where a shot segment (world) crosses the print, as print uv; false when it misses the cloth.</summary>
        public bool ShotUv(Vector3 a, Vector3 b, out Vector2 uv)
        {
            uv = default;
            if (!rectOk) return false;
            Vector3 la = transform.InverseTransformPoint(a), lb = transform.InverseTransformPoint(b);
            var n = Vector3.Cross(rectU, rectV); var d = lb - la;
            float den = Vector3.Dot(d, n); if (Mathf.Abs(den) < 1e-6f) return false;
            float t = Vector3.Dot(rectO - la, n) / den; if (t < 0f || t > 1f) return false;
            var q = la + d * t - rectO;
            float u = Vector3.Dot(q, rectU) / rectU.sqrMagnitude, v = Vector3.Dot(q, rectV) / rectV.sqrMagnitude;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            uv = new Vector2(u, v); return true;
        }

        /// <summary>overhead: where a round's top-down path passes under the print (crosses its mid line within the print width), as print
        /// uv with a random height; false for rounds at print height (ShotUv's) or beside it.</summary>
        bool UnderPrint(Vector3 a, Vector3 b, out Vector2 uv)
        {
            uv = default;
            if (!rectOk) return false;
            Vector3 la = transform.InverseTransformPoint(a), lb = transform.InverseTransformPoint(b);
            if (Mathf.Max(la.y, lb.y) >= rectO.y) return false;
            float zm = rectO.z + rectV.z * 0.5f, dz = lb.z - la.z;
            if (Mathf.Abs(dz) < 1e-4f) return false;
            float t = (zm - la.z) / dz; if (t < 0f || t > 1f) return false;
            float u = (la.x + (lb.x - la.x) * t - rectO.x) / rectU.x;
            if (u < 0.03f || u > 0.97f) return false;
            uv = new Vector2(u, Random.Range(0.12f, 0.88f)); return true;
        }

        /// <summary>World point of the print at uv (the PrintRect plane).</summary>
        public Vector3 PrintPoint(Vector2 uv) { return transform.TransformPoint(rectO + rectU * uv.x + rectV * uv.y); }

        /// <summary>overhead: distance (m) from a blast to the nearest point of the print (DetailProp.BlastDist, read by the hub's reach rule).</summary>
        float PrintDist(Vector3 p)
        {
            if (!rectOk) return (p - transform.position).magnitude;
            var q = transform.InverseTransformPoint(p) - rectO;
            float s = Mathf.Clamp01(Vector3.Dot(q, rectU) / rectU.sqrMagnitude), t = Mathf.Clamp01(Vector3.Dot(q, rectV) / rectV.sqrMagnitude);
            var ls = transform.lossyScale;
            return (q - rectU * s - rectV * t).magnitude * Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z));
        }

        // ------------------------------------------------------------------ damage paths
        void OnShot(Vector3 a, Vector3 b)
        {
            if (!prop || prop.Broken || State >= 2) return;
            Vector2 uv = default;
            if (prop.ShotHits(a, b) && ShotUv(a, b, out uv)) { }                             // crosses the print (DetailProp knocked it already)
            else if (overhead && strayHole > 0f && UnderPrint(a, b, out uv) && Random.value < strayHole)
                DetailProp.Play(prop.hitEvent, prop.hitClips, PrintPoint(uv), 0.3f, 1f, GetInstanceID() + 3);   // a high round: hole + canvas knock
            else return;
            lastFrom = a;
            Stamp(uv);
            if (State == 0 && Holes >= shotsToTear) Tear(a, "shots");
        }

        void OnDamaged(DetailProp p, float amount, Vector3 from, bool blast)
        {
            lastFrom = from;
            if (!blast || p.Broken) return;
            if (overhead)
            {   // view-only ladder: each blast in reach one step, full reach two (DetailPropHub.OnBlast: health + 1 in reach, half beyond)
                for (int i = amount > p.health ? 2 : 1; i > 0; i--)
                {
                    if (State == 0) Tear(from, "splash");
                    else if (State == 1) Shred(from, "blast");
                    else { Note("break: blast"); p.Break(from); return; }
                }
                return;
            }
            if (amount > p.health) Shred(from, "blast"); else Tear(from, "splash");      // DetailPropHub.OnBlast: health + 1 in reach, half beyond
        }

        void Update()
        {
            if (poleDisc.z > 0f && Time.time >= nextObstacleCheck)                          // fuel_banner_v1_pole: units walk round the pole
            {
                nextObstacleCheck = Time.time + 1f;
                if (!UnitView.Obstacles.Contains(poleDisc)) UnitView.Obstacles.Add(poleDisc);
            }
            if (!prop || prop.Broken || State >= 2 || !prop.SimBacked) return;
            var w = ZGame.I ? ZGame.I.World : null;
            var o = w != null ? w.Get(prop.SimId) : null;
            if (o == null || o.MaxHealth <= 0) return;
            float f = o.Health / (float)o.MaxHealth;                                       // the sim decides how torn it is
            if (f <= shredAt) Shred(lastFrom == Vector3.zero ? transform.position - transform.forward * 10f : lastFrom, "sim hp " + f.ToString("0.00"));
            else if (f <= tearAt && State == 0) Tear(lastFrom == Vector3.zero ? transform.position - transform.forward * 10f : lastFrom, "sim hp " + f.ToString("0.00"));
        }

        void OnBreaking(DetailProp p, Vector3 from)
        {
            if (State < 2) ShredVisuals(from, false);                                     // the strips are thrown with the frame
            foreach (var t in strips) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) SetMpb(r, 0f);   // thrown strips are rigid chunks (kit README): no wave on the ground
            var at = spark ? spark.position : transform.position + Vector3.up * 1f;
            DetailProp.Play("", new[] { clangClips[Random.Range(0, clangClips.Length)] }, at, 0.7f, 1f, GetInstanceID(), 0f);
            var fx = OpenZFxPlayer.I;
            if (fx)
            {
                for (int i = 0; i < 16; i++)
                    fx.EmitChannel("m12_spark", at + Random.insideUnitSphere * 0.25f, Random.onUnitSphere * Random.Range(2f, 5f) + Vector3.up * 1.5f, Random.Range(0.25f, 0.6f), Random.Range(0.05f, 0.1f), new Color(1f, 0.85f, 0.5f), 1f);
                fx.AddLight(at, new Color(1f, 0.85f, 0.6f), 2.2f, 5f, 0.2f);
            }
            DetailProp.Play("", sparkClips, at, 0.45f);                                    // the flood lamps short out
            Note("break: clang + sparks, strips thrown with the frame");
        }

        // ------------------------------------------------------------------ states
        void Tear(Vector3 from, string why)
        {
            if (State >= 1 || !prop || prop.Broken) return;
            State = 1;
            Show(d0, false); Show(d1, true);
            for (int i = 0; i < 3; i++) Stamp(new Vector2(Random.Range(0.12f, 0.88f), Random.Range(0.3f, 0.75f)));   // the tear adds a few scorched holes
            var at = flap ? FlapCentre() : prop.Centre;
            DetailProp.Play("", new[] { ripClips[0] }, at, 0.7f, 1f, GetInstanceID(), 0f);  // short rip: the flap tears off
            if (flap) StartCoroutine(FlapFall(from));
            Note("D1 torn (" + why + "): damaged print, flap falls");
        }

        void Shred(Vector3 from, string why)
        {
            if (State >= 2 || !prop || prop.Broken) return;
            ShredVisuals(from, true);
            Note("D2 shredded (" + why + "): 10 strips");
        }

        void ShredVisuals(Vector3 from, bool sound)
        {
            bool flapOn = State == 0 && flap;                                              // still on the cloth: it flies off
            State = 2;
            Show(d0, false); Show(d1, false); Show(d2, true);
            foreach (var s in strips) Show(s, true);
            if (flapOn)
            {
                Show(flap, true);
                prop.ReleaseLoose(flap);
                var away = (transform.position - from); away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
                var hub = DetailPropHub.I;
                foreach (var r in flap.GetComponentsInChildren<Renderer>(true)) SetMpb(r, 0f);
                if (hub) hub.Chunks.Throw(flap, away * Random.Range(2f, 3.5f) + Vector3.up * Random.Range(2.5f, 4f), Random.onUnitSphere * Random.Range(200f, 500f), 12f);
                else Show(flap, false);
                flap = null;
            }
            if (!sound) return;
            var c = overhead ? PrintPoint(new Vector2(0.5f, 0.6f)) : prop.Centre + Vector3.up * prop.hitHeight * 0.15f;
            DetailProp.Play("", new[] { ripClips.Length > 1 ? ripClips[1] : ripClips[0] }, c, 0.8f, 1f, GetInstanceID(), 0f);   // the double rip = the shred
            DetailProp.Play("", new[] { clangClips[Random.Range(0, clangClips.Length)] }, overhead ? PrintPoint(new Vector2(0.5f, 1f)) : prop.Foot + Vector3.up * 0.8f, 0.5f, 1f, GetInstanceID() + 1, 0f);
            prop.Bits().Burst(c, 10, 0, 2.2f, 0.07f, new System.Random(GetInstanceID() ^ Time.frameCount));   // a few scraps (banner red)
            prop.Bits().Burst(c, 6, 1, 2.0f, 0.06f, new System.Random(GetInstanceID() * 7 + Time.frameCount)); // and cream
        }

        Vector3 FlapCentre()
        {
            var r = flap ? flap.GetComponentInChildren<Renderer>(true) : null;
            return r ? r.bounds.center : prop.Centre;
        }

        /// <summary>The flap peels off and flutters down in front of the print; the clip is chosen first and the fall lasts exactly until
        /// its slap, so the slap is on the landing frame. It lies there (sway off) and sinks away after 14 s.</summary>
        IEnumerator FlapFall(Vector3 from)
        {
            var f = flap; if (!f) yield break;
            Show(f, true);
            prop.ReleaseLoose(f);                                                         // it is on the ground now: the break must not throw it again
            f.SetParent(transform.parent, true);
            var rs = f.GetComponentsInChildren<Renderer>(true);
            var game = ZGame.I;
            int k = overhead ? 0 : Random.Range(0, Mathf.Min(flapClips.Length, flapSlap.Length));   // overhead: a 5-6 m drop takes ~1.1 s = clip a's slap (1.123 s)
            float T = Mathf.Max(0.3f, flapSlap[k]);
            var c0 = FlapCentreOf(rs, f.position);
            var off0 = Quaternion.Inverse(f.rotation) * (f.position - c0);
            var r0 = f.rotation;
            var fwd = -transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.back;   // the print side (camera)
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var land = c0 + fwd * Random.Range(0.5f, 0.9f) + right * Random.Range(-0.3f, 0.3f);
            float gy = game ? game.GroundY(land.x, land.z) : c0.y - 1.5f;
            land.y = gy + 0.03f;
            // lying flat on its face toward the camera: tipped about the banner's horizontal axis, a little yaw twist
            var rFlat = Quaternion.AngleAxis(Random.Range(-25f, 25f), Vector3.up) * Quaternion.AngleAxis(-72f, right) * r0;
            DetailProp.Play("", new[] { flapClips[k] }, c0, 0.65f, 1f, GetInstanceID() + 2, 0f);
            Note("flap falls: " + flapClips[k] + ", slap at " + T.ToString("0.000") + " s");
            float t = 0f;
            while (t < T)
            {
                float dt = game && game.paused ? 0f : Time.deltaTime;
                t = Mathf.Min(T, t + dt);
                float s = t / T, e = s * s * (1.6f - 0.6f * s), sm = s * s * (3f - 2f * s);
                var c = Vector3.Lerp(c0, land, sm); c.y = Mathf.Lerp(c0.y, land.y, e);
                c += right * Mathf.Sin(s * Mathf.PI * 3f) * 0.25f * (1f - s);            // flutter side to side
                var r = Quaternion.Slerp(r0, rFlat, sm) * Quaternion.AngleAxis(Mathf.Sin(s * Mathf.PI * 4f) * 15f * (1f - s), Vector3.forward);
                f.SetPositionAndRotation(c + r * off0, r);
                if (s > 0.6f) { float sw = Mathf.Clamp01((1f - s) / 0.4f); foreach (var x in rs) SetMpb(x, sw); }   // the cloth stops waving as it lands (ramped, no pop)
                yield return null;
            }
            OzSoundLog.Add("DetailsV2", flapEvent, flapClips[k], "flap landed on the slap frame (" + T.ToString("0.000") + " s)", 0.65f, land);
            Note("flap landed after " + T.ToString("0.000") + " s at " + land.ToString("F1"));
            float wait = 14f;
            while (wait > 0f) { wait -= game && game.paused ? 0f : Time.deltaTime; yield return null; }
            var p = f.position; float sink = 0f;
            while (sink < 1.5f) { sink += game && game.paused ? 0f : Time.deltaTime; f.position = p + Vector3.down * (0.06f * sink / 1.5f); yield return null; }
            f.gameObject.SetActive(false);
        }

        static Vector3 FlapCentreOf(Renderer[] rs, Vector3 fb)
        {
            if (rs == null || rs.Length == 0) return fb;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        /// <summary>A new bullet hole at print uv: one holes-atlas cell (random of 8) stamped into the mask (alpha min = the cut,
        /// rgb multiply = the scorch ring). Same mask for every state, so the holes stay through D1.</summary>
        public void Stamp(Vector2 uv)
        {
            if (!mask) return;
            if (!ReadAtlas()) return;
            int cell = Random.Range(0, 8), cx0 = (cell % 4) * (atlasW / 4), cy0 = (cell / 4) * (atlasH / 2), cw = atlasW / 4, ch = atlasH / 2;
            int S = Mathf.Max(6, stampPx), x0 = Mathf.RoundToInt(uv.x * MaskW) - S / 2, y0 = Mathf.RoundToInt(uv.y * MaskH) - S / 2;
            for (int y = 0; y < S; y++)
            {
                int my = y0 + y; if (my < 0 || my >= MaskH) continue;
                int ay = cy0 + Mathf.Clamp(y * ch / S, 0, ch - 1);
                for (int x = 0; x < S; x++)
                {
                    int mx = x0 + x; if (mx < 0 || mx >= MaskW) continue;
                    var a = atlasPx[ay * atlasW + cx0 + Mathf.Clamp(x * cw / S, 0, cw - 1)];
                    var m = maskPx[my * MaskW + mx];
                    maskPx[my * MaskW + mx] = new Color32((byte)(m.r * a.r / 255), (byte)(m.g * a.g / 255), (byte)(m.b * a.b / 255), (byte)Mathf.Min(m.a, a.a));
                }
            }
            maskDirty = true;                                                               // uploaded once per frame in LateUpdate (a burst stamps many)
            Holes++;
        }

        void LateUpdate()
        {
            if (!maskDirty || !mask) return;
            maskDirty = false;
            mask.SetPixels32(maskPx); mask.Apply(false);
        }

        bool ReadAtlas()
        {
            if (atlasPx != null && atlasFor == holesAtlas) return true;
            if (!holesAtlas || !holesAtlas.isReadable) return false;
            atlasPx = holesAtlas.GetPixels32(); atlasW = holesAtlas.width; atlasH = holesAtlas.height; atlasFor = holesAtlas;
            return true;
        }

        void Note(string s)
        {
            var line = "[OpenZ] fuel banner " + name + " t " + Time.time.ToString("0.00") + ": " + s + " (holes " + Holes + ")";
            Log.Add(line); if (Log.Count > 200) Log.RemoveAt(0);
            Debug.Log(line);
        }
    }
}

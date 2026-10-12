using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// On the Z_bs01_electrobot prefab, or added at run time by OpenZElectroFx to any
    /// Electrobot view that lacks it. Reads the model's sockets (robots_v3 electrobot builder: CoilTop_L / R, CoilBall_L0 / L1 / R0 /
    /// R1, CoilCore_L / R, BatteryCore, AntennaTip, GaugeCentre + the Gauge_Needle bone on the body; ElectroMuzzle / Muzzle,
    /// ChargeBall, RingCentre, Ring_0..7, CellsCentre and the BarrelSpin node on the tesla gatling) with fallbacks when one is missing,
    /// and the glow material slots M_Electro_Glow_Coils / _Pack / _Cells / _Emitter (their emission, per material slot, through a
    /// SetColor gamma-decodes each channel in this linear project: 2.4 x the cyan became (0.02, 1.19, 7.6), royal blue, and the windup
    /// blew out). A lit slot's _EmissionColor = OpenZElectroFx.Glow x intensity; an unlit TEXTURED slot (the model track's recipe:
    /// URP Unlit, _BaseMap = the atlas's cyan / ice cells) keeps the texture's hue: _BaseColor = the material's own tint (normalised)
    /// x intensity, so the cyan is not tinted twice. Every frame (order 96, after WeaponHold / BS01TypeKit posed the gun, before
    /// OpenZElectroFx draws):
    /// <list type="bullet">
    /// <item>idle: the coil gaps pulse at 0.8-1.2 Hz; 1-3 short arcs jump between each coil's spark ball and its small balls every
    /// 0.2-0.6 s; a big arc leaps from coil to coil over the crazy head every 2-5 s (a snap and a few sparks); the emitter crackles 2-4
    /// times a second while charged; soft glows on the coil tops, the cells and the tip; at night a small real light (3 m).</item>
    /// <item>the weapon cycle (sim DV45: ElPhase, ElectroCharge01): the cells show the charge (0.2 right after a bolt, full when ready),
    /// the gauge needle drops to 0 and climbs back, the barrels spin up to 1800 deg/s in the 0.35 s windup, where a charge ball grows in
    /// the emitter with 8 ring arcs converging on it and the coils flash; after the bolt the barrels spin down over 1.2 s.</item>
    /// </list>
    /// View only (view-side random, never the sim's). The recoil, the bolt and the sounds are OpenZElectroFx's.
    /// </summary>
    [DefaultExecutionOrder(96)]
    public sealed class ElectrobotView : MonoBehaviour
    {
        UnitView view; ZObject obj;
        Transform muzzle, chargeBall, ringCentre, cells, intake, barrelSpin, needle, gaugeCentre, antenna, battery;
        readonly Transform[] ring = new Transform[8], coilTop = new Transform[2], coilCore = new Transform[2];
        readonly Transform[,] coilBall = new Transform[2, 2];
        Quaternion spinRest, needleRest; Vector3 spinAxis, needleAxis;
        /// <summary>base0 = what the shader receives without us (linear, restored as it is); hue = the colour the intensity scales.</summary>
        struct GlowSlot { public Renderer r; public int index, kind; public int prop; public Vector4 base0, hue; }
        readonly List<GlowSlot> glows = new List<GlowSlot>();
        MaterialPropertyBlock mpbF; MaterialPropertyBlock Mpb => mpbF ??= new MaterialPropertyBlock();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
        static bool HasTex(Material m, string name) => m.HasProperty(name) && m.GetTexture(name);

        public bool Bound { get; private set; }
        public int Id => obj != null ? obj.Id : -1;
        public bool Dying => !view || view.Dying;
        public int SocketsFound { get; private set; }
        public int GlowSlots => glows.Count;

        // state
        float windupStart = -99f, windupDur = 0.35f, fireAt = -99f, readyAt = -99f, fizzleAt = -99f;
        float spinAngle, spinSpeed, needleShown = 1f, needleVel, cellsShown = 1f;
        float nextCoil0, nextCoil1, nextBig, nextTip, seedPhase;
        ElectroBoltShape.Rng rng;
        struct Mini { public int a; public int b; public float until, born, width, strength, bulge; public uint seed; public int kind; }
        readonly List<Mini> minis = new List<Mini>(16);
        const int N = 9;
        readonly float[] t = new float[N]; readonly Vector2[] off = new Vector2[N]; readonly Vector3[] p = new Vector3[N];

        // ------------------------------------------------------------------------------------------------ bind
        void Start() => Bind();

        public void Bind()
        {
            view = GetComponent<UnitView>();
            obj = view ? view.Obj : null;
            if (!view || obj == null) return;
            rng = new ElectroBoltShape.Rng(ElectroBoltShape.Hash(obj.Id, 91, 3));
            seedPhase = rng.Range(0f, 10f);
            int found = 0;
            foreach (var tr in GetComponentsInChildren<Transform>(true))
            {
                string n = tr.name;
                switch (n)
                {
                    case "ElectroMuzzle": muzzle = tr; found++; break;
                    case "Muzzle": if (!muzzle) muzzle = tr; break;
                    case "ChargeBall": chargeBall = tr; found++; break;
                    case "RingCentre": ringCentre = tr; found++; break;
                    case "CellsCentre": cells = tr; found++; break;
                    case "GunIntake": intake = tr; break;
                    case "BarrelSpin": barrelSpin = tr; found++; break;
                    case "Gauge_Needle": needle = tr; found++; break;
                    case "GaugeCentre": gaugeCentre = tr; break;
                    case "AntennaTip": antenna = tr; break;
                    case "BatteryCore": battery = tr; break;
                    case "CoilTop_L": coilTop[1] = tr; found++; break;
                    case "CoilTop_R": coilTop[0] = tr; found++; break;
                    case "CoilCore_L": coilCore[1] = tr; break;
                    case "CoilCore_R": coilCore[0] = tr; break;
                    case "CoilBall_R0": coilBall[0, 0] = tr; break;
                    case "CoilBall_R1": coilBall[0, 1] = tr; break;
                    case "CoilBall_L0": coilBall[1, 0] = tr; break;
                    case "CoilBall_L1": coilBall[1, 1] = tr; break;
                    default:
                        if (n.Length == 6 && n.StartsWith("Ring_") && n[5] >= '0' && n[5] <= '7') { ring[n[5] - '0'] = tr; found++; }
                        break;
                }
            }
            SocketsFound = found;
            if (barrelSpin)
            {
                spinRest = barrelSpin.localRotation;
                var axisW = muzzle && cells ? (muzzle.position - cells.position) : barrelSpin.forward;
                spinAxis = barrelSpin.InverseTransformDirection(axisW.normalized);           // in its own space: spin = rest * AngleAxis(a, spinAxis)
            }
            if (needle)
            {
                needleRest = needle.localRotation;
                var axisW = gaugeCentre ? gaugeCentre.position - needle.position : needle.forward;
                if (axisW.sqrMagnitude < 1e-8f) axisW = needle.forward;
                needleAxis = needle.InverseTransformDirection(axisW.normalized);
            }
            glows.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    string mn = m.name;
                    int kind = mn.Contains("Electro_Glow_Coils") ? 0 : mn.Contains("Electro_Glow_Pack") ? 1 : mn.Contains("Electro_Glow_Cells") ? 2 : mn.Contains("Electro_Glow_Emitter") ? 3 : -1;
                    if (kind < 0) continue;
                    int prop = m.HasProperty(EmissionId) ? EmissionId : m.HasProperty(BaseColorId) ? BaseColorId : m.HasProperty(ColorId) ? ColorId : 0;
                    if (prop == 0) continue;
                    var base0 = ElectroColour.OfMaterial(m, prop);
                    Vector4 hue = (Color)OpenZElectroFx.Glow;
                    if (prop != EmissionId && (HasTex(m, "_BaseMap") || HasTex(m, "_MainTex")))
                    {   // the texture carries the cyan: scale the material's own tint (white on the model track's glow materials)
                        float mx = Mathf.Max(base0.x, Mathf.Max(base0.y, base0.z));
                        hue = mx > 1e-4f ? new Vector4(base0.x / mx, base0.y / mx, base0.z / mx, 1f) : new Vector4(1f, 1f, 1f, 1f);
                    }
                    hue.w = base0.w;
                    glows.Add(new GlowSlot { r = r, index = i, kind = kind, prop = prop, base0 = base0, hue = hue });
                }
            }
            nextCoil0 = Time.time + rng.Range(0.1f, 0.5f); nextCoil1 = Time.time + rng.Range(0.1f, 0.5f);
            nextBig = Time.time + rng.Range(1.5f, 4f); nextTip = Time.time + rng.Range(0.2f, 0.4f);
            Bound = true;
        }

        void OnDisable() => ResetParts();
        void OnDestroy() => ResetParts();
        void ResetParts()
        {
            if (barrelSpin) barrelSpin.localRotation = spinRest;
            if (needle) needle.localRotation = needleRest;
            if (view && view.Dying) return;                                                             // a dead one keeps its glows as its fade left them
            foreach (var g in glows) { if (!g.r) continue; g.r.GetPropertyBlock(Mpb, g.index); Mpb.SetVector(g.prop, g.base0); g.r.SetPropertyBlock(Mpb, g.index); }   // linear, as the material has it
        }

        // ------------------------------------------------------------------------------------------------ sockets (with fallbacks)
        Vector3 Up => transform.up;
        public Vector3 MuzzlePos => muzzle ? muzzle.position : view ? view.MuzzlePos() : transform.position + Vector3.up * 1.2f;
        public Vector3 CellsPos => cells ? cells.position : MuzzlePos - AimDir * 0.85f;
        public Vector3 EmitterPos => chargeBall ? chargeBall.position : MuzzlePos;
        public Vector3 AimDir
        {
            get
            {
                if (muzzle && cells) { var d = muzzle.position - cells.position; if (d.sqrMagnitude > 1e-6f) return d.normalized; }
                var f = -transform.forward; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;   // BS-01 models face -z
            }
        }
        public Vector3 RingPoint(int k)
        {
            k = ((k % 8) + 8) % 8;
            if (ring[k]) return ring[k].position;
            var c = ringCentre ? ringCentre.position : EmitterPos;
            ElectroBoltShape.Frame(AimDir, out var u, out var v);
            float a = k * Mathf.PI * 0.25f;
            return c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * 0.3f;
        }
        public Vector3 CoilTop(int side)
        {
            side = Mathf.Clamp(side, 0, 1);
            if (coilTop[side]) return coilTop[side].position;
            var right = -transform.right;                                                   // model faces -z: its right is -x
            float s = side == 0 ? 1f : -1f, k = Mathf.Abs(transform.lossyScale.y);
            return transform.position + Up * (2.58f * k) + right * (0.37f * s * k) + transform.forward * (0.46f * k);
        }
        Vector3 CoilBall(int side, int k)
        {
            var b = coilBall[Mathf.Clamp(side, 0, 1), Mathf.Clamp(k, 0, 1)];
            if (b) return b.position;
            var right = -transform.right; float s = (k == 0 ? 1f : -1f) * (side == 0 ? 1f : -1f);
            return CoilTop(side) + right * (0.15f * s) - Up * 0.06f;
        }

        // ------------------------------------------------------------------------------------------------ events (OpenZElectroFx)
        public void OnCharge(float dur) { windupStart = Time.time; windupDur = Mathf.Max(0.05f, dur); }
        public void OnFire(Vector3 dir) { fireAt = Time.time; windupStart = -99f; cellsShown = 0.2f; spinSpeed = Mathf.Max(spinSpeed, 1800f); }
        public void OnFizzle() { fizzleAt = Time.time; windupStart = -99f; }
        public void OnReady() { readyAt = Time.time; nextTip = Time.time; }

        // ------------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (!Bound) { Bind(); if (!Bound) return; }
            if (!view || view.Dying || !view.isActiveAndEnabled) return;
            var fx = OpenZElectroFx.I;
            float now = Time.time, dt = Time.deltaTime;
            float charge = obj != null ? (float)obj.ElectroCharge01 : 1f;
            float wk = windupStart > 0f ? Mathf.Clamp01((now - windupStart) / windupDur) : 0f;
            bool winding = windupStart > 0f && now - windupStart < windupDur + 0.25f;
            float sinceFire = now - fireAt;
            // ---- barrels: spin up in the windup, down over 1.2 s after the bolt, a slow idle turn
            float wantSpin = winding ? Mathf.Lerp(200f, 1800f, wk * wk) : 25f;
            spinSpeed = winding ? Mathf.Max(spinSpeed, wantSpin) : Mathf.MoveTowards(spinSpeed, wantSpin, Mathf.Max(60f, spinSpeed) * dt / 0.4f);
            spinAngle = (spinAngle + spinSpeed * dt) % 360f;
            if (barrelSpin) barrelSpin.localRotation = spinRest * Quaternion.AngleAxis(spinAngle, spinAxis);
            // ---- cells refill with the charge; the gauge needle follows (drops to 0 at the bolt, climbs with the recharge)
            float cellsWant = winding ? 1f : sinceFire < 0.25f ? 0.2f : Mathf.Lerp(0.2f, 1f, charge);
            cellsShown = Mathf.MoveTowards(cellsShown, cellsWant, dt * (cellsWant > cellsShown ? 1.5f : 8f));
            float needleWant = winding ? 1f : sinceFire < 0.3f ? 0f : charge;
            needleShown = Mathf.SmoothDamp(needleShown, needleWant, ref needleVel, sinceFire < 0.3f ? 0.05f : 0.25f, 12f, dt);
            if (needle) needle.localRotation = needleRest * Quaternion.AngleAxis((needleShown - 1f) * 100f, needleAxis);
            // ---- glows (HDR emission on the model's glow slots; linear intensities: idle coils 2.4 x (0.06, 0.45, 1) = (0.14, 1.08, 2.4),
            //      the windup's peak 6 x = (0.36, 2.7, 6): the designed cyan at every level, the hue never drifts toward team blue)
            float pulse = 1f + 0.25f * Mathf.Sin((now + seedPhase) * 6.2831853f * (0.8f + 0.4f * Mathf.PerlinNoise(seedPhase, now * 0.1f)));
            float coils = winding ? Mathf.Lerp(2.4f, 6f, wk) : sinceFire < 0.4f ? Mathf.Lerp(1f, 2.4f, sinceFire / 0.4f) : 2.4f * pulse * Mathf.Lerp(0.6f, 1f, charge);
            float pack = 1.8f + 0.3f * Mathf.Sin((now + seedPhase) * 2.3f);
            float cellsG = winding ? Mathf.Lerp(3f, 6f, wk) : Mathf.Lerp(0.35f, 3f, cellsShown) * (1f + 0.08f * Mathf.Sin(now * 5f + seedPhase));
            float emitter = winding ? Mathf.Lerp(2f, 5f, wk) : Mathf.Lerp(0.5f, 1.6f, charge) + (now - readyAt < 0.3f ? 2f * (1f - (now - readyAt) / 0.3f) : 0f);
            foreach (var g in glows)
            {
                if (!g.r) continue;
                float k = g.kind == 0 ? coils : g.kind == 1 ? pack : g.kind == 2 ? cellsG : emitter;
                var lin = new Color(g.hue.x * k, g.hue.y * k, g.hue.z * k, g.hue.w);
                g.r.GetPropertyBlock(Mpb, g.index);
                Mpb.SetVector(g.prop, ElectroColour.Shader(lin));
                g.r.SetPropertyBlock(Mpb, g.index);
            }
            if (!fx || fx.Arcs == null) return;
            var cam = fx.CamRef; if (!cam) return;
            bool vis = fx.OnScreen(transform.position + Up * 1.3f, 0.15f);
            if (!vis) { minis.Clear(); return; }
            var arcs = fx.Arcs;
            // ---- soft glows: coil tops, cells, tip (read over the model's own glow; bloom a little)
            float coilHalo = Mathf.Clamp01(coils / 6f);
            arcs.Glow(CoilTop(0), 0.55f, 0.3f, 4f, 1.2f, OpenZElectroFx.Glow, 0.55f * coilHalo + 0.1f);
            arcs.Glow(CoilTop(1), 0.55f, 0.3f, 4f, 1.2f, OpenZElectroFx.Glow, 0.55f * coilHalo + 0.1f);
            arcs.Glow(CellsPos, 0.6f, 0.3f, 3f, 1f, OpenZElectroFx.Glow, 0.25f + 0.35f * cellsShown);
            if (!winding) arcs.Glow(EmitterPos, 0.3f + 0.12f * charge, 0.3f, 6f, 1.5f, OpenZElectroFx.Glow, 0.3f + 0.5f * charge);
            if (battery) arcs.Glow(battery.position, 0.35f, 0.3f, 3f, 1f, OpenZElectroFx.Glow, 0.35f);
            // ---- the windup: a charge ball in the emitter, 8 ring arcs converging, the coils flash
            if (winding && wk < 1f + 1e-3f)
            {
                var cb = EmitterPos;
                arcs.Glow(cb, Mathf.Lerp(0.15f, 0.9f, Mathf.Pow(wk, 0.7f)), 0.3f, 9f * (0.5f + wk), 2f, OpenZElectroFx.Glow, Mathf.Clamp01(0.3f + wk));
                var r = new ElectroBoltShape.Rng(ElectroBoltShape.Hash(Id, Mathf.FloorToInt(now * 30f), 5));
                for (int k = 0; k < 8; k++)
                {
                    int n = ElectroBoltShape.Jag(ref r, 3, 0.2f, 0.6f, t, off);
                    ElectroBoltShape.Place(RingPoint(k), cb, t, off, n, p);
                    arcs.Ribbon(p, n, 0.1f + 0.08f * wk, 0.22f, 8f, 1.6f, OpenZElectroFx.Glow, 0.4f + 0.6f * wk, k * 3.1f, 0.8f, 0.4f, 2f);
                }
                for (int s = 0; s < 2; s++)
                {
                    int n = ElectroBoltShape.Jag(ref r, 3, 0.18f, 0.6f, t, off);
                    ElectroBoltShape.Place(CoilTop(s), CoilBall(s, r.Int(2)), t, off, n, p);
                    arcs.Ribbon(p, n, 0.1f, 0.22f, 8f, 1.6f, OpenZElectroFx.Glow, 0.9f, s * 5.3f, 0.6f, 0.6f, 2f);
                }
                fx.AddCand(12_500 + Id, cb, 14f * wk, 7f, 5f, false);
            }
            // ---- idle life: coil crackle, the big coil-to-coil arc, the tip crackle
            if (!winding)
            {
                if (now >= nextCoil0) { SpawnCoil(0, now); nextCoil0 = now + rng.Range(0.2f, 0.6f); }
                if (now >= nextCoil1) { SpawnCoil(1, now); nextCoil1 = now + rng.Range(0.2f, 0.6f); }
                if (now >= nextBig)
                {
                    nextBig = now + rng.Range(2f, 5f);
                    minis.Add(new Mini { kind = 1, born = now, until = now + 0.14f, width = 0.16f, strength = 1f, bulge = 0.35f, seed = (uint)rng.Int(1 << 20) });
                    var mid = (CoilTop(0) + CoilTop(1)) * 0.5f + Up * 0.3f;
                    if (OpenZFxPlayer.I) for (int i = 0; i < 6; i++) OpenZFxPlayer.I.EmitChannel("wv2_spark", mid, Random.onUnitSphere * rng.Range(1.5f, 3.5f) + Up, rng.Range(0.2f, 0.4f), 0.04f, OpenZElectroFx.Ice, 1f);
                    if (fx.sounds) AudioDirector.Fire("electro_spark", mid, Id, 1f, rng.Range(0.9f, 1.1f));
                }
                if (charge >= 0.999f && now >= nextTip)
                {
                    nextTip = now + rng.Range(0.25f, 0.5f);
                    minis.Add(new Mini { kind = 2, a = rng.Int(8), born = now, until = now + 0.06f, width = 0.07f, strength = 0.9f, seed = (uint)rng.Int(1 << 20) });
                }
            }
            DrawMinis(arcs, now);
            // ---- at night a small real light on the coils
            float nk = NightGrade.NightK;
            if (nk > 0.25f) fx.AddCand(30_000 + Id, (CoilTop(0) + CoilTop(1)) * 0.5f, 3f * nk * (0.8f + 0.2f * pulse), 3.5f, 1f, false);
        }

        void SpawnCoil(int side, float now)
        {
            int n = 1 + rng.Int(3);
            for (int i = 0; i < n; i++)
                minis.Add(new Mini { kind = 0, a = side, b = rng.Int(3), born = now, until = now + rng.Range(0.06f, 0.1f), width = rng.Range(0.05f, 0.08f), strength = rng.Range(0.6f, 1f), seed = (uint)rng.Int(1 << 20) });
        }

        void DrawMinis(ElectroArcs arcs, float now)
        {
            int frame = Mathf.FloorToInt(now * 30f);
            for (int i = minis.Count - 1; i >= 0; i--)
            {
                var m = minis[i];
                if (now >= m.until) { minis.RemoveAt(i); continue; }
                var r = new ElectroBoltShape.Rng(ElectroBoltShape.Hash((int)m.seed, frame, i));
                Vector3 A, B; float bulge = m.bulge; var bulgeDir = Up;
                switch (m.kind)
                {
                    case 0:
                        A = CoilTop(m.a);
                        B = m.b < 2 ? CoilBall(m.a, m.b) : CoilTop(m.a) + new Vector3(r.Range(-1f, 1f), r.Range(-0.3f, 1f), r.Range(-1f, 1f)).normalized * 0.18f + Up * 0.08f;   // re-picked with the shape (30 Hz), never per frame
                        bulge = 0.2f; bulgeDir = (B - A).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.Cross(B - A, Up), B - A).normalized : Up;
                        if (Vector3.Dot(bulgeDir, Up) < 0f) bulgeDir = -bulgeDir;
                        break;
                    case 1: A = CoilTop(0); B = CoilTop(1); break;
                    default: A = RingPoint(m.a); B = EmitterPos; bulge = 0.1f; bulgeDir = AimDir; break;
                }
                int n = ElectroBoltShape.Jag(ref r, 3, m.kind == 1 ? 0.12f : 0.2f, 0.6f, t, off);
                ElectroBoltShape.Place(A, B, t, off, n, p, 0f, 0f, 0f, bulge, bulgeDir);
                float k = 1f - (now - m.born) / Mathf.Max(0.01f, m.until - m.born) * 0.5f;
                arcs.Ribbon(p, n, m.width, 0.22f, 8f, 1.5f, OpenZElectroFx.Glow, m.strength * k, i * 1.3f, 0.6f, 0.6f, 2f);
                if (m.kind == 1) arcs.Glow((A + B) * 0.5f + Up * 0.3f, 0.6f, 0.3f, 6f, 1.3f, OpenZElectroFx.Glow, 0.6f * k);
            }
        }
    }
}

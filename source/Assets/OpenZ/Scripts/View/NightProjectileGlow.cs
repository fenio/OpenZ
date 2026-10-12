using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// sparkle trails, muzzle flashes and their pooled lights: reused, untouched); at night this adds, in ONE additive draw
    /// (OpenZ/VehicleGlow, mode 2 cores + mode 3 streaks, rebuilt every frame):
    /// <list type="bullet">
    /// <item>tank shells / cannon shells: a hot orange core at the base + a short glowing streak along the flight</item>
    /// <item>rockets (bazooka, MLRS, missile cannon): a big exhaust-coloured core + a long exhaust streak</item>
    /// <item></item>
    /// <item>rifle / machine-gun / sniper tracers: a thin warm sleeve</item>
    /// <item>grenades (FX port flights and GrenadeFx throws): a small red-orange fuse glow + an ember trail; never white
    /// (the project rules "grenades never white": saturated colour, low gain)</item>
    /// </list>
    /// Scaled by NightGrade.NightK (DayNight): nothing by day unless dayShare > 0. Hooks (one-line patches):
    /// OpenZFxPlayer.ProjectileDrawn / ShotTraced, GrenadeFx.FuseDrawn. No real lights (the budget is for the lamps and the
    /// headlights; the FX port's muzzle / launch / impact flashes already light the ground).
    /// </summary>
    [DefaultExecutionOrder(120)]   // after OpenZFxPlayer (110, StepProjectiles in LateUpdate) and GrenadeFx (101)
    public class NightProjectileGlow : MonoBehaviour
    {
        public static NightProjectileGlow I { get; private set; }

        [Range(0f, 4f)] public float gain = 1f;
        [Tooltip("Glow by day as a share of the night glow (0 = night only)")] [Range(0f, 1f)] public float dayShare = 0f;
        /// <summary>
        /// Rockets (MLRS, missile cannon, Tough bazooka, mech arm rockets) keep this share of their night
        /// motor glow + exhaust streak by day, so the hot motor blooms in daylight too (shells, bolts, tracers stay night only).</summary>
        [Tooltip("Rocket motor glow by day as a share of the night glow")] [Range(0f, 1f)] public float rocketDayShare = 0.75f;
        /// <summary>Proof: rocket glows pushed by day (NightK below 0.5).</summary>
        public static int RocketDayGlows;
        public bool shells = true, rockets = true, bolts = true, tracers = true, grenades = true;
        [Range(16, 512)] public int maxStreaks = 256;

        public int LastCount { get; private set; }
        public int Peak { get; private set; }

        struct Streak { public Vector3 head, dir; public float len, width, core, coreBack; public Color col; }
        struct Shot { public Vector3 from, to; public float t0, life; public FxWeapon w; }

        readonly List<Streak> frame = new List<Streak>(256);
        readonly List<Shot> shots = new List<Shot>(256);
        readonly List<Vector3> v = new List<Vector3>(1024), n = new List<Vector3>(1024);
        readonly List<Vector4> u0 = new List<Vector4>(1024), u1 = new List<Vector4>(1024);
        readonly List<int> t = new List<int>(1536);
        Mesh mesh; Material mat; Camera cam;

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            mat = VehicleLightMeshes.GlowMaterial("VehicleGlow (projectiles)");
            mesh = new Mesh { name = "VL_ProjectileGlow", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
        }

        void OnEnable()
        {
            OpenZFxPlayer.ProjectileDrawn += OnProjectile;
            OpenZFxPlayer.ShotTraced += OnShot;
            GrenadeFx.FuseDrawn += OnFuse;
        }

        void OnDisable()
        {
            OpenZFxPlayer.ProjectileDrawn -= OnProjectile;
            OpenZFxPlayer.ShotTraced -= OnShot;
            GrenadeFx.FuseDrawn -= OnFuse;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mesh) Destroy(mesh);
            if (mat) Destroy(mat);
        }

        float K => gain * Mathf.Lerp(dayShare, 1f, NightGrade.NightK);
        float KRocket => gain * Mathf.Lerp(Mathf.Max(dayShare, rocketDayShare), 1f, NightGrade.NightK);

        static readonly Color ShellCol = new Color(1f, 0.42f, 0.1f), RocketCol = new Color(1f, 0.55f, 0.16f), GrenadeCol = new Color(1f, 0.3f, 0.06f),
            BoltCol = new Color(1f, 0.25f, 0.015f), TracerCol = new Color(1f, 0.42f, 0.06f);

        void Push(Vector3 head, Vector3 dir, float len, float width, float core, float coreBack, Color col)
        {
            if (frame.Count >= maxStreaks) return;
            frame.Add(new Streak { head = head, dir = dir, len = len, width = width, core = core, coreBack = coreBack, col = col });
        }

        void OnProjectile(FxWeapon w, Vector3 pos, Vector3 vel, float age)
        {
            bool rocket = w == FxWeapon.Bazooka || w == FxWeapon.Rocket || w == FxWeapon.Mlrs;
            float k = rocket ? KRocket : K; if (k <= 0.001f) return;
            if (rocket && NightGrade.NightK < 0.5f) RocketDayGlows++;
            var dir = vel.sqrMagnitude > 1e-6f ? vel.normalized : Vector3.forward;
            float ramp = Mathf.Clamp01(age / 0.12f);                                              // grows out of the muzzle flash
            switch (w)
            {
                case FxWeapon.TankShell: case FxWeapon.HeavyShell: case FxWeapon.Cannon:
                    if (shells) Push(pos, dir, 3.2f * ramp, w == FxWeapon.HeavyShell ? 0.255f : 0.24f, w == FxWeapon.HeavyShell ? 0.36f : 0.34f, 0.35f, ShellCol * (2.2f * k));
                    break;
                case FxWeapon.Bazooka: case FxWeapon.Rocket: case FxWeapon.Mlrs:
                    if (rockets) Push(pos, dir, 5f * ramp, 0.42f, 0.6f, 0.55f, RocketCol * (2.8f * k));
                    break;
                case FxWeapon.Grenade:
                    if (grenades) Push(pos, dir, 1.0f * ramp, 0.08f, 0.2f, 0f, GrenadeCol * (1.3f * k));
                    break;
            }
        }

        void OnShot(Vector3 from, Vector3 to, float life, FxWeapon w)
        {
            if (K <= 0.001f || shots.Count >= maxStreaks) return;
            if (w == FxWeapon.Bolt ? !bolts : !tracers) return;
            shots.Add(new Shot { from = from, to = to, t0 = Time.time, life = Mathf.Max(0.02f, life), w = w });
        }

        void OnFuse(Vector3 tip, Vector3 vel)
        {
            float k = K; if (k <= 0.001f || !grenades) return;
            bool flying = vel.sqrMagnitude > 0.01f;
            Push(tip, flying ? vel.normalized : Vector3.up, flying ? 0.9f : 0f, 0.07f, 0.16f, 0f, GrenadeCol * (1.2f * k));
        }

        void LateUpdate()
        {
            float now = Time.time, k = K;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i]; float a = (now - s.t0) / s.life;
                if (a >= 1f || k <= 0.001f) { shots.RemoveAt(i); continue; }
                var d = s.to - s.from; float dist = d.magnitude; if (dist < 0.05f) { shots.RemoveAt(i); continue; }
                var dir = d / dist; var head = s.from + d * Mathf.Clamp01(a);
                float travelled = dist * Mathf.Clamp01(a);
                if (s.w == FxWeapon.Bolt) Push(head, dir, Mathf.Min(2.4f, travelled), 0.22f, 0.3f, 0f, BoltCol * (3.2f * k));
                else Push(head, dir, Mathf.Min(2.6f, travelled), s.w == FxWeapon.Sniper ? 0.08f : 0.1f, 0.12f, 0f, TracerCol * (2f * k));
            }
            LastCount = frame.Count; Peak = Mathf.Max(Peak, frame.Count);
            if (frame.Count == 0) return;
            if (!cam) cam = Camera.main;
            var camPos = cam ? cam.transform.position : Vector3.zero;
            v.Clear(); n.Clear(); u0.Clear(); u1.Clear(); t.Clear();
            var b = new Bounds(frame[0].head, Vector3.zero);
            foreach (var s in frame)
            {
                var c = new Vector4(s.col.r, s.col.g, s.col.b, 8f);                                // channel 8 = constant
                if (s.len > 0.05f)
                {   // streak: camera-facing quad from the tail (y 0) to the head (y 1)
                    var tail = s.head - s.dir * s.len;
                    var side = Vector3.Cross(s.dir, camPos - s.head);
                    side = side.sqrMagnitude > 1e-6f ? side.normalized * (s.width * 0.5f) : Vector3.right * (s.width * 0.5f);
                    int i0 = v.Count;
                    v.Add(tail - side); v.Add(tail + side); v.Add(s.head + side); v.Add(s.head - side);
                    u0.Add(new Vector4(-1, 0, 0, 3)); u0.Add(new Vector4(1, 0, 0, 3)); u0.Add(new Vector4(1, 1, 0, 3)); u0.Add(new Vector4(-1, 1, 0, 3));
                    for (int k2 = 0; k2 < 4; k2++) { n.Add(Vector3.zero); u1.Add(c); }
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                    b.Encapsulate(tail);
                }
                if (s.core > 0.01f)
                {   // hot core billboard (at the base for shells / rockets: the burning tracer / motor)
                    var p = s.head - s.dir * s.coreBack;
                    int i0 = v.Count;
                    for (int k2 = 0; k2 < 4; k2++)
                    {
                        v.Add(p); n.Add(Vector3.zero); u1.Add(new Vector4(c.x * 1.3f, c.y * 1.3f, c.z * 1.3f, 8f));
                    }
                    u0.Add(new Vector4(-1, -1, s.core, 2)); u0.Add(new Vector4(1, -1, s.core, 2)); u0.Add(new Vector4(1, 1, s.core, 2)); u0.Add(new Vector4(-1, 1, s.core, 2));
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                }
                b.Encapsulate(s.head);
            }
            frame.Clear();
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, u0); mesh.SetUVs(1, u1); mesh.SetTriangles(t, 0, false);
            b.Expand(2f);
            mesh.bounds = b;
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }

        public string Status() => $"projectile night glow: {LastCount} this frame (peak {Peak}), {shots.Count} shots in flight, x{K:F2} (night {NightGrade.NightK:F2})";
    }
}

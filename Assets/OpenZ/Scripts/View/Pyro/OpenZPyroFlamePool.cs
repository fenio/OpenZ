using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// OpenZ/PyroFlame expands each sprite into a view-space quad (rotation + scale), picks the 4x2 flipbook frame, erodes it with
    /// the noise, blurs the sticky licks and fades the whole sprite before it meets its floor.
    /// kept only the muzzle floor), seed, HDR boost, softness (0 = crisp jet, 1 = blurred sticky lick) and an optional anchor
    /// (sticky fire rides the bone / hull it was spawned on, and dies when that anchor is hidden).
    /// Jet sprites fly straight with a buoyancy of .6 m/s^2; sticky sprites take drag exp(-2.6 dt) and rise at 3.4 m/s^2.
    /// Sprites never sink under their floor (+.22 m): downward fire licks along the ground.
    /// full range, and when it has travelled to the target it splashes there: its speed drops to 12 %, it spreads out sideways and
    /// up, and burns out in place (the burst on the target).
    /// </summary>
    public sealed class OpenZPyroFlamePool
    {
        public const int Frames = 8;
        /// <summary>The splash on the target. Round 1 kept 12 % of the speed and aged the splashed sprites 2.5x: a tight white burst,
        /// as is). It never reaches past the Pyro's 15 m attack range. previews/pyro_fire_v2_r2_closeup_t1.2_legacy_vs_port.png:
        /// </summary>
        public const float SplashAgeRate = 1.0f, SplashSpreadMin = 1.2f, SplashSpreadMax = 3.0f, SplashKeep = 0.6f;
        /// <summary>
        /// </summary>
        public float splashAgeRate = SplashAgeRate, splashKeep = SplashKeep;
        public readonly int Capacity;
        public int Count { get; private set; }
        public int Dropped { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        struct Vert { public Vector3 pos; public Vector4 d0, d1, d2; }   // d0 corner.xy scale rot, d1 frame alpha floorY lifeT, d2 seed boost soft 0

        readonly Vector3[] pos, vel, anchorLocal, anchorAt, origin;
        readonly float[] age, life, rot, rotV, baseScale, floor0, floor1, seed, boost, soft, splash;
        /// <summary>
        /// Per jet sprite the arc height
        /// h (m) over its straight stream (ShotClearance.ArcHeight), its horizontal share of the speed and the lift already applied: the sprite
        /// rises 4 h u (1 - u) at u = the share of the way to its splash point, so it leaves the nozzle, climbs over the bank and lands on the
        /// target (u = 1: no lift).</summary>
        readonly float[] arcH, arcHf, arcNow;
        readonly Transform[] anchor;
        readonly Vert[] verts;
        readonly Mesh mesh;
        static readonly Vector2[] Corners = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        public OpenZPyroFlamePool(int capacity)
        {
            Capacity = capacity;
            pos = new Vector3[capacity]; vel = new Vector3[capacity]; anchorLocal = new Vector3[capacity]; anchorAt = new Vector3[capacity]; origin = new Vector3[capacity];
            splash = new float[capacity];
            arcH = new float[capacity]; arcHf = new float[capacity]; arcNow = new float[capacity];
            age = new float[capacity]; life = new float[capacity]; rot = new float[capacity]; rotV = new float[capacity]; baseScale = new float[capacity];
            floor0 = new float[capacity]; floor1 = new float[capacity]; seed = new float[capacity]; boost = new float[capacity]; soft = new float[capacity];
            anchor = new Transform[capacity];
            verts = new Vert[capacity * 4];
            mesh = new Mesh { name = "OZ_PyroFlamePool" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(capacity * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
            var idx = new ushort[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, k = i * 6;
                idx[k] = (ushort)v; idx[k + 1] = (ushort)(v + 1); idx[k + 2] = (ushort)(v + 2);
                idx[k + 3] = (ushort)v; idx[k + 4] = (ushort)(v + 2); idx[k + 5] = (ushort)(v + 3);
            }
            mesh.SetIndexBufferParams(capacity * 6, IndexFormat.UInt16);
            mesh.SetIndexBufferData(idx, 0, 0, idx.Length, Quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), Quiet);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
        }

        public float Fill => Capacity > 0 ? Count / (float)Capacity : 1f;

        /// <summary>spawnFlameJetSprite(px, py, pz, vx, vy, vz, life, baseScale, floorY, boost, spin, soft, anchor).
        /// `floorEnd` = the floor this sprite slides to by the end of its life (jet: ground under the jet end); `spin` &lt; 0 = the
        /// jet's own churn (+-1.6 rad/s).</summary>
        public bool Spawn(Vector3 p, Vector3 v, float lifeS, float scale, float floorStart, float floorEnd, float hdrBoost = 0f,
                          float spin = -1f, float softness = 0f, Transform anchorT = null, float rnd0 = -1f, float rnd1 = -1f, float rnd2 = -1f,
                          float splashDist = 0f, float arc = 0f)
        {
            if (Count >= Capacity) { Dropped++; return false; }
            int i = Count++;
            anchor[i] = anchorT;
            if (anchorT) { anchorAt[i] = p; anchorLocal[i] = anchorT.InverseTransformPoint(p); }
            pos[i] = p; vel[i] = v; age[i] = 0f; life[i] = Mathf.Max(0.02f, lifeS);
            rot[i] = (rnd0 >= 0f ? rnd0 : Random.value) * Mathf.PI * 2f;
            rotV[i] = ((rnd1 >= 0f ? rnd1 : Random.value) - 0.5f) * (spin < 0f ? 3.2f : spin);
            baseScale[i] = scale; floor0[i] = floorStart; floor1[i] = floorEnd;
            seed[i] = rnd2 >= 0f ? rnd2 : Random.value; boost[i] = hdrBoost; soft[i] = softness;
            origin[i] = p; splash[i] = splashDist;
            arcH[i] = splashDist > 0f ? Mathf.Max(0f, arc) : 0f; arcNow[i] = 0f;
            float vm = v.magnitude; arcHf[i] = vm > 1e-4f ? Mathf.Sqrt(v.x * v.x + v.z * v.z) / vm : 1f;
            return true;
        }

        static bool AnchorVisible(Transform t) => t && t.gameObject.activeInHierarchy;

        /// <summary>updateFlameJet(dt): ages, moves (anchored sprites ride their anchor), compacts and writes the vertex stream.</summary>
        public void Step(float dt)
        {
            float drag = Mathf.Exp(-dt * 2.6f);
            bool blend = OzFxSmooth.On;                                             // MOTION BLUR option: the flipbook blends to the next frame instead of stepping
            int write = 0;
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int i = 0; i < Count; i++)
            {
                float a = age[i] + dt * (splash[i] < 0f ? splashAgeRate : 1f);      // a splashed sprite burns out fast (Zod's EPyroFire: .24-.36 s)
                var an = anchor[i];
                if (a >= life[i] || (!ReferenceEquals(an, null) && !AnchorVisible(an))) continue;
                Vector3 d = Vector3.zero;
                if (!ReferenceEquals(an, null))
                {
                    var now = an.TransformPoint(anchorLocal[i]);
                    d = now - anchorAt[i]; anchorAt[i] = now;
                }
                bool sticky = soft[i] > 0.001f; float flight = sticky ? drag : 1f;
                var v = vel[i];
                float sp = splash[i];
                if (sp > 0f && (pos[i] - origin[i]).sqrMagnitude >= sp * sp)
                {   // reached its target: the burst (speed x .12, a sideways / upward spread from the seed), once
                    var fwd = v.normalized; var side = Vector3.Cross(fwd, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                    float a0 = seed[i] * 6.2832f, k = Mathf.Lerp(SplashSpreadMin, SplashSpreadMax, Mathf.Repeat(seed[i] * 7.31f, 1f));
                    v = v * splashKeep + (side * Mathf.Cos(a0) + Vector3.up * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(a0)))) * k;
                    sp = -1f;
                }
                v.x *= flight; v.z *= flight;
                v.y = v.y * flight + (sticky ? 3.4f : 0.6f) * dt;
                float t = a / life[i];
                float f0 = floor0[i] + d.y, f1 = floor1[i] + d.y;
                // the floor under the sprite: jet sprites slide from the muzzle's ground to the target's by the distance travelled
                float u = sp > 0f ? Mathf.Clamp01((pos[i] - origin[i]).magnitude / sp) : sp < 0f ? 1f : t;
                float fl = Mathf.Lerp(f0, f1, u);
                var p = pos[i];
                p.x += v.x * dt + d.x; p.z += v.z * dt + d.z;
                p.y = Mathf.Max(p.y + v.y * dt + d.y, fl + 0.22f);                // slide along the floor instead of sinking through it
                float ah = arcH[i], arcN = arcNow[i];
                if (ah > 0f)
                {
                    float hx = p.x - origin[i].x, hz = p.z - origin[i].z, spH = Mathf.Abs(splash[i] > 0f ? splash[i] : sp) * Mathf.Max(0.05f, arcHf[i]);
                    float uu = sp < 0f ? 1f : Mathf.Clamp01(Mathf.Sqrt(hx * hx + hz * hz) / Mathf.Max(0.05f, spH));
                    float b = 4f * ah * uu * (1f - uu);
                    p.y += b - arcN; arcN = b;
                    if (sp < 0f && b <= 0.001f) ah = 0f;
                }
                // compact
                anchor[write] = an; anchorLocal[write] = anchorLocal[i]; anchorAt[write] = anchorAt[i];
                pos[write] = p; vel[write] = v; age[write] = a; life[write] = life[i];
                rot[write] = rot[i] + rotV[i] * dt; rotV[write] = rotV[i]; baseScale[write] = baseScale[i];
                floor0[write] = f0; floor1[write] = f1; seed[write] = seed[i]; boost[write] = boost[i]; soft[write] = soft[i];
                origin[write] = origin[i]; splash[write] = sp;
                arcH[write] = ah; arcHf[write] = arcHf[i]; arcNow[write] = arcN;
                // vertex data
                float scale = baseScale[write] * (0.55f + t * 2.9f);                  // GROWS like the demo
                float frame = blend ? Mathf.Min(Frames - 0.001f, t * Frames) : Mathf.Min(Frames - 1, Mathf.Floor(t * Frames));   // whole part = the frame, fraction = blend to the next (OpenZ/PyroFlame)
                float alpha = (t < 0.07f ? t / 0.07f : 1f) * (sticky ? 1f - t * t : 1f - SmoothStep(0.82f, 1f, t)) * 0.95f;
                var d0z = new Vector4(0f, 0f, scale, rot[write]);
                var d1 = new Vector4(frame, alpha, fl, t);
                var d2 = new Vector4(seed[write], boost[write], soft[write], 0f);
                int vb = write * 4;
                for (int c = 0; c < 4; c++)
                {
                    d0z.x = Corners[c].x; d0z.y = Corners[c].y;
                    verts[vb + c] = new Vert { pos = p, d0 = d0z, d1 = d1, d2 = d2 };
                }
                float r = scale * 0.75f;
                lo = Vector3.Min(lo, p - new Vector3(r, r, r)); hi = Vector3.Max(hi, p + new Vector3(r, r, r));
                write++;
            }
            for (int i = write; i < Count; i++) anchor[i] = null;
            Count = write;
            if (Count > 0)
            {
                mesh.SetVertexBufferData(verts, 0, 0, Count * 4, 0, Quiet);
                var sm = new SubMeshDescriptor(0, Count * 6, MeshTopology.Triangles) { firstVertex = 0, vertexCount = Count * 4 };
                mesh.SetSubMesh(0, sm, Quiet);
                var b = new Bounds(); b.SetMinMax(lo, hi); mesh.bounds = b;
            }
        }

        public static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Kills every sprite anchored under `root` (a unit that left the map / was hidden for good).</summary>
        public void RemoveAnchored(Transform root)
        {
            if (!root) return;
            for (int i = 0; i < Count; i++)
            {
                var an = anchor[i]; if (ReferenceEquals(an, null) || !an) continue;
                if (an == root || an.IsChildOf(root)) { life[i] = 0f; anchor[i] = null; }
            }
        }

        public void Clear() { for (int i = 0; i < Count; i++) anchor[i] = null; Count = 0; }

        public void Draw(Material mat)
        {
            if (Count == 0 || !mat) return;
            var rp = new RenderParams(mat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = mesh.bounds };
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
        }

        public void Dispose() { if (mesh) Object.Destroy(mesh); }
    }
}

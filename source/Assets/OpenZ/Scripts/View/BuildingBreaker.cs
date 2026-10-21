using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// intact model is swapped for its pre-fractured cells (Resources/OpenZ/Fractures), each a rigid body launched out of the
    /// .28 / angular .95; ground contacts raise dust and play the collapse bank (cooldown per piece, 5 contacts max); each
    /// piece lives 9-17 s (seeded), then pops out, except pieces that settle inside the footprint, which stay as rubble
    /// with the charred, collapsed stump, fire and a lingering demolition cloud. Visual only: the sim keeps the ruin.
    /// Revive (auto-repair or crane) clears the rubble and raises the building again.
    /// </summary>
    public partial class BuildingBreaker : MonoBehaviour                                   // endings_v1: + BuildingBreaker.Ruin.cs
    {
        static int debrisLayer = -1;
        static float globalSoundBudget; static int soundFrame;
        ZObject obj; GameObject fracture; readonly List<Renderer> intact = new List<Renderer>();
        Vector3 restScale; bool collapsed;
        readonly List<Piece> pieces = new List<Piece>();
        float fireUntil; readonly List<Transform> fireSpots = new List<Transform>();

        static string Key(string type) => type == "fort_front" || type == "fort_back" ? "fort" : type;
        static PhysicsMaterial bouncy;
        static PhysicsMaterial Bouncy() => bouncy ??= new PhysicsMaterial("DebrisBouncy") { bounciness = 0.38f, dynamicFriction = 0.55f, staticFriction = 0.7f, bounceCombine = PhysicsMaterialCombine.Maximum };
        /// <summary>
        /// The big model parts are heavy concrete: almost no bounce, high friction (they tumble, thud and stay).</summary>
        static PhysicsMaterial concrete;
        static PhysicsMaterial Concrete() => concrete ??= new PhysicsMaterial("DebrisConcrete") { bounciness = 0.04f, dynamicFriction = 0.8f, staticFriction = 0.95f, bounceCombine = PhysicsMaterialCombine.Minimum, frictionCombine = PhysicsMaterialCombine.Maximum };

        public static BuildingBreaker For(GameObject go, ZObject o)
        {
            var b = go.GetComponent<BuildingBreaker>(); if (!b) b = go.AddComponent<BuildingBreaker>();
            b.obj = o; return b;
        }

        public static int DebrisLayer => GetDebrisLayer();
        static int GetDebrisLayer()
        {
            if (debrisLayer >= 0) return debrisLayer;
            debrisLayer = 31;                                                                   // ZGame's walkable layer takes a lower free one
            if (debrisLayer < 0) debrisLayer = 0;
            Physics.IgnoreLayerCollision(debrisLayer, debrisLayer, true);                       // cosmetic cells contact the world only
            return debrisLayer;
        }

        /// <summary>Returns false when this building has no fracture cells (the caller falls back to charring).</summary>
        public bool Collapse(Vector3 blast, float power = 1f)
        {
            if (collapsed) return true;
            if (CollapseV1(blast, power)) return true;                                         // endings_v1: prepared set, ruin, big parts, chunk look
            var pf = Resources.Load<GameObject>("OpenZ/Fractures/F_" + Key(obj.Type));
            if (!pf) return false;
            collapsed = true;
            var t = transform;
            intact.Clear(); foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled) { intact.Add(r); r.enabled = false; }
            fracture = Instantiate(pf, t.position, t.rotation);
            fracture.name = name + "_fracture";
            fracture.transform.localScale = t.lossyScale;
            var tp = fracture.GetComponent<TeamPaint>(); if (tp) { tp.enabled = false; Destroy(tp); }
            var palette = Palette(intact, obj.Type);
            var accent = TeamColors.Of(obj.Owner); bool owned = obj.Owner != Team.Neutral;
            int layer = GetDebrisLayer();
            var game = ZGame.I;
            var bounds = new Bounds(t.position, Vector3.zero);
            foreach (Transform c in fracture.transform) bounds.Encapsulate(c.GetComponent<Renderer>().bounds);
            float h = Mathf.Max(1f, bounds.size.y), size = Mathf.Max(bounds.size.x, bounds.size.z);
            var center = new Vector3(blast.x, t.position.y + h * 0.15f, blast.z);
            int i = 0;
            foreach (Transform c in fracture.transform)
            {
                var go = c.gameObject; go.layer = layer;
                go.GetComponent<Renderer>().motionVectorGenerationMode = MotionVectorGenerationMode.Object;   // flying chunks blur
                Panelize(go, i, palette, accent, owned);
                var mc = go.AddComponent<BoxCollider>(); mc.sharedMaterial = Bouncy();
                var rb = go.AddComponent<Rigidbody>();
                var sz = go.GetComponent<Renderer>().bounds.size;
                rb.mass = Mathf.Clamp(sz.x * sz.y * sz.z * 5f, 0.8f, 160f);                               // pieceFrom(): volume x 5, .8..160
                rb.linearDamping = 0.28f; rb.angularDamping = 0.95f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                float up01 = Mathf.Clamp01((c.position.y - t.position.y) / h);
                var away = c.position - center; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                float kick = power * (0.6f + 0.4f * size / 10f);
                rb.linearVelocity = away * Random.Range(2f, 6.5f) * kick * (0.5f + up01) + Vector3.up * Random.Range(6f, 12.5f) * kick * (0.45f + up01);   // thrown high
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(2f, 7f);
                rb.maxAngularVelocity = 14f;
                var p = go.AddComponent<Piece>();
                p.owner = this; p.rb = rb; p.life = Lifetime(obj.Id, i++); p.footprint = new Bounds(t.position, new Vector3(size * 0.8f, 100f, size * 0.8f));
                pieces.Add(p);
            }
            // fire, dust, sound
            var fx = FxManager.I; var au = CombatAudio.I;
            if (fx)
            {
                fx.DemolitionDust(t.position, bounds.size.x, bounds.size.z, Mathf.Clamp(size / 10f, 0.7f, 1.8f));
                StartCoroutine(Blasts(bounds, Mathf.Clamp(Mathf.RoundToInt(size), 5, 14)));
            }
            bool big = obj.IsFort, small = obj.Type == "radar";
            au?.Play("collapse", t.position, 1f, 0, obj.Id, big ? new[] { "v3-collapse-large-a", "v3-collapse-large-b", "building-collapse-20260909" }
                                                          : small ? new[] { "v3-collapse-small-a", "v3-collapse-medium-a" } : new[] { "v3-collapse-medium-a", "v3-collapse-medium-b", "building-collapse-20260909" });
            StartCoroutine(Settle(t, h));
            fireUntil = Time.time + 14f;
            RuinFireBegin(bounds);                                                               // ground fires and smoke in the ruin for a while (BuildingBreaker.RuinFire.cs)
            return true;
        }

        // The Voronoi cells (~2.3 m convex blobs whose UVs hit flat grey areas) read as grey boulders. Each cell now becomes a
        // wall PANEL (its two long sides, thin third side) or, every third one, a chunky block, coloured from the building's
        // own texture (cream walls, dark trim; imported olive paint turned to neutral khaki like TeamLit's degreen), about one
        // piece in eight in the owner's team accent, slightly charred. Same physics as before (thrown high, bounce, settle).
        static Mesh cube;
        static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Color>> palettes = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Color>>();
        static readonly System.Collections.Generic.Dictionary<Material, Material> debrisMats = new System.Collections.Generic.Dictionary<Material, Material>();

        static System.Collections.Generic.List<Color> Palette(System.Collections.Generic.List<Renderer> rs, string key)
        {
            if (palettes.TryGetValue(key, out var cached)) return cached;
            var list = new System.Collections.Generic.List<Color>();
            foreach (var r in rs)
            {
                var m = r ? r.sharedMaterial : null; if (!m) continue;
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                Color tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                if (!tex) { list.Add(tint); continue; }
                var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t2 = new Texture2D(16, 16, TextureFormat.RGBA32, false); t2.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); t2.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                foreach (var px in t2.GetPixels())
                {
                    var c = px * tint; if (c.a < 0.3f) continue;
                    if (c.g > c.r * 1.02f && c.g > c.b * 1.1f) { float l = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b; c = new Color(l * 1.08f, l * 1.03f, l * 0.92f); }   // olive paint -> khaki
                    c.a = 1f; list.Add(c);
                }
                Destroy(t2);
            }
            if (list.Count == 0) list.Add(new Color(0.82f, 0.78f, 0.68f));
            list.Sort((a, b) => (a.r + a.g + a.b).CompareTo(b.r + b.g + b.b));                   // dark trim first, light walls last
            palettes[key] = list; return list;
        }

        static void Panelize(GameObject go, int i, System.Collections.Generic.List<Color> pal, Color accent, bool owned)
        {
            var mf = go.GetComponent<MeshFilter>(); var mr = go.GetComponent<MeshRenderer>(); if (!mf || !mr || !mf.sharedMesh) return;
            var b = mf.sharedMesh.bounds; var t = go.transform;
            t.position = t.TransformPoint(b.center);
            float[] d = { b.size.x, b.size.y, b.size.z };
            int thin = 0; for (int k = 1; k < 3; k++) if (d[k] < d[thin]) thin = k;
            Vector3 sc;
            if (i % 3 == 2) { float m = Mathf.Clamp(Mathf.Max(d[0], Mathf.Max(d[1], d[2])) * 0.38f, 0.35f, 1.0f); sc = new Vector3(m, m * 0.8f, m * 0.9f); }   // chunky block
            else { sc = new Vector3(d[0] * 0.6f, d[1] * 0.6f, d[2] * 0.6f); sc[thin] = Mathf.Clamp(d[thin] * 0.25f, 0.1f, 0.22f); }                       // wall panel
            if (!cube) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            sc *= 0.7f + 0.6f * (((i * 0.7548776f) % 1f));                                              // critic: pieces all the same size
            mf.sharedMesh = cube; t.localScale = Vector3.Scale(t.localScale, sc);
            // gate re-check: a cream building gave white "sugar cubes": every 4th piece from the darkest quarter (trim, frames),
            // the rest from the lighter part, all pulled down ~20 % (cream blows out under the desert sun) and charred 20-45 %
            int n = pal.Count, q = Mathf.Max(1, n / 4);
            var c = owned && i % 8 == 3 ? accent : i % 4 == 1 ? pal[(i * 37 + 11) % q] : pal[q + (i * 37 + 11) % Mathf.Max(1, n - q)];
            c *= 0.8f; c.a = 1f;
            c = Color.Lerp(c, new Color(0.16f, 0.14f, 0.12f), 0.2f + 0.25f * ((i * 13) % 5) / 4f);
            var src = mr.sharedMaterial;
            if (src && !debrisMats.TryGetValue(src, out var dm))
            {   // v2: keep the building's texture and its own team mode: olive paint -> neutral khaki like the intact building
                // (team mode 0 left the imported olive camo green: "never a green team")
                dm = new Material(src) { name = src.name + "_debris" };
                debrisMats[src] = dm;
            }
            if (src) mr.sharedMaterial = debrisMats[src];
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_TeamColor", owned ? accent.linear : TeamColors.Neutral.linear * 3f);             // = TeamPaint (neutral lifted x3)
            bool accentPiece = owned && i % 8 == 3;
            float charK = 0.18f + 0.22f * ((i * 13) % 5) / 4f;
            if (accentPiece) { mpb.SetTexture("_BaseMap", Texture2D.whiteTexture); mpb.SetColor("_BaseColor", Color.Lerp(accent, Color.black, charK)); }
            else
            {   // a random quarter-size slice of the building texture on every face: real panel detail, not flat chips
                float u = ((i * 0.6180339f) % 1f) * 0.75f, v = ((i * 0.4142135f + 0.21f) % 1f) * 0.75f;
                mpb.SetVector("_BaseMap_ST", new Vector4(0.25f, 0.25f, u, v));
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.3f, 0.27f, 0.24f), charK));
            }
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>debrisLifetime: 9..17 s, seeded per fragment.</summary>
        static float Lifetime(int id, int index)
        {
            uint seed = 2166136261; foreach (char ch in id.ToString()) seed = (seed ^ ch) * 16777619;
            seed = (seed ^ (uint)(index + 1)) * 2246822519;
            return 9f + seed / 4294967296f * 8f;
        }

        IEnumerator Blasts(Bounds b, int n)
        {
            var fx = FxManager.I;
            for (int i = 0; i < n && fx; i++)
            {
                var p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.min.y + b.size.y * 0.7f), Random.Range(b.min.z, b.max.z));
                fx.Explosion(p, Random.Range(0.9f, 1.4f));
                yield return new WaitForSeconds(Random.Range(0.06f, 0.2f));
            }
        }

        /// <summary>The ruin: after the cells have fallen, the charred intact model reappears collapsed to a low stump.</summary>
        IEnumerator Settle(Transform t, float h)
        {
            yield return new WaitForSeconds(1.1f);
            if (!collapsed) yield break;
            restScale = t.localScale;
            var s = restScale; s.y *= 0.24f; t.localScale = s;
            var tp = GetComponent<TeamPaint>(); if (tp) tp.Charred = 0.9f;
            foreach (var r in intact) if (r) r.enabled = true;
            yield return new WaitForSeconds(1.2f);
            CombatAudio.I?.Play("settle", t.position, 0.8f, 0, obj.Id, "building-settle-20260909");
        }

        /// <summary></summary>
        public bool Collapsed => collapsed;
        public void Restore()
        {
            if (!collapsed) return;
            RuinFireEnd();                                                                     // it stands again: the ruin's fire goes at once
            if (v1) { RestoreV1(); return; }                                                   // endings_v1
            collapsed = false;
            StopAllCoroutines();
            if (restScale != Vector3.zero) transform.localScale = restScale;
            foreach (var r in intact) if (r) r.enabled = true;
            var tp = GetComponent<TeamPaint>(); if (tp) tp.Charred = 0f;
            if (fracture)
            {
                foreach (var pc in pieces) if (pc) PopOut.Begin(pc.transform, PopOut.Style.Debris, null, false, false);
                Destroy(fracture, PopOut.Duration + 0.05f);
            }
            pieces.Clear(); fireUntil = 0;
            FxManager.I?.DemolitionDust(transform.position, 6f, 6f, 0.6f);
        }

        float nextFire;
        void Update()
        {
            if (!collapsed || Time.time > fireUntil || Time.time < nextFire || !FxManager.I) return;
            nextFire = Time.time + 0.18f;
            pieces.RemoveAll(p => !p);
            if (pieces.Count == 0) return;
            var p = pieces[Random.Range(0, pieces.Count)];
            if (OzWaterLanding.InWater(p.transform)) return;                                     // b27: a piece that lies in water never burns
            float k = Mathf.Clamp01((fireUntil - Time.time) / fireSpan);                         // endings_v1: the fort burns 28 s
            FxManager.I.Burn(p.transform.position + Vector3.up * 0.3f, 0.5f + 0.6f * k);
        }

        internal bool TrySound()
        {
            if (soundFrame != Time.frameCount) { soundFrame = Time.frameCount; globalSoundBudget = Mathf.Min(3f, globalSoundBudget + Time.deltaTime * 6f); }
            if (globalSoundBudget < 1f) return false;
            globalSoundBudget -= 1f; return true;
        }

        /// <summary>One rigid cell: contact dust + sound (age > .14 s, speed > 1.4, .3 s apart, 5 contacts max), then retire.</summary>
        public class Piece : MonoBehaviour
        {
            public BuildingBreaker owner; public Rigidbody rb; public float life; public Bounds footprint;
            public ConcreteChunkLib.Entry lib;
            float age, contactAt = -10; int contacts; bool resting; Vector3 popBase;           // endings_v1: the pop keeps the piece's own scale
            void Start() { OzWaterLanding.Watch(transform, "building piece", rb); }             // b27: the shared water landing rule (rubble that never flies is not enabled and never gets here)
            void OnCollisionEnter(Collision c)
            {
                float speed = c.relativeVelocity.magnitude;
                if (age < 0.14f || speed < 1.4f || age - contactAt < 0.30f || contacts >= 5) return;
                contactAt = age; contacts++;
                var p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                if (OzWaterLanding.UnderSurface(p)) return;                                         // b27: it touched the bed under a drawn water surface: no dust and no knock there (its splash is made at the surface)
                FxManager.I?.Dust(p, Mathf.Min(1.2f, 0.22f + speed * 0.05f));
                if (lib != null) { if (owner && owner.TrySound()) { AudioDirector.Fire("dst_debris_concrete", p, GetInstanceID(), Mathf.Clamp(0.16f + speed * 0.02f, 0.16f, 0.36f)); LibCrashes[2]++; } return; }
                if (owner && owner.TrySound())
                    CombatAudio.I?.Play("debris", p, Mathf.Clamp01(0.3f + speed * 0.06f), 0, GetInstanceID(), "v3-collapse-small-a", "v3-collapse-small-b", "final-metal-land-a", "final-metal-land-b");
            }
            void Update()
            {
                age += Time.deltaTime;
                if (resting) return;
                if (age < life) return;
                if (footprint.Contains(transform.position) && rb && rb.IsSleeping() && (GetInstanceID() & 1) == 0)
                { rb.isKinematic = true; resting = true; return; }                                  // part of the rubble pile
                // debrisPopScale: +12 % then shrink to nothing over the last .5 s
                float t = age - life;
                float s = t < 0.14f ? 1f + 0.12f * t / 0.14f : 1.12f * (1f - Mathf.Clamp01((t - 0.14f) / 0.36f));
                if (popBase == Vector3.zero) popBase = transform.localScale;
                transform.localScale = popBase * Mathf.Max(0.001f, s);
                if (t >= 0.5f) Destroy(gameObject);
            }
        }
    }
}

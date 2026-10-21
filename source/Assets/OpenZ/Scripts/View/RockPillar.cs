using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Intact until 50 % HP, then the broken stump with a dust puff and a few chunks; at 0 HP (Zod
    /// removes the rock) the biome's 8-chunk set flies (no rubble heap: only a scorch and a few loose stones that fade) with real physics, bounces,
    /// settles, freezes and sinks away. Rocks take explosive damage only (orock.cpp), so every stage change is a blast.
    /// </summary>
    public class RockPillar : MonoBehaviour
    {
        public GameObject pillar, damaged, rubble;
        public Mesh[] chunks;
        public Material chunkMaterial;
        public int simId = -1;
        int stage;                                                             // 0 intact, 1 damaged, 2 rubble
        float nextPoll;

        const int MaxLiveChunks = 72;
        static readonly List<GameObject> live = new List<GameObject>();

        void Update()
        {
            if (stage >= 1 || Time.time < nextPoll || simId < 0 || ZGame.I == null) return;
            nextPoll = Time.time + 0.2f;
            var o = ZGame.I.World.Get(simId);
            if (o != null && o.MaxHealth > 0 && o.Health <= o.MaxHealth / 2) Damage();
        }

        /// <summary>
        /// crack in sync).</summary>
        public void Damage()
        {
            if (stage >= 1) return;
            stage = 1;
            StartCoroutine(DamageRoutine(Random.Range(0f, 0.25f)));
        }

        System.Collections.IEnumerator DamageRoutine(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (stage >= 2) yield break;
            if (pillar) pillar.SetActive(false);
            if (damaged) damaged.SetActive(true);
            var p = transform.position;
            FxManager.I?.DemolitionDust(p, Random.Range(1.2f, 1.6f), 1.6f, 0.3f);
            Throw(p + Vector3.up * 2.5f, Random.Range(2, 5), Random.Range(0.55f, 0.85f), null, 1f, 0f, Random.Range(0.8f, 1.3f));
            if (AudioDirector.Has("dst_rock_crumble_medium")) AudioDirector.Fire("dst_rock_crumble_medium", p + Vector3.up * 2f, simId);
            else CombatAudio.I?.Play("rock", p, 0.55f, 0.08f, -1, "v3-rock-break-a", "v3-rock-break-b");
        }

        /// <summary>
        /// Every pillar now has its own break delay (0-0.4 s), topples its own way (tilt away from
        /// the blast +- 50 deg, 0.25-0.6 s, sinking), throws its own chunk count / spread / power, and its loose stones fade on
        /// </summary>
        public void Break(Vector3 blast)
        {
            if (stage >= 2) return;
            bool wasIntact = stage == 0;
            stage = 2;
            StartCoroutine(BreakRoutine(blast, wasIntact, Random.Range(0f, 0.4f)));
        }

        System.Collections.IEnumerator BreakRoutine(Vector3 blast, bool wasIntact, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var p = transform.position;
            if (rubble) rubble.SetActive(false);
            if (wasIntact) FxManager.I?.DemolitionDust(p, 1.4f, 1.6f, 0.3f);
            FxManager.I?.Explosion(p + Vector3.up * 0.6f, Random.Range(0.5f, 0.7f));
            FxManager.I?.DemolitionDust(p, Random.Range(2.0f, 2.8f), Random.Range(2.2f, 3.0f), Random.Range(0.4f, 0.6f));
            ScorchDecals.Add(p, Random.Range(2.0f, 2.8f), ScorchKind.Explosion);
            if (AudioDirector.Has("dst_rock_crash_large")) AudioDirector.Fire("dst_rock_crash_large", p + Vector3.up * 2f, simId, Random.Range(0.85f, 1.05f), Random.Range(0.92f, 1.08f));
            else CombatAudio.I?.Play("rock", p, 0.9f, 0.05f, -1, "v3-rock-break-a", "v3-rock-break-b");
            if (AudioDirector.Has("dst_rock_chunks")) AudioDirector.Fire("dst_rock_chunks", p, simId, 0.8f);
            // the standing part topples: tilt about its base away from the blast (+- 50 deg), sink a little, then gone
            var body = pillar && pillar.activeSelf ? pillar : damaged && damaged.activeSelf ? damaged : null;
            var away = p - blast; away.y = 0; if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
            away = Quaternion.Euler(0, Random.Range(-50f, 50f), 0) * away;
            var axis = Vector3.Cross(Vector3.up, away).normalized;
            float fall = Random.Range(0.25f, 0.6f), maxTilt = Random.Range(25f, 70f), sink = Random.Range(0.6f, 1.6f);
            int nChunks = chunks != null ? Mathf.Max(3, chunks.Length + Random.Range(-3, 3)) : 0;
            float spread = Random.Range(0.7f, 1.4f), power = Random.Range(0.8f, 1.2f);
            bool thrown = false;
            if (body)
            {
                var t0 = body.transform; var startPos = t0.position; var startRot = t0.rotation; float t = 0;
                while (t < fall)
                {
                    t += Time.deltaTime; float k = Mathf.Clamp01(t / fall), e = k * k;               // accelerating fall
                    t0.SetPositionAndRotation(startPos + Vector3.down * sink * e, startRot);
                    t0.RotateAround(p, axis, maxTilt * e);
                    if (!thrown && k >= 0.4f) { thrown = true; Throw(p + Vector3.up * 2f, nChunks, power, blast, 1f, 0f, spread); }
                    yield return null;
                }
                body.SetActive(false); t0.SetPositionAndRotation(startPos, startRot);
            }
            if (!thrown) Throw(p + Vector3.up * 2f, nChunks, power, blast, 1f, 0f, spread);
            if (pillar) pillar.SetActive(false);
            if (damaged) damaged.SetActive(false);
            Throw(p + Vector3.up * 0.6f, Random.Range(4, 9), Random.Range(0.25f, 0.45f), blast, Random.Range(0.26f, 0.38f), Random.Range(14f, 26f), Random.Range(0.8f, 1.3f));   // loose stones
        }

        void Throw(Vector3 at, int n, float power, Vector3? blast = null, float scale = 1f, float lifeMin = 0f, float spread = 1f)
        {
            if (chunks == null || chunks.Length == 0) return;
            int layer = BuildingBreaker.DebrisLayer;
            for (int i = 0; i < n; i++)
            {
                live.RemoveAll(g => !g);
                if (live.Count >= MaxLiveChunks)
                {
                    var old = live[0]; live.RemoveAt(0);
                    if (old) { var orb = old.GetComponent<Rigidbody>(); if (orb) orb.isKinematic = true; PopOut.Begin(old.transform, PopOut.Style.Debris, null, true, false); }
                }
                var mesh = chunks[(i + Random.Range(0, chunks.Length)) % chunks.Length];
                var go = new GameObject("RockChunk") { layer = layer };
                go.transform.SetPositionAndRotation(at + Random.insideUnitSphere * 1.2f * spread + Vector3.up * Random.Range(scale < 1f ? 0f : -0.8f, 1.6f), Random.rotation);
                if (scale != 1f) go.transform.localScale = Vector3.one * scale * Random.Range(0.8f, 1.2f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = chunkMaterial;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
                var rb = go.AddComponent<Rigidbody>();
                float size = mesh.bounds.size.magnitude * scale;
                rb.mass = Mathf.Lerp(20f, 120f, Mathf.InverseLerp(0.4f, 1.3f, size));
                rb.linearDamping = 0.05f; rb.angularDamping = 0.4f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var away = go.transform.position - (blast ?? transform.position - Random.insideUnitSphere); away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                rb.linearVelocity = (away * Random.Range(2f, 4.5f) * spread + Vector3.up * Random.Range(6f, 10f)) * power;   // thrown high
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(3f, 9f);
                var life = go.AddComponent<RockChunkLife>(); if (lifeMin > 0f) life.lifeMin = lifeMin;
                live.Add(go);
            }
        }
    }

    /// <summary></summary>
    public class RockChunkLife : MonoBehaviour
    {
        public float lifeMin;                                                  // > 0: loose stones on the spot linger longer
        public bool floats;                                                    // wood (the Jungle bridge's planks): it rests half under the surface instead of going under
        float age, life; Rigidbody rb; bool frozen, popping;
        void Start()
        {
            rb = GetComponent<Rigidbody>(); life = lifeMin > 0f ? Random.Range(lifeMin, lifeMin + 8f) : Random.Range(6f, 10f);
            OzWaterLanding.Watch(transform, name == "RockChunk" ? "rock chunk" : name == "BaseChunk" ? "prop chunk" : "bridge chunk", rb, false, false, floats);   // b27: the shared water landing rule
        }
        void Update()
        {
            age += Time.deltaTime;
            if (!frozen && rb && (rb.IsSleeping() || age > life - 1.5f)) { frozen = true; rb.isKinematic = true; var c = GetComponent<Collider>(); if (c) c.enabled = false; }
            if (age > life - PopOut.Duration && !popping) { popping = true; PopOut.Begin(transform, PopOut.Style.Debris, null, true, false); }   // combatview
        }
    }
}

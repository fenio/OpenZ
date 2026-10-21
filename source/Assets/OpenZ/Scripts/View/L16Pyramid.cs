using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: a stepped pyramid in three stages like a rock (intact, damaged at half its hit points, a rubble mound when the
    /// rules destroy it). It sits beside the DetailProp of the same object: the detail path binds it to its map item
    /// (ZWorld.L16Pyramids.cs, uid = this object's name), knocks on hits and calls the break on the death event; this component
    /// owns what is the pyramid's own:
    /// <list type="bullet">
    /// <item>the hit test is the stepped shape, not a cylinder (a round that passes over a lower tier does not chip it);</item>
    /// <item>half hit points: the intact shape goes, the damaged one shows (the top and the temple down on the stair, the tree
    /// over), with dust, flying stone and the rock crumble sound; fire bowls on its ledges fall and go out;</item>
    /// <item>destroyed: what stands sinks into a dust cloud while the mound (the detail's RUIN_ child) takes its place, stone flies,
    /// the rock crash sounds. The tiles under it stay closed (level data).</item>
    /// </list>
    /// Stone that flies = the released rock chunk meshes in the pyramid's stone. View only.
    /// Play check: Stage, LastEvent.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public sealed class L16Pyramid : MonoBehaviour
    {
        public static readonly List<L16Pyramid> All = new List<L16Pyramid>();
        public GameObject intact, damaged;
        public Mesh[] chunks; public Material chunkMaterial;
        [Tooltip("1 = the big pyramid (16 m), 0.75 = the small one")] public float size = 1f;
        [Tooltip("ids of the fire bowls that stand on its ledges")] public string[] fireIds = new string[0];
        [Tooltip("the bowls' models (fall with the top)")] public GameObject[] fireModels = new GameObject[0];

        /// <summary>0 intact, 1 damaged, 2 mound.</summary>
        public int Stage { get; private set; }
        public string LastEvent { get; private set; } = "";
        DetailProp dp; float nextPoll;
        const int MaxLiveChunks = 60; static readonly List<GameObject> live = new List<GameObject>();
        // the stepped shape of the big pyramid (m): half width at the foot, at the platform, platform height, temple half width, top
        const float FootHalf = 7.9f, TopHalf = 3.7f, PlatformY = 6f, TempleHalf = 2.1f, TopY = 9.4f;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            dp = GetComponent<DetailProp>();
            dp.HpOwned = true;                                   // shots and blasts only knock: the rules' hit points decide, also when the rule is switched off
            dp.ShotTest = ShotHits; dp.BlastDist = BlastDist;
            dp.Breaking += OnBreaking;
            if (damaged) damaged.SetActive(false);
            if (intact) intact.SetActive(true);
        }

        void OnDestroy() { if (dp) dp.Breaking -= OnBreaking; }

        float HalfAt(float y)
        {
            y /= Mathf.Max(0.01f, size);
            if (y < 0f || y > TopY) return -1f;
            return (y <= PlatformY ? Mathf.Lerp(FootHalf, TopHalf, y / PlatformY) : TempleHalf) * size;
        }

        bool ShotHits(Vector3 a, Vector3 b)
        {
            var la = a - transform.position; var lb = b - transform.position;
            int n = Mathf.Clamp(Mathf.CeilToInt((lb - la).magnitude / 0.5f), 1, 80);
            for (int i = 0; i <= n; i++)
            {
                var p = Vector3.Lerp(la, lb, i / (float)n); float h = HalfAt(p.y);
                if (h > 0f && Mathf.Abs(p.x) <= h && Mathf.Abs(p.z) <= h) return true;
            }
            return false;
        }

        float BlastDist(Vector3 p)
        {
            var d = p - transform.position; float h = FootHalf * size;
            float dx = Mathf.Max(0f, Mathf.Abs(d.x) - h), dz = Mathf.Max(0f, Mathf.Abs(d.z) - h);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Update()
        {
            if (Stage >= 1 || Time.time < nextPoll || dp == null || !dp.SimBacked || ZGame.I == null || ZGame.I.World == null) return;
            nextPoll = Time.time + 0.2f;
            var o = ZGame.I.World.Get(dp.SimId);
            if (o != null && o.MaxHealth > 0 && o.Health > 0 && o.Health <= o.MaxHealth / 2) Damage();
        }

        /// <summary>Stage 1 (also callable by a check): the top comes down.</summary>
        public void Damage()
        {
            if (Stage >= 1) return;
            Stage = 1;
            var p = transform.position; var top = p + Vector3.up * (PlatformY + 1.5f) * size;
            if (intact) intact.SetActive(false);
            if (damaged) damaged.SetActive(true);
            if (FxManager.I)
            {
                FxManager.I.DemolitionDust(top, 5f * size, 5f * size, 0.5f);
                FxManager.I.DemolitionDust(p + new Vector3(0f, 1.5f * size, -5f * size), 4f * size, 3f * size, 0.4f);        // the stair side, where the top lands
            }
            Throw(top, 10, 0.7f, null, 1.2f * size);
            Sound("dst_rock_crumble_medium", top, 1f, 0.85f);
            Named("dst_l16_pyramid_slide", "OpenZ/Audio/Sfx/L16/sd_pyramid_slide_a", top, 0.8f);
            DropFires("the pyramid's top came down");
            LastEvent = name + ": damaged stage at " + Time.time.ToString("0.0") + " s";
            Debug.Log("[OpenZ] L16 pyramid " + LastEvent);
        }

        /// <summary>The detail's break starts (the rules destroyed the pyramid): the standing shape sinks into the dust, the mound shows.</summary>
        void OnBreaking(DetailProp d, Vector3 from)
        {
            if (Stage >= 2) return;
            bool wasIntact = Stage == 0;
            Stage = 2;
            DropFires("the pyramid collapsed");
            var body = damaged && damaged.activeSelf ? damaged : intact;
            var p = transform.position;
            if (body)
            {   // a copy of what stood sinks (the detail hides its own parts in this frame)
                var ghost = Instantiate(body, body.transform.parent, true); ghost.name = "PyramidSinking";
                foreach (var lg in ghost.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false;
                var rs = ghost.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) rs[i].enabled = i == 0 || rs[i].name.Contains("LOD0");
                StartCoroutine(Sink(ghost, 1.5f, 3.4f * size));
            }
            if (FxManager.I)
            {
                for (int i = 0; i < 5; i++)
                {
                    var q = p + new Vector3((i == 0 ? 0f : (i % 2 == 0 ? -1f : 1f) * 4.5f) * size, 1.2f * size, (i == 0 ? 0f : (i < 3 ? -1f : 1f) * 4.5f) * size);
                    FxManager.I.DemolitionDust(q, 6f * size, 6f * size, i == 0 ? 0.8f : 0.55f);
                }
            }
            Throw(p + Vector3.up * 3f * size, wasIntact ? 26 : 18, 0.9f, from, 3.5f * size);
            Sound("dst_rock_chunks", p + Vector3.up * 2f, 0.9f, 0.9f);
            Named("dst_l16_pyramid_collapse", "OpenZ/Audio/Sfx/L16/sd_pyramid_collapse_a", p + Vector3.up * 2f, 1f);
            LastEvent = name + ": collapsed at " + Time.time.ToString("0.0") + " s";
            Debug.Log("[OpenZ] L16 pyramid " + LastEvent);
        }

        IEnumerator Sink(GameObject ghost, float seconds, float depth)
        {
            var t0 = ghost.transform.position; float t = 0f;
            while (t < seconds && ghost)
            {
                t += Time.deltaTime; float k = Mathf.Clamp01(t / seconds);
                ghost.transform.position = t0 + Vector3.down * depth * k * k + new Vector3(Mathf.Sin(t * 47f), 0f, Mathf.Cos(t * 41f)) * 0.04f * (1f - k);
                yield return null;
            }
            if (ghost) Destroy(ghost);
        }

        void DropFires(string why)
        {
            for (int i = 0; i < fireIds.Length; i++)
            {
                if (L16Fires.I) L16Fires.I.Extinguish(fireIds[i], why);
                var m = i < fireModels.Length ? fireModels[i] : null;
                if (!m || !m.activeSelf) continue;
                var at = m.transform.position + Vector3.up * 0.6f;
                m.SetActive(false);
                Throw(at, 3, 0.45f, transform.position, 0.5f, 0.45f);
                if (FxManager.I) FxManager.I.DemolitionDust(at, 1.2f, 1.2f, 0.3f);
                Sound("dst_rock_chunks", at, 0.6f, 1.15f);
            }
        }

        static void Sound(string ev, Vector3 at, float vol, float pitch)
        {
            if (AudioDirector.Has(ev)) AudioDirector.Fire(ev, at, -1, vol, pitch);
            else OzSoundLog.Add("L16", ev, null, "DROP no event", 0f, at);
        }

        /// <summary>A sound that has no file yet: its named event is in the sound log either way, silent until the file exists.</summary>
        internal static void Named(string ev, string res, Vector3 at, float vol)
        {
            var clip = DetailKit.Pool3D.Clip(res);
            if (clip) DetailKit.Pool3D.Play(res, at, vol, 1f);
            OzSoundLog.Add("L16", ev, clip ? clip.name : null, clip ? "PLAY World" : "DROP clip missing", vol, at);
        }

        /// <summary>Flying stone: the rock chunk meshes with real physics, as a rock tile throws them (settle, freeze, pop out).</summary>
        void Throw(Vector3 at, int n, float power, Vector3? blast, float spread, float scale = 1f)
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
                var mesh = chunks[(i + Random.Range(0, chunks.Length)) % chunks.Length]; if (!mesh) continue;
                var go = new GameObject("PyramidChunk") { layer = layer };
                var off = Random.insideUnitSphere * spread; off.y = Mathf.Abs(off.y) * 0.5f;
                go.transform.SetPositionAndRotation(at + off, Random.rotation);
                go.transform.localScale = Vector3.one * scale * Random.Range(0.6f, 1.25f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = chunkMaterial;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 60f; rb.linearDamping = 0.05f; rb.angularDamping = 0.4f;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var away = go.transform.position - (blast ?? transform.position); away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f; away.Normalize();
                rb.linearVelocity = (away * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(3f, 7f)) * power;
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(3f, 8f);
                go.AddComponent<RockChunkLife>();
                live.Add(go);
            }
        }
    }
}

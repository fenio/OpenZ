using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L02 D12 tumbleweeds): up to `count` dry tumbleweeds roll across the open desert along the lanes of the
    /// placement file (from the west edge toward ENE, with the wind): roll = distance / radius, hops on gusts, a squash on
    /// landing, a dry rustle while they roll near the camera. They never rest on a walkable tile: at the lane end, against an
    /// impassable tile (rock, building, cliff) or in deep water they shake and crumble; in shallow water they float and sink.
    /// A shot within `hitRadius`, a blast in reach or a unit touching one bursts it: twigs in its own colours, dust and a
    /// crunch. A new one starts every 25-60 s. The prefab child "Template" (or the first renderer child) is the model.
    /// </summary>
    [DefaultExecutionOrder(75)]
    public class TumbleweedRoll : MonoBehaviour
    {
        public int count = 3;
        public Vector2 respawn = new Vector2(25f, 60f);
        [Tooltip("lanes: x = from.x, y = from.z, z = to.x, w = to.z (Unity metres)")] public Vector4[] lanes = new Vector4[0];
        public Vector2 wind = new Vector2(3f, 1.5f);
        public Vector2 speed = new Vector2(2.4f, 4.2f), radius = new Vector2(0.45f, 0.65f);
        public float hitRadius = 0.4f, unitTouch = 1.0f;
        public Color[] twigs = { new Color(0.72f, 0.58f, 0.36f), new Color(0.55f, 0.42f, 0.25f), new Color(0.83f, 0.72f, 0.5f) };
        public string rollEvent = "dtl_tumbleweed_roll";
        public string[] rollClips = { "DetailsV2/dv2_tumbleweed_rustle_a", "DetailsV2/dv2_tumbleweed_rustle_b" };
        public string burstEvent = "dtl_tumbleweed_burst";
        public string[] burstClips = { "DetailsV2/dv2_tumbleweed_burst_a", "DetailsV2/dv2_tumbleweed_burst_b" };
        public string[] landClips = { "sd_debris_plop_small_a", "sd_debris_plop_small_b", "sd_debris_plop_small_c" };

        sealed class W { public Transform t; public Vector3 p, dir; public float r, v, vy, hop, rustle, shake, sink, life, squash; public bool alive, wet; public Vector3 end; }
        readonly List<W> ws = new List<W>();
        GameObject template; ZGame game; DetailKit.Bits bits; float nextSpawn; float waterY = -0.35f; System.Random rng;
        readonly List<Vector3> units = new List<Vector3>(); float unitsT;

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            var tt = transform.Find("Template");
            if (!tt) foreach (Transform c in transform) if (c.GetComponentInChildren<Renderer>(true)) { tt = c; break; }
            if (!tt) { Debug.LogWarning("[OpenZ] details v2: tumbleweed spawner has no model child"); enabled = false; return; }
            template = tt.gameObject; template.SetActive(false);
            waterY = DetailKit.WaterY(game ? game.level : "L02");
            rng = new System.Random(1996);
            bits = new DetailKit.Bits(twigs); DetailPropHub.Ensure(); DetailPropHub.Track(bits);
            DetailPropHub.Shot += OnShot; DetailPropHub.Blast += OnBlast;
            nextSpawn = Time.time + Random.Range(2f, 8f);
            for (int i = 0; i < count; i++) ws.Add(new W());
        }

        void OnDestroy() { DetailPropHub.Shot -= OnShot; DetailPropHub.Blast -= OnBlast; }

        void Spawn(W w)
        {
            if (lanes == null || lanes.Length == 0) return;
            var ln = lanes[Random.Range(0, lanes.Length)];
            var a = new Vector3(ln.x, 0f, ln.y); var b = new Vector3(ln.z, 0f, ln.w);
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * Random.Range(-3f, 3f);
            w.p = a + side; w.end = b + side;
            w.dir = (w.end - w.p).normalized;
            for (int k = 0; k < 40 && OpenZFxPlayer.ClassAt(w.p) == ZClass.Impassable; k++) w.p += w.dir;   // start on the first open ground of the lane
            w.r = Random.Range(radius.x, radius.y); w.v = Random.Range(speed.x, speed.y);
            w.vy = 0f; w.hop = 0f; w.shake = 0f; w.sink = 0f; w.wet = false; w.squash = 1f; w.life = 0f; w.rustle = Random.Range(0.5f, 2f);
            if (!w.t) { var go = Instantiate(template, transform); go.name = "Tumbleweed"; w.t = go.transform; }
            w.t.gameObject.SetActive(true);
            w.t.localScale = Vector3.one * (w.r / 0.55f);
            w.t.rotation = Random.rotationUniform;
            w.p.y = Ground(w.p) + w.r;
            w.t.position = w.p;
            w.alive = true;
        }

        float Ground(Vector3 p) { return game ? game.GroundY(p.x, p.z) : 0f; }

        void OnShot(Vector3 a, Vector3 b)
        {
            foreach (var w in ws) if (w.alive && w.shake <= 0f && DetailKit.DistPointSegment(w.p, a, b) < hitRadius + w.r * 0.5f) Burst(w, true);
        }

        void OnBlast(Vector3 at, float s)
        {
            float reach = 1.2f + 2.4f * s + 0.5f;
            foreach (var w in ws) { if (!w.alive) continue; var d = w.p - at; d.y = 0f; if (d.sqrMagnitude < reach * reach) Burst(w, true); }
        }

        void Burst(W w, bool loud)
        {
            if (!w.alive) return;
            w.alive = false;
            if (w.t) w.t.gameObject.SetActive(false);
            for (int i = 0; i < twigs.Length; i++) bits.Burst(w.p, loud ? 9 : 5, (byte)i, loud ? 3f : 1.6f, 0.07f, rng);
            var fx = OpenZFxPlayer.I;
            if (fx) { fx.Dust(w.p - Vector3.up * w.r * 0.6f, 0.45f); fx.EmitChannel("m12_dust", w.p, Vector3.up * 0.4f, 1f, 0.6f, new Color(0.86f, 0.72f, 0.52f), 0.4f); }
            DetailProp.Play(burstEvent, burstClips, w.p, loud ? 0.6f : 0.45f);
            if (!DetailKit.Pool3D.Clip("OpenZ/Audio/Sfx/" + burstClips[0]) && !AudioDirector.Has(burstEvent))
                DetailProp.Play("", new[] { "sd_debris_wood_c" }, w.p, 0.5f, 1.5f);                 // plan fallback: sd_debris_wood_c pitched 1.5
            nextSpawn = Mathf.Max(nextSpawn, Time.time + Random.Range(respawn.x, respawn.y) * 0.5f);
        }

        void Update()
        {
            if (!game || template == null) return;
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Clamp(game.gameSpeed, 0.25f, 2f);
            if (dt <= 0f) return;
            unitsT -= dt;
            if (unitsT <= 0f)
            {
                unitsT = 0.15f; units.Clear();
                foreach (var kv in game.Views) if (kv.Value && kv.Value.gameObject.activeInHierarchy) units.Add(kv.Value.transform.position);
            }
            int alive = 0; foreach (var w in ws) if (w.alive) alive++;
            if (alive < count && Time.time >= nextSpawn)
            {
                foreach (var w in ws) if (!w.alive) { Spawn(w); break; }
                nextSpawn = Time.time + Random.Range(respawn.x, respawn.y);
            }
            var cam = Camera.main;
            var windDir = new Vector3(wind.x, 0f, wind.y).normalized;
            foreach (var w in ws)
            {
                if (!w.alive) continue;
                w.life += dt;
                if (w.shake > 0f)
                {   // blocked: shake 2-3 s, then crumble (never rests on a walkable tile)
                    w.shake -= dt;
                    w.t.position = w.p + Random.insideUnitSphere * 0.04f;
                    if (w.shake <= 0f) Burst(w, false);
                    continue;
                }
                if (w.wet)
                {   // floating downstream, then it sinks
                    w.sink += dt; w.p += new Vector3(0.3f, 0f, -0.9f) * dt; w.p.y = waterY + w.r * (0.4f - 0.5f * w.sink / 6f);
                    w.t.position = w.p; w.t.Rotate(Vector3.right * 20f * dt, Space.World);
                    if (w.sink > 6f) { w.alive = false; w.t.gameObject.SetActive(false); OpenZFxPlayer fz = OpenZFxPlayer.I; if (fz) fz.Ripple(w.p, 0.4f); }
                    continue;
                }
                float gustK = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 0.4f, w.r * 10f);
                var dir = (w.dir * 0.7f + windDir * 0.3f).normalized;
                float step = w.v * gustK * dt;
                var np = w.p + dir * step;
                // hops on gusts and bumps
                float g = Ground(np);
                if (w.hop <= 0f && Random.value < dt * 0.35f * gustK) { w.vy = Random.Range(1.6f, 3.2f); w.hop = 0.01f; }
                if (w.hop > 0f)
                {
                    w.vy -= 9.81f * dt; w.hop += w.vy * dt;
                    if (w.hop <= 0f)
                    {
                        w.hop = 0f; w.squash = 0.8f;
                        if (DetailProp.NearListener(w.p, DetailProp.HearRange)) DetailProp.Play("", landClips, w.p, 0.18f, 1.2f);   // review fix: listener gate
                    }
                }
                w.squash = Mathf.MoveTowards(w.squash, 1f, dt * 2.5f);
                np.y = g + w.r * w.squash + w.hop;
                // roll rotation from the distance travelled
                var axis = Vector3.Cross(Vector3.up, dir);
                w.t.Rotate(axis, step / Mathf.Max(0.1f, w.r) * Mathf.Rad2Deg, Space.World);
                w.p = np;
                w.t.position = w.p;
                w.t.localScale = new Vector3(1f / Mathf.Sqrt(w.squash), w.squash, 1f / Mathf.Sqrt(w.squash)) * (w.r / 0.55f);
                // rustle while rolling near the camera
                w.rustle -= dt;
                if (w.rustle <= 0f)
                {
                    w.rustle = Random.Range(1.6f, 3.2f);
                    if (DetailProp.NearListener(w.p, DetailProp.HearRange))                   // review fix: the listener rig, not the camera
                        DetailProp.Play(rollEvent, rollClips, w.p, 0.22f, Random.Range(0.9f, 1.15f));
                }
                // touched by a unit
                bool hit = false;
                foreach (var u in units) { var d = u - w.p; d.y = 0f; if (d.sqrMagnitude < unitTouch * unitTouch) { hit = true; break; } }
                if (hit) { Burst(w, true); continue; }
                // blocked / water / lane end
                var cls = OpenZFxPlayer.ClassAt(w.p + dir * w.r);
                bool past = Vector3.Dot(w.end - w.p, dir) < 0f;
                if (cls == ZClass.Impassable || past || w.life > 90f) { w.shake = Random.Range(2f, 3f); continue; }
                if ((cls == ZClass.Water || cls == ZClass.DeepWater) && g < waterY - 0.05f)
                {
                    w.wet = true; var fx = OpenZFxPlayer.I; if (fx) fx.Splash(new Vector3(w.p.x, waterY, w.p.z), 0.3f);
                    DetailProp.Play("", new[] { "sd_debris_splash_a", "sd_debris_splash_b" }, w.p, 0.35f);
                }
            }
        }
    }
}

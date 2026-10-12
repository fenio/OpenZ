using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// while a station heals the vehicle on its pad (ZObject.RepairBay.cs: RepairingUnit + RepairOccupantId) the hoist hook is down over it
    /// (BuildingAnimView), and here the weld work: bursts of 0.5-1.3 s with short pauses, each burst moving to a new spot on the hull (top
    /// edges, upper flanks), a white-hot core, falling orange sparks and a blue-white arc flicker light every 0.06-0.14 s
    /// (FxManager.WeldSparks) with the arc crackle (bld_repair_weld, 3D at the spot); every 20 % of health added a green ring pulses out
    /// from under the vehicle with a green glow and a soft tick (bld_repair_tick). Nothing while the station is idle, neutral or destroyed;
    /// the work stops the frame the vehicle is called off. View only, the sim is not touched. Boots itself in level scenes (ZGame present).
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class RepairBayView : MonoBehaviour
    {
        public static bool AutoBoot = true;
        /// <summary>Proof counters: weld sparks drawn, weld bursts started, health pulses.</summary>
        public static int Sparks, Bursts, Pulses;
        static bool hooked;

        sealed class Bay
        {
            public int vehId = -1, lastStep; public float burstEnd, gapUntil, nextSpark; public Vector3 at, normal;
        }
        readonly Dictionary<int, Bay> bays = new Dictionary<int, Bay>();
        readonly List<ZObject> stations = new List<ZObject>();
        float nextScan;
        static Material ringMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I) return;
            if (!FindFirstObjectByType<RepairBayView>()) new GameObject("RepairBayView").AddComponent<RepairBayView>();
        }

        void Update()
        {
            var g = ZGame.I; var W = g ? g.World : null;
            if (W == null) return;
            float now = Time.time;
            if (now >= nextScan)
            {
                nextScan = now + 2f; stations.Clear();
                foreach (var o in W.Objects) if (o.Kind == ZKind.Building && o.Type == "repair") stations.Add(o);
            }
            var fx = FxManager.I;
            foreach (var st in stations)
            {
                bool active = ZWorld.RepairInPlaceDeviation && st.RepairingUnit && st.RepairOccupantId >= 0 && !st.Destroyed && st.Owner != Team.Neutral;
                var u = active ? W.Get(st.RepairOccupantId) : null;
                UnitView v = null;
                if (u == null || u.Destroyed || !g.Views.TryGetValue(u.Id, out v) || !v) { bays.Remove(st.Id); continue; }
                if (!bays.TryGetValue(st.Id, out var bay) || bay.vehId != u.Id) bays[st.Id] = bay = new Bay { vehId = u.Id, gapUntil = now + 0.35f };
                // health tick: a pulse every 20 % of the repair
                int step = Mathf.FloorToInt(Mathf.Max(0f, st.RepairPadProgress) * 5f + 1e-4f);
                if (step > bay.lastStep)
                {
                    bay.lastStep = step; Pulses++;
                    var gp = g.CenterWorld(u);
                    StartCoroutine(Pulse(gp + Vector3.up * 0.08f, HullRadius(v)));
                    fx?.AddLight(gp + Vector3.up * 1.2f, new Color(0.35f, 1f, 0.45f), 1.6f, 5f, 0.35f, 0.1f, true);
                    AudioDirector.I?.Play("bld_repair_tick", gp, u.Id);
                }
                // weld bursts
                if (now >= bay.burstEnd && now >= bay.gapUntil)
                {
                    PickSpot(v, out bay.at, out bay.normal);
                    bay.burstEnd = now + Random.Range(0.5f, 1.3f); bay.gapUntil = bay.burstEnd + Random.Range(0.25f, 0.6f);
                    bay.nextSpark = now; Bursts++;
                }
                if (now < bay.burstEnd && now >= bay.nextSpark && fx)
                {
                    var p = bay.at + Random.insideUnitSphere * 0.06f;
                    fx.WeldSparks(p, bay.normal, Random.Range(0.7f, 1.1f));
                    bay.nextSpark = now + Random.Range(0.06f, 0.14f); Sparks++;
                    AudioDirector.I?.Play("bld_repair_weld", p, u.Id);
                }
            }
        }

        static Bounds HullBounds(UnitView v)
        {
            Bounds b = default; bool any = false;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer) || r.name.StartsWith("OZ_")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(v.transform.position + Vector3.up, new Vector3(2f, 1.5f, 3f));
            return b;
        }

        static float HullRadius(UnitView v) { var e = HullBounds(v).extents; return Mathf.Clamp(Mathf.Max(e.x, e.z) + 0.4f, 1.2f, 3.5f); }

        /// <summary>A weld spot on the hull: the top near an edge (40 %) or an upper flank / end (60 %), with its outward normal.</summary>
        static void PickSpot(UnitView v, out Vector3 at, out Vector3 normal)
        {
            var b = HullBounds(v); var c = b.center; var e = b.extents;
            var cam = Camera.main; Vector3 tc = cam ? cam.transform.position - c : Vector3.back; tc.y = 0f;
            float camX = tc.x >= 0f ? 1f : -1f, camZ = tc.z >= 0f ? 1f : -1f; bool camSideX = Mathf.Abs(tc.x) > Mathf.Abs(tc.z);
            if (Random.value < 0.4f)
            {
                float sx = Random.value < 0.8f ? camX : -camX;
                at = new Vector3(c.x + sx * e.x * Random.Range(0.45f, 0.85f), b.max.y - 0.02f, c.z + e.z * Random.Range(-0.75f, 0.75f));
                normal = (Vector3.up * 1.4f + new Vector3(sx, 0, 0)).normalized;
                return;
            }
            bool flankX = Random.value < 0.8f ? camSideX : !camSideX; float s = Random.value < 0.85f ? (flankX ? camX : camZ) : -(flankX ? camX : camZ);
            float y = b.min.y + e.y * 2f * Random.Range(0.5f, 0.85f);
            at = flankX ? new Vector3(c.x + s * e.x * 0.97f, y, c.z + e.z * Random.Range(-0.7f, 0.7f))
                        : new Vector3(c.x + e.x * Random.Range(-0.6f, 0.6f), y, c.z + s * e.z * 0.97f);
            normal = flankX ? new Vector3(s, 0.35f, 0).normalized : new Vector3(0, 0.35f, s).normalized;
        }

        /// <summary>The health tick: a thin green ring that grows out from under the vehicle and fades (0.7 s).</summary>
        IEnumerator Pulse(Vector3 at, float r0)
        {
            var go = new GameObject("OZ_RepairPulse"); var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true; lr.positionCount = 40; lr.widthMultiplier = 0.12f;
            if (!ringMat) ringMat = new Material(Shader.Find("Sprites/Default"));
            lr.sharedMaterial = ringMat; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            const float life = 0.7f;
            for (float t = 0; t < life; t += Time.deltaTime)
            {
                float k = t / life, r = r0 * (0.75f + 0.6f * Mathf.Sqrt(k));
                var col = new Color(0.4f, 1f, 0.5f, 0.85f * (1f - k) * (1f - k)) * new Color(2.2f, 2.2f, 2.2f, 1f);
                lr.startColor = lr.endColor = col;
                for (int i = 0; i < 40; i++) { float a = i / 40f * Mathf.PI * 2f; lr.SetPosition(i, at + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); }
                yield return null;
            }
            Destroy(go);
        }
    }
}

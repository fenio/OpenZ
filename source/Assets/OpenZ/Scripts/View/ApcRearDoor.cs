using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// x +-0.5, y 0.74..2.33, recess z 2.10..2.28 inside a frame at z 2.35..2.40; belly 0.70, roof >= 2.33, flanks >= +-1.03) is cut
    /// out of the hull mesh and gets a real ramp door: a khaki leaf (gunmetal hinge knuckles, handle, stiffeners, inner treads)
    /// hinged at the sill that drops to the ground (118 deg) and closes again. Behind it a closed cargo bay (dark walls, roof, front
    /// wall, a gunmetal floor plate with ribs, two olive side benches with backrests, an amber ceiling lamp strip + a small red lamp,
    /// a dim amber point light while the ramp is open; a sill plate and a lintel close the gaps to the cut, so nothing is see-through).
    /// BoardingView asks for the ramp (Request) 0.35 s + OpenTime before the first passenger reaches its foot and while anyone files
    /// in / out; it closes when nobody needs it. Sounds: hydraulic on open and close (sd_crane_arm_hydraulic), a soft thud when the
    /// ramp lands, the existing latch clank when it locks shut. The leaf flies off with the APC debris (BreakOff). View only.
    /// </summary>
    public class ApcRearDoor : MonoBehaviour
    {
        public const float HalfW = 0.485f, Sill = 0.745f, Len = 1.50f, Thick = 0.09f, HingeZ = 2.27f, OpenDeg = 118f,
                           OpenTime = 0.55f, CloseTime = 0.6f, Lead = 0.35f, Hold = 0.45f;
        public const float BayX = 0.86f, BayFloor = 0.80f, BayTop = 2.25f, BayFront = 0.30f, BayRear = 2.31f, BenchIn = 0.50f, BenchTop = 1.14f;
        public const float FitHeight = 1.30f;                                         // a passenger's height inside (bay 1.45 m, doorway 1.45 m)
        /// <summary>Proof counters.</summary>
        public static int Built, Opens, Closes, Landed, Latched, CutTris;
        static readonly Dictionary<Mesh, Mesh> cutCache = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Material, Material[]> matCache = new Dictionary<Material, Material[]>();
        static Mesh leafMesh, trimMesh, treadMesh, bayMesh, floorMesh, benchMesh, frameMesh, lampMesh;

        UnitView view; Transform hinge, bay; Light lamp; float open, wantUntil = -1f; int phase; bool landed;   // phase 0 shut, 1 opening/open, 2 closing
        readonly List<GameObject> held = new List<GameObject>();
        public float Open01 => open;
        public bool IsOpen => open > 0.98f;
        public bool IsShut => open < 0.005f && phase != 1;
        public int Seated => held.Count;

        public static ApcRearDoor Of(UnitView v) => v ? v.GetComponent<ApcRearDoor>() : null;

        public static ApcRearDoor Ensure(UnitView v)
        {
            if (!v || v.Obj == null || v.Obj.Type != "apc") return null;
            var d = v.GetComponent<ApcRearDoor>();
            if (d) return d;
            d = v.gameObject.AddComponent<ApcRearDoor>();
            if (!d.Build(v)) { Destroy(d); return null; }
            return d;
        }

        // ------------------------------------------------------------------ points for BoardingView (world, robot transform = feet + RobotY)
        public Transform Root => view ? view.transform : transform;
        Vector3 RampLocal(float u) => new Vector3(0f, Sill, HingeZ) + Quaternion.Euler(OpenDeg, 0f, 0f) * new Vector3(0f, u * Len, -Thick * 0.5f);
        /// <summary>On the open ramp's tread surface, u = 0 at the sill .. 1 at the tip.</summary>
        public Vector3 RampPoint(float u, float robotY) => Root.TransformPoint(RampLocal(u)) + Root.up * robotY;
        /// <summary>Ground spot 0.45 m past the ramp tip (where a passenger steps on / off), not grounded yet.</summary>
        public Vector3 FootPoint() { var l = RampLocal(1f); return Root.TransformPoint(new Vector3(0f, 0f, l.z + 0.45f)); }
        /// <summary>Bay floor in the aisle at the seat's row (just inside the doorway for k = -1).</summary>
        public Vector3 AislePoint(int k, float robotY) => Root.TransformPoint(new Vector3(0f, BayFloor, k < 0 ? BayRear - 0.25f : SeatZ(k))) + Root.up * robotY;
        /// <summary>The top of the bench cushion under a passenger's hips (k: 0,1 rear pair .. 4,5 front pair, even = +x side).</summary>
        public Vector3 SeatPoint(int k) => Root.TransformPoint(new Vector3(SeatSide(k) * (BenchIn + 0.13f), BenchTop, SeatZ(k)));
        public static float SeatSide(int k) => (k & 1) == 0 ? 1f : -1f;
        public static float SeatZ(int k) => 1.85f - 0.40f * ((k % 6) >> 1);
        /// <summary>Facing of a seated passenger: toward the aisle.</summary>
        public Vector3 AisleDir(int k) => Root.TransformDirection(new Vector3(-SeatSide(k), 0f, 0f));

        /// <summary>BoardingView: someone needs the ramp down now (re-sent every frame while needed).</summary>
        public void Request() { wantUntil = Time.time + Hold; }
        /// <summary>BoardingView: a seated passenger figure stays on its bench until the ramp is shut.</summary>
        public void HoldFigure(GameObject go) { if (go) held.Add(go); }

        // ------------------------------------------------------------------ build
        bool Build(UnitView v)
        {
            view = v;
            var model = v.transform.Find("Model");
            var mr = model ? model.GetComponent<MeshRenderer>() : null; var mf = model ? model.GetComponent<MeshFilter>() : null;
            if (!mr || !mf || !mf.sharedMesh) return false;
            mf.sharedMesh = CutDoor(mf.sharedMesh, v.transform.worldToLocalMatrix * model.localToWorldMatrix);
            var mats = Mats(mr.sharedMaterial);
            if (!leafMesh) BuildMeshes();
            hinge = new GameObject("OZ_ApcDoor_Hinge").transform;
            hinge.SetParent(v.transform, false); hinge.localPosition = new Vector3(0f, Sill, HingeZ); hinge.localRotation = Quaternion.identity;
            Part(hinge, "OZ_ApcDoor_Leaf", leafMesh, mats[0], true);
            Part(hinge, "OZ_ApcDoor_Trim", trimMesh, mats[1], true);
            Part(hinge, "OZ_ApcDoor_Tread", treadMesh, mats[1], true);
            bay = new GameObject("OZ_ApcBay").transform; bay.SetParent(v.transform, false);
            Part(bay, "OZ_ApcBay_Shell", bayMesh, mats[2], false);
            Part(bay, "OZ_ApcBay_Floor", floorMesh, mats[1], false);
            Part(bay, "OZ_ApcBay_Benches", benchMesh, mats[3], false);
            Part(bay, "OZ_ApcBay_Lamp", lampMesh, mats[4], false);
            Part(bay, "OZ_ApcDoor_Frame", frameMesh, mats[1], true);
            var lg = new GameObject("OZ_ApcBay_Light"); lg.transform.SetParent(bay, false); lg.transform.localPosition = new Vector3(0f, BayTop - 0.18f, 1.45f);
            lamp = lg.AddComponent<Light>(); lamp.type = LightType.Point; lamp.color = new Color(1f, 0.45f, 0.18f); lamp.range = 2.6f; lamp.intensity = 2.2f;
            lamp.shadows = LightShadows.None; lamp.renderMode = LightRenderMode.ForcePixel; lamp.enabled = false;
            var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();
            Built++;
            return true;
        }

        /// <summary>The recess panel's rear-facing triangles (all three corners inside the recess box) leave the hull mesh, once per
        /// source mesh; the jambs, the sill and the lintel of the recess stay as the door frame.</summary>
        /// <summary></summary>
        internal static Mesh CutDoorOf(Mesh src, Matrix4x4 toRoot) => CutDoor(src, toRoot);

        static Mesh CutDoor(Mesh src, Matrix4x4 toRoot)
        {
            if (cutCache.TryGetValue(src, out var m) && m) return m;
            m = Instantiate(src); m.name = src.name + "_door";
            var v = src.vertices; int cut = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s); var keep = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = toRoot.MultiplyPoint3x4(v[t[i]]), b = toRoot.MultiplyPoint3x4(v[t[i + 1]]), c = toRoot.MultiplyPoint3x4(v[t[i + 2]]);
                    if (In(a) && In(b) && In(c) && Vector3.Cross(b - a, c - a).normalized.z > 0.3f) { cut++; continue; }
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                }
                m.SetTriangles(keep, s);
            }
            m.RecalculateBounds();
            CutTris = cut;
            cutCache[src] = m;
            return m;
        }
        static bool In(Vector3 q) => Mathf.Abs(q.x) < 0.505f && q.y > 0.72f && q.y < 2.345f && q.z > 2.05f && q.z < 2.33f;

        static void Part(Transform parent, string name, Mesh mesh, Material m, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
        }

        /// <summary>Boxes facing inward (the bay shell), the +z (rear) face left open.</summary>
        static Mesh InwardBox(string name, Vector3 min, Vector3 max)
        {
            var b = OzLathe.Boxes(name, new List<(Vector3, Vector3)> { ((min + max) * 0.5f, (max - min) * 0.5f) });
            var v = b.vertices; var n = b.normals; var t = b.triangles; var keep = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                if (n[t[i]].z > 0.5f) continue;                                                            // the open rear
                keep.Add(t[i]); keep.Add(t[i + 2]); keep.Add(t[i + 1]);
            }
            for (int i = 0; i < n.Length; i++) n[i] = -n[i];
            b.vertices = v; b.normals = n; b.SetTriangles(keep, 0); b.RecalculateBounds();
            return b;
        }

        static void BuildMeshes()
        {
            float h2 = Thick * 0.5f;
            // leaf (hinge space: up the leaf = +y, outside = +z): the panel + two stiffener ribs on the outside
            leafMesh = OzLathe.Boxes("OZ_ApcDoor_Leaf", new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, Len * 0.5f, 0f), new Vector3(HalfW, Len * 0.5f, h2)),
                (new Vector3(0f, 0.52f, h2 + 0.012f), new Vector3(HalfW - 0.05f, 0.03f, 0.012f)),
                (new Vector3(0f, 1.05f, h2 + 0.012f), new Vector3(HalfW - 0.05f, 0.03f, 0.012f)),
            });
            // gunmetal: three hinge knuckles at the sill, the handle + its two posts, a vision slit
            trimMesh = OzLathe.Boxes("OZ_ApcDoor_Trim", new List<(Vector3, Vector3)>
            {
                (new Vector3(-0.33f, 0.02f, h2 + 0.02f), new Vector3(0.08f, 0.035f, 0.03f)),
                (new Vector3(0f, 0.02f, h2 + 0.02f), new Vector3(0.08f, 0.035f, 0.03f)),
                (new Vector3(0.33f, 0.02f, h2 + 0.02f), new Vector3(0.08f, 0.035f, 0.03f)),
                (new Vector3(0.28f, 0.80f, h2 + 0.045f), new Vector3(0.10f, 0.016f, 0.016f)),
                (new Vector3(0.19f, 0.80f, h2 + 0.02f), new Vector3(0.014f, 0.014f, 0.022f)),
                (new Vector3(0.37f, 0.80f, h2 + 0.02f), new Vector3(0.014f, 0.014f, 0.022f)),
                (new Vector3(0f, 1.32f, h2 + 0.006f), new Vector3(0.16f, 0.022f, 0.008f)),
            });
            // treads on the inner face (the ramp's walking side once it is down)
            var tr = new List<(Vector3, Vector3)>();
            for (int i = 0; i < 6; i++) tr.Add((new Vector3(0f, 0.16f + 0.235f * i, -h2 - 0.01f), new Vector3(HalfW - 0.07f, 0.022f, 0.01f)));
            treadMesh = OzLathe.Boxes("OZ_ApcDoor_Tread", tr);
            // bay (root space)
            bayMesh = InwardBox("OZ_ApcBay_Shell", new Vector3(-BayX, BayFloor - 0.03f, BayFront), new Vector3(BayX, BayTop, BayRear));
            var fl = new List<(Vector3, Vector3)> { (new Vector3(0f, BayFloor - 0.015f, (BayFront + BayRear) * 0.5f), new Vector3(BayX - 0.01f, 0.015f, (BayRear - BayFront) * 0.5f)) };
            for (float z = 0.55f; z < 2.2f; z += 0.3f) fl.Add((new Vector3(0f, BayFloor + 0.004f, z), new Vector3(BenchIn - 0.04f, 0.006f, 0.022f)));
            floorMesh = OzLathe.Boxes("OZ_ApcBay_Floor", fl);
            float bz = (0.45f + 2.10f) * 0.5f, bh = (2.10f - 0.45f) * 0.5f, sx = (BenchIn + BayX - 0.02f) * 0.5f, sh = (BayX - 0.02f - BenchIn) * 0.5f;
            benchMesh = OzLathe.Boxes("OZ_ApcBay_Benches", new List<(Vector3, Vector3)>
            {
                (new Vector3(sx, (BayFloor + BenchTop) * 0.5f, bz), new Vector3(sh, (BenchTop - BayFloor) * 0.5f, bh)),
                (new Vector3(-sx, (BayFloor + BenchTop) * 0.5f, bz), new Vector3(sh, (BenchTop - BayFloor) * 0.5f, bh)),
                (new Vector3(BayX - 0.04f, BenchTop + 0.30f, bz), new Vector3(0.025f, 0.28f, bh)),
                (new Vector3(-BayX + 0.04f, BenchTop + 0.30f, bz), new Vector3(0.025f, 0.28f, bh)),
            });
            lampMesh = OzLathe.Boxes("OZ_ApcBay_Lamp", new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, BayTop - 0.012f, 1.30f), new Vector3(0.05f, 0.012f, 0.60f)),
                (new Vector3(0f, 1.95f, BayFront + 0.012f), new Vector3(0.07f, 0.045f, 0.012f)),
            });
            // door frame: the sill plate (top = the bay floor) and the lintel over the opening (closes the gap to the roof)
            frameMesh = OzLathe.Boxes("OZ_ApcDoor_Frame", new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, (0.70f + BayFloor) * 0.5f, 2.20f), new Vector3(0.52f, (BayFloor - 0.70f) * 0.5f, 0.12f)),
                (new Vector3(0f, (Sill + Len + 2.345f) * 0.5f, 2.185f), new Vector3(0.52f, (2.345f - Sill - Len) * 0.5f, 0.145f)),
            });
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode"), VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha");
        /// <summary>[0] APC khaki paint, [1] gunmetal, [2] dark bay, [3] olive bench, [4] amber lamp (emissive).</summary>
        static Material[] Mats(Material src)
        {
            if (matCache.TryGetValue(src, out var m) && m[0]) return m;
            m = new[] { Clone(src, "OZ_ApcDoorPaint", new Color32(176, 136, 80, 255), 0.30f),
                        Clone(src, "OZ_ApcDoorMetal", new Color(0.36f, 0.38f, 0.40f), 0.35f),
                        Clone(src, "OZ_ApcBayDark", new Color(0.11f, 0.12f, 0.11f), 0.12f),
                        Clone(src, "OZ_ApcBayBench", new Color(0.24f, 0.26f, 0.18f), 0.15f),
                        Clone(src, "OZ_ApcBayLamp", new Color(1f, 0.55f, 0.22f), 0.5f) };
            if (m[4].HasProperty(EmissionId)) m[4].SetColor(EmissionId, new Color(1f, 0.42f, 0.12f) * 2.6f);
            matCache[src] = m;
            return m;
        }
        static Material Clone(Material src, string name, Color c, float smooth)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            return m;
        }

        // ------------------------------------------------------------------ animation + sounds
        void LateUpdate()
        {
            if (!hinge || !view || view.Obj == null) return;
            bool alive = !view.Dying && !view.Obj.Destroyed;
            bool want = alive && Time.time < wantUntil;
            float dt = Time.deltaTime;
            if (want && phase != 1)
            {
                phase = 1; landed = false; Opens++;
                Sound("apc_ramp", 0.55f, 1.05f, "sd_crane_arm_hydraulic");
                OzSoundLog.Add("DOOR", "apc_ramp", "sd_crane_arm_hydraulic", "ramp opening", 0.55f, hinge.position);
            }
            else if (!want && phase == 1)
            {
                phase = 2; Closes++;
                Sound("apc_ramp", 0.5f, 0.95f, "sd_crane_arm_hydraulic");
                OzSoundLog.Add("DOOR", "apc_ramp", "sd_crane_arm_hydraulic", "ramp closing", 0.5f, hinge.position);
            }
            if (phase == 1) open = Mathf.MoveTowards(open, 1f, dt / OpenTime);
            else if (phase == 2) open = Mathf.MoveTowards(open, 0f, dt / CloseTime);
            if (phase == 1 && open >= 1f && !landed)
            {
                landed = true; Landed++;
                Sound("apc_ramp_land", 0.35f, 0.8f, "sd_hatch_close_a", "sd_hatch_close_b");
            }
            if (phase == 2 && open <= 0f)
            {
                phase = 0; Latched++;
                if (alive && BoardingView.I && BoardingView.I.sounds) BoardingView.Latch(hinge.position, view.Obj.Id);
                OzSoundLog.Add("DOOR", "latch", null, "ramp shut + latched", 0f, hinge.position);
                ClearHeld();
            }
            // ease: hydraulic (slow start, firm stop); the ramp drops a little faster at the end (gravity)
            float e = phase == 2 ? Mathf.SmoothStep(0f, 1f, open) : open * open * (3f - 2f * open);
            hinge.localRotation = Quaternion.Euler(OpenDeg * e, 0f, 0f);
            bool lit = open > 0.02f && alive;
            if (lamp && lamp.enabled != lit) lamp.enabled = lit;
        }

        void Sound(string key, float vol, float pitch, params string[] clips)
        {
            var au = CombatAudio.I; if (!au || !BoardingView.I || !BoardingView.I.sounds) return;
            au.PlayPitched(key, hinge.position, vol, 0.15f, view.Obj.Id, pitch, clips);
        }

        void ClearHeld() { foreach (var go in held) if (go) Destroy(go); held.Clear(); }
        void OnDestroy() => ClearHeld();

        /// <summary>UnitView.BreakApart: the ramp leaf leaves as its own debris piece; the bay and any seated figures go.</summary>
        public static void BreakOff(UnitView v, Vector3 centre, float power, int id)
        {
            var d = Of(v);
            if (!d || !d.hinge) return;
            d.ClearHeld();
            WreckPieces.Single(d.hinge, centre, power, id + 6113); d.hinge = null;
            if (d.bay) d.bay.gameObject.SetActive(false);
            d.enabled = false;
        }
    }
}

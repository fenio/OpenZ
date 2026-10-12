using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// casts on the hull, root space: the two small square ports on the door panels at z -0.15 / +0.715 and the slot box at z +1.33,
    /// centres y 1.78-1.84, flank surface x +-1.195) get real hatch covers: a khaki lid hinged at its top edge (it covers the
    /// moulded knob) that flips up 150 deg, clear of the port (as a 100 deg awning it hid its own port, the head and the rifle from
    /// the game camera, which looks down at 55 deg), a dark port behind it, and while it is open a crew head
    /// (gunmetal, amber eyes) and a rifle barrel poke out of the port. Every bullet the APC fires (CombatView.DrawShot) leaves from
    /// the barrel tip of one open hatch on the side facing the target (round robin over the three), so the tracer, the muzzle flash
    /// and the recoil sit at the port; the barrel swings toward the target (+-50 deg off the flank). A hatch opens on its first shot
    /// (0.12 s), the other two hatches of that side open with it, and they close 2 s after the side's last shot; sounds: hatch open clank (s3_hatch_open_*) and close (sd_hatch_close_*), 3D at the
    /// hatch. View only, nothing here changes the sim.
    /// </summary>
    public class ApcSideHatches : MonoBehaviour
    {
        const float PortW = 0.36f, PortH = 0.30f, LidT = 0.035f, SurfX = 1.207f, OpenDeg = 150f, OpenTime = 0.12f, CloseTime = 0.25f, Hold = 2f, Stagger = 0.07f;
        static readonly Vector3[] PortCentres = { new Vector3(0f, 1.78f, -0.15f), new Vector3(0f, 1.80f, 0.715f), new Vector3(0f, 1.84f, 1.33f) };
        /// <summary>Proof counters (all APCs).</summary>
        public static int Built, Opens, Closes, Shots;

        sealed class Port
        {
            public int side; public Vector3 local; public Transform hinge, crew, barrel, tip; public float open, wantFrom, wantUntil; public bool wasOpen;
        }
        readonly List<Port> ports = new List<Port>();
        UnitView view; int nextL, nextR;
        static Mesh lidMesh, plateMesh, barrelMesh, eyeMesh;

        public static ApcSideHatches Ensure(UnitView v)
        {
            if (!v || v.Obj == null || v.Obj.Type != "apc") return null;
            var h = v.GetComponent<ApcSideHatches>();
            if (h) return h;
            h = v.gameObject.AddComponent<ApcSideHatches>();
            if (!h.Build(v)) { Destroy(h); return null; }
            return h;
        }

        /// <summary>CombatView.DrawShot: the world point an APC round leaves from (the barrel tip of an open-or-opening hatch on the
        /// side facing `target`); false = not an APC with hatches.</summary>
        public static bool ShotFrom(UnitView av, Vector3 target, out Vector3 muzzle)
        {
            muzzle = default;
            var h = av ? av.GetComponent<ApcSideHatches>() : null;
            if (!h || h.ports.Count == 0 || av.Dying) return false;
            return h.Fire(target, out muzzle);
        }

        bool Build(UnitView v)
        {
            view = v;
            var model = v.transform.Find("Model");
            var mr = model ? model.GetComponent<MeshRenderer>() : null;
            if (!mr) return false;
            if (!lidMesh) BuildMeshes();
            var paint = Clone(mr.sharedMaterial, "OZ_ApcHatchPaint", new Color32(176, 136, 80, 255), 0.30f);
            var dark = Clone(mr.sharedMaterial, "OZ_ApcHatchPort", new Color(0.05f, 0.05f, 0.05f), 0.1f);
            var metal = Clone(mr.sharedMaterial, "OZ_ApcHatchMetal", new Color(0.30f, 0.32f, 0.34f), 0.4f);
            var eyes = Clone(mr.sharedMaterial, "OZ_ApcHatchEyes", new Color(1f, 0.6f, 0.2f), 0.5f);
            if (eyes.HasProperty("_EmissionColor")) { eyes.EnableKeyword("_EMISSION"); eyes.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * 3f); }
            var root = new GameObject("OZ_ApcSideHatches").transform; root.SetParent(v.transform, false);
            for (int s = -1; s <= 1; s += 2)
                foreach (var c in PortCentres)
                {
                    var p = new Port { side = s, local = new Vector3(s * SurfX, c.y, c.z) };
                    // the dark port (inside the closed lid, visible when it opens)
                    Part(root, "OZ_ApcPort", plateMesh, dark, false, new Vector3(s * (SurfX + 0.026f), c.y, c.z), Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f));
                    // the lid: hinge on the top edge, the lid hangs down from it, outer face at +x (mirrored on the left flank)
                    p.hinge = new GameObject("OZ_ApcHatchHinge").transform; p.hinge.SetParent(root, false);
                    p.hinge.localPosition = new Vector3(s * SurfX, c.y + PortH * 0.5f, c.z);
                    p.hinge.localScale = new Vector3(s, 1f, 1f);
                    Part(p.hinge, "OZ_ApcHatchLid", lidMesh, paint, true, Vector3.zero, Quaternion.identity);
                    // crew head + rifle (shown while open)
                    p.crew = new GameObject("OZ_ApcHatchCrew").transform; p.crew.SetParent(root, false);
                    p.crew.localPosition = new Vector3(s * (SurfX + 0.03f), c.y - 0.01f, c.z);
                    var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); var col = head.GetComponent<Collider>(); if (col) Destroy(col);
                    head.name = "OZ_ApcHatchHead"; head.transform.SetParent(p.crew, false); head.transform.localPosition = new Vector3(s * 0.01f, 0.035f, -0.06f);
                    head.transform.localScale = new Vector3(0.14f, 0.13f, 0.15f); var hr = head.GetComponent<MeshRenderer>(); hr.sharedMaterial = metal; hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Part(p.crew, "OZ_ApcHatchEyes", eyeMesh, eyes, false, new Vector3(s * 0.055f, 0.05f, -0.06f), Quaternion.Euler(0f, s > 0 ? 90f : -90f, 0f));
                    p.barrel = new GameObject("OZ_ApcHatchBarrel").transform; p.barrel.SetParent(p.crew, false); p.barrel.localPosition = new Vector3(0f, -0.03f, 0.05f);
                    p.barrel.localRotation = Quaternion.LookRotation(new Vector3(s, 0f, 0f));
                    Part(p.barrel, "OZ_ApcHatchGun", barrelMesh, metal, false, Vector3.zero, Quaternion.identity);
                    p.tip = new GameObject("OZ_ApcHatchTip").transform; p.tip.SetParent(p.barrel, false); p.tip.localPosition = new Vector3(0f, 0f, 0.36f);
                    p.crew.gameObject.SetActive(false);
                    ports.Add(p);
                }
            var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();
            Built++;
            return true;
        }

        bool Fire(Vector3 target, out Vector3 muzzle)
        {
            muzzle = default;
            var root = view.transform;
            var lt = root.InverseTransformPoint(target);
            int side = lt.x >= 0f ? 1 : -1;
            int k = side > 0 ? nextR++ : nextL++;
            Port pick = null; int n = 0;
            foreach (var p in ports) if (p.side == side) { if (n == k % 3) pick = p; n++; }
            if (pick == null) return false;
            n = 0;
            foreach (var p in ports)
            {   // the whole firing side opens and stays open through the burst: the firing hatch at once, its neighbours a beat later
                if (p.side != side) continue;
                float lag = Mathf.Abs(n - k % 3) * Stagger; n++;
                if (Time.time >= p.wantUntil) p.wantFrom = Time.time + lag;
                p.wantUntil = Time.time + Hold + lag;
            }
            // the barrel swings toward the target, at most 50 deg off the flank
            var d = lt - pick.local; d.y = Mathf.Clamp(d.y, -0.6f * Mathf.Abs(d.x) - 0.3f, 0.6f * Mathf.Abs(d.x) + 0.3f);
            var flat = new Vector3(d.x, 0f, d.z); if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(side, 0f, 0f);
            float yaw = Vector3.SignedAngle(new Vector3(side, 0f, 0f), flat, Vector3.up); yaw = Mathf.Clamp(yaw, -50f, 50f);
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * new Vector3(side, 0f, 0f);
            float pitch = Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Max(0.5f, flat.magnitude)) * Mathf.Rad2Deg, -20f, 20f);
            dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, dir)) * dir;
            pick.barrel.localRotation = Quaternion.LookRotation(pick.crew.InverseTransformDirection(root.TransformDirection(dir)));
            muzzle = pick.tip.position;
            Shots++;
            return true;
        }

        void LateUpdate()
        {
            if (!view || view.Obj == null) return;
            bool alive = !view.Dying && !view.Obj.Destroyed;
            float dt = Time.deltaTime;
            foreach (var p in ports)
            {
                bool want = alive && Time.time >= p.wantFrom && Time.time < p.wantUntil;
                p.open = Mathf.MoveTowards(p.open, want ? 1f : 0f, dt / (want ? OpenTime : CloseTime));
                bool isOpen = p.open > 0.02f;
                if (isOpen && !p.wasOpen) { Opens++; Sound(p, "apc_hatch_open", 0.32f, "s3_hatch_open_a", "s3_hatch_open_b", "s3_hatch_open_c"); }
                if (!isOpen && p.wasOpen) { Closes++; if (alive) Sound(p, "apc_hatch_close", 0.28f, "sd_hatch_close_a", "sd_hatch_close_b"); }
                p.wasOpen = isOpen;
                float e = p.open * p.open * (3f - 2f * p.open);
                p.hinge.localRotation = Quaternion.Euler(0f, 0f, p.side * OpenDeg * e);   // the mirrored left lid swings the other way
                bool crew = alive && p.open > 0.45f;
                if (p.crew.gameObject.activeSelf != crew) p.crew.gameObject.SetActive(crew);
            }
        }

        void Sound(Port p, string key, float vol, params string[] clips)
        {
            var au = CombatAudio.I; if (!au) return;
            var at = p.hinge.position;
            au.PlayPitched(key, at, vol, 0.04f, view.Obj.Id * 8 + ports.IndexOf(p), Random.Range(0.95f, 1.1f), clips);   // logs its own PLAY / DROP line
        }

        static void Part(Transform parent, string name, Mesh mesh, Material m, bool shadows, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
        }

        static void BuildMeshes()
        {
            // lid in hinge space: hangs down from the hinge (y 0 .. -PortH), thickness outward (+x), width along z; a gunmetal-free
            // rim lip and a little handle bar on its face
            lidMesh = OzLathe.Boxes("OZ_ApcHatchLid", new List<(Vector3, Vector3)>
            {
                (new Vector3(LidT * 0.5f, -PortH * 0.5f, 0f), new Vector3(LidT * 0.5f, PortH * 0.5f, PortW * 0.5f)),
                (new Vector3(LidT + 0.008f, -PortH * 0.62f, 0f), new Vector3(0.008f, 0.012f, PortW * 0.28f)),
                (new Vector3(LidT * 0.5f, -0.012f, 0f), new Vector3(LidT * 0.62f, 0.014f, PortW * 0.52f)),
            });
            plateMesh = OzLathe.Boxes("OZ_ApcPortPlate", new List<(Vector3, Vector3)> { (Vector3.zero, new Vector3(PortW * 0.5f - 0.02f, PortH * 0.5f - 0.02f, 0.004f)) });
            barrelMesh = OzLathe.Boxes("OZ_ApcHatchBarrel", new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, 0f, 0.18f), new Vector3(0.016f, 0.016f, 0.18f)),
                (new Vector3(0f, 0f, 0.345f), new Vector3(0.022f, 0.022f, 0.016f)),
                (new Vector3(0f, -0.03f, 0.02f), new Vector3(0.018f, 0.035f, 0.05f)),
            });
            eyeMesh = OzLathe.Boxes("OZ_ApcHatchEyes", new List<(Vector3, Vector3)>
            {
                (new Vector3(-0.028f, 0f, 0f), new Vector3(0.016f, 0.01f, 0.004f)),
                (new Vector3(0.028f, 0f, 0f), new Vector3(0.016f, 0.01f, 0.004f)),
            });
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode"), VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha");
        static readonly Dictionary<(Material, string), Material> matCache = new Dictionary<(Material, string), Material>();
        static Material Clone(Material src, string name, Color c, float smooth)
        {
            if (matCache.TryGetValue((src, name), out var cm) && cm) return cm;
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            matCache[(src, name)] = m;
            return m;
        }
    }
}

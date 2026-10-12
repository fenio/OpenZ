using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// closed sleeves, a hallway in each building's own palette with a lit inner portal, real door leaves / a slatted sectional shutter),
    /// driven by FactoryView's door openness (the Sim's production timing; view only):
    ///  * slide doors (fort, robot factory): gate_door_L / _R slide apart by the kit's travel (1.52 / 1.64 m), eased, fully hidden in the
    ///    shell when open;
    ///  * sectional shutter (vehicle factory): the 8 chained slats move the same distance along the real track: up the side channels,
    ///    round the 0.14 m arc (each turning 90 deg) and back along the ceiling rails, the bottom slat (hazard band + handle) last, so it
    ///    ends at the lintel, the rest stacked in the hall ceiling;
    ///  * the frame lamp lenses glow with the openness and blink while the door moves; the hallway strips glow (emissive, bloom);
    ///  * the door start sounds stay FactoryView's (bld_fort_gate / bld_door / bld_shutter per building); a leaf seating closed adds a
    ///    heavy door thud on that frame;
    ///  * every gate_* node hides when the building is destroyed (with the old door quads it was FactoryView's own children).
    /// Positions are taken from the kit's closed pose in prefab-root metres (the report's numbers), so any import convention works.
    /// </summary>
    public class GateKitView : MonoBehaviour
    {
        public static bool Enabled = true;
        public static int Bound, Seats;
        public string Key { get; private set; }
        Transform root, doorL, doorR, lensL, lensR; Vector3 doorL0, doorR0; float travel;
        readonly List<Transform> slats = new List<Transform>(); readonly List<Vector3> slat0 = new List<Vector3>(); readonly List<Quaternion> slatQ0 = new List<Quaternion>();
        float arcY, arcR, zDoor, dMax, slatH; Vector3 slatAxisLocal;
        readonly List<Renderer> renderers = new List<Renderer>(); readonly List<Renderer> lenses = new List<Renderer>();
        MaterialPropertyBlock mpb; float lastOpen = -1f; bool shown = true;
        public Vector3 LightPoint { get; private set; }
        public Vector3 InPoint { get; private set; }
        public Vector3 DoorPoint { get; private set; }
        public bool HasLight { get; private set; }
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static Transform Find(Transform t, string n) { if (t.name == n) return t; foreach (Transform c in t) { var f = Find(c, n); if (f) return f; } return null; }

        /// <summary>Binds the kit under `building` (the static view root). Null when the building has no gate kit.</summary>
        public static GateKitView Bind(Transform building, string key)
        {
            if (!Enabled || !building || !Find(building, "gate_hall")) return null;
            var g = building.GetComponent<GateKitView>(); if (!g) g = building.gameObject.AddComponent<GateKitView>();
            g.Init(building, key);
            return g;
        }

        Vector3 RootPt(Transform t) => root.InverseTransformPoint(t.position);
        Vector3 Mark(string n, Vector3 fallback) { var t = Find(root, n); return t ? RootPt(t) : fallback; }

        void Init(Transform building, string key)
        {
            root = building; Key = key; mpb ??= new MaterialPropertyBlock();
            doorL = Find(root, "gate_door_L"); doorR = Find(root, "gate_door_R");
            if (doorL) doorL0 = doorL.localPosition; if (doorR) doorR0 = doorR.localPosition;
            travel = key == "fort" ? 1.52f : 1.64f;
            slats.Clear(); slat0.Clear(); slatQ0.Clear();
            for (int i = 0; i < 16; i++) { var s = Find(root, "gate_slat_" + i.ToString("00")); if (!s) break; slats.Add(s); slat0.Add(RootPt(s)); slatQ0.Add(s.localRotation); }
            if (slats.Count > 0)
            {
                var arc = Mark("gate_mark_arc", new Vector3(0f, 2.04f, 2.454f));
                slatH = slats.Count > 1 ? Mathf.Abs(slat0[1].y - slat0[0].y) : 0.2385f;
                arcY = arc.y; zDoor = slat0[0].z; arcR = Mathf.Max(0.05f, zDoor - arc.z);
                dMax = (arcY - slat0[0].y) + arcR * Mathf.PI * 0.5f + 0.5f * slatH;            // the bottom slat clears the arc
                slatAxisLocal = slats[0].parent ? slats[0].parent.InverseTransformDirection(root.TransformDirection(Vector3.right)) : Vector3.right;
            }
            lensL = Find(root, "gate_lamp_lens_L"); lensR = Find(root, "gate_lamp_lens_R");
            lenses.Clear(); foreach (var l in new[] { lensL, lensR }) if (l && l.TryGetComponent<Renderer>(out var lr)) lenses.Add(lr);
            renderers.Clear();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("gate_") || (r.transform.parent && r.transform.parent.name.StartsWith("gate_"))) renderers.Add(r);
            var lp = Find(root, "gate_mark_light"); HasLight = lp; LightPoint = lp ? RootPt(lp) : Vector3.zero;
            InPoint = Mark("gate_mark_in", Vector3.zero); DoorPoint = Mark("gate_mark_door", Vector3.zero);
            Bound++;
            Debug.Log($"[OpenZ Gates] {key} on {root.name}: {(doorL && doorR ? "slide doors" : slats.Count + " slats")}, {renderers.Count} renderers, light {(HasLight ? LightPoint.ToString() : "-")}, portal {InPoint}");
        }

        /// <summary>Openness 0..1 (FactoryView.Open), every frame the door changes, plus once a frame for the lamps.</summary>
        public void Apply(float open, float time)
        {
            float k = open * open * (3f - 2f * open);
            if (doorL && doorR)
            {
                var dir = root.TransformVector(Vector3.right);
                var pL = doorL.parent; var pR = doorR.parent;
                doorL.localPosition = doorL0 - pL.InverseTransformVector(dir * (travel * k));
                doorR.localPosition = doorR0 + pR.InverseTransformVector(dir * (travel * k));
            }
            if (slats.Count > 0)
            {
                float d = dMax * k;
                for (int i = 0; i < slats.Count; i++)
                {
                    var s0 = slat0[i]; float sy = s0.y + d, y, z, ang;
                    if (sy <= arcY) { y = sy; z = zDoor; ang = 0f; }
                    else
                    {
                        float a = sy - arcY, q = arcR * Mathf.PI * 0.5f;
                        if (a <= q) { float th = a / arcR; y = arcY + arcR * Mathf.Sin(th); z = (zDoor - arcR) + arcR * Mathf.Cos(th); ang = th; }
                        else { y = arcY + arcR; z = (zDoor - arcR) - (a - q); ang = Mathf.PI * 0.5f; }
                    }
                    var t = slats[i];
                    t.position = root.TransformPoint(new Vector3(s0.x, y, z));
                    t.localRotation = Quaternion.AngleAxis(-ang * Mathf.Rad2Deg, slatAxisLocal) * slatQ0[i];
                }
            }
            if (lastOpen > 0.001f && open <= 0.001f)
            {   // the leaves / shutter seat closed: a heavy thud on that frame
                Seats++;
                var au = CombatAudio.I;
                if (au) au.Play("gate_seat", root.TransformPoint(DoorPoint + Vector3.up * 1f), 0.38f, 0.2f, root.GetInstanceID(), "sd_door_heavy_close_a", "sd_door_heavy_close_b");
            }
            bool moving = lastOpen >= 0f && Mathf.Abs(open - lastOpen) > 1e-4f;
            lastOpen = open;
            float glow = Mathf.Lerp(0.25f, 2.6f, k) * (moving ? (Mathf.Repeat(time * 4f, 1f) < 0.5f ? 1.2f : 0.35f) : 1f);
            foreach (var r in lenses)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetColor(EmissionId, new Color(1f, 0.62f, 0.25f) * glow); r.SetPropertyBlock(mpb);
            }
        }

        public void SetShown(bool on)
        {
            if (on == shown) return;
            shown = on;
            foreach (var r in renderers) if (r) r.enabled = on;
        }
    }
}

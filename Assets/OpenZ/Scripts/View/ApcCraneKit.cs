using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the APC and the crane use the MODELLED apc_crane_rebuild_v1 kit (Blender, closed shells, 0 open edges) instead of meshes cut
    /// at run time. Live (UnitView.Bind -> WheelSplit.Apply): the Model node shows the closed body (capped wheel wells, axle stubs)
    /// with the original whole-model bounds (TeamPaint's panel / accent band and the crane's roof hatch keep their place), the
    /// closed tyres become WheelSpin_Kit_n nodes (axle = local x, VehicleMotion spins them like every WheelSpin_ node; the tyre
    /// texels are neutral grey, the accent band is off), the crane's turntable / boom / jib / hook / hydraulic ram / pins are their
    /// own nodes in the rest pose. Death (WheeledBreakup.Tear): variant Vn = id hash % 3 swaps the hull for its modelled wreck
    /// (torn holes with 5 cm walls, charred liner + ribs, torn-edge metal; the APC rear door recess cut as on the live hull),
    /// its closed torn plates fly off as wreck pieces (WreckPieces.Single: clanks, settle, sink / pop after ~9-17 s), a thrown
    /// wheel is the modelled tyre itself and its well shows the modelled axle stub. The crane keeps the boom pose it died in.
    /// No WheelSplit cut, no CloseTyre / OZ_TyreCore cylinder, no OZ_HoleCore / Chunkify box for these two types; Enabled = false
    /// (fallback) brings the old path back. The baked OZ_WreckFragments of the old imported mesh are dropped at the swap, so a later
    /// DV18 break throws the plates, tyres, dome and door but leaves the modelled hull standing (IsKitHull).
    /// </summary>
    public static class ApcCraneKit
    {
        public static bool Enabled = true;
        public static int LiveBuilt, Wrecks, Plates; public static string Last = "";
        static ApcCraneKitData data; static bool loaded;
        static readonly HashSet<Renderer> kitHulls = new HashSet<Renderer>();
        static readonly Dictionary<string, Bounds> whole = new Dictionary<string, Bounds>();
        static readonly Dictionary<(Mesh, Vector3), Mesh> boundsCopy = new Dictionary<(Mesh, Vector3), Mesh>();
        static readonly Dictionary<Mesh, Mesh> tyreCopy = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Material, Material> charMats = new Dictionary<Material, Material>(), tornMats = new Dictionary<Material, Material>();

        static ApcCraneKitData Data
        {
            get
            {
                if (!loaded) { loaded = true; data = Resources.Load<ApcCraneKitData>("OpenZ/Vehicles/ApcCraneKit"); if (!data) Debug.LogWarning("[OpenZ] ApcCraneKit: Resources/OpenZ/Vehicles/ApcCraneKit missing: the APC / crane keep the run-time cut"); }
                return data;
            }
        }
        static GameObject LiveOf(string type) => Data == null ? null : type == "apc" ? Data.apcLive : type == "crane" ? Data.craneLive : null;
        static GameObject[] WrecksOf(string type) => Data == null ? null : type == "apc" ? Data.apcWreck : type == "crane" ? Data.craneWreck : null;
        public static bool Has(string type) => Enabled && (type == "apc" || type == "crane") && LiveOf(type) != null;
        /// <summary>WreckPieces.Break: the modelled wreck hull stays standing (the plates, tyres, dome and door fly).</summary>
        public static bool IsKitHull(Renderer r) => r && kitHulls.Contains(r);
        public static bool IsKitTyre(string n) => n != null && n.StartsWith("WheelSpin_Kit_");
        /// <summary>UnitView.BreakApart: this wreck's hull is a modelled kit hull, so it stays (burns, smokes, sinks at its normal time).</summary>
        public static bool HullStays(UnitView v)
        {
            var m = v ? v.transform.Find("Model") : null; var r = m ? m.GetComponent<MeshRenderer>() : null;
            return r && kitHulls.Contains(r) && r.enabled;
        }

        /// <summary>WheelSplit.Apply for the apc / crane: the live kit on the Model node. true = done (no run-time cut).</summary>
        public static bool ApplyLive(GameObject go, string type)
        {
            if (!go || !Has(type)) return false;
            var root = go.transform; var model = root.Find("Model");
            var mf = model ? model.GetComponent<MeshFilter>() : null; var mr = model ? model.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) return false;
            if (model.Find("OZ_KitLive")) return true;
            var src = LiveOf(type); var bodyMf = src.GetComponent<MeshFilter>();
            if (!bodyMf || !bodyMf.sharedMesh) { Debug.LogWarning("[OpenZ] ApcCraneKit: " + src.name + " has no body mesh on its root"); return false; }
            var wb = mf.sharedMesh.bounds;                                                                   // the original whole-model bounds
            if (!whole.ContainsKey(type)) whole[type] = wb;
            wb = whole[type];
            var lk = mr.sharedMaterial;
            mf.sharedMesh = WithBounds(bodyMf.sharedMesh, wb, Vector3.zero);
            var axis = model.InverseTransformDirection(root.right); axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
            var rot = Quaternion.FromToRotation(Vector3.right, axis);                                         // WheelSplit's wheel frame: local x = the axle
            var wheelMats = WheelSplit.WheelMatsFor(new[] { lk });
            int wi = 0, parts = 0;
            foreach (Transform c in src.transform)
            {
                var cmf = c.GetComponent<MeshFilter>(); if (!cmf || !cmf.sharedMesh) continue;
                if (c.name.Contains("_Tyre_"))
                {
                    var w = new GameObject("WheelSpin_Kit_" + wi++) { layer = model.gameObject.layer };
                    w.transform.SetParent(model, false);
                    w.transform.localPosition = c.localPosition; w.transform.localRotation = rot; w.transform.localScale = Vector3.one;
                    w.AddComponent<MeshFilter>().sharedMesh = TyreMesh(cmf.sharedMesh, rot);
                    var r = w.AddComponent<MeshRenderer>(); r.sharedMaterials = wheelMats; Copy(mr, r);
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;                          // spinning: motion blur
                }
                else parts += CloneTree(c, model, lk, mr, wb, Vector3.zero);
            }
            new GameObject("OZ_KitLive") { layer = model.gameObject.layer }.transform.SetParent(model, false);
            LiveBuilt++;
            Last = $"{type} live kit: body {bodyMf.sharedMesh.name} ({bodyMf.sharedMesh.vertexCount} v), {wi} closed tyres, {parts} articulated parts";
            if (LiveBuilt <= 4) Debug.Log("[OpenZ] ApcCraneKit " + Last);
            return true;
        }

        static int CloneTree(Transform s, Transform parent, Material lk, MeshRenderer like, Bounds wb, Vector3 parentPivot)
        {
            var g = new GameObject(s.name) { layer = parent.gameObject.layer };
            g.transform.SetParent(parent, false);
            g.transform.localPosition = s.localPosition; g.transform.localRotation = s.localRotation; g.transform.localScale = s.localScale;
            var pivot = parentPivot + s.localPosition;                                                        // the rest pose has no rotations
            int n = 0;
            var smf = s.GetComponent<MeshFilter>();
            if (smf && smf.sharedMesh)
            {
                g.AddComponent<MeshFilter>().sharedMesh = WithBounds(smf.sharedMesh, wb, pivot);
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = lk; Copy(like, r); n++;
            }
            foreach (Transform c in s) n += CloneTree(c, g.transform, lk, like, wb, pivot);
            return n;
        }

        static void Copy(MeshRenderer from, MeshRenderer to)
        {
            to.shadowCastingMode = from.shadowCastingMode; to.receiveShadows = from.receiveShadows;
            to.lightProbeUsage = from.lightProbeUsage; to.reflectionProbeUsage = from.reflectionProbeUsage;
            to.renderingLayerMask = from.renderingLayerMask;
        }

        /// <summary>A copy of m whose bounds are the whole model's in the part's own frame (README: TeamPaint's panel rect is built
        /// from the renderer's mesh bounds, the trick OpenZTurretSplit uses for the split cannons).</summary>
        static Mesh WithBounds(Mesh m, Bounds wb, Vector3 pivot)
        {
            var key = (m, pivot);
            if (boundsCopy.TryGetValue(key, out var c) && c) return c;
            c = Object.Instantiate(m); c.name = m.name + "_oz";
            c.bounds = new Bounds(wb.center - pivot, wb.size);
            boundsCopy[key] = c;
            return c;
        }

        /// <summary>The closed tyre re-based into the wheel frame (axle = local x), once per source mesh.</summary>
        static Mesh TyreMesh(Mesh src, Quaternion rot)
        {
            if (tyreCopy.TryGetValue(src, out var c) && c) return c;
            c = Object.Instantiate(src); c.name = src.name + "_wheel";
            var inv = Quaternion.Inverse(rot);
            var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = inv * v[i];
            c.vertices = v;
            var n = src.normals; if (n != null && n.Length == v.Length) { for (int i = 0; i < n.Length; i++) n[i] = inv * n[i]; c.normals = n; }
            var t = src.tangents;
            if (t != null && t.Length == v.Length)
            {
                for (int i = 0; i < t.Length; i++) { var tv = inv * new Vector3(t[i].x, t[i].y, t[i].z); t[i] = new Vector4(tv.x, tv.y, tv.z, t[i].w); }
                c.tangents = t;
            }
            c.RecalculateBounds();
            tyreCopy[src] = c;
            return c;
        }

        static Material CharMat(Material like)
        {
            if (charMats.TryGetValue(like, out var m) && m) return m;
            m = new Material(WreckPieces.HoleMat(like)) { name = "OZ_WreckChar" };                         // README: burnt liner + ribs rgb .045 / .038 / .034, rough
            var c = new Color(0.045f, 0.038f, 0.034f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            charMats[like] = m; return m;
        }

        static Material TornMat(Material like)
        {
            if (tornMats.TryGetValue(like, out var m) && m) return m;
            m = new Material(WreckPieces.CoreMat(like)) { name = "OZ_WreckTornEdge" };                     // README: torn metal rim .20 / .19 / .18, metallic .6
            var c = new Color(0.20f, 0.19f, 0.18f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.6f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            if (m.HasProperty("_UnitShade")) { var us = m.GetVector("_UnitShade"); us.w = 0.6f; m.SetVector("_UnitShade", us); }
            tornMats[like] = m; return m;
        }

        /// <summary>WheeledBreakup.Tear for a kit APC / crane: swap the hull for wreck variant Vn under the death blast and throw its
        /// closed torn plates. Returns the plates thrown, or -1 when the kit is not on this vehicle (the old TearHull runs).</summary>
        public static int Wreck(UnitView v, Vector3 centre, int id, List<string> names)
        {
            if (!v || v.Obj == null) return -1;
            string type = v.Obj.Type; var set = WrecksOf(type);
            var model = v.transform.Find("Model");
            if (!Has(type) || set == null || set.Length == 0 || !model || !model.Find("OZ_KitLive")) return -1;
            var mf = model.GetComponent<MeshFilter>(); var mr = model.GetComponent<MeshRenderer>();
            if (!mf || !mr) return -1;
            int vn = (int)(RobotDeathRule.Hash(id, 61, 37u) % (uint)set.Length);
            var src = set[vn]; if (!src) return -1;
            Transform hull = null; var plates = new List<Transform>();
            foreach (Transform c in src.transform)
            {
                if (!c.GetComponent<MeshFilter>()) continue;
                if (c.name.Contains("_Plate_")) plates.Add(c); else hull = c;
            }
            if (!hull) return -1;
            var wb = whole.TryGetValue(type, out var b0) ? b0 : mf.sharedMesh.bounds;
            var hm = WithBounds(hull.GetComponent<MeshFilter>().sharedMesh, wb, hull.localPosition);
            if (type == "apc") hm = ApcRearDoor.CutDoorOf(hm, v.transform.worldToLocalMatrix * model.localToWorldMatrix);   // the door leaf sits in the recess, as on the live hull
            var lk = mr.sharedMaterial;
            var mats = new[] { lk, CharMat(lk), TornMat(lk) };
            mf.sharedMesh = hm; mr.sharedMaterials = mats;
            kitHulls.Add(mr);
            var frag = model.Find("OZ_WreckFragments"); if (frag) Object.Destroy(frag.gameObject);           // the old imported mesh's baked bins: never shown now
            var made = new List<Transform>();
            foreach (var pl in plates)
            {
                var g = new GameObject(pl.name) { layer = model.gameObject.layer };
                g.transform.SetParent(model, false);
                g.transform.localPosition = pl.localPosition; g.transform.localRotation = pl.localRotation; g.transform.localScale = pl.localScale;
                g.AddComponent<MeshFilter>().sharedMesh = WithBounds(pl.GetComponent<MeshFilter>().sharedMesh, wb, pl.localPosition);
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats; Copy(mr, r);
                made.Add(g.transform);
            }
            var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();                                        // team / dead tint on the hull and the plates before they leave
            int n = 0;
            foreach (var g in made) if (WreckPieces.Single(g, centre, 1f, id + 313 * (n + 1))) n++;
            Wrecks++; Plates += n;
            Last = $"{type}#{id}: modelled wreck V{vn + 1} ({hm.vertexCount} v, 3 slots), {n}/{plates.Count} closed plates thrown";
            names.Add($"modelled wreck V{vn + 1} + {n} plates");
            Debug.Log("[OpenZ] ApcCraneKit " + Last);
            return n;
        }
    }
}

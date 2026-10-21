using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only.
    /// Every wheeled vehicle death (jeep, APC, crane; the MLRS too, which is tracked in our model: parts only) tears 2-4 real parts of its
    /// own model off as physics debris (WreckPieces.Single: clanks, settle, sink) and throws 1 wheel (a second one 30 %) that flies off
    /// spinning and ROLLS (<see cref="RollingWheel"/>). What stays is a charred wreck with holes: the single-mesh imported APC / crane show
    /// their baked wreck fragments (tyres stripped, OpenZWreckFragments) minus the torn ones, with a dark burnt core in each hole, and a
    /// bare dark axle stub where a wheel was; a missing corner wheel lets that corner slump. Runs at the death, before the DV17 flip /
    /// DV18 break (which then take what is left).
    /// </summary>
    public static class WheeledBreakup
    {
        public static bool Enabled = true;
        public static int Deaths, Parts, Wheels, HullBins, Cores, Stubs, Slumps;
        public static string Last = "";
        static readonly HashSet<string> Types = new HashSet<string> { "jeep", "apc", "crane", "missile_launcher" };
        static readonly HashSet<string> Big = new HashSet<string> { "TurretYaw", "Seat_driver", "mlrs_HatchHinge", "MLRSRackPitch", "mlrs_EntryRail" };

        public static bool Applies(ZObject o) => Enabled && o != null && o.Kind == ZKind.Vehicle && Types.Contains(o.Type);
        public static bool IsWheelName(string n) => n.StartsWith("Wheel_") || n.StartsWith("WheelSpin_");

        /// <summary>UnitView.BeginWreck. centre = the blast centre (about 1 m over the hull's pivot).</summary>
        public static void Tear(UnitView v, Vector3 centre, bool flipping)
        {
            if (!v || v.Obj == null) return;
            var root = v.transform; var o = v.Obj; int id = o.Id;
            uint h = RobotDeathRule.Hash(id, 5, 31u);
            int want = 2 + (int)(h % 3u), torn = 0, bins = 0;
            var names = new List<string>();

            // ---- parts of its own model
            if (o.Type == "apc")
            {
                if (v.GetComponent<ApcDome>()) { ApcDome.BreakOff(v, centre, 1f, id); torn++; names.Add("dome"); }
                if (RoundHatch.BreakOff(v, centre, 1f, id)) names.Add("roof hatch");                          // DV87: two of the three modelled wrecks are torn open under the driver's roof hatch
                if ((h >> 3) % 10u < 7u && ApcRearDoor.Of(v)) { ApcRearDoor.BreakOff(v, centre, 1f, id); torn++; names.Add("rear door"); }
                int kw = ApcCraneKit.Wreck(v, centre, id, names);
                bins = kw >= 0 ? kw : TearHull(v, centre, Mathf.Max(1, want - torn), 4, id, names);
            }
            else if (o.Type == "crane") { int kw = ApcCraneKit.Wreck(v, centre, id, names); bins = kw >= 0 ? kw : TearHull(v, centre, want, 6, id, names); }
            else
            {
                var cand = new List<Transform[]>();
                if (o.Type == "jeep")
                {
                    Add(root, cand, "TurretYaw", "GunMount"); Add(root, cand, "Seat_driver"); Add(root, cand, "Seat_passenger"); Add(root, cand, "SteeringWheel");   // the gun leaves with its mount (never a floating gun)
                    Add(root, cand, "Antenna", "Antenna_owner_pennant"); Add(root, cand, "SpareCover_owner");
                }
                else if (o.Type == "missile_launcher")
                {
                    Add(root, cand, "mlrs_HatchHinge"); Add(root, cand, "mlrs_EntryRail", "mlrs_EntryRail.001", "mlrs_EntryRung", "mlrs_EntryRung.001", "mlrs_EntryRung.002", "mlrs_EntryRung.003");
                    Add(root, cand, "MLRS_headlamp"); Add(root, cand, "MLRS_headlamp.001"); Add(root, cand, "MLRSLoadedRocket_0" + (1 + h % 8u));
                    Add(root, cand, "MLRSLoadedRocket_0" + (1 + (h >> 4) % 8u)); Add(root, cand, "MLRSRackPitch");
                    if ((h >> 9) % 10u < 3u) Add(root, cand, (h >> 11) % 2u == 0u ? "MLRS_RegularTread_L" : "MLRS_RegularTread_R");
                }
                uint s = h | 1u;
                for (int i = cand.Count - 1; i > 0; i--) { int j = (int)(WreckFx.Rnd(ref s) * (i + 1)); var t = cand[i]; cand[i] = cand[j]; cand[j] = t; }
                for (int i = 0; i < cand.Count; i++)
                    if (cand[i][0] && Big.Contains(cand[i][0].name)) { var t = cand[0]; cand[0] = cand[i]; cand[i] = t; break; }   // always one big part (gun, seat, hatch, rack, ladder)
                for (int i = 0; i < cand.Count && torn < want; i++)
                {
                    var group = cand[i]; if (group[0] == null || !group[0].IsChildOf(root)) continue;
                    Transform part = group[0];
                    if (group.Length > 1)
                    {   // several siblings leave as one piece (the antenna with its pennant, the ladder rails with their rungs)
                        var holder = new GameObject("OZ_TornPart").transform; holder.SetPositionAndRotation(group[0].position, group[0].rotation);
                        holder.SetParent(group[0].parent, true);
                        foreach (var t in group) if (t && t.IsChildOf(root)) t.SetParent(holder, true);
                        part = holder;
                    }
                    if (WreckPieces.Single(part, centre, 1f, id + 977 * (i + 1))) { torn++; names.Add(group[0].name); }
                }
            }
            Parts += torn + bins; HullBins += bins;

            // ---- wheels: 1, a second one 30 %
            var wheels = new List<Transform>(); float maxFwd = 0.01f;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.enabled || !IsWheelName(mr.name) || !mr.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                wheels.Add(mr.transform); maxFwd = Mathf.Max(maxFwd, Mathf.Abs(root.InverseTransformPoint(mr.bounds.center).z));
            }
            int nw = wheels.Count == 0 ? 0 : Mathf.Min(wheels.Count, 1 + ((h >> 7) % 10u < 3u ? 1 : 0));
            uint ws = (h >> 2) | 1u; int thrown = 0, plain = 0;
            var fwd = root.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            for (int k = 0; k < nw; k++)
            {
                int pick = (int)(WreckFx.Rnd(ref ws) * wheels.Count) % wheels.Count;
                var w = wheels[pick]; wheels.RemoveAt(pick);
                var wr = w.GetComponent<MeshRenderer>(); var wc = wr.bounds.center; float wrad = Mathf.Max(wr.bounds.extents.y, 0.25f);
                var local = root.InverseTransformPoint(wc);
                if (!ApcCraneKit.IsKitTyre(w.name)) AxleStub(w, wc, wrad);
                RollingWheel.BurnTyre(w);
                bool rolled = RollingWheel.CanSpawn() && RollingWheel.Throw(w, root.position, fwd, 1f, id * 7 + k, RobotDeathRule.Hash(id, 40 + k, 33u)) != null;
                if (!rolled) { WreckPieces.Single(w, centre, 0.8f, id + 4441 + k); plain++; }
                else thrown++;
                Wheels++;
                if (!flipping) Slump(root, local, Mathf.Abs(local.z) / maxFwd, wrad);
            }
            Deaths++;
            Last = $"{o.Type}#{id}: {torn + bins} parts torn ({string.Join(", ", names.ToArray())}{(bins > 0 ? $"; {bins} hull bin(s)" : "")}), " +
                   $"{thrown} wheel(s) rolling{(plain > 0 ? $", {plain} as plain debris (3 already rolling)" : "")}{(nw == 0 ? " (tracked: no wheels)" : "")}{(flipping ? ", hull flips" : "")}";
            Debug.Log("[OpenZ] wheeled wreck " + Last);
        }

        static void Add(Transform root, List<Transform[]> cand, params string[] names)
        {
            var g = new Transform[names.Length]; bool any = false;
            for (int i = 0; i < names.Length; i++) { g[i] = Find(root, names[i]); any |= g[i] != null; }
            if (!any || g[0] == null) return;
            foreach (var r in g[0].GetComponentsInChildren<Renderer>()) if (r.enabled) { cand.Add(g); return; }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }

        /// <summary>The single-mesh imported APC / crane: the hull is shown as its baked fragments (tyres stripped, the whole model's bounds
        /// so TeamPaint's accent band stays put), `want` of the `top` highest fragments are torn off and each hole gets a dark core.</summary>
        static int TearHull(UnitView v, Vector3 centre, int want, int top, int id, List<string> names)
        {
            var model = v.transform.Find("Model");
            var mr = model ? model.GetComponent<MeshRenderer>() : null; var mf = model ? model.GetComponent<MeshFilter>() : null;
            var holder = model ? model.Find("OZ_WreckFragments") : null;
            if (!mr || !mf || !mf.sharedMesh || !holder) return 0;
            if (!holder.gameObject.activeSelf)
            {
                var b = mf.sharedMesh.bounds;
                foreach (Transform f in holder) if (f.TryGetComponent<MeshFilter>(out var fmf) && fmf.sharedMesh) fmf.sharedMesh = WheelSplit.WreckFragment(fmf.sharedMesh, v.Obj.Type, b);
                holder.gameObject.SetActive(true); mr.enabled = false;
                var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();
            }
            var frs = new List<(Transform t, float y)>();
            foreach (Transform f in holder)
                if (f.TryGetComponent<MeshRenderer>(out var r) && r.enabled && f.name.StartsWith("frag_") && f.TryGetComponent<MeshFilter>(out var ff))
                    frs.Add((f, f.TransformPoint(WheelSplit.FragmentBounds(ff.sharedMesh).center).y));   // its own geometry, not the copy's whole-model bounds
            frs.Sort((a, b) => b.y.CompareTo(a.y));
            if (frs.Count > top) frs.RemoveRange(top, frs.Count - top);
            uint s = RobotDeathRule.Hash(id, 9, 35u) | 1u; int n = 0;
            var tornBoxes = new List<Bounds>();
            var pend = new List<(int box, Bounds fb, Material lk)>();
            while (n < want && frs.Count > 0)
            {
                int i = (int)(WreckFx.Rnd(ref s) * frs.Count) % frs.Count;
                if (n == 0 && HoleFix) i = CameraSide(frs);
                var f = frs[i].t; frs.RemoveAt(i);
                int bi = -1;
                if (f.TryGetComponent<MeshFilter>(out var tf0) && tf0.sharedMesh) { tornBoxes.Add(WheelSplit.FragmentBounds(tf0.sharedMesh)); bi = tornBoxes.Count - 1; }   // before Chunkify: the bin's own box
                WreckPieces.Chunkify(f);
                var fm = f.GetComponent<MeshFilter>().sharedMesh; var fb = fm.bounds;
                if (HoleFix && bi >= 0 && Mathf.Min(fb.size.x, Mathf.Min(fb.size.y, fb.size.z)) * holder.lossyScale.y > 0.55f)
                    pend.Add((bi, fb, f.GetComponent<MeshRenderer>().sharedMaterial));
                else if (Mathf.Min(fb.size.x, Mathf.Min(fb.size.y, fb.size.z)) * holder.lossyScale.y > 0.55f)
                {   // the hole shows burnt insides, never the sky through the shell
                    var core = new GameObject("OZ_HoleCore");
                    core.transform.SetParent(holder, false);
                    if (HoleFix)
                    {
                        core.transform.localPosition = fb.center - Vector3.up * fb.size.y * 0.18f; core.transform.localScale = Vector3.Scale(fb.size, new Vector3(0.9f, 0.7f, 0.9f));
                    }
                    else { core.transform.localPosition = fb.center; core.transform.localScale = fb.size * 0.7f; }
                    core.AddComponent<MeshFilter>().sharedMesh = WreckPieces.CoreMesh();
                    var cr = core.AddComponent<MeshRenderer>(); var lk = f.GetComponent<MeshRenderer>().sharedMaterial;
                    cr.sharedMaterial = HoleFix ? WreckPieces.HoleMat(lk) : WreckPieces.CoreMat(lk);
                    Cores++;
                }
                if (WreckPieces.Single(f, centre, 1f, id + 313 * (n + 1))) { n++; names.Add("hull " + f.name); }
            }
            if (HoleFix && tornBoxes.Count > 0)
            {
                var rimLow = RimAndMatte(holder, tornBoxes);
                var hull = HullBox(model, holder, mf.sharedMesh.bounds);
                foreach (var pc in pend) { Cavity(holder, tornBoxes[pc.box], pc.fb, rimLow[pc.box], hull, pc.lk); Cores++; }
            }
            return n;
        }

        /// <summary>The model mesh's bounds in the fragment holder's space (the fragments' own space).</summary>
        static Bounds HullBox(Transform model, Transform holder, Bounds b)
        {
            var o = new Bounds(); bool any = false;
            for (int k = 0; k < 8; k++)
            {
                var c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                var p = holder.InverseTransformPoint(model.TransformPoint(c));
                if (!any) { o = new Bounds(p, Vector3.zero); any = true; } else o.Encapsulate(p);
            }
            return o;
        }

        public static int CavityLowRim;
        /// <summary>
        /// triangles reached that far into the neighbours, where the road showed through), clamped inside the hull; the top just below
        /// the low torn rim (20th percentile of the clamped rim heights: never a raised black slab), at most 0.6 of the bin deep, never
        /// through the hull floor. rimLow = NaN: no rim measured, the old depth (bin centre).</summary>
        static void Cavity(Transform holder, Bounds tb, Bounds fb, float rimLow, Bounds hull, Material lk)
        {
            float sc = Mathf.Max(1e-4f, holder.lossyScale.y);
            var ext = tb; ext.Expand(new Vector3(tb.size.x * 0.06f, 0f, tb.size.z * 0.06f));
            var hmin = hull.min + hull.size * 0.04f; var hmax = hull.max - hull.size * 0.04f;
            float x0 = Mathf.Max(ext.min.x, hmin.x), x1 = Mathf.Min(ext.max.x, hmax.x), z0 = Mathf.Max(ext.min.z, hmin.z), z1 = Mathf.Min(ext.max.z, hmax.z);
            if (x1 - x0 < 0.05f / sc || z1 - z0 < 0.05f / sc) { x0 = fb.min.x; x1 = fb.max.x; z0 = fb.min.z; z1 = fb.max.z; }
            float rim = float.IsNaN(rimLow) ? fb.center.y : rimLow;
            if (!float.IsNaN(rimLow)) CavityLowRim++;
            float top = Mathf.Min(rim, fb.max.y) - tb.size.y * 0.04f;
            float bottom = Mathf.Max(top - Mathf.Max(tb.size.y * 0.6f, 0.25f / sc), hull.min.y + hull.size.y * 0.2f);
            if (top - bottom < 0.08f / sc) bottom = top - 0.08f / sc;
            var core = new GameObject("OZ_HoleCore");
            core.transform.SetParent(holder, false);
            core.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (top + bottom) * 0.5f, (z0 + z1) * 0.5f);
            core.transform.localScale = new Vector3(x1 - x0, top - bottom, z1 - z0);
            core.AddComponent<MeshFilter>().sharedMesh = WreckPieces.CoreMesh();
            core.AddComponent<MeshRenderer>().sharedMaterial = WreckPieces.HoleMat(lk);
        }

        /// <summary>
        /// the L17 crop, the hole on the far side behind spikes) and light-grey wedge faces stayed on the charred hull. HoleFix: the first torn
        /// top bin is the one on the camera's side, the remaining bins' triangles that poked into a torn bin (the jagged spikes: binned by
        /// their centre, reaching into the hole) are clamped back to their own bin wall and drawn in the burnt core tone (a dark torn rim), the
        /// cavity is deeper and darker than the rim, and the whole charred hull is matte (TeamLit _UnitShade.w 1: the bare-metal sheen on the
        /// </summary>
        public static bool HoleFix = true;
        public static int RimTris, RimFrags, MatteFrags;
        static readonly int UnitShadeId = Shader.PropertyToID("_UnitShade");

        static int CameraSide(List<(Transform t, float y)> frs)
        {
            var cam = Camera.main; if (!cam || frs.Count == 0) return 0;
            var mid = Vector3.zero; foreach (var e in frs) mid += Centre(e.t); mid /= frs.Count;
            var toCam = cam.transform.position - mid; toCam.y = 0f; if (toCam.sqrMagnitude < 1e-4f) return 0;
            int best = 0; float bs = float.MinValue;
            for (int i = 0; i < frs.Count; i++) { var d = Centre(frs[i].t) - mid; d.y = 0f; float sc = Vector3.Dot(d, toCam); if (sc > bs) { bs = sc; best = i; } }
            return best;
        }

        static Vector3 Centre(Transform f) => f.TryGetComponent<MeshFilter>(out var m) && m.sharedMesh ? f.TransformPoint(WheelSplit.FragmentBounds(m.sharedMesh).center) : f.position;

        static float[] RimAndMatte(Transform holder, List<Bounds> torn)
        {
            var mpb = new MaterialPropertyBlock();
            // box's own top, were outside the old all-axis shrink and never clamped)
            var test = new Bounds[torn.Count]; var rimY = new List<float>[torn.Count];
            for (int j = 0; j < torn.Count; j++)
            {
                var tb = torn[j]; var sb = tb; sb.Expand(new Vector3(-tb.size.x * 0.02f, tb.size.y * 0.04f, -tb.size.z * 0.02f));
                test[j] = sb; rimY[j] = new List<float>();
            }
            foreach (Transform f in holder)
            {
                if (!f.name.StartsWith("frag_") || !f.TryGetComponent<MeshRenderer>(out var r) || !r.enabled || !f.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var src = mf.sharedMesh; var mats = r.sharedMaterials;
                if (src.isReadable && mats.Length > 0 && mats[0])
                {
                    var v = src.vertices;
                    // its own bin: the box of its triangle centres (+3 %), like WreckPieces.Chunkify
                    var box = new Bounds(); bool any = false;
                    for (int sm = 0; sm < src.subMeshCount; sm++)
                    {
                        var tri = src.GetTriangles(sm);
                        for (int i = 0; i + 2 < tri.Length; i += 3) { var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f; if (!any) { box = new Bounds(c, Vector3.zero); any = true; } else box.Encapsulate(c); }
                    }
                    var moved = new bool[v.Length]; int nm = 0;
                    if (any)
                    {
                        box.Expand(box.size * 0.03f);
                        for (int i = 0; i < v.Length; i++)
                        {
                            int inTorn = -1;
                            for (int j = 0; j < test.Length; j++) if (test[j].Contains(v[i])) { inTorn = j; break; }
                            if (inTorn < 0) continue;
                            var q = Vector3.Min(box.max, Vector3.Max(box.min, v[i]));
                            if ((q - v[i]).sqrMagnitude > 1e-10f)
                            {
                                v[i] = q; moved[i] = true; nm++;
                                var tb = torn[inTorn]; if (q.y >= tb.min.y + tb.size.y * 0.3f) rimY[inTorn].Add(q.y);   // the top-surface rim (not lower bins pushed up)
                            }
                        }
                    }
                    if (nm > 0)
                    {
                        var m = Object.Instantiate(src); m.name = src.name + "_rim";
                        var keep = new List<List<int>>(); var rim = new List<int>();
                        for (int sm = 0; sm < src.subMeshCount; sm++)
                        {
                            var tri = src.GetTriangles(sm); var k = new List<int>(tri.Length);
                            for (int i = 0; i + 2 < tri.Length; i += 3)
                            {
                                if (moved[tri[i]] || moved[tri[i + 1]] || moved[tri[i + 2]]) { rim.Add(tri[i]); rim.Add(tri[i + 1]); rim.Add(tri[i + 2]); RimTris++; }
                                else { k.Add(tri[i]); k.Add(tri[i + 1]); k.Add(tri[i + 2]); }
                            }
                            keep.Add(k);
                        }
                        m.vertices = v; m.subMeshCount = src.subMeshCount + 1;
                        for (int sm = 0; sm < keep.Count; sm++) m.SetTriangles(keep[sm], sm, false);
                        m.SetTriangles(rim, src.subMeshCount, false);
                        m.bounds = src.bounds;                                                               // the whole model's bounds (TeamPaint band / panels)
                        mf.sharedMesh = m;
                        var nmats = new Material[src.subMeshCount + 1];
                        for (int i = 0; i < nmats.Length; i++) nmats[i] = i < src.subMeshCount ? mats[Mathf.Min(i, mats.Length - 1)] : WreckPieces.CoreMat(mats[0]);
                        r.sharedMaterials = nmats; mats = nmats; RimFrags++;
                        f.gameObject.AddComponent<OzOwnedMesh>().mesh = m;
                    }
                }
                for (int i = 0; i < mats.Length; i++)
                {   // matte charred hull (the block keeps TeamPaint's values: Apply reads the block back first)
                    if (!mats[i] || !mats[i].HasProperty(UnitShadeId)) continue;
                    r.GetPropertyBlock(mpb, i); var us = mats[i].GetVector(UnitShadeId); us.w = 1f; mpb.SetVector(UnitShadeId, us); r.SetPropertyBlock(mpb, i);
                }
                MatteFrags++;
            }
            var tp = holder.GetComponentInParent<TeamPaint>(); if (tp) tp.Apply();                                     // the rim submesh gets the char factor too
            var low = new float[torn.Count];
            for (int j = 0; j < torn.Count; j++)
            {
                var l = rimY[j]; if (l.Count == 0) { low[j] = float.NaN; continue; }
                l.Sort(); low[j] = l[Mathf.Clamp((int)(l.Count * 0.2f), 0, l.Count - 1)];
            }
            return low;
        }

        /// <summary></summary>
        public static void SetWreckSheen(Transform root, bool sheen)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.transform.parent || r.transform.parent.name != "OZ_WreckFragments") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || !mats[i].HasProperty(UnitShadeId) || mats[i].name.StartsWith("OZ_Wreck")) continue;
                    r.GetPropertyBlock(mpb, i); var us = mats[i].GetVector(UnitShadeId); us.w = sheen ? mats[i].GetVector(UnitShadeId).w : 1f; mpb.SetVector(UnitShadeId, us); r.SetPropertyBlock(mpb, i);
                }
            }
        }

        /// <summary>A short dark axle where the wheel was (the hole reads as a bare axle, not a missing texture).</summary>
        static void AxleStub(Transform wheel, Vector3 centre, float radius)
        {
            var mr = wheel.GetComponent<MeshRenderer>(); if (!mr || !wheel.parent) return;
            var stub = new GameObject("OZ_AxleStub");
            stub.transform.SetParent(wheel.parent, true);
            stub.transform.SetPositionAndRotation(centre, wheel.rotation);
            var mb = wheel.GetComponent<MeshFilter>().sharedMesh.bounds; var sz = mb.size;
            int ax = sz.x <= sz.y && sz.x <= sz.z ? 0 : (sz.y <= sz.z ? 1 : 2);
            var axis = wheel.TransformDirection(ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward);
            stub.transform.rotation = Quaternion.FromToRotation(Vector3.right, axis);
            float w = Mathf.Max(0.12f, sz[ax] * Mathf.Abs(wheel.lossyScale[ax]));
            stub.AddComponent<MeshFilter>().sharedMesh = RollingWheel.Cylinder(radius * 0.2f, w * 0.45f, 10);
            var r = stub.AddComponent<MeshRenderer>(); r.sharedMaterial = WreckPieces.CoreMat(mr.sharedMaterial);
            Stubs++;
        }

        /// <summary>A missing corner wheel: the wreck tips down at that corner (pivot on the opposite side), at most 7 deg.</summary>
        static void Slump(Transform root, Vector3 local, float corner, float radius)
        {
            var d = root.TransformVector(new Vector3(local.x, 0f, local.z)); d.y = 0f;
            float len = d.magnitude; if (len < 0.3f || corner < 0.35f) return;
            float drop = radius * 0.55f * Mathf.Clamp01(corner);
            float ang = Mathf.Min(7f, Mathf.Atan2(drop, 2f * len) * Mathf.Rad2Deg);
            var axis = Vector3.Cross(Vector3.up, d / len);
            root.RotateAround(root.position - d, axis, ang);
            Slumps++;
        }
    }

    /// <summary>
    /// forward, rear wheels back) and a little out to the side, then ROLLS on Unity physics: a convex 16-sided cylinder collider on the
    /// debris layer (rolls down slopes, bounces off rocks), rubber material (bounciness .32), a light gyroscopic righting torque while it
    /// spins fast so it runs on its tread, faded out when it slows so it wobbles and falls flat. Sounds: a rubber bounce on every hard
    /// contact (sd_tyre_bounce_a/b), the vehicle tyre roll loop while it rolls (veh_wheels_sand, asphalt on L17 and roads, volume and
    /// pitch from its speed), a wobble-and-slap when it falls flat (sd_tyre_flat_a); into water = splash (dst_debris_splash) and it sinks
    /// away. At rest 6-10 s later the usual pop puff + pop. At most <see cref="MaxRolling"/> at once; no gameplay effect.
    /// </summary>
    public class RollingWheel : MonoBehaviour
    {
        public const int MaxRolling = 3;
        /// <summary>Lean-over push about the centre of mass (rad/s^2). The contact edge turns ~1/5 of it into tipping, against ~4.2
        /// </summary>
        public static float TipAccel = 40f;
        static readonly List<RollingWheel> live = new List<RollingWheel>();
        public static int Made, PeakLive, Bounces, Flats, Splashes, Pops, Sinks, LoopStarts;
        public static float MaxSpeed, MaxDist;
        public static readonly List<string> Log = new List<string>();
        public static RollingWheel Newest;
        public static int Live { get { live.RemoveAll(w => !w); return live.Count; } }

        Rigidbody rb; float tipSign = 1f, radius, age, lastGround = -9f, lastBounce = -9f, restT, popAt = -1f, wetT = -1f, sinkT = -1f, peakV; int bounces, id; bool flat, wet, popping, loopOn, frozen, waded;
        Vector3 start; Vector3 sinkFrom; string loopKey;

        static PhysicsMaterial tyreMat;
        static PhysicsMaterial TyreMat => tyreMat ? tyreMat : (tyreMat = new PhysicsMaterial("OZ_Tyre") { dynamicFriction = 0.9f, staticFriction = 1f, bounciness = 0.32f, bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Maximum });
        static readonly Dictionary<int, Mesh> cyl = new Dictionary<int, Mesh>();
        public static int Burnt;
        /// <summary></summary>
        public static float TyreGain = 1.33f;

        /// <summary>
        /// flight mesh (no ground through holes, no arch horns) in matte burnt rubber + a scorched hub (no khaki texels, no team
        /// paint, no sheen flashing on the flat lugs) with a closed core backstop. Jeep tyres are their own closed meshes: unchanged.</summary>
        public static void BurnTyre(Transform wheel) { if (WheelSplit.CloseTyre(wheel, true)) Burnt++; }

        /// <summary>The cap: a 4th wheel makes the oldest resting / sinking one pop now; if all three still roll, false (plain debris).</summary>
        public static bool CanSpawn()
        {
            live.RemoveAll(w => !w || w.popping);
            if (live.Count < MaxRolling) return true;
            foreach (var w in live) if (w.frozen || w.restT > 0.3f || w.wet) { w.Pop(); live.Remove(w); return true; }
            return false;
        }

        public static RollingWheel Throw(Transform wheel, Vector3 vehCentre, Vector3 vehFwd, float power, int id, uint seed)
        {
            if (!wheel || !wheel.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) return null;
            var mb = mf.sharedMesh.bounds; var sz = mb.size; var ls = wheel.lossyScale;
            int ax = sz.x <= sz.y && sz.x <= sz.z ? 0 : (sz.y <= sz.z ? 1 : 2);
            float width = Mathf.Max(0.12f, sz[ax] * Mathf.Abs(ls[ax]));
            float radius = Mathf.Max(0.2f, Mathf.Max(sz[(ax + 1) % 3] * Mathf.Abs(ls[(ax + 1) % 3]), sz[(ax + 2) % 3] * Mathf.Abs(ls[(ax + 2) % 3])) * 0.5f);
            var axis = wheel.TransformDirection(ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward).normalized;
            var centre = wheel.TransformPoint(mb.center);

            var go = new GameObject("OZ_RollingWheel");
            go.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.right, axis));
            wheel.SetParent(go.transform, true);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            var hull = new GameObject("Hull") { layer = BuildingBreaker.DebrisLayer };
            hull.transform.SetParent(go.transform, false);
            var mc = hull.AddComponent<MeshCollider>(); mc.sharedMesh = Cylinder(radius * 0.97f, width * 0.5f, 16); mc.convex = true; mc.sharedMaterial = TyreMat;
            var body = go.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(radius * radius * width * 180f, 25f, 120f);
            body.linearDamping = 0.02f; body.angularDamping = 0.05f; body.maxAngularVelocity = 45f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            uint s = seed | 1u; float u1 = WreckFx.Rnd(ref s), u2 = WreckFx.Rnd(ref s), u3 = WreckFx.Rnd(ref s), u4 = WreckFx.Rnd(ref s);
            var off = centre - vehCentre; off.y = 0f;
            var rollDir = Vector3.Cross(axis, Vector3.up); rollDir.y = 0f; rollDir = rollDir.sqrMagnitude > 1e-4f ? rollDir.normalized : vehFwd;
            float fs = Vector3.Dot(off, vehFwd); float sgn = Mathf.Abs(fs) > 0.25f ? Mathf.Sign(fs) : (u4 < 0.5f ? -1f : 1f);
            if (Vector3.Dot(rollDir, vehFwd) * sgn < 0f) rollDir = -rollDir;                              // away along the vehicle's length
            var side = new Vector3(axis.x, 0f, axis.z); side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.zero;
            if (Vector3.Dot(side, off) < 0f) side = -side;                                                     // out of its own side
            var v = rollDir * Mathf.Lerp(3.5f, 5.5f, u1) * power + side * Mathf.Lerp(1.2f, 2.4f, u2) * power + Vector3.up * Mathf.Lerp(4.2f, 6.2f, u3) * Mathf.Lerp(0.75f, 1f, power);
            body.linearVelocity = v;
            float spin = Vector3.Dot(v, Vector3.Cross(axis, Vector3.up)) / radius * 1.15f;                   // rolls with v on landing: v = r (w x up)
            body.angularVelocity = axis * spin + rollDir * (u4 - 0.5f) * 1.6f;                                 // a little tumble: it wobbles in the air

            var w = go.AddComponent<RollingWheel>();
            w.rb = body; w.radius = radius; w.id = id; w.start = centre; w.loopKey = "tyre:" + id + ":" + Made; w.tipSign = u2 < 0.5f ? -1f : 1f;
            live.Add(w); Made++; PeakLive = Mathf.Max(PeakLive, live.Count); Newest = w;
            AudioDirector.Fire("dst_turret_toss", centre, id, 0.45f * TyreGain, 1.35f);                                   // the whoosh as it leaves
            OzSoundLog.Add("WHEEL", "dst_turret_toss", null, "wheel thrown r " + radius.ToString("F2") + " m", 0.45f * TyreGain, centre);
            return w;
        }

        /// <summary>A cylinder along local x (radius r, half width hw, n sides) for the collider and the axle stubs. Cached per size.</summary>
        public static Mesh Cylinder(float r, float hw, int n)
        {
            int key = Mathf.RoundToInt(r * 100f) * 100000 + Mathf.RoundToInt(hw * 100f) * 100 + n;
            if (cyl.TryGetValue(key, out var m) && m) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n; float y = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                v.Add(new Vector3(-hw, y, z)); v.Add(new Vector3(hw, y, z));
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = 2 * i, a1 = 2 * i + 1, b0 = 2 * ((i + 1) % n), b1 = 2 * ((i + 1) % n) + 1;
                t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                if (i > 0 && i < n - 1) { t.Add(0); t.Add(2 * (i + 1)); t.Add(2 * i); t.Add(1); t.Add(2 * i + 1); t.Add(2 * (i + 1) + 1); }
            }
            m = new Mesh { name = "OZ_Cylinder" }; m.SetVertices(v); m.SetTriangles(t, 0);
            // outward winding (the side quads and the caps were written in mixed order)
            var tri = m.triangles; var vv = m.vertices;
            for (int i = 0; i < tri.Length; i += 3)
            {
                var c = (vv[tri[i]] + vv[tri[i + 1]] + vv[tri[i + 2]]) / 3f;
                if (Vector3.Dot(Vector3.Cross(vv[tri[i + 1]] - vv[tri[i]], vv[tri[i + 2]] - vv[tri[i]]), c) < 0f) { int sw = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = sw; }
            }
            m.triangles = tri; m.RecalculateNormals(); m.RecalculateBounds();
            cyl[key] = m; return m;
        }

        Vector3 Axis => transform.right;
        bool Grounded => age - lastGround < 0.15f;

        /// <summary>Checks: wheels lifted onto the drawn ground at rest, and the largest lift (m).</summary>
        public static int Seated; public static float SeatedMost;
        /// <summary>How far the wheel's lowest point stands in the drawn ground under that point (m, 0 = on it or over it).</summary>
        public float InGround()
        {
            var g = ZGame.I; var mc = GetComponentInChildren<MeshCollider>(true); if (!g || !mc || !mc.sharedMesh) return 0f;
            float pen = 0f;
            foreach (var v in mc.sharedMesh.vertices) { var wp = mc.transform.TransformPoint(v); pen = Mathf.Max(pen, g.GroundY(wp.x, wp.z) - wp.y); }
            return pen;
        }
        /// <summary>At rest the wheel lies ON the drawn ground. Where its rim stands in it (the sloping bed of a ford, a soft contact),
        /// it is lifted clear: never lowered, never more than 0.4 m.</summary>
        void SeatOnGround()
        {
            float pen = InGround();
            if (pen <= 0.01f) return;
            pen = Mathf.Min(pen, 0.4f); transform.position += Vector3.up * pen; Seated++; SeatedMost = Mathf.Max(SeatedMost, pen);
        }

        void FixedUpdate()
        {
            if (!rb || rb.isKinematic || wet) return;
            if (rb.IsSleeping()) { if (flat) return; lastGround = age; rb.WakeUp(); }                       // asleep standing up: still has to fall flat
            var a = Axis; float spin = Vector3.Dot(rb.angularVelocity, a);
            float g = Mathf.Clamp01((Mathf.Abs(spin) - 4f) / 6f);
            if (g > 0f)
            {   // gyroscope stand-in: a fast spinning wheel keeps its axle level (runs on its tread); slow, it is free to topple
                float tilt = Vector3.Dot(a, Vector3.up);
                var r = Vector3.Cross(a, Vector3.up); if (r.sqrMagnitude > 1e-6f) r.Normalize();
                rb.AddTorque((-r * tilt * 42f - r * Vector3.Dot(rb.angularVelocity, r) * 5f) * g, ForceMode.Acceleration);
            }
            else if (!flat && age > 0.8f && Grounded && Mathf.Abs(Vector3.Dot(a, Vector3.up)) < 0.6f && rb.linearVelocity.magnitude < 3f)
            {   // too slow to stay up: it leans over (seeded side), wobbles on its rim and falls flat
                var fh = Vector3.Cross(a, Vector3.up); if (fh.sqrMagnitude > 1e-6f) rb.AddTorque(fh.normalized * tipSign * TipAccel, ForceMode.Acceleration);
            }
            float k = Mathf.Clamp01((age - 2.5f) / 4f);                                                     // rolling resistance builds up
            rb.angularDamping = Mathf.Lerp(0.05f, 0.9f, k); rb.linearDamping = Mathf.Lerp(0.02f, 0.35f, k);
        }

        void Update()
        {
            float dt = Time.deltaTime; age += dt;
            if (popping) return;
            if (sinkT >= 0f)
            {   // under water: sinks away out of sight
                sinkT += dt; transform.position = sinkFrom + Vector3.down * radius * 2.4f * Mathf.SmoothStep(0f, 1f, sinkT / 1.6f);
                if (sinkT >= 1.6f) { Sinks++; Note("sank"); Destroy(gameObject); }
                return;
            }
            var p = transform.position;
            float sp = rb && !rb.isKinematic ? rb.linearVelocity.magnitude : 0f;
            peakV = Mathf.Max(peakV, sp); MaxSpeed = Mathf.Max(MaxSpeed, sp);
            var fxp = OpenZFxPlayer.I;
            if (!wet && fxp && fxp.isActiveAndEnabled && OzWaterLanding.OpenWaterNear(p, radius + 0.5f, out float wy, out float wbed))   // b27: the drawn water with the bed under it, not the map's water tiles
            {
                bool deep = wy - wbed >= OzWaterLanding.SinkDepth;
                if (p.y - radius * 0.4f <= wy && (deep || !waded))
                {   // b27: into the water: the shared water landing rule makes the splash, the rings and one sound. Deep water: it sinks
                    // away as before. A ford or a stream shallower than the wheel sinks: one splash, then it rolls on and falls flat on the bed
                    Splashes++; waded = true;
                    OzWaterLanding.Splash(new Vector3(p.x, wy, p.z), OzWaterLanding.SizeOf(radius * 2f), "wheel", sp, false, id);
                    OzSoundLog.Add("WHEEL", "water landing", null, deep ? "wheel splash" : "wheel splash (shallow: it rolls on)", 0.8f * TyreGain, p);
                    if (deep)
                    {
                        wet = true; wetT = age;
                        StopLoop(0.15f);
                        if (rb) { rb.linearDamping = 4f; rb.angularDamping = 4f; }
                    }
                }
            }
            if (wet)
            {
                if (age - wetT > 1.2f) { sinkT = 0f; sinkFrom = transform.position; if (rb) rb.isKinematic = true; foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false; live.Remove(this); }
                return;
            }
            // roll loop: on from the first ground contact, volume / pitch from the rolling speed, silent in the air
            if (loopOn && AudioDirector.I)
            {
                var hv = rb ? rb.linearVelocity : Vector3.zero; hv.y = 0f; float kv = Mathf.Clamp01(hv.magnitude / 6f);
                AudioDirector.I.SetLoop(loopKey, Grounded && !flat ? (0.15f + 0.85f * kv) * TyreGain : 0f, 0.75f + 0.35f * kv);
                if (flat || frozen) StopLoop(0.4f);
            }
            float tiltAbs = Mathf.Abs(Vector3.Dot(Axis, Vector3.up));
            if (!flat && age > 0.6f && Grounded && tiltAbs > 0.45f && sp < 2.5f)
            {   // it topples: the wobble and the slap onto its side
                flat = true; Flats++;
                CombatAudio.I?.PlayPitched("debris", p, 0.8f * TyreGain, 0f, id, Mathf.Lerp(1.1f, 0.85f, Mathf.InverseLerp(0.3f, 0.8f, radius)), "sd_tyre_flat_a");
                OzSoundLog.Add("WHEEL", "sd_tyre_flat_a", "sd_tyre_flat_a", "wheel falls flat", 0.8f * TyreGain, p);
            }
            if (!frozen && rb)
            {
                bool still = (rb.IsSleeping() || (sp < 0.12f && rb.angularVelocity.magnitude < 0.35f)) && (flat || age > 12f);   // never frozen standing up
                restT = still ? restT + dt : 0f;
                if (restT > 0.5f)
                {   // at rest: stop simulating, keep the pose
                    frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
                    SeatOnGround();
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                    StopLoop(0.3f);
                    popAt = age + 6f + (RobotDeathRule.Hash(id, 3, 37u) & 1023u) / 1023f * 4f;          // 6-10 s at rest
                    MaxDist = Mathf.Max(MaxDist, Vector3.Distance(Flat(start), Flat(p)));
                }
            }
            if ((popAt > 0f && age >= popAt) || age > 30f) Pop();
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        void OnCollisionStay(Collision c) { lastGround = age; }

        void OnCollisionEnter(Collision c)
        {
            lastGround = age;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (!loopOn && AudioDirector.I && !wet)
            {
                var gm = ZGame.I;
                string ev = gm && gm.level == "L17" ? "veh_wheels_asphalt" : "veh_wheels_sand";
                if (ev == "veh_wheels_sand" && gm && gm.World != null && gm.World.Map != null)
                {
                    int tx = (int)(at.x * 0.5f), ty = (int)(-at.z * 0.5f);
                    if (gm.World.Map.ClassAt(tx, ty) == ZClass.Road) ev = "veh_wheels_asphalt";
                    else if (gm.level == "L16") { string jg = AudioDirector.JungleGround(at); string want = jg == "mud" ? "veh_wheels_mud" : "veh_wheels_grass"; if (AudioDirector.Has(want)) ev = want; }   // level 16: a thrown wheel rolls on grass or mud
                }
                if (AudioDirector.I.StartLoop(loopKey, ev, at, transform, 0.01f, 1f) > 0) { loopOn = true; LoopStarts++; OzSoundLog.Add("WHEEL", ev, null, "wheel roll loop on", 0.5f, at); }
            }
            float n = c.contactCount > 0 ? Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal)) : c.relativeVelocity.magnitude;
            if (n < 1.6f || age - lastBounce < 0.12f || bounces >= 10) return;
            lastBounce = age; bounces++; Bounces++;
            float vol = Mathf.Clamp(0.3f + n * 0.09f, 0.3f, 1f) * TyreGain;
            CombatAudio.I?.PlayPitched("debris", at, vol, 0f, id, Random.Range(0.92f, 1.08f) * Mathf.Lerp(1.12f, 0.88f, Mathf.InverseLerp(0.3f, 0.8f, radius)), "sd_tyre_bounce_a", "sd_tyre_bounce_b");
            OzSoundLog.Add("WHEEL", "sd_tyre_bounce", "sd_tyre_bounce_a|b", "wheel bounce " + n.ToString("F1") + " m/s", vol, at);
            if (n > 3f && WreckFx.OnScreen(at) && !OzWaterLanding.UnderSurface(at)) { var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, Mathf.Min(0.9f, 0.25f + n * 0.05f)); else FxManager.I?.Dust(at, 0.4f); }
        }

        void StopLoop(float fade)
        {
            if (!loopOn) return;
            loopOn = false; AudioDirector.I?.StopLoop(loopKey, fade);
        }

        /// <summary></summary>
        public void Pop()
        {
            if (popping) return;
            popping = true; live.Remove(this); StopLoop(0.1f);
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var p = transform.position;
            if (WreckFx.OnScreen(p))
            {
                var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Pop(p, 0.5f); else FxManager.I?.Pop(p, 0.5f);
                CombatAudio.I?.PlayPitched(RobotPopDeath.SoundKey, p, 0.45f * TyreGain, 0f, id, 0.8f, RobotPopDeath.Bank);
                OzSoundLog.Add("WHEEL", RobotPopDeath.SoundKey, "rp_pop", "wheel pop", 0.45f * TyreGain, p);
            }
            Pops++; Note("popped");
            PopOut.Begin(transform, PopOut.Style.Debris, null, true, false);
        }

        void Note(string end)
        {
            var p = transform.position;
            if (Log.Count < 40) Log.Add($"wheel {id}: r {radius:F2} m, rolled {Vector3.Distance(Flat(start), Flat(p)):F1} m, peak {peakV:F1} m/s, {bounces} bounces, flat {flat}, wet {wet}, {end} at {age:F1} s");
        }

        void OnDestroy() { live.Remove(this); StopLoop(0.1f); }
    }

    /// <summary></summary>
    public sealed class OzOwnedMesh : MonoBehaviour
    {
        public Mesh mesh;
        public static int Freed;
        void OnDestroy() { if (mesh) { Destroy(mesh); Freed++; } }
    }
}

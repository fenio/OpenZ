using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only. When a dead cannon throws its turret (UnitView.UpdateWreck, Zod's
    /// ECannonDeath turret missile) the gunner's seat kit first breaks off as its own physics piece (WreckPieces.Single: tumbles,
    /// clanks, settles, sinks away) and the flying head goes without it:
    ///  * imported guns (gatling, howitzer, missile cannon): gunner_seats_v1 appended the seat kit (cushion, tray, backrest, post,
    ///    deck, pole, console, levers, bracket) to the TurretYaw mesh, so the kit = the connected parts made of the appended
    ///    vertices (index &gt;= kitFrom, models/manifest.json) on the seat's side of the turn axis (the gatling's ammo crate on the
    ///    other side stays on the gun);
    ///  * a tower gun's raised perch (GunnerPerch) and an old outrigger saddle (GunnerSaddle) are their own nodes: they go as they are.
    /// The split meshes are cached per source mesh (built once, shared by every dead gun of that type).
    /// </summary>
    public static class GunChairDebris
    {
        /// <summary>Proof counters: guns whose chair broke off, chair triangles thrown.</summary>
        public static int Broken, Tris;

        struct Spec { public int verts, kitFrom; public Vector3 seat; }
        static readonly Dictionary<string, Spec> Gen = new Dictionary<string, Spec>
        {   // gunner_seats_v1 models/manifest.json: (old verts -> new verts), seat socket in TurretYaw space (BoardingView.GenSpots)
            ["gatling"] = new Spec { verts = 10438, kitFrom = 7390, seat = new Vector3(-0.75f, 0.90f, 1.36f) },
            ["howitzer"] = new Spec { verts = 9666, kitFrom = 7458, seat = new Vector3(0.10f, 0.95f, 1.62f) },
            ["missile_cannon"] = new Spec { verts = 2849, kitFrom = 545, seat = new Vector3(-2.42f, 0.15f, 0f) },
        };
        static readonly Dictionary<Mesh, Mesh[]> cache = new Dictionary<Mesh, Mesh[]>();

        /// <summary>Breaks the seat kit off `turret` (call right before the turret is thrown). Returns the pieces made.</summary>
        public static int BreakOff(Transform turret, string type, int id)
        {
            if (!turret) return 0;
            int n = 0, tris = 0; var centre = turret.position;
            foreach (var mf in turret.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !(mf.transform == turret || mf.name == "CannonTurretYaw_Surface")) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled) continue;
                var seatNode = turret.Find("CannonGunnerSeat");
                var split = Split(mf.sharedMesh, type, seatNode ? mf.transform.InverseTransformPoint(seatNode.position) : (Vector3?)null);
                if (split == null) continue;
                var go = new GameObject("OZ_GunChair") { layer = mf.gameObject.layer };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = split[1];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials; r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                var b = new MaterialPropertyBlock();
                mr.GetPropertyBlock(b); if (!b.isEmpty) r.SetPropertyBlock(b);
                for (int i = 0; i < mr.sharedMaterials.Length; i++) { b = new MaterialPropertyBlock(); mr.GetPropertyBlock(b, i); if (!b.isEmpty) r.SetPropertyBlock(b, i); }   // TeamPaint: per material slot
                mf.sharedMesh = split[0];
                if (WreckPieces.Single(go.transform, centre, 1f, id)) { n++; tris += split[1].triangles.Length / 3; }
                else Object.Destroy(go);
            }
            foreach (var nm in new[] { GunnerPerch.NodeName, GunnerSaddle.NodeName })
            {
                var t = turret.Find(nm);
                if (t && WreckPieces.Single(t, centre, 1f, id)) n++;
            }
            if (n > 0)
            {
                Broken++; Tris += tris;
                AudioDirector.Fire("dst_debris_metal", centre, id);                                     // the seat snaps off (clanks follow on contact)
                Debug.Log($"[OpenZ] GX1 gun chair: {type} #{id} seat kit broke off as its own debris ({n} piece(s), {tris} tris)");
            }
            return n;
        }

        /// <summary>{turret without the seat, the seat alone} for this source mesh, or null (no seat found / not readable).</summary>
        static Mesh[] Split(Mesh src, string type, Vector3? legacySeat)
        {
            if (cache.TryGetValue(src, out var c)) return c;
            cache[src] = null;
            if (!src.isReadable || src.subMeshCount != 1) return null;
            var v = src.vertices; var tr = src.triangles; int nv = v.Length;
            bool gen = Gen.TryGetValue(type, out var sp) && nv == sp.verts;
            if (!gen && !legacySeat.HasValue) return null;
            var seat = gen ? sp.seat : legacySeat.Value;
            // connected parts: welded positions (never across the kit boundary) + triangles
            var par = new int[nv]; for (int i = 0; i < nv; i++) par[i] = i;
            int Find(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
            var weld = new Dictionary<Vector4, int>();
            for (int i = 0; i < nv; i++)
            {
                var k = new Vector4(Mathf.Round(v[i].x * 1000f), Mathf.Round(v[i].y * 1000f), Mathf.Round(v[i].z * 1000f), gen && i >= sp.kitFrom ? 1f : 0f);
                if (weld.TryGetValue(k, out int j)) par[Find(i)] = Find(j); else weld[k] = i;
            }
            for (int i = 0; i < tr.Length; i += 3) { int a = Find(tr[i]); par[Find(tr[i + 1])] = a; par[Find(tr[i + 2])] = a; }
            var sum = new Dictionary<int, Vector4>(); var allKit = new Dictionary<int, bool>();
            for (int i = 0; i < tr.Length; i += 3)
            {
                int root = Find(tr[i]);
                var ctr = (v[tr[i]] + v[tr[i + 1]] + v[tr[i + 2]]) / 3f;
                sum.TryGetValue(root, out var s); sum[root] = s + new Vector4(ctr.x, ctr.y, ctr.z, 1f);
                bool kit = !gen || (tr[i] >= sp.kitFrom && tr[i + 1] >= sp.kitFrom && tr[i + 2] >= sp.kitFrom);
                allKit[root] = (!allKit.TryGetValue(root, out bool was) || was) && kit;
            }
            var chairRoots = new HashSet<int>();
            foreach (var kv in sum)
            {
                var ctr = new Vector3(kv.Value.x, kv.Value.y, kv.Value.z) / kv.Value.w;
                bool chair = gen ? allKit[kv.Key] && ctr.x * seat.x + ctr.z * seat.z > 0f          // the seat's side of the turn axis
                                   : (ctr - seat).magnitude < 0.6f;
                if (chair) chairRoots.Add(kv.Key);
            }
            if (chairRoots.Count == 0) return null;
            var rest = new List<int>(tr.Length); var seatTris = new List<int>();
            for (int i = 0; i < tr.Length; i += 3)
            {
                var dst = chairRoots.Contains(Find(tr[i])) ? seatTris : rest;
                dst.Add(tr[i]); dst.Add(tr[i + 1]); dst.Add(tr[i + 2]);
            }
            var mRest = Object.Instantiate(src); mRest.name = src.name + "_noseat"; mRest.SetTriangles(rest, 0, false);
            var mSeat = Object.Instantiate(src); mSeat.name = src.name + "_seat"; mSeat.SetTriangles(seatTris, 0, true);
            c = new[] { mRest, mSeat }; cache[src] = c;
            return c;
        }
    }

    /// <summary>
    /// Every drawn gatling round throws two brass casings out of the right side of the barrel drum (away from the
    /// gunner's seat on the left) on the pooled weapons_v2 casing channel (wv2_casing: lit brass FX_Shell, gravity, bounce);
    /// the tinkle is AudioDirector's wpn_casings (60 % of gatling rounds). View only.
    /// </summary>
    public static class GatlingBrass
    {
        /// <summary>Proof counter: casings thrown.</summary>
        public static int Casings;
        static readonly Dictionary<int, Transform> drums = new Dictionary<int, Transform>();

        public static void Eject(UnitView av, Vector3 muzzle, Vector3 to, int id)
        {
            var z = OpenZFxPlayer.I; if (!z || !z.isActiveAndEnabled || !av) return;
            if (!drums.TryGetValue(id, out var drum) || !drum) { drum = Deep(av.transform, "BarrelSpin"); drums[id] = drum; }
            var dir = to - muzzle; dir.y = 0f; if (dir.sqrMagnitude < 1e-4f) dir = av.transform.forward; dir.Normalize();
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            float s = Mathf.Clamp(av.transform.lossyScale.x, 0.5f, 2f);
            var port = (drum ? drum.position : muzzle - dir * 1.2f * s) + right * 0.35f * s - dir * 0.3f * s;
            for (int i = 0; i < 2; i++)
            {
                var vel = right * Random.Range(1.4f, 2.4f) + Vector3.up * Random.Range(1.2f, 2.2f) - dir * Random.Range(0.2f, 0.8f) + Random.insideUnitSphere * 0.35f;
                z.EmitChannel("wv2_casing", port - dir * (i * 0.1f * s), vel, Random.Range(1.6f, 2.4f), 0.32f * s, Color.white, 1f);
                Casings++;
            }
        }

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Deep(c, name); if (f) return f; }
            return null;
        }
    }
}

using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// The firing range of a gun as a thin steady ring on the ground: round the spot under the pointer while a stored gun is
    /// being placed (PlayerInput.Placing / PlaceTile), and round each of the player's own guns while it is selected.
    /// Radius: the weapon table's attack_radius in px / 8 (8 px = 1 m), read at run time: for a placed gun its own
    /// ZObject.AttackRadius, for the gun being placed ZSettings.Unit(type).attack_radius (the value ZWorld.CreateObject gives it).
    /// Centre: the gun's rule centre (the middle of its 2x2 spot), because the rule measures from there to the target's centre
    /// (ZObject.WithinAttackRadius; to a building's nearest wall). No gun has a minimum range, so there is no inner ring.
    /// A gun on a fort tower keeps its rule centre on the fort's tower spot, so its ring is centred there, not on the raised pad.
    /// Several guns selected: guns of one type draw one common outline (the part of a ring inside another ring of the same type
    /// is left out, cut exactly where the rings cross, as AttackRangeRings does with its dots); different types keep whole rings.
    /// Look: the team's ground-disc colour (the colour of the game's range dots), a bright core between two soft dark edges so
    /// it reads on light and dark ground; the band keeps about four pixels of width on screen and is never wider (0.40 m) or
    /// brighter (gain 1.3, alpha 0.85) than the selection ring under a vehicle (0.40 m band, gain 1.9, alpha 0.95). The ring
    /// under an invalid spot is dimmed, not hidden, so it does not blink while the pointer crosses rocks.
    /// Ground: one height per half metre of ring from ZGame.GroundY, lifted like the range dots, drawn with OpenZ/Overlay (depth
    /// tested, polygon offset); a segment across a cliff step is left out instead of drawn up the cliff face. Heights are sampled
    /// when the spot changes (placing) or once (placed gun), at most ringsPerFrame rings a frame; nothing is rebuilt while
    /// nothing changes. While this ring shows for a selected gun, AttackRangeRings leaves that gun out (one ring, not two).
    /// View only. No new sound: the ring appears with the placement mode (ui_cannon_place_arm) or with a selection (its click).
    /// </summary>
    [DefaultExecutionOrder(77)]
    public class OzGunRange : MonoBehaviour
    {
        public static OzGunRange I { get; private set; }
        /// <summary>Adds itself next to PlayerInput in every loaded scene (set false to add it by hand).</summary>
        public static bool AutoAttach = true;

        public PlayerInput input;

        [Header("Look")]
        [Tooltip("HDR gain of the ring colour (selection ring 1.9, range dots 1.7)")] public float gain = 1.3f;
        [Range(0f, 1f)] public float alpha = 0.85f, edgeDark = 0.28f;
        [Tooltip("the ring under a spot that takes no gun")] [Range(0f, 1f)] public float invalidDim = 0.4f;
        [Tooltip("band width (m): camera distance x 0.0021, inside these limits (the selection ring's band under a vehicle is 0.40 m)")] public Vector2 lineWidth = new Vector2(0.06f, 0.40f);
        [Tooltip("above the sampled ground (m), as the range dots")] public float lift = 0.12f;
        [Tooltip("one ground sample per this length of ring (m)")] public float spacing = 0.5f;
        public float fadeIn = 0.18f, fadeOut = 0.15f;
        [Tooltip("a height step between two samples larger than this is a cliff: that segment is left out (m)")] public float maxStep = 1.5f;
        [Tooltip("the ground is also read this far inside and outside the ring (m, half the widest band); a segment whose band edge would hang over a cliff edge or sit in a cliff face is left out")] public float edgeProbe = 0.2f;
        [Tooltip("a band edge whose ground differs from the ring's own by more than this is at a cliff (m)")] public float edgeStep = 0.3f;
        public int ringsPerFrame = 2;

        public int Builds { get; private set; }
        public int RingsShown { get; private set; }

        sealed class Ring
        {
            public ZObject o; public string type; public Vector3 c; public float R; public int n;
            public float[] h, hIn, hOut; public Mesh mesh; public float a, builtW; public bool sampled, dirty; public int skipped;
        }

        readonly List<Ring> rings = new List<Ring>();
        readonly Stack<Ring> pool = new Stack<Ring>();
        readonly List<ZObject> sel = new List<ZObject>();
        readonly Ring place = new Ring();
        string placeType; Vector2Int placeTile; int placingFrames; float placeTarget = 1f;
        readonly List<Vector3> vv = new List<Vector3>(); readonly List<Color> cc = new List<Color>(); readonly List<int> tt = new List<int>();
        readonly float[] off = new float[6]; readonly Color[] oc = new Color[6];
        Material mat; MaterialPropertyBlock mpb;
        ZGame game;

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var pi = Object.FindFirstObjectByType<PlayerInput>();
            if (pi && !pi.GetComponent<OzGunRange>()) pi.gameObject.AddComponent<OzGunRange>();
        }

        void Awake()
        {
            I = this;
            if (!input) input = GetComponent<PlayerInput>();
        }

        void OnDestroy()
        {
            foreach (var r in rings) if (r.mesh) Destroy(r.mesh);
            foreach (var r in pool) if (r.mesh) Destroy(r.mesh);
            if (place.mesh) Destroy(place.mesh);
            if (mat) Destroy(mat);
            if (I == this) I = null;
        }

        Material Mat()
        {
            if (mat) return mat;
            Material baseMat = null;
            var so = GetComponent<SelectionOverlay>();
            if (!so) so = Object.FindFirstObjectByType<SelectionOverlay>();
            if (so) baseMat = so.overlayMaterial;                                  // the scene's overlay material keeps the shader in builds
            if (baseMat) mat = new Material(baseMat);
            else { var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return null; mat = new Material(sh); }
            mat.name = "OZ_GunRange";
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);              // on the ground, under units and buildings
            return mat;
        }

        // ------------------------------------------------------------------ public
        /// <summary>The weapon table's range of a gun type in px (0 = no such unit).</summary>
        public static int RadiusPx(ZWorld w, string type)
        {
            var u = w != null && w.S != null ? w.S.Unit(type) : null;
            return u != null ? u.attack_radius : 0;
        }

        /// <summary>This gun's range is shown by the steady ring while it is selected (AttackRangeRings then leaves it out).</summary>
        public static bool Owns(ZObject o)
        {
            if (I == null || !I.isActiveAndEnabled || o == null || o.Kind != ZKind.Cannon) return false;
            var g = I.game ? I.game : ZGame.I;
            return g && o.Owner == g.player;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!game) game = input && input.game ? input.game : ZGame.I;
            if (!input || !game || game.World == null) return;
            float dt = Time.unscaledDeltaTime;
            float w = OzGunZone.ScreenWidth(lineWidth);
            int shown = 0;

            // ---- the gun being placed (from the second frame of the mode on: PlaceTile is this placement's own by then)
            var b = input.Placing;
            bool on = false;
            if (b != null && b.StoredCannons.Count > 0)
            {
                placingFrames++;
                string type = b.StoredCannons[0];
                int rpx = RadiusPx(game.World, type);
                if (placingFrames >= 2 && rpx > 0)
                {
                    var t = input.PlaceTile;
                    if (!place.sampled || type != placeType || t != placeTile || place.R != rpx / 8f)
                    {
                        placeType = type; placeTile = t; place.type = type;
                        place.c = ZGame.PxToWorld(t.x * 16 + 16, t.y * 16 + 16);
                        place.R = rpx / 8f;
                        Sample(place);
                    }
                    placeTarget = input.PlaceValid ? 1f : invalidDim;
                    on = true;
                }
            }
            else placingFrames = 0;
            place.a = Mathf.MoveTowards(place.a, on ? placeTarget : 0f, dt / Mathf.Max(0.01f, on ? fadeIn : fadeOut));
            if (place.sampled && place.a > 0f)
            {
                if (place.dirty || Mathf.Abs(w - place.builtW) > 0.06f * place.builtW) Build(place, w, false);
                DrawRing(place); shown++;
            }

            // ---- the player's own guns in the selection
            sel.Clear();
            foreach (var o in input.Selection)
                if (o != null && !o.Destroyed && o.Kind == ZKind.Cannon && o.Owner == game.player && o.AttackRadius > 0) sel.Add(o);
            bool changed = false;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                if (sel.Contains(r.o) && Mathf.Approximately(r.R, r.o.AttackRadius / 8f)) continue;
                r.o = null; pool.Push(r); rings.RemoveAt(i); changed = true;
            }
            foreach (var o in sel)
            {
                bool have = false;
                for (int i = 0; i < rings.Count; i++) if (rings[i].o == o) { have = true; break; }
                if (have) continue;
                var r = pool.Count > 0 ? pool.Pop() : new Ring();
                r.o = o; r.type = o.Type; r.c = ZGame.PxToWorld(o.CenterX, o.CenterY); r.R = o.AttackRadius / 8f;
                r.sampled = false; r.a = 0f; r.dirty = true;
                rings.Add(r); changed = true;
            }
            if (changed) foreach (var r in rings) r.dirty = true;                                   // the common outline of each type is cut anew
            int budget = Mathf.Max(1, ringsPerFrame);
            foreach (var r in rings)
            {
                if (!r.sampled) { if (budget <= 0) continue; budget--; Sample(r); }
                if (r.dirty || Mathf.Abs(w - r.builtW) > 0.06f * r.builtW) Build(r, w, true);
                r.a = Mathf.MoveTowards(r.a, 1f, dt / Mathf.Max(0.01f, fadeIn));
                DrawRing(r); shown++;
            }
            RingsShown = shown;
        }

        void DrawRing(Ring r)
        {
            var m = Mat();
            if (!m || !r.mesh) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            var p = game.player;
            Color c = p == Team.Red ? TeamColors.DiscRed : p == Team.Blue ? TeamColors.DiscBlue : TeamColors.DiscEmpty;   // as the range dots
            mpb.SetColor("_Color", new Color(c.r * gain, c.g * gain, c.b * gain, r.a * alpha));
            Graphics.DrawMesh(r.mesh, Matrix4x4.identity, m, 0, null, 0, mpb, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------ ring
        void Sample(Ring r)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * r.R / Mathf.Max(0.1f, spacing)), 96, 420);
            if (r.h == null || r.h.Length != n) { r.h = new float[n]; r.hIn = new float[n]; r.hOut = new float[n]; }
            r.n = n;
            float step = 2f * Mathf.PI / n;
            for (int i = 0; i < n; i++)
            {
                float a = i * step;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                r.h[i] = game.GroundY(r.c.x + cs * r.R, r.c.z + sn * r.R);
                r.hIn[i] = game.GroundY(r.c.x + cs * (r.R - edgeProbe), r.c.z + sn * (r.R - edgeProbe));
                r.hOut[i] = game.GroundY(r.c.x + cs * (r.R + edgeProbe), r.c.z + sn * (r.R + edgeProbe));
            }
            r.sampled = true; r.dirty = true;
        }

        void Profile(float w)
        {
            // across the band, from inside the ring outwards: soft dark edge, bright core, soft dark edge
            off[0] = -0.50f * w; off[1] = -0.30f * w; off[2] = -0.12f * w; off[3] = 0.12f * w; off[4] = 0.30f * w; off[5] = 0.50f * w;
            oc[0] = new Color(0f, 0f, 0f, 0f); oc[1] = new Color(0f, 0f, 0f, edgeDark);
            oc[2] = new Color(1f, 1f, 1f, 1f); oc[3] = oc[2];
            oc[4] = oc[1]; oc[5] = oc[0];
        }

        static float Away(Vector3 p, Ring o)
        {
            float dx = p.x - o.c.x, dz = p.z - o.c.z;
            return Mathf.Sqrt(dx * dx + dz * dz) - o.R;
        }

        void Build(Ring r, float w, bool common)
        {
            r.builtW = w; r.dirty = false; r.skipped = 0; Builds++;
            vv.Clear(); cc.Clear(); tt.Clear();
            Profile(w);
            int n = r.n;
            float step = 2f * Mathf.PI / n;
            float up = lift + 0.35f * 0.5f * w;                                                      // + a little for a sideways slope under a wide (far zoom) band
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float h0 = r.h[i], h1 = r.h[j];
                if (Mathf.Abs(h0 - h1) > maxStep) { r.skipped++; continue; }
                if (Mathf.Abs(r.hIn[i] - h0) > edgeStep || Mathf.Abs(r.hOut[i] - h0) > edgeStep || Mathf.Abs(r.hIn[j] - h1) > edgeStep || Mathf.Abs(r.hOut[j] - h1) > edgeStep) { r.skipped++; continue; }   // the band's edge at a cliff
                float lo = 0f, hi = 1f;
                if (common && rings.Count > 1)
                {
                    float a0 = i * step, a1 = (i + 1) * step;
                    var p0 = new Vector3(r.c.x + Mathf.Cos(a0) * r.R, 0f, r.c.z + Mathf.Sin(a0) * r.R);
                    var p1 = new Vector3(r.c.x + Mathf.Cos(a1) * r.R, 0f, r.c.z + Mathf.Sin(a1) * r.R);
                    for (int k = 0; k < rings.Count; k++)
                    {
                        var o = rings[k];
                        if (o == r || o.type != r.type) continue;
                        float f0 = Away(p0, o), f1 = Away(p1, o);                                    // below 0 = inside the other ring
                        if (f0 < 0f && f1 < 0f) { lo = 1f; hi = 0f; break; }
                        if (f0 >= 0f && f1 >= 0f) continue;
                        float t = f0 / (f0 - f1);                                                    // where this segment crosses the other ring
                        if (f0 < 0f) lo = Mathf.Max(lo, t); else hi = Mathf.Min(hi, t);
                    }
                }
                if (hi - lo < 0.001f) continue;
                int v0 = vv.Count;
                for (int e = 0; e < 2; e++)
                {
                    float t = e == 0 ? lo : hi;
                    float a = (i + t) * step;
                    float cs = Mathf.Cos(a), sn = Mathf.Sin(a), y = Mathf.Lerp(h0, h1, t) + up;
                    for (int k = 0; k < 6; k++)
                    {
                        float rad = r.R + off[k];
                        vv.Add(new Vector3(r.c.x + cs * rad, y, r.c.z + sn * rad)); cc.Add(oc[k]);
                    }
                }
                for (int k = 0; k < 5; k++)
                {
                    int q = v0 + k;
                    tt.Add(q); tt.Add(q + 1); tt.Add(q + 7); tt.Add(q); tt.Add(q + 7); tt.Add(q + 6);
                }
            }
            if (!r.mesh) { r.mesh = new Mesh { name = "OZ_GunRange" }; r.mesh.MarkDynamic(); }
            r.mesh.Clear();
            r.mesh.SetVertices(vv); r.mesh.SetColors(cc); r.mesh.SetTriangles(tt, 0);
            r.mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ audit (Play mode, for the proof steps)
        /// <summary>For every gun on the map: the table value its type would be previewed with against the gun's own range, and a
        /// ring built on the spot measured against its radius and the ground. violations = the number of lines that must be 0.</summary>
        public static string Audit(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("gun range audit\n");
            var z = I; var g = z ? (z.game ? z.game : ZGame.I) : null;
            if (!z || !g || g.World == null) { violations = 1; return sb.Append("FAIL no game / no OzGunRange in the scene\n").ToString(); }
            z.game = g;
            float w = OzGunZone.ScreenWidth(z.lineWidth);
            foreach (var type in ZSettings.Cannons)
                sb.Append("table: ").Append(type).Append(' ').Append(RadiusPx(g.World, type)).Append(" px = ").Append((RadiusPx(g.World, type) / 8f).ToString("0.0")).Append(" m\n");
            int guns = 0;
            var tmp = new Ring();
            foreach (var o in g.World.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed || o.AttackRadius <= 0) continue;
                guns++;
                tmp.o = o; tmp.type = o.Type; tmp.c = ZGame.PxToWorld(o.CenterX, o.CenterY); tmp.R = o.AttackRadius / 8f;
                z.Sample(tmp); z.Build(tmp, w, false);
                int table = RadiusPx(g.World, o.Type) == o.AttackRadius ? 0 : 1, radial = 0, below = 0, high = 0;
                for (int i = 0; i < z.vv.Count; i++)
                {
                    var p = z.vv[i];
                    float dx = p.x - tmp.c.x, dz = p.z - tmp.c.z, dr = Mathf.Sqrt(dx * dx + dz * dz) - tmp.R;
                    if (Mathf.Abs(dr) > 0.5f * w + 0.002f) radial++;
                    float dh = p.y - g.GroundY(p.x, p.z);
                    if (dh < -0.25f) below++; else if (dh > 1.0f) high++;                             // a band vertex sits at its segment's sampled height: a small difference on a slope is normal
                }
                int bad = table + radial + below + high + (w > 0.4001f ? 1 : 0);
                violations += bad;
                sb.Append(bad == 0 ? "OK   " : "FAIL ").Append(o.Type).Append(" #").Append(o.Id).Append(": ring ").Append(tmp.R.ToString("0.000")).Append(" m, gun ")
                  .Append(o.AttackRadius).Append(" px, table differs ").Append(table).Append("; ").Append(z.vv.Count).Append(" vertices: off the radius ").Append(radial)
                  .Append(", under the ground ").Append(below).Append(", more than 1 m above it ").Append(high).Append("; band ").Append(w.ToString("0.000"))
                  .Append(" m; segments left out at cliff steps ").Append(tmp.skipped).Append(" of ").Append(tmp.n).Append('\n');
            }
            if (tmp.mesh) Destroy(tmp.mesh);
            foreach (var r in z.rings) r.dirty = true;
            z.place.dirty = true;
            sb.Append("guns measured: ").Append(guns).Append('\n');
            return sb.ToString();
        }
    }
}

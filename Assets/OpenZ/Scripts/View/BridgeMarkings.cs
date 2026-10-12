using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [Serializable] public class RoadDeckSpec { public string name, axis; public float centre, phase, deckFrom, deckTo; }

    [Serializable]
    public class RoadMarkingsFile
    {
        public int version; public string level, note;
        public float period = 3f, dash = 1.5f, dashWidth = 0.2f, edgeOffset = 1.63f, edgeWidth = 0.18f, lift = 0.03f;
        public float[] colour; public RoadDeckSpec[] decks;
    }

    /// <summary>
    /// The roads themselves carry their markings in the ground paint (L17_ground_paint.png, no decals);
    /// a bridge deck is a mesh over the canal, so its dashed centre line and two edge lines are a thin mesh lying
    /// <c>lift</c> (3 cm) above the highest point of the deck plates (rivets included): never co-planar, no z-fighting.
    /// The dash pattern is the ground paint's own (Resources/OpenZ/RoadMarkings/&lt;level&gt;_road_markings.json, written by
    /// run on from the road over the abutment onto the deck without a jump. Drawn with Graphics.DrawMesh in the deck plate's
    /// own material (vertex colour = the paint, so day/night light, lamps, shadows and fog match the deck exactly); no
    /// renderer or collider is added to the bridge (MakeWalkable, BuildingBreaker and TeamPaint never see it). Only the
    /// INTACT deck carries markings: damaged / destroyed variants and a bridge still waiting for its auto-repair show none
    /// (Z L17's two bridges start destroyed: the markings appear with the rebuilt deck). View side only.
    /// </summary>
    [DefaultExecutionOrder(96)]
    public class BridgeMarkings : MonoBehaviour
    {
        public static BridgeMarkings I { get; private set; }
        public static bool AutoBoot = true;
        public const string ResourceDir = "OpenZ/RoadMarkings/";

        [Tooltip("brightness spread of the paint (lightly worn)")] [Range(0, 0.3f)] public float wear = 0.12f;

        sealed class Deck
        {
            public RoadDeckSpec Spec; public Transform Plate; public Renderer PlateR; public Mesh Mesh; public Material Mat; public int Layer;
        }

        ZGame game; RoadMarkingsFile file; Color paint = new Color(226 / 255f, 222 / 255f, 208 / 255f);
        readonly List<Deck> decks = new List<Deck>();
        readonly Dictionary<Material, Material> mats = new Dictionary<Material, Material>();
        float nextScan;

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I) return;
            if (!FindFirstObjectByType<BridgeMarkings>()) new GameObject("BridgeMarkings").AddComponent<BridgeMarkings>();
        }

        void Awake() { if (I && I != this) { Destroy(this); return; } I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            var ta = game ? Resources.Load<TextAsset>(ResourceDir + game.level + "_road_markings") : null;
            if (!ta) { enabled = false; return; }                                             // L02: no deck markings (its paint has its own dashes)
            file = JsonUtility.FromJson<RoadMarkingsFile>(ta.text);
            if (file?.decks == null || file.decks.Length == 0) { enabled = false; return; }
            if (file.colour != null && file.colour.Length >= 3) paint = new Color(file.colour[0], file.colour[1], file.colour[2]);
            foreach (var s in file.decks) decks.Add(new Deck { Spec = s });
            Debug.Log($"[OpenZ] BridgeMarkings {game.level}: {decks.Count} bridge decks (dash {file.dash} / {file.period} m, lift {file.lift * 100f:F0} cm)");
        }

        void OnDestroy()
        {
            foreach (var d in decks) if (d.Mesh) Destroy(d.Mesh);
            foreach (var kv in mats) if (kv.Value) Destroy(kv.Value);
            if (I == this) I = null;
        }

        void LateUpdate()
        {
            if (file == null) return;
            if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; Scan(); }
            for (int i = 0; i < decks.Count; i++)
            {
                var d = decks[i];
                if (!d.Plate || !d.Mesh || !d.Mat || !d.Plate.gameObject.activeInHierarchy) continue;
                Graphics.DrawMesh(d.Mesh, Matrix4x4.identity, d.Mat, d.Layer, null, 0, null, ShadowCastingMode.Off, true, null, LightProbeUsage.Off);
            }
        }

        /// <summary>Finds each bridge's INTACT deck plate (the placed view or RulesView's "_intact" variant made at run time).</summary>
        void Scan()
        {
            var root = GameObject.Find("ZodObjects");
            if (!root) return;
            foreach (var d in decks)
            {
                if (d.Plate) continue;
                foreach (Transform t in root.transform)
                {
                    if (!t.name.StartsWith(d.Spec.name, StringComparison.Ordinal)) continue;
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!mf.name.Contains("intact_plate")) continue;
                        var r = mf.GetComponent<Renderer>(); if (!r || !r.sharedMaterial) continue;
                        d.Plate = mf.transform; d.PlateR = r; d.Layer = mf.gameObject.layer;
                        d.Mat = MaterialFor(r.sharedMaterial);
                        d.Mesh = Build(d);
                        break;
                    }
                    if (d.Plate) break;
                }
            }
        }

        Material MaterialFor(Material deck)
        {
            if (mats.TryGetValue(deck, out var m) && m) return m;
            if (deck.HasProperty("_UseVertexColor"))
            {   // the deck's own TeamLit material, reading the paint from the vertex colours
                m = new Material(deck) { name = deck.name + " (road markings)", hideFlags = HideFlags.DontSave };
                m.SetFloat("_UseVertexColor", 1f); m.EnableKeyword("_USEVERTEXCOLOR_ON"); m.EnableKeyword("_VERTEXCOLORSRGB_ON");
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            }
            else
            {   // fallback: URP Lit in the paint colour
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh ? sh : deck.shader) { name = "RoadMarkings (Lit)", hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", paint);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            }
            mats[deck] = m;
            return m;
        }

        /// <summary>World-space mesh: centre dashes + two edge lines, clipped to the deck plate's world bounds.</summary>
        Mesh Build(Deck d)
        {
            var s = d.Spec; var b = d.PlateR.bounds;
            bool alongX = s.axis == "x";
            float bMin = alongX ? b.min.x : b.min.z, bMax = alongX ? b.max.x : b.max.z;
            float cMin = alongX ? b.min.z : b.min.x, cMax = alongX ? b.max.z : b.max.x;
            float a0 = Mathf.Max(bMin, Mathf.Min(s.deckFrom, s.deckTo)) + 0.05f, a1 = Mathf.Min(bMax, Mathf.Max(s.deckFrom, s.deckTo)) - 0.05f;
            float y = b.max.y + Mathf.Max(0.03f, file.lift);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            var rng = new System.Random(s.name.GetHashCode());

            void Strip(float along0, float along1, float across, float width)
            {
                if (along1 - along0 < 0.1f) return;
                if (across - width / 2 < cMin + 0.05f || across + width / 2 > cMax - 0.05f) return;   // stays on the plates
                int cells = Mathf.Max(1, Mathf.CeilToInt((along1 - along0) / 0.3f));
                int i0 = v.Count;
                for (int k = 0; k <= cells; k++)
                {
                    float a = Mathf.Lerp(along0, along1, k / (float)cells);
                    float w = 1f - wear * (float)rng.NextDouble();                               // lightly worn: brightness spread per cell
                    var col = new Color(paint.r * w, paint.g * w, paint.b * w, 1f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = across + side * width / 2;
                        v.Add(alongX ? new Vector3(a, y, x) : new Vector3(x, y, a));
                        n.Add(Vector3.up); c.Add(col); uv.Add(new Vector2(side < 0 ? 0 : 1, k / (float)cells));
                    }
                }
                for (int k = 0; k < cells; k++)
                {
                    int q = i0 + k * 2;
                    // (Unity front face: cross(b - a, c - a) = +y)
                    if (alongX) { t.Add(q); t.Add(q + 1); t.Add(q + 2); t.Add(q + 1); t.Add(q + 3); t.Add(q + 2); }
                    else { t.Add(q); t.Add(q + 2); t.Add(q + 1); t.Add(q + 1); t.Add(q + 2); t.Add(q + 3); }
                }
            }

            int kmin = Mathf.FloorToInt((a0 - s.phase) / file.period) - 1, kmax = Mathf.CeilToInt((a1 - s.phase) / file.period) + 1;
            for (int k = kmin; k <= kmax; k++)
            {
                float m = s.phase + k * file.period;
                Strip(Mathf.Max(a0, m - file.dash / 2), Mathf.Min(a1, m + file.dash / 2), s.centre, file.dashWidth);
            }
            Strip(a0, a1, s.centre - file.edgeOffset, file.edgeWidth);
            Strip(a0, a1, s.centre + file.edgeOffset, file.edgeWidth);
            var mesh = new Mesh { name = "RoadMarkings_" + s.name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        public string Status()
        {
            var sb = new System.Text.StringBuilder($"[OpenZ] BridgeMarkings {(game ? game.level : "?")}:");
            foreach (var d in decks)
                sb.Append($" {d.Spec.name}={(d.Plate ? (d.Plate.gameObject.activeInHierarchy ? "drawn" : "hidden (not intact)") : "no intact deck yet")}" +
                          (d.Mesh ? $" ({d.Mesh.vertexCount} v, y {d.Mesh.bounds.center.y:F2})" : ""));
            return sb.ToString();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 375: the fort cannon's pieces as real broken metal in the cannon's own paint, instead of clean pale boxes with a
    /// white tiled face.
    /// <list type="bullet">
    /// <item><b>Shape</b>: every cut of the breakup is a fracture (<see cref="OzFractureCut"/>): a bent, stepped break with a jagged outline,
    /// thickness and a relief face stitched to the skin, the painted lip bent in; the hidden sides closed as before, dressed as broken
    /// steel a shade darker. The turret housing breaks into two shell halves along an oblique break (<see cref="SplitHousing"/>), the
    /// barrel into two sections (<see cref="SplitBarrel"/>): 17 pieces instead of 15. The masses keep the breakup's rule (a split part's
    /// halves together weigh what the whole did); the launch is unchanged (the halves are "head", the sections "barrel").</item>
    /// <item><b>Look</b>: the paint of the pieces is the cannon's own (its sand, gunmetal and war paint with the wreck's chips, scratches and
    /// cracks, a little dust; FortV2_Gun_Albedo[_B]_Pieces_Wreck), soot and scorch near every break by vertex colour; the broken faces
    /// are torn dark cast steel (FortGun_Fracture: grain and crystalline facets, no grid, no rivets, no straight lines) at 3 m a repeat
    /// with its own turn per face, bright on the ridges, sooty in the pits, rust and temper colours at the rim, a little metallic.
    /// The rims, ribs and rebar the wreck look adds to plain cuts stay off for these pieces (the break itself is the torn edge).</item>
    /// <item>The cutting still runs in the air, one step per frame; seeded by the fort (its own numbers: the other draws keep theirs).</item>
    /// </list>
    /// Switch <see cref="FractureLook"/> (on). Off = the plain cuts and the wreck look's break faces exactly as before. Missing textures =
    /// the shape stays, the materials fall back to the plain look.
    /// </summary>
    public partial class OzFortGunBreakup
    {
        public static bool FractureLook = true;
        /// <summary>The housing in two shell halves; the barrel in two sections.</summary>
        public static bool SplitHousing = true, SplitBarrel = true;
        /// <summary>The housing's break: degrees off the side axis, and where along it (share of the housing).</summary>
        public static float HousingTurnMin = 20f, HousingTurnMax = 35f, HousingAtMin = 0.4f, HousingAtMax = 0.6f;
        /// <summary>The barrel's break: share of its length.</summary>
        public static float BarrelAtMin = 0.4f, BarrelAtMax = 0.6f;
        /// <summary>The broken steel: metallic and smoothness on top of its mask map (x), base colour (sRGB).</summary>
        public static float FractureMetallic = 0.45f, FractureSmoothness = 0.30f;
        public static Color FractureColor = Color.white;
        public const string FractureDir = "OpenZ/FortV2/Wreck/";
        // proof
        public static int FracturePieces, PaintSwapped, FractureFaces;
        public static string FractureMatState = "-";
        /// <summary>This piece wears the fracture look (its own paint and broken steel; the wreck look leaves it alone).</summary>
        public bool Fracture { get; private set; }

        static System.Random fracRnd;                                                               // the plan's numbers for the shard corners (set right before them)

        static float R3(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>A cut of the breakup: the fracture when the look is on, else the plain plane.</summary>
        static bool FSplit(OzMeshSlice.Part src, Vector3 n, float w, out OzMeshSlice.Part front, out OzMeshSlice.Part back, float sc, System.Random fr, bool tilt = true)
        {
            if (FractureLook && fr != null) return OzFractureCut.Split(src, n, w, sc, fr, tilt, out front, out back);
            return OzMeshSlice.Split(src, n, w, out front, out back);
        }

        /// <summary>A hidden open side closed (the plain closing), dressed as broken steel when the look is on.</summary>
        static OzMeshSlice.Part FClose(OzMeshSlice.Part p, Vector3 n, float w, float sc, System.Random fr)
            => FractureLook && fr != null ? OzFractureCut.Close(p, n, w, sc, fr) : OzMeshSlice.Close(p, n, w);

        /// <summary>The breakup's mass rule (Build) for a part's mesh bounds (sc = metres per mesh unit).</summary>
        static float RuleMass(Bounds b, float sc) { var s = b.size * sc; return Mathf.Clamp(Mathf.Abs(s.x * s.y * s.z) * 5f, 0.8f, 400f) * 10f; }

        static float BoxVolume(OzMeshSlice.Part p) { var s = p.Box().size; return Mathf.Abs(s.x * s.y * s.z); }

        /// <summary>The turret housing in two shell halves along an oblique fractured plane (turned 20-35 degrees off the side axis about
        /// the up axis, through 40-60 % of it). `rest` keeps the bigger half; returns the other (null = no split). k = the mass scale that
        /// makes the two halves weigh what the whole housing did.</summary>
        static OzMeshSlice.Part HousingHalf(ref OzMeshSlice.Part rest, int up, int side, float sc, System.Random fr, out float k)
        {
            k = 1f;
            float turn = (fr.Next(2) == 0 ? -1f : 1f) * R3(fr, HousingTurnMin, HousingTurnMax), at = R3(fr, HousingAtMin, HousingAtMax);
            if (rest == null) return null;
            float whole = RuleMass(rest.Box(), sc);
            if (!OzFractureCut.Halves(rest, Axis(side, 1f), Axis(up, 1f), turn, at, sc, fr, out var a, out var b) || a == null || b == null) return null;
            if (BoxVolume(b) > BoxVolume(a)) { var t = a; a = b; b = t; }
            float sum = RuleMass(a.Box(), sc) + RuleMass(b.Box(), sc);
            k = sum > 1e-3f ? whole / sum : 1f;
            rest = a;
            return b;
        }

        /// <summary>The last two defs of the plan (a split part's halves) weigh k x their rule mass.</summary>
        static void HalfMass(Plan plan, float k)
        {
            for (int i = plan.defs.Count - 1, n = 0; i >= 0 && n < 2; i--, n++) plan.defs[i].massScale = k;
        }

        /// <summary>The barrel in two sections at a fractured ring break (40-60 % of its length); both are "barrel" pieces that roll about
        /// the bore. False = no split (it stays whole).</summary>
        static bool BarrelHalves(Plan plan, Transform recoil, Mesh mesh, Vector3 boreW, System.Random fr)
        {
            float at = R3(fr, BarrelAtMin, BarrelAtMax);
            var bp = OzMeshSlice.FromMesh(mesh); if (bp == null) return false;
            var bl = recoil.InverseTransformDirection(boreW); int ax = 0;
            for (int i = 1; i < 3; i++) if (Mathf.Abs(bl[i]) > Mathf.Abs(bl[ax])) ax = i;
            float s = bl[ax] >= 0f ? 1f : -1f, sc = MeshScale(recoil);
            var bx = bp.Box(); float lo = s > 0f ? bx.min[ax] : -bx.max[ax], hi = s > 0f ? bx.max[ax] : -bx.min[ax];
            if (!OzFractureCut.Split(bp, Axis(ax, s), lo + at * (hi - lo), sc, fr, true, out var muzzleSide, out var breechSide) || muzzleSide == null || breechSide == null) return false;
            float whole = RuleMass(bx, sc), sum = RuleMass(muzzleSide.Box(), sc) + RuleMass(breechSide.Box(), sc);
            int before = plan.defs.Count;
            Cut(plan, "barrel", "barrel", recoil, mesh, breechSide, Vector3.zero);
            Cut(plan, "barrel_b", "barrel", recoil, mesh, muzzleSide, Axis(ax, s));
            if (plan.defs.Count != before + 2) return plan.defs.Count > before;
            HalfMass(plan, sum > 1e-3f ? whole / sum : 1f);
            plan.defs[before].bore = Axis(ax, 1f); plan.defs[before + 1].bore = Axis(ax, 1f);
            return true;
        }

        // ------------------------------------------------------------------ the materials
        static Texture2D fAlb, fNrm, fMask, pAlbA, pAlbB; static bool fLoaded, fOk;
        static readonly Dictionary<Shader, Material> fractureMats = new Dictionary<Shader, Material>();
        static readonly Dictionary<(Material, bool), Material> piecePaint = new Dictionary<(Material, bool), Material>();

        static bool FractureLoad()
        {
            if (fLoaded) return fOk;
            fLoaded = true;
            fAlb = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Albedo");
            fNrm = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Normal");
            fMask = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Mask");
            pAlbA = Resources.Load<Texture2D>(FractureDir + "FortV2_Gun_Albedo_Pieces_Wreck");
            pAlbB = Resources.Load<Texture2D>(FractureDir + "FortV2_Gun_Albedo_B_Pieces_Wreck");
            fOk = fAlb && fNrm && fMask && pAlbA && pAlbB;
            FractureMatState = fOk ? "fracture textures loaded" : "fracture textures MISSING under Resources/" + FractureDir + ": the pieces keep the plain look";
            if (!fOk) Debug.LogWarning("[OpenZ] fort cannon pieces: " + FractureMatState);
            return fOk;
        }

        /// <summary>The broken steel (one per shader): no team paint, its own maps, vertex colours (ridges, pits, rust, soot).</summary>
        static Material FractureMat(Material like)
        {
            if (!like) return null;
            if (fractureMats.TryGetValue(like.shader, out var m) && m) return m;
            m = new Material(like) { name = "FortGun_Fracture" };                                     // not "FortGun_BreakFace": the rest look's face lift does not reach it
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", fAlb); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); }
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", fNrm); m.SetFloat("_UseNormalMap", 1f); }
            if (m.HasProperty("_MaskMap")) { m.SetTexture("_MaskMap", fMask); m.SetFloat("_UseMaskMap", 1f); }
            foreach (var p in new[] { "_TeamMode", "_Accent", "_PaintFromAlpha", "_OwnerPart", "_Degreen", "_UnitLook", "_VertexColorSRGB" }) if (m.HasProperty(p)) m.SetFloat(p, 0f);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", FractureMetallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", FractureSmoothness);
            if (m.HasProperty("_EmissionMapColor")) m.SetColor("_EmissionMapColor", Color.black);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", FractureColor);
            fractureMats[like.shader] = m;
            return m;
        }

        /// <summary>The pieces' own paint: a copy of the thrown cannon's wreck paint (name still ends in "_Wreck", so the rest look eases it)
        /// with the cannon's own colours and the vertex colours (scorch) on.</summary>
        static Material PiecesOf(Material wreck, bool vc)
        {
            if (piecePaint.TryGetValue((wreck, vc), out var m) && m) return m;
            string baseName = wreck.name.EndsWith("_Wreck") ? wreck.name.Substring(0, wreck.name.Length - "_Wreck".Length) : wreck.name;
            m = new Material(wreck) { name = baseName + "_Pieces_Wreck" };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", baseName.Contains("_B") ? pAlbB : pAlbA);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", vc ? 1f : 0f);
            if (m.HasProperty("_VertexColorSRGB")) m.SetFloat("_VertexColorSRGB", 0f);
            piecePaint[(wreck, vc)] = m; PaintSwapped++;
            return m;
        }

        /// <summary>Build: the piece's materials for the fracture look (before they go on the renderer). True = it wears it.</summary>
        static bool FractureMats(Def d, Material[] mats, Material[] src, int nSub)
        {
            if (!d.fracture || !FractureLook || !FractureLoad() || src.Length == 0 || !src[0]) return false;
            bool vc = d.mesh && d.mesh.HasVertexAttribute(VertexAttribute.Color);
            for (int i = 0; i < mats.Length; i++)
            {
                if (d.capped && i == nSub - 1) { mats[i] = FractureMat(src[0]); FractureFaces++; continue; }
                if (mats[i] && mats[i].name.EndsWith("_Wreck")) mats[i] = PiecesOf(mats[i], vc);
            }
            FracturePieces++;
            return true;
        }
    }
}

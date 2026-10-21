using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// <list type="bullet">
    /// <item><b>183 the unit material.</b> On the map each hero body wears the grunts' own unit material (M_BS01_Body: OpenZ/TeamLit, the
    /// unit look, the night look, every setting the same) with his own albedo Models/HeroSquad/Hero_{Alan,Brad}_Unit_BaseColor.png: the
    /// hero's texture with its red paint panels made a neutral grey of the grunt paint's own brightness and the panels' mask in alpha
    /// (TeamLit team mode 3, "texture alpha", as the grunt's BS01_Body_toy.png), so TeamPaint lays red or blue on exactly those panels
    /// and nothing turns pink (the first try had no mask: the red texture went through the paint-free branch, desaturated to pink).
    /// One material for both teams (TeamPaint sets the colour). The briefing keeps its cinematic materials.</item>
    /// <item><b>184 / 185 the legs.</b> (1) A true T-pose for the avatar from the model's own bones: each thigh turned as a whole so
    /// that its bind knee hinge lies across the body (the knee bends straight to the front, no leg twist carried over from the lounger
    /// bind pose), shin straight below it (the v3 robots' small knee bend kept), each foot keeping its bind turn (flat on the ground,
    /// toes out as modelled), then the hips raised by what the straightening lowered the ankles, so the soles stand on the ground and
    /// the avatar's hips height is the robot's real standing height (Brad's KC4 T-pose had his soles 12 cm under the ground and the toes
    /// 17 deg up: every clip played on top of that). (2) An option, OFF as shipped (<see cref="HeroFootIk"/> = true): the heroes on a
    /// copy of the grunt's controller without the states' foot IK (Models/HeroSquad/RobotBS01_Hero.controller, remade from
    /// RobotBS01.controller at every menu 1). The game's RobotLegIK pins every robot's feet to the clips' foot goals and needs them,
    /// because the grunt clips' leg muscles hold bent knees as a large thigh twist (View/RobotLegIK.cs); try it only as an A/B in the
    /// editor. (3) In Play, View/RobotLegIK.Hero.cs spreads a hero's foot goals to his own hips, keeps each foot on its own side and
    /// scales the leg distances with his size.</item>
    /// <item><b>Size measured in the pose the player sees.</b> The drawn scale makes the hero's body top in the grunt controller's idle
    /// (<see cref="SizeClip"/> at <see cref="SizeTime"/> s, sampled on his own avatar) exactly AlanToGrunt x the grunt's in the same
    /// idle (Brad = Alan / AlanToBrad); the menu 1 log prints the old (bind pose) and the new scale. If the idle cannot be sampled, the
    /// larger of the bind-pose and standing scales (never smaller than before). Menu 3 measures the three the same way.</item>
    /// <item><b>Play numbers.</b> Menu Play: look numbers (each hero's mean screen brightness against the nearest grunts', the game's
    /// own camera and light) and Play: legs numbers (the hero rows of the legs gate's trace: feet over the middle, crossed, knee flips,
    /// stride).</item>
    /// </list>
    /// </summary>
    public static partial class OzHeroSquadSetup
    {
        /// <summary>KH: the avatar's true T-pose (knee hinge across the body, feet flat, soles on the ground). false = the KC4 skeleton.</summary>
        public static bool TrueTPose = true;
        /// <summary>KH: a bind knee bent less than this (deg) carries no usable hinge: the thigh is only swung down (the KC4 way).</summary>
        public const float MinBindBend = 5f;
        /// <summary>KH: true = the heroes play the grunt's own controller with foot IK (as KC4, shipped); false = the copy without foot IK (A/B only).</summary>
        public static bool HeroFootIk = true;
        const string HeroCtl = Dir + "/RobotBS01_Hero.controller";

        /// <summary>The leg straightening's record: ankles moved (m, summed), legs done, the hips lift, per leg lines.</summary>
        public sealed class KhLegs { public float drop; public int legs; public float lift; public readonly List<string> lines = new List<string>(); }
        /// <summary>The last Describe's record (menu 1 log, menu 3 check).</summary>
        public static readonly Dictionary<string, KhLegs> LegLog = new Dictionary<string, KhLegs>();

        /// <summary>One leg of the avatar skeleton (the instance at its bind pose): the thigh turned whole so it points down with its bind knee
        /// hinge across the body (knee to the front), the shin straight under it, the small knee bend, the foot back at its bind turn.</summary>
        static void StraightLeg(string side, Transform up, Transform kn, Transform ft, Vector3 fwd, Vector3 lateral, KhLegs kh)
        {
            var footRot0 = ft.rotation; float ank0 = ft.position.y;
            Vector3 th = kn.position - up.position, sh = ft.position - kn.position;
            float bend = Vector3.Angle(th, sh);
            var hinge = Vector3.Cross(th.normalized, sh.normalized);
            var hRef = Vector3.Cross(fwd, Vector3.down).normalized;                                   // the hinge of a knee bent to the front, thigh down
            string how;
            if (bend >= MinBindBend && hinge.sqrMagnitude > 1e-8f)
            {
                hinge.Normalize();
                var q = Quaternion.LookRotation(hRef, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(hinge, -th.normalized));
                up.rotation = q * up.rotation;
                how = $"bind knee {bend:0} deg, hinge turned {Vector3.Angle(hinge, hRef):0} deg onto the body's knee axis";
            }
            else
            {
                up.rotation = Quaternion.FromToRotation(th, Vector3.down) * up.rotation;
                how = $"bind knee {bend:0} deg (straight: thigh swung down only)";
            }
            kn.rotation = Quaternion.FromToRotation(ft.position - kn.position, Vector3.down) * kn.rotation;
            var toFront = Quaternion.AngleAxis(OpenZRobotsV3.AvatarKneeBend * 0.5f, lateral);
            if (Vector3.Dot(toFront * Vector3.down, fwd) < 0f) toFront = Quaternion.Inverse(toFront);
            up.rotation = toFront * up.rotation;
            kn.rotation = Quaternion.Inverse(toFront) * Quaternion.Inverse(toFront) * kn.rotation;
            ft.rotation = footRot0;                                                                   // flat on the ground as modelled
            kh.drop += ft.position.y - ank0; kh.legs++;
            kh.lines.Add($"{side} leg: {how}; ankle moved {(ft.position.y - ank0) * 100f:0.0} cm");
        }

        /// <summary>After both legs: the hips (and the whole body) raised by what the ankles dropped, so the soles stand where they did.</summary>
        static void KhGround(Transform hips, KhLegs kh)
        {
            if (!hips || kh.legs == 0) return;
            kh.lift = -kh.drop / kh.legs;
            hips.position += Vector3.up * kh.lift;
        }

        /// <summary>Menu 1: the heroes' controller. HeroFootIk: the grunt's own. Else RobotBS01.controller copied over RobotBS01_Hero.controller
        /// (the file, so the copy keeps its id and every prefab's reference) with foot IK off in every state of every layer.</summary>
        static RuntimeAnimatorController KhController(RuntimeAnimatorController grunt, List<string> log)
        {
            if (HeroFootIk || !grunt) return grunt;
            string src = AssetDatabase.GetAssetPath(grunt);
            if (string.IsNullOrEmpty(src) || !src.EndsWith(".controller")) { log.Add("hero controller: the grunt's is no controller asset: kept"); return grunt; }
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroCtl)) { File.Copy(src, HeroCtl, true); AssetDatabase.ImportAsset(HeroCtl, ImportAssetOptions.ForceUpdate); }
            else if (!AssetDatabase.CopyAsset(src, HeroCtl)) { log.Add("hero controller: could not copy " + src); return grunt; }
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroCtl);
            if (!ctl) { log.Add("hero controller: " + HeroCtl + " not loaded"); return grunt; }
            int n = 0, off = 0;
            void Walk(AnimatorStateMachine sm)
            {
                if (!sm) return;
                foreach (var cs in sm.states) { n++; if (cs.state.iKOnFeet) { cs.state.iKOnFeet = false; off++; EditorUtility.SetDirty(cs.state); } }
                foreach (var c in sm.stateMachines) Walk(c.stateMachine);
            }
            foreach (var l in ctl.layers) Walk(l.stateMachine);
            EditorUtility.SetDirty(ctl); AssetDatabase.SaveAssets();
            log.Add($"hero controller {HeroCtl}: a copy of {src}, {n} states, foot IK off in {off}");
            return ctl;
        }

        /// <summary>Height (m) of a model standing in its avatar T-pose (the importer's skeleton applied below its root; arms out, which
        /// leaves the top of the head the highest point); the bind pose is put back afterwards.</summary>
        static float StandingHeight(GameObject model, string fbx)
        {
            if (!model) return 0f;
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi) || mi.humanDescription.skeleton == null || mi.humanDescription.skeleton.Length == 0) return BodyHeight(model);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t != model.transform && !byName.ContainsKey(t.name)) byName[t.name] = t;
            foreach (var sb in mi.humanDescription.skeleton)
                if (byName.TryGetValue(sb.name, out var t)) { t.localPosition = sb.position; t.localRotation = sb.rotation; }
            float h = BodyHeight(model);
            OpenZRobotsV3.ToBindPose(model);
            return h;
        }

        /// <summary>KH (item 183 size): the clip and time of "the pose the player sees": the grunt controller's idle.</summary>
        public static string SizeClip = "idle_calm_humanoid"; public static float SizeTime = 0.5f;
        /// <summary>Menu 3: the robots whose idle could not be sampled in the last check (null = none).</summary>
        static string khSizeFallback;

        /// <summary>The highest point of the body (skinned renderers; weapons, kit parts and the briefing's cans left out, as BodyHeight)
        /// above the object's root, with the grunt controller's idle sampled on its humanoid Animator; NaN if it cannot be sampled.</summary>
        static float PosedTop(GameObject root, Animator an)
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            var clip = ctl ? ctl.animationClips.FirstOrDefault(c => c.name == SizeClip) : null;
            if (!root || !an || !an.avatar || !an.avatar.isHuman || !clip) return float.NaN;
            bool started = false;
            try
            {
                if (!AnimationMode.InAnimationMode()) { AnimationMode.StartAnimationMode(); started = true; }
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(an.gameObject, clip, SizeTime);
                AnimationMode.EndSampling();
                float top = float.NegativeInfinity; var mesh = new Mesh();
                try
                {
                    foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!r.sharedMesh || r.name.StartsWith("BS01_Weapon") || r.name.Contains("FUEL_CAN") || r.name.StartsWith("BS01Kit") || r.name.StartsWith("BS01_Kit")) continue;
                        r.BakeMesh(mesh, true);
                        var m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                        foreach (var v in mesh.vertices) top = Mathf.Max(top, m.MultiplyPoint3x4(v).y);
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
                return float.IsNegativeInfinity(top) ? float.NaN : top - root.transform.position.y;
            }
            finally { if (started) AnimationMode.StopAnimationMode(); }
        }

        /// <summary>PosedTop of a fresh copy of an asset (a model file or a prefab) in a preview scene; avatar / controller set when given.</summary>
        static float PosedTopOf(GameObject asset, Avatar av, RuntimeAnimatorController ctl)
        {
            if (!asset) return float.NaN;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Object.Instantiate(asset);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                var an = go.GetComponentInChildren<Animator>();
                if (!an) { if (!av) return float.NaN; an = go.AddComponent<Animator>(); }
                if (av) an.avatar = av;
                if (ctl) an.runtimeAnimatorController = ctl;
                return PosedTop(go, an);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Menu 1 (BuildPrefab, in place of the bind-pose height): the hero model's height to divide his target height by, so
        /// that in the idle he stands exactly height / gruntH x the grunt's idle height (the grunt measured the same way). Logs the old
        /// (bind pose) and the new drawn scale. Fallback when the idle cannot be sampled: the larger of the bind and standing scales.</summary>
        static float KhBaseHeight(GameObject hm, string model, Avatar av, RuntimeAnimatorController ctl, float height, float gruntH, List<string> log)
        {
            float bind = BodyHeight(hm);
            float hero = PosedTopOf(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx(model)), av, ctl);
            float grunt = PosedTopOf(AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefab), null, null);
            float h0; string how;
            if (!float.IsNaN(hero) && !float.IsNaN(grunt) && hero > 0.01f && grunt > 0.01f && gruntH > 0.01f)
            {
                h0 = hero * gruntH / grunt;
                how = $"the idle pose, {SizeClip} @{SizeTime:0.0} s: his top {hero:F3} m at scale 1, the grunt's {grunt:F3} m (bind height {gruntH:F3} m)";
            }
            else
            {
                float stand = StandingHeight(hm, Fbx(model));
                h0 = stand > 0.01f ? Mathf.Min(bind, stand) : bind;
                how = $"the idle could not be sampled: the larger of the bind-pose ({bind:F3} m) and standing ({stand:F3} m) scales";
            }
            if (bind > 0.01f && h0 > 0.01f) log.Add($"drawn scale {height / bind:F4} (bind pose, as before) -> {height / h0:F4} ({how})");
            return h0 > 0.01f ? h0 : bind;
        }

        /// <summary>Menu 3: a height measured the same way for the grunt (inst null: a fresh copy of its prefab) and for a hero prefab
        /// instance; the bind-pose height when the idle cannot be sampled (then named in the KH line).</summary>
        static float KhCheckHeight(GameObject inst, GameObject bindFallback = null)
        {
            if (inst == null)
            {
                khSizeFallback = null;
                float g = PosedTopOf(AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefab), null, null);
                if (!float.IsNaN(g) && g > 0.01f) return g;
                khSizeFallback = "the grunt"; return GruntHeight(new List<string>());
            }
            float h = PosedTop(inst, inst.GetComponentInChildren<Animator>());
            if (!float.IsNaN(h) && h > 0.01f) return h;
            khSizeFallback = (khSizeFallback == null ? "" : khSizeFallback + ", ") + inst.name;
            return BodyHeight(bindFallback ? bindFallback : inst);
        }

        /// <summary>The map body material's base colour per hero (K1: by day at least 0.90 x the grunts' brightness, at night at most
        /// 1.25 x; both measured 0.91 by day and 1.22 at night with 1.2).</summary>
        public static float BradUnitBright = 1.2f, AlanUnitBright = 1.2f;

        /// <summary>Menu 1: the body's map material (item 183): a copy of the grunt's body material with the hero's unit albedo.</summary>
        static Material KhUnitMaterial(string model, Material grunt, List<string> log)
        {
            string tp = $"{Dir}/Hero_{model}_Unit_BaseColor.png", p = $"{Dir}/M_Hero_{model}_Body_Unit.mat";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            if (!tex) { log.Add($"{model}: {tp} missing (kit KH): the cinematic body material stays on the map"); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(grunt) { name = Path.GetFileNameWithoutExtension(p) }; AssetDatabase.CreateAsset(m, p); }
            else { m.shader = grunt.shader; m.CopyPropertiesFromMaterial(grunt); }
            m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            float lift = model.ToLowerInvariant() == "brad" ? BradUnitBright : model.ToLowerInvariant() == "alan" ? AlanUnitBright : 1f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(lift, lift, lift, 1f));
            EditorUtility.SetDirty(m);
            log.Add($"{model}: map body material {p} = {GruntBodyMat} (shader {grunt.shader.name}, team mode {(grunt.HasProperty("_TeamMode") ? grunt.GetFloat("_TeamMode") : -1):0}) with {Path.GetFileName(tp)} (paint mask in alpha); both teams (TeamPaint sets the colour)");
            return m;
        }

        /// <summary>Menu 3: the KH lines.</summary>
        static string KhCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine(V(khSizeFallback == null, khSizeFallback == null
                ? $"the heights above are measured in the pose the player sees (the grunt controller's {SizeClip} @{SizeTime:0.0} s on each robot's own avatar)"
                : $"the heights above: the idle could not be sampled for {khSizeFallback} (bind-pose height used there)"));
            foreach (var (member, model) in Members)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(member));
                if (!pf) continue;
                var body = pf.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.EndsWith("_Body"));
                var mat = body ? body.sharedMaterial : null;
                var tex = mat && mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                sb.AppendLine(V(mat && mat.shader && mat.shader.name == "OpenZ/TeamLit" && mat.HasProperty("_TeamMode") && mat.GetFloat("_TeamMode") > 2.5f && tex && tex.name.Contains("_Unit_BaseColor"),
                    $"{member}: map body material {(mat ? mat.name : "-")} ({(mat && mat.shader ? mat.shader.name : "-")}, team mode {(mat && mat.HasProperty("_TeamMode") ? mat.GetFloat("_TeamMode") : -1):0}, albedo {(tex ? tex.name : "-")}) = the grunt's unit material with his own masked albedo"));
                var look = pf.GetComponent<OzHeroLook>();
                sb.AppendLine(V(look && look.red != null && look.blue != null && look.red.Length > 0 && look.red[0] == look.blue[0], $"{member}: the same body material for red and blue (TeamPaint colours it)"));
                var an = pf.GetComponentInChildren<Animator>();
                var ctl = an ? an.runtimeAnimatorController as AnimatorController : null;
                int ik = 0, n = 0;
                if (ctl) foreach (var l in ctl.layers) foreach (var cs in l.stateMachine.states) { n++; if (cs.state.iKOnFeet) ik++; }
                sb.AppendLine(V(HeroFootIk || (ctl && ctl.name == "RobotBS01_Hero" && ik == 0 && n > 10), $"{member}: controller {(ctl ? ctl.name : "-")}, {n} states, foot IK on in {ik} ({(HeroFootIk ? "the grunt's own, as shipped" : "wanted 0: the hero copy")})"));
                if (LegLog.TryGetValue(model, out var kh))
                    sb.AppendLine(V(kh.legs == 2 && kh.lift > -0.01f, $"{member}: avatar T-pose: {string.Join("; ", kh.lines)}; hips raised {kh.lift * 100f:0.0} cm (soles on the ground)"));
                else sb.AppendLine("      " + member + ": the leg record of menu 1 is not in this editor session (run menu 1 first to see it)");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ KH Play numbers
        [MenuItem("OpenZ/Hero Squad/Play: look numbers (heroes vs the nearest grunts)", false, 32)]
        static void PlayLook() => Say(Application.isPlaying ? KhLookNumbers() : "enter Play first");

        /// <summary>K1 (item 183): each hero's and the nearest grunts' mean screen brightness (Rec. 709 luma of the final picture, 0-1)
        /// with the game's own camera, light, post effects and time of day: the unit alone is drawn once on a spare layer and compared
        /// with the same frame drawn empty (the pixels that change are the unit's; its rifle left out). Wanted: each hero at least
        /// <see cref="LookMin"/> x the grunts' mean. Run it by day and at night.</summary>
        public static string KhLookNumbers()
        {
            var g = ZGame.I; var cam = Camera.main;
            if (!g || g.World == null || !cam) return "no running game or main camera";
            int tmp = -1;
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { tmp = i; break; }
            if (tmp < 0) return "no spare layer for the measure";
            var views = g.Views.Values.Where(v => v && v.Obj != null && v.Obj.Kind == OpenZ.Sim.ZKind.Robot && !v.Dying && v.isActiveAndEnabled).ToList();
            var heroes = views.Where(v => v.Obj.IsHero).ToList();
            if (heroes.Count == 0) return "no hero on the map";
            var c0 = heroes[0].transform.position;
            var grunts = views.Where(v => !v.Obj.IsHero && v.Obj.Type == "grunt").OrderBy(v => (v.transform.position - c0).sqrMagnitude).Take(3).ToList();
            if (grunts.Count == 0) return "no grunt on the map";
            int W = cam.pixelWidth, H = cam.pixelHeight;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var keepT = cam.targetTexture; int keepM = cam.cullingMask;
            var sb = new StringBuilder();
            try
            {
                Color32[] Shot(int mask)
                {
                    cam.targetTexture = rt; cam.cullingMask = mask; cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    return tex.GetPixels32();
                }
                var empty = Shot(0);
                (float mean, int px) One(UnitView v)
                {
                    var rs = v.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer) && !r.name.StartsWith("BS01_Weapon")).ToList();
                    var keep = rs.Select(r => r.gameObject.layer).ToList();
                    try
                    {
                        foreach (var r in rs) r.gameObject.layer = tmp;
                        var px = Shot(1 << tmp); double s = 0; int n = 0;
                        for (int i = 0; i < px.Length; i++)
                        {
                            var a = px[i]; var b = empty[i];
                            if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 12) continue;
                            s += 0.2126 * a.r + 0.7152 * a.g + 0.0722 * a.b; n++;
                        }
                        return (n > 0 ? (float)(s / n / 255.0) : 0f, n);
                    }
                    finally { for (int i = 0; i < rs.Count; i++) if (rs[i]) rs[i].gameObject.layer = keep[i]; }
                }
                var gs = grunts.Select(One).Where(x => x.px > 30).ToList();
                if (gs.Count == 0) return "the grunts are not on screen: put the pair and a grunt squad in view at the standard zoom";
                float gm = gs.Average(x => x.mean);
                sb.AppendLine($"grunts ({gs.Count}): mean brightness {gm:0.000} ({string.Join(", ", gs.Select(x => $"{x.mean:0.000} on {x.px} px"))})");
                foreach (var h in heroes)
                {
                    var (m, n) = One(h);
                    sb.AppendLine(V(n > 30 && m >= LookMin * gm, $"{h.Obj.HeroMemberName}: mean brightness {m:0.000} on {n} px = x {(gm > 0 ? m / gm : 0):0.00} the grunts' (wanted at least x {LookMin:0.00}){(n <= 30 ? " (not on screen)" : "")}"));
                }
            }
            finally
            {
                cam.targetTexture = keepT; cam.cullingMask = keepM;
                RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(tex);
            }
            return sb.ToString();
        }

        /// <summary>K1: a hero's mean screen brightness at least this share of the grunts'.</summary>
        public static float LookMin = 0.9f;

        /// <summary>One robot's steady walking frames in the legs gate trace (KhLegsNumbers).</summary>
        sealed class LegRow { public string type; public int n, over, crossed, flips; public float zl0 = 9f, zl1 = -9f, zr0 = 9f, zr1 = -9f; }

        [MenuItem("OpenZ/Hero Squad/Play: legs numbers (after the legs gate)", false, 33)]
        static void PlayLegs() => Say(KhLegsNumbers());

        /// <summary>K3 (items 184, 185): the hero rows (and the grunts' for comparison) of the last legs gate (menu OpenZ/Checks/Legs gate:
        /// middle (left foot right of the middle or the right foot left of it, the robot's own frame), feet crossed (left right of the
        /// right one), knee flips (knee behind the hip-ankle midpoint by 2 cm), stride, and the gate's own verdict for the robot.
        /// PASS for a hero: at least 90 walking frames, 0 over the middle, 0 crossed, 0 flips, stride at least 20 cm, the gate's row ok.
        /// Run it in the same Play session as the gate (the hero ids come from the running game).</summary>
        public static string KhLegsNumbers()
        {
            string csv = Path.Combine(OpenZPaths.Records, "leg_gate_trace.csv"), md = Path.Combine(OpenZPaths.Records, "leg_gate.md");
            if (!File.Exists(csv)) return "no legs gate trace: in Play run OpenZ/Checks/Legs gate: Play (8 s, walks squads) first";
            var heroes = new Dictionary<int, string>();
            if (Application.isPlaying && ZGame.I && ZGame.I.World != null)
                foreach (var o in ZGame.I.World.Objects) if (o.IsHero) heroes[o.Id] = o.HeroMemberName;
            var lines = File.ReadAllLines(csv); if (lines.Length < 2) return "the legs gate trace is empty";
            var head = lines[0].Split(','); int C(string n) => System.Array.IndexOf(head, n);
            int cId = C("id"), cType = C("type"), cSt = C("state"), cTr = C("trans"), cLx = C("lx"), cRx = C("rx"), cLz = C("lz"), cRz = C("rz"), cKl = C("kneeL_fwd"), cKr = C("kneeR_fwd");
            if (new[] { cId, cType, cSt, cTr, cLx, cRx, cLz, cRz, cKl, cKr }.Any(i => i < 0)) return "the legs gate trace has other columns than expected";
            var rows = new Dictionary<int, LegRow>();
            float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Split(','); if (c.Length < head.Length - 2) continue;
                if (c[cSt] != "walking" || c[cTr] != "0") continue;
                int id = int.Parse(c[cId]);
                if (!rows.TryGetValue(id, out var r)) rows[id] = r = new LegRow { type = c[cType] };
                float lx = F(c[cLx]), rx = F(c[cRx]), lz = F(c[cLz]), rz = F(c[cRz]), kl = F(c[cKl]), kr = F(c[cKr]);
                r.n++; if (lx < 0f || rx > 0f) r.over++; if (lx <= rx) r.crossed++; if (kl < -0.02f || kr < -0.02f) r.flips++;
                r.zl0 = Mathf.Min(r.zl0, lz); r.zl1 = Mathf.Max(r.zl1, lz); r.zr0 = Mathf.Min(r.zr0, rz); r.zr1 = Mathf.Max(r.zr1, rz);
            }
            var verdict = new Dictionary<int, string>();
            if (File.Exists(md))
                foreach (var l in File.ReadAllLines(md))
                {
                    var c = l.Split('|'); if (c.Length < 17 || !int.TryParse(c[1].Trim(), out int id)) continue;
                    verdict[id] = c[c.Length - 2].Trim();
                }
            var sb = new StringBuilder();
            sb.AppendLine($"legs gate trace: {rows.Count} robots walked; heroes in this game: {(heroes.Count > 0 ? string.Join(", ", heroes.Select(h => $"{h.Value} #{h.Key}")) : "none known (run in the gate's Play session)")}");
            foreach (var kv in rows.OrderBy(k => heroes.ContainsKey(k.Key) ? 0 : 1).ThenBy(k => k.Key))
            {
                var r = kv.Value; bool hero = heroes.ContainsKey(kv.Key);
                if (!hero && r.type != "grunt") continue;
                float stride = Mathf.Max(r.zl1 - r.zl0, r.zr1 - r.zr0) * 100f;
                string gate = verdict.TryGetValue(kv.Key, out var vv) ? vv : "-";
                string line = $"{(hero ? heroes[kv.Key].ToUpperInvariant() : "grunt")} #{kv.Key}: {r.n} walking frames, a foot over the middle {r.over}, feet crossed {r.crossed}, knee flips {r.flips}, stride {stride:0} cm, the gate's row: {gate}";
                if (hero) sb.AppendLine(V(r.n >= 90 && r.over == 0 && r.crossed == 0 && r.flips == 0 && stride >= 20f && gate == "ok", line));
                else sb.AppendLine("      " + line + " (for comparison)");
            }
            if (heroes.Count > 0 && !heroes.Keys.Any(rows.ContainsKey)) sb.AppendLine(V(false, "no hero walked in the gate: put the hero squad on screen (the gate walks up to four on-screen player squads) and run the gate again"));
            return sb.ToString();
        }
    }
}

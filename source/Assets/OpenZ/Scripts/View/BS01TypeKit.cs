using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public enum BS01Type { Grunt, Psycho, Tough, Sniper, Pyro }

    /// <summary>
    /// grunt steel pot + pack/bedroll, psycho ammo drum + brass feed belt + spikes, tough slab pauldron + bucket helmet
    /// (x1.08), sniper boonie hat + ghillie cape + whip antenna (lean and tall), pyro twin yellow tanks + hose + goggles.
    /// The kit FBX (BS01_Type_&lt;Type&gt;.fbx) holds one empty per humanoid bone (KitBone_&lt;HumanBodyBones&gt;) at that bone's
    /// rest matrix, with the rigid kit meshes and sockets under it. Build() parents each anchor to the real bone
    /// (Animator.GetBoneTransform, avatar name map as fallback) with the offset measured against the body's bind pose, so the
    /// kit follows every clip, the aim layer, WeaponHold's IK and the ragdoll without skinning. Team accents use the atlas
    /// alpha (TeamLit _TeamMode 3) and TeamPaint, like the body. At run time: "_Back_" parts (packs, drum, tanks, cape, radio)
    /// are hidden while the robot sits in a jeep or cannon (seated_driver / seated_gunner), the feed belt / fuel hose cables
    /// are rebuilt after WeaponHold, and the pyro's antenna smokes. OpenZBS01Types bakes this into the Z_bs01_* prefabs.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(85)]
    public class BS01TypeKit : MonoBehaviour
    {
        public BS01Type type;
        [Tooltip("BS01_Type_<Type>.fbx (only needed for a run-time attach; the baked prefabs already carry the kit)")]
        public GameObject kitModel;
        [Tooltip("M_BS01_Kit: OpenZ/TeamLit, _TeamMode 3, _BaseMap = BS01_Kit_BaseColor (A = team mask), vertex colour = baked AO")]
        public Material kitMaterial;
        [Tooltip("Model scale before the type tweak (captured on the first Build)")]
        public Vector3 baseModelScale;
        [Tooltip("DESIGN.md fallback: a uniform 0.98 for the sniper instead of (0.95, 1.03, 0.95) if the ragdoll ever misbehaves")]
        public bool uniformSniperScale;
        [SerializeField] List<Renderer> backParts = new List<Renderer>();
        [SerializeField] List<Renderer> coreParts = new List<Renderer>();
        [SerializeField] List<BS01KitCable> cables = new List<BS01KitCable>();
        [SerializeField] Transform smokeSocket;
        [SerializeField] bool built;

        Animator anim;
        bool seated, first = true;
        ParticleSystem smoke;
        static readonly int SeatedDriver = Animator.StringToHash("seated_driver"), SeatedGunner = Animator.StringToHash("seated_gunner");

        public bool Built => built;
        public bool Seated => seated;
        public IReadOnlyList<Renderer> BackParts => backParts;
        public IReadOnlyList<Renderer> CoreParts => coreParts;

        // --------------------------------------------------------------------------------------------- per-type data
        /// <summary>Unity-axes scale on the prefab's Model node, times its base (height-matching) scale.</summary>
        public static Vector3 TypeScale(BS01Type t, bool uniformSniper) => t switch
        {
            BS01Type.Tough => Vector3.one * 1.08f,                                  // heaviest robot in Zod (3378 HP): looks it
            BS01Type.Sniper => uniformSniper ? Vector3.one * 0.98f : new Vector3(0.95f, 1.03f, 0.95f),   // leaner and taller
            _ => Vector3.one,
        };

        // weapon-local sockets (Unity weapon space: grip at the origin, barrel -Z, up +Y), measured on the weapon meshes
        // (bs01_types/tools/kit_weapon_sockets.py): the psycho MG receiver's left side and the rear face of the flamer body
        public static readonly Vector3 FeedEndLocal = new Vector3(0.192f, 0.21f, -0.30f);
        public static readonly Vector3 HoseEndLocal = new Vector3(0f, 0.10f, 0.207f);

        static readonly Dictionary<HumanBodyBones, string> Mixamo = new Dictionary<HumanBodyBones, string>
        {
            { HumanBodyBones.Hips, "mixamorigHips" }, { HumanBodyBones.Spine, "mixamorigSpine" }, { HumanBodyBones.Chest, "mixamorigSpine1" },
            { HumanBodyBones.UpperChest, "mixamorigSpine2" }, { HumanBodyBones.Neck, "mixamorigNeck" }, { HumanBodyBones.Head, "mixamorigHead" },
            { HumanBodyBones.LeftUpperArm, "mixamorigLeftArm" }, { HumanBodyBones.RightUpperArm, "mixamorigRightArm" },
            { HumanBodyBones.LeftLowerArm, "mixamorigLeftForeArm" }, { HumanBodyBones.RightLowerArm, "mixamorigRightForeArm" },
            { HumanBodyBones.LeftUpperLeg, "mixamorigLeftUpLeg" }, { HumanBodyBones.RightUpperLeg, "mixamorigRightUpLeg" },
            { HumanBodyBones.LeftLowerLeg, "mixamorigLeftLeg" }, { HumanBodyBones.RightLowerLeg, "mixamorigRightLeg" },
        };

        public static bool TryParseType(string s, out BS01Type t)
        {
            t = BS01Type.Grunt;
            if (string.IsNullOrEmpty(s)) return false;
            return Enum.TryParse(s.Trim(), true, out t);
        }

        // --------------------------------------------------------------------------------------------- build
        /// <summary>Adds (or rebuilds) the kit of this type on a BS-01 unit (Z_bs01_* prefab contents or a live instance).</summary>
        public static BS01TypeKit Attach(GameObject unit, BS01Type type, GameObject kitModel, Material mat)
        {
            var kit = unit.GetComponent<BS01TypeKit>();
            if (!kit) kit = unit.AddComponent<BS01TypeKit>();
            kit.type = type; kit.kitModel = kitModel; if (mat) kit.kitMaterial = mat;
            return kit.Build() ? kit : null;
        }

        void Awake()
        {
            // run-time path for a prefab that only carries the component (not baked yet)
            if (Application.isPlaying && !built && kitModel) Build();
        }

        public bool Build()
        {
            Remove();
            anim = GetComponentInChildren<Animator>(true);
            if (!anim || !kitModel) { Debug.LogWarning($"[BS01TypeKit] {name}: no Animator or kit model"); return false; }
            var model = anim.transform;
            var body = FindBody(model);
            if (baseModelScale == Vector3.zero) baseModelScale = model.localScale;
            model.localScale = Vector3.Scale(baseModelScale, TypeScale(type, uniformSniperScale));

            // the kit root = the BS-01 model origin (both FBXs share the same export space)
            var inst = Instantiate(kitModel, model, false);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            int layer = body ? body.gameObject.layer : gameObject.layer;
            // with Preserve Hierarchy the file root wraps the FBX root (BS01_Type_<Type>), so look for the anchors at any depth
            var anchors = new List<Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("KitBone_")) anchors.Add(t);
            foreach (var a in anchors)
            {
                if (!a.name.StartsWith("KitBone_")) continue;
                string hbName = a.name.Substring("KitBone_".Length);
                if (!Enum.TryParse(hbName, out HumanBodyBones hb)) { Debug.LogWarning($"[BS01TypeKit] unknown bone {hbName}"); continue; }
                var bone = Bone(anim, model, hb);
                if (!bone) { Debug.LogWarning($"[BS01TypeKit] {name}: bone {hb} not found"); continue; }
                var rel = RestMatrix(body, bone).inverse * a.localToWorldMatrix;     // anchor in the bone's rest frame
                a.SetParent(bone, false);
                a.localPosition = rel.GetColumn(3);
                a.localRotation = rel.rotation;
                a.localScale = rel.lossyScale;
                a.name = "BS01Kit_" + hbName;
                foreach (var t in a.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (kitMaterial) r.sharedMaterial = kitMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                    (r.name.Contains("_Back_") ? backParts : coreParts).Add(r);
                }
            }
            Kill(inst);

            // cables: feed belt (psycho) and fuel hose (pyro), rebuilt every frame after WeaponHold
            var weapon = FindWeaponFrame(model);
            if (type == BS01Type.Psycho)
            {
                var feedEnd = weapon ? Socket(weapon, "FeedEnd", FeedEndLocal) : null;
                AddCable("BS01KitCable_FeedA", BS01KitCable.Style.Belt, 0.06f, Find("FeedStart"), Find("FeedPadIn"));
                AddCable("BS01KitCable_FeedB", BS01KitCable.Style.Belt, 0.10f, Find("FeedPadOut"), feedEnd);
            }
            if (type == BS01Type.Pyro)
            {
                var hoseEnd = weapon ? Socket(weapon, "HoseEnd", HoseEndLocal) : null;
                AddCable("BS01KitCable_Hose", BS01KitCable.Style.Hose, 0.12f, Find("HoseStart"), Find("HoseMid"), hoseEnd);
                smokeSocket = Find("AntennaSmoke");
            }
            built = true;
            var tp = GetComponent<TeamPaint>(); if (tp) tp.Apply();
            return true;
        }

        /// <summary>Takes the kit off again (anchors, cables, weapon sockets) and restores the Model scale.</summary>
        public void Remove()
        {
            var kill = new List<GameObject>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && (t.name.StartsWith("BS01Kit_") || t.name.StartsWith("BS01KitCable_") || t.name == "FeedEnd" || t.name == "HoseEnd"))
                    kill.Add(t.gameObject);
            foreach (var g in kill) if (g) Kill(g);
            var a = anim ? anim : GetComponentInChildren<Animator>(true);
            if (a && baseModelScale != Vector3.zero) a.transform.localScale = baseModelScale;
            backParts.Clear(); coreParts.Clear(); cables.Clear(); smokeSocket = null; built = false;
        }

        void AddCable(string n, BS01KitCable.Style style, float sag, params Transform[] pts)
        {
            foreach (var p in pts) if (!p) { Debug.LogWarning($"[BS01TypeKit] {name}: cable {n} misses a socket"); return; }
            var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<BS01KitCable>();
            c.style = style; c.points = pts; c.sag = sag;
            c.radius = style == BS01KitCable.Style.Belt ? 0.026f : 0.03f;
            var r = go.GetComponent<MeshRenderer>();
            if (kitMaterial) r.sharedMaterial = kitMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            cables.Add(c);
        }

        Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n && t.parent && t.parent.name.StartsWith("BS01Kit_")) return t;
            return null;
        }

        static Transform Socket(Transform frame, string n, Vector3 local)
        {
            var s = new GameObject(n).transform;
            s.SetParent(frame, false); s.localPosition = local; s.localRotation = Quaternion.identity;
            return s;
        }

        /// <summary>The weapon's socket frame: the parent of its Muzzle (grip at the origin, barrel -Z).</summary>
        static Transform FindWeaponFrame(Transform model)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("BS01_Weapon_"))
                {
                    foreach (var m in t.GetComponentsInChildren<Transform>(true)) if (m.name == "Muzzle") return m.parent;
                    return t;
                }
            return null;
        }

        static SkinnedMeshRenderer FindBody(Transform model)
        {
            SkinnedMeshRenderer best = null;
            foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s.name.StartsWith("BS01_Body")) return s;
                if (!best || s.bones.Length > best.bones.Length) best = s;
            }
            return best;
        }

        /// <summary>Humanoid bone: Animator first, then the avatar's human map (edit mode / prefab stage), then mixamorig names.</summary>
        public static Transform Bone(Animator a, Transform model, HumanBodyBones hb)
        {
            Transform t = null;
            try { if (a && a.isHuman) t = a.GetBoneTransform(hb); } catch (Exception) { }
            if (t) return t;
            string boneName = null;
            if (a && a.avatar && a.avatar.isHuman)
            {
                string human = HumanTrait.BoneName[(int)hb];
                foreach (var h in a.avatar.humanDescription.human) if (h.humanName == human) { boneName = h.boneName; break; }
            }
            if (boneName == null) Mixamo.TryGetValue(hb, out boneName);
            if (boneName == null) return null;
            foreach (var c in model.GetComponentsInChildren<Transform>(true)) if (c.name == boneName) return c;
            return null;
        }

        /// <summary>World matrix of the bone in the body's bind (rest) pose, whatever the rig is doing right now.</summary>
        static Matrix4x4 RestMatrix(SkinnedMeshRenderer body, Transform bone)
        {
            if (body && body.sharedMesh)
            {
                var bones = body.bones; var bp = body.sharedMesh.bindposes;
                int i = Array.IndexOf(bones, bone);
                if (i >= 0 && i < bp.Length) return body.transform.localToWorldMatrix * bp[i].inverse;
            }
            return bone.localToWorldMatrix;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // --------------------------------------------------------------------------------------------- run time
        void Start()
        {
            if (Application.isPlaying && built && type == BS01Type.Pyro && smokeSocket) smoke = MakeSmoke(smokeSocket);
        }

        void LateUpdate()
        {
            if (!built) return;
            if (!anim) anim = GetComponentInChildren<Animator>();
            bool live = anim && anim.isActiveAndEnabled;            // RobotRagdoll switches the Animator off
            bool s = false;
            if (live && anim.runtimeAnimatorController && anim.layerCount > 0)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                s = st.shortNameHash == SeatedDriver || st.shortNameHash == SeatedGunner;
                if (anim.IsInTransition(0))
                {
                    var nx = anim.GetNextAnimatorStateInfo(0);
                    s = nx.shortNameHash == SeatedDriver || nx.shortNameHash == SeatedGunner;
                }
            }
            if (s != seated || first)
            {
                seated = s; first = false;
                // forceRenderingOff, not enabled: BoardingView hides and restores whole units through Renderer.enabled
                foreach (var r in backParts) if (r) r.forceRenderingOff = seated;
            }
            foreach (var c in cables) if (c) c.Suspended = seated || !live;
            if (smoke)
            {
                var em = smoke.emission; em.enabled = live;
            }
        }

        static Material smokeMat;
        static ParticleSystem MakeSmoke(Transform at)
        {
            var go = new GameObject("BS01Kit_AntennaSmoke");
            go.transform.SetParent(at, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = true; m.playOnAwake = true; m.maxParticles = 14; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            m.startColor = new Color(0.30f, 0.29f, 0.28f, 0.5f); m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var em = ps.emission; em.rateOverTime = 5f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.01f;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); v.y = new ParticleSystem.MinMaxCurve(0.28f, 0.42f); v.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 3.6f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.25f, 0.24f, 0.23f), 0), new GradientColorKey(new Color(0.55f, 0.54f, 0.52f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = SmokeMaterial(); pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Material SmokeMaterial()
        {
            if (smokeMat) return smokeMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            smokeMat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "BS01Kit_Smoke" };
            smokeMat.SetFloat("_Surface", 1); smokeMat.SetFloat("_Blend", 0);
            smokeMat.SetOverrideTag("RenderType", "Transparent"); smokeMat.renderQueue = 3000;
            smokeMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); smokeMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smokeMat.SetFloat("_ZWrite", 0); smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x + 0.5f) / 16f - 1, dy = (y + 0.5f) / 16f - 1, a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * 32 + x] = new Color32(255, 255, 255, (byte)(255 * a * a * (3 - 2 * a)));
                }
            tex.SetPixels32(px); tex.Apply();
            smokeMat.SetTexture("_BaseMap", tex);
            return smokeMat;
        }
    }
}

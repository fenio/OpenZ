using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// the locomotion on every bone under mixamorigSpine. Here: an "UpperBody" override layer with a
    /// Spine-and-up avatar mask on Robot.controller, holding aim_hold (the shooting clip's first frame = rifle shouldered
    /// and level), shooting, the bazooka clips and the grenade throw over the walk/run legs. UnitView drives its weight.
    /// </summary>
    public static class OpenZRobotAnim
    {
        const string Dir = "Assets/OpenZ/Animation", Ctl = Dir + "/Robot.controller", Spine = "SkinRoot/Armature/mixamorigHips/mixamorigSpine";

        [MenuItem("OpenZ/Robot Upper Body Layer")]
        public static string Build()
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            var shooting = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "/Robot/shooting.anim");
            if (!ctl || !shooting) return "missing controller or shooting clip";

            // 1. aim_hold: every upper-body curve of "shooting" frozen at t = 0
            string holdPath = Dir + "/Robot/aim_hold.anim";
            var hold = new AnimationClip { name = "aim_hold" };
            foreach (var b in AnimationUtility.GetCurveBindings(shooting))
            {
                if (!b.path.StartsWith(Spine)) continue;
                float v = AnimationUtility.GetEditorCurve(shooting, b).Evaluate(0f);
                AnimationUtility.SetEditorCurve(hold, b, AnimationCurve.Constant(0f, 1f, v));
            }
            var st = AnimationUtility.GetAnimationClipSettings(hold); st.loopTime = true; AnimationUtility.SetAnimationClipSettings(hold, st);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(holdPath)) AssetDatabase.DeleteAsset(holdPath);
            AssetDatabase.CreateAsset(hold, holdPath);

            // 2. avatar mask: the spine chain and everything under it (arms, head, weapon sockets)
            var paths = AnimationUtility.GetCurveBindings(shooting).Select(b => b.path).Distinct().ToList();
            var mask = new AvatarMask { name = "RobotUpperBody" };
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) { mask.SetTransformPath(i, paths[i]); mask.SetTransformActive(i, paths[i].StartsWith(Spine)); }
            string maskPath = Dir + "/RobotUpperBody.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath)) AssetDatabase.DeleteAsset(maskPath);
            AssetDatabase.CreateAsset(mask, maskPath);

            // 3. the layer (rebuilt if present)
            var layers = ctl.layers.ToList();
            int old = layers.FindIndex(l => l.name == "UpperBody");
            if (old >= 0) ctl.RemoveLayer(old);
            var sm = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, ctl);
            var empty = sm.AddState("up_empty"); sm.defaultState = empty;
            sm.AddState("aim_hold").motion = hold;
            foreach (var n in new[] { "shooting", "bazooka_idle", "bazooka_fire", "grenade_throw" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Dir}/Robot/{n}.anim");
                if (c) sm.AddState("up_" + n).motion = c;
            }
            ctl.AddLayer(new AnimatorControllerLayer { name = "UpperBody", stateMachine = sm, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f });
            EditorUtility.SetDirty(ctl); AssetDatabase.SaveAssets();
            return $"UpperBody layer: {paths.Count(p => p.StartsWith(Spine))}/{paths.Count} masked transforms, states {string.Join(",", sm.states.Select(s => s.state.name))}";
        }
    }
}

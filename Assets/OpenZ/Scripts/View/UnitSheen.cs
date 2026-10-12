using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Global for every unit-look material (TeamLit _UnitLook: robots, vehicles, cannons, weapons): _OZSheen = (highlight cap gain,
    /// paint smoothness add, bare-metal smoothness add, day environment reflection). The reflection is fresnel-free (no rim) and
    /// fades out at night (TeamLit x (1 - _OZNight)). PlayerPrefs oz_sheen 0 = the old matte look.
    /// </summary>
    public static class UnitSheen
    {
        public static Vector4 Value = new Vector4(2.6f, 0.15f, 0.32f, 0.08f);
        static readonly int Id = Shader.PropertyToID("_OZSheen");

        public static void Apply(bool on) => Shader.SetGlobalVector(Id, on ? Value : new Vector4(1f, 0f, 0f, 0f));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { bool on = true; try { on = PlayerPrefs.GetInt("oz_sheen", 1) == 1; } catch { } Apply(on); }
    }
}

using System.Collections;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K248b: the sound of a headshot (DV116; its view: UnitView.Headshot.cs). Before, the head played the same three takes as every
    /// armour-hit ping (v3-armor-ricochet-a / impact-1 / impact-2), at the level of the hit ricochet that played in the same frame and
    /// under the death clang, the sniper's crack and the call: never missing, never heard as a head popping off.
    /// <list type="bullet">
    /// <item>its own sound: the event <see cref="HeadPopEvent"/> (three takes of a metal head popping off, sd_head_pop_a-c), on the
    /// impacts bus about 6 dB over the loudest other sound of that moment (the sound table's row);</item>
    /// <item>the killing round's own ricochet is not played on a headshot victim (<see cref="HeadshotRound"/>, WeaponsV2Runner), so it
    /// does not mask the pop;</item>
    /// <item>the call ("Headshot!" etc.) comes <see cref="HeadCallDelay"/> s after the pop, so the pop is heard first;</item>
    /// <item>the loose head's landing clatter (the rocket-fuel can's) is <see cref="HeadClatterDb"/> dB louder than a can's.</item>
    /// </list>
    /// Counter <see cref="HeadPops"/> (one per headshot death drawn; the Play check compares it with HeadshotDeaths).
    /// </summary>
    public partial class UnitView
    {
        public const string HeadPopEvent = "dst_head_pop";
        public static float HeadCallDelay = 0.3f, HeadClatterDb = 4f;
        public static int HeadPops;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HeadPopStatics() { HeadPops = 0; }

        /// <summary>UnitView.Headshot.cs, the head comes off at <paramref name="at"/>: its pop.</summary>
        void HeadPop(Vector3 at)
        {
            HeadPops++;
            AudioDirector.Fire(HeadPopEvent, at, Obj != null ? Obj.Id : -1);
        }

        /// <summary>UnitView.Headshot.cs: the loose head (the can's physics) clatters louder than a can. True = it was thrown.</summary>
        static bool HeadThrown(AnticCan can)
        {
            if (!can) return false;
            can.clatterGain = Mathf.Pow(10f, HeadClatterDb / 20f);
            return true;
        }

        /// <summary>UnitView.Headshot.cs: the call, a moment after the pop (on CombatAudio, which outlives the dead robot's view).</summary>
        static void HeadCallLater(string pool)
        {
            if (HeadCallDelay > 0f && CombatAudio.I && CombatAudio.I.isActiveAndEnabled) CombatAudio.I.StartCoroutine(SayAfter(pool, HeadCallDelay));
            else AudioDirector.Say(pool);
        }

        static IEnumerator SayAfter(string pool, float delay)
        {
            yield return new WaitForSeconds(delay);
            AudioDirector.Say(pool);
        }

        /// <summary>WeaponsV2Runner (the sniper round's impact): this round is the headshot that killed <paramref name="targetId"/>
        /// (its own ricochet stays silent, the head pop is that moment's sound).</summary>
        public static bool HeadshotRound(int targetId, int shooterId)
        {
            var w = ZGame.I ? ZGame.I.World : null;
            var t = w != null && targetId >= 0 ? w.GetAny(targetId) : null;
            return t != null && t.Kind == ZKind.Robot && t.HeadshotBy >= 0 && t.HeadshotBy == shooterId;
        }
    }
}

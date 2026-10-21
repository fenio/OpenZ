using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the 15 posts stand in the scene as lamp props (DEC_Service_lamp_L16_&lt;id&gt;, the service lamp of level 2), so the night
    /// lights find them by name like every lamp prop: warm spot at dusk, out at dawn, knocked over by a vehicle or a blast with
    /// their sounds. The one thing a prop does not get from the night lights is the small obstacle robots step round (only posts
    /// added from the level's list get it): this component gives each of the level's posts that same obstacle, once, at the start.
    /// View only. Play check: Registered.
    /// </summary>
    public sealed class L16LampPosts : MonoBehaviour
    {
        public const string NamePart = "_L16_";
        public static int Registered { get; private set; }

        void Start()
        {
            Registered = 0;
            var nl = NightLights.I; if (!nl) return;
            foreach (var f in nl.Fixtures)
            {
                if (f == null || !f.IsLamp || f.ObstacleIndex >= 0 || !f.LampGO || f.LampGO.name.IndexOf(NamePart, System.StringComparison.Ordinal) < 0) continue;
                f.ObstacleIndex = UnitView.Obstacles.Count; UnitView.Obstacles.Add(new Vector3(f.Foot.x, f.Foot.z, 0.35f));
                Registered++;
            }
            Debug.Log($"[OpenZ] L16 lamp posts: {Registered} posts are obstacles for robots (night lights: {nl.FixtureCount} fixtures)");
        }
    }
}

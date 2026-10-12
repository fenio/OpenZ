using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// vehicle / gun's crew is exposed (sim CanBeSniped: jeep driver, crewed open gun, tank with its lid open), every drawn small-arms
    /// round (grunt, psycho, sniper, jeep MG, gatling, APC MG) flies at the crew figure's chest and lands there (tracer + sparks on the
    /// crew), with no hull / bounds test in between, from any side; misses fly past the crew. Heavy weapons keep hitting the vehicle.
    /// The sim decides the damage (Zod's snipe roll per hit, DV2 driver hits, DV27 exposed-driver targeting).</summary>
    public static class CrewAim
    {
        public static bool Enabled = true;
        /// <summary>Proof counters: drawn small-arms rounds aimed at an exposed crew, and the last impact's distance to the chest.</summary>
        public static int Shots, Hits, Misses; public static float LastErr;
        static readonly HashSet<string> Small = new HashSet<string> { "grunt", "psycho", "sniper", "jeep", "gatling" };

        public static bool SmallArms(string type) => type != null && Small.Contains(type);

        /// <summary>The exposed crew's chest of `t` (the visible figure, else the seat / hatch); false when its crew is not exposed.</summary>
        public static bool Point(ZObject t, out Vector3 p)
        {
            p = default;
            if (!Enabled || t == null || t.Destroyed || t.Kind == ZKind.Robot || t.Kind == ZKind.MapItem || t.Kind == ZKind.Building || !t.CanBeSniped()) return false;
            if (BoardingView.TryCrewChest(t.Id, out p)) return true;
            var g = ZGame.I; if (g == null) return false;
            p = g.Views.TryGetValue(t.Id, out var v) && v ? v.transform.position : g.CenterWorld(t);
            p += Vector3.up * (t.HasLid ? 2.1f : t.Kind == ZKind.Cannon ? 1.5f : 1.35f);
            return true;
        }

        /// <summary>Where one drawn small-arms round from `from` ends: on the chest (hit) or past the crew into the ground (miss).</summary>
        public static Vector3 Impact(Vector3 from, Vector3 chest, bool hit)
        {
            Shots++;
            if (hit) { Hits++; var to = chest + Random.insideUnitSphere * 0.12f; LastErr = Vector3.Distance(to, chest); return to; }
            Misses++;
            var d = chest - from; d.y = 0f; var side = d.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, d.normalized) : Vector3.right;
            return HitSurface.MissGround(from, chest + side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.35f, 0.9f) + Vector3.up * Random.Range(-0.2f, 0.35f));
        }
    }
}

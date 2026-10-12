using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    // ------------------------------------------------------------------------------------------------ data (JSON)

    /// <summary>One light of a building rig. p = fractions of the building model's local bounds (x min->max, y bottom->top,
    /// z back->front, the FactoryView frame); yaw 0 = +z (door side), pitch = degrees below horizontal (negative = up).</summary>
    [Serializable]
    public class NightElementSpec
    {
        public string id, kind, when, pattern, housing;
        public float[] p, pm, color;
        public float yaw, pitch, angle, range, intensity, priority, beam, pool, sweep, period, teamTint, onDelay, arm;
        public bool shadow, track;
    }

    [Serializable] public class NightRigSpec { public string key; public float[] boundsMin, boundsMax; public NightElementSpec[] elements; }

    [Serializable]
    public class NightLampType
    {
        public string key; public float[] color; public string[] glowNames;
        public float intensity, range, angle, pitch, cone, coneLen, coneAngle, pool, priority, flickerShare;
    }

    [Serializable] public class NightLevelSpec { public string level, lampTemplate; public float haze = 1f, fogLampRange = 10f; public bool streets; }

    [Serializable]
    public class NightRigsFile
    {
        public int version; public string note;
        public NightRigSpec[] rigs; public NightLampType[] lampTypes; public NightLevelSpec[] levels;
    }

    [Serializable] public class NightLampPlacement { public string id, kind; public float x, z, yaw; }
    [Serializable] public class NightExistingLamp { public string name, type; public float x, z; }
    [Serializable] public class NightLevelFile { public string level, note; public NightExistingLamp[] existing; public NightLampPlacement[] lamps; }

    // ------------------------------------------------------------------------------------------------ run-time objects
    public enum NightKind { Searchlight, Flood, Uplight, WorkBeacon, WorkLight, Weld, Lamp }

    /// <summary>The lights of one building (fort, factory, radar, repair station): a rig root at the building's position and
    /// yaw under the NightLights object (never inside the building, so TeamPaint / BuildingBreaker never touch it).</summary>
    public sealed class NightRig
    {
        public ZObject B; public string Key; public Transform Root; public Bounds MB;
        public readonly List<NightFixture> Fixtures = new List<NightFixture>();
        public Team LastOwner; public bool Shown = true, WasDestroyed; public float Blackout;
    }

    /// <summary>One light fixture: its real Light (the budget enables it), emissive head / lens / bulb, halo, beam cone and
    /// projected pool impostor, plus the state (switch level, flicker, aim, damage).</summary>
    public sealed class NightFixture
    {
        public NightKind Kind; public string Id; public NightRig Rig; public NightElementSpec Spec; public NightLampType LampType;
        public Transform Root, Yaw, Pitch, Head;              // Pitch (or Head) forward = light direction
        public Light Light;
        public Renderer Beam, Lens, Halo, Pool, Bulb, Led;
        public Transform BeamT, PoolT;
        public Renderer[] Emissive; public Color[] EmissiveBase;
        public Color Col, ColLinear; public float Intensity, Range, Angle, Priority; public bool ShadowCandidate;
        public float BeamK, PoolK, HaloR = 0.6f;
        public float OnAt = 0.34f, OffAt = 0.9f;
        public string When = "owned";
        public float Level, Seed; public bool Faulty;
        public float Out, LightOut, OutShown = -1f, PoolShown = -1f, BeamShown = -1f, HaloShown = -1f, LedShown = -1f;
        public float StaticPoolE;                             // light at the pool centre (static fixtures)
        // searchlight aim
        public float YawNow, PitchNow, PitchRest, Sweep, Period; public int Pattern; public bool Track;
        public ZObject Target; public float TargetUntil, TrackCooldown, NextTrackScan, ScanNext, ScanT0; public Vector2 ScanFrom, ScanTo;
        public float BeamLen = 10f, WeldNext, BulbShown = -1f, LensShown = -1f; public Vector3 PoolCentre; public float PoolW = 4f, PoolL = 4f; public Quaternion PoolRot = Quaternion.identity;
        // lamps (existing or new street posts)
        public int ObstacleIndex = -1;                                                  // UnitView.Obstacles slot (lamp posts)
        public GameObject LampGO; public Vector3 Foot, Pos0, HeadLocal; public Quaternion Rot0, HeadLocalRot = Quaternion.identity;
        public float Tilt, TiltVel, TiltTarget; public Vector3 TiltAxis = Vector3.right; public int Damage; public bool Falling, Landed, GlassBroken;
        public float FlickerUntil;
        // budget
        public float Score; public bool Budgeted, Shadowed, Visible; public float RealK;
        public float ShadowK, ShadowUntil, ShadowScore; public bool ShadowHigh;

        public bool IsLamp => Kind == NightKind.Lamp;
        public Vector3 LightPos => Light ? Light.transform.position : Root ? Root.position : Vector3.zero;
    }
}

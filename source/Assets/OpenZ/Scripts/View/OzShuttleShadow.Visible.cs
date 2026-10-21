using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The mark was drawn on every frame in the air, but at its cruise strength (0.0585) it darkened the ground by only
    /// 3 .. 4 %, under the grass's own pattern, so it could not be seen. Now it is a soft dark disc that reads on grass, sand, roads and
    /// water, and is still not a real shadow (much softer than the sun shadows of trees and buildings, no hard edge, no ring):
    /// <list type="bullet">
    /// <item>By day its core is about 18 % darker than the ground at cruise speed (<see cref="OzShuttleShadow.MarkDay"/> x
    /// <see cref="OzShuttleShadow.MarkFastK"/>), 20 % hovering or slow (MarkDay) and 22 % near a landing or while a landing spot is
    /// aimed (<see cref="OzShuttleShadow.MarkNear"/>).</item>
    /// <item>While this ship is selected it is about 5 % darker still (<see cref="SelectedAdd"/>), eased in and out over
    /// <see cref="SelectEaseSec"/> (smoothstep), then through the mark's own ease: never a jump.</item>
    /// <item>At night the same share of the ground's light (<see cref="OzShuttleShadow.MarkNightK"/> 1; the tint is dimmed as before,
    /// so it never lightens the dark ground).</item>
    /// <item>The shape: fully dark out to <see cref="CoreFar"/> of its radius at cruise height (0.5 near the ground), then a smooth fall to
    /// nothing at the rim: a disc the size of the hull, soft all round.</item>
    /// <item>The red "cannot land" mark is never lighter than the dark one (it turns red, it never fades out).</item>
    /// </list>
    /// Where it lies, how it glides, the cross-fades and the red's timing are unchanged (OzShuttleShadow.cs).
    /// </summary>
    public sealed partial class OzShuttleShadow
    {
        /// <summary>The strength added while this ship is selected (before the night factor); its ease (s); the share of the radius that
        /// stays fully dark at cruise height (12 m and up; 0.5 at the ground).</summary>
        public static float SelectedAdd = 0.07f, SelectEaseSec = 0.25f, CoreFar = 0.4f;
        /// <summary>Proof: frames drawn while selected.</summary>
        public static int SelectedFrames;
        /// <summary>This ship is selected now; the eased selection share (0 .. 1).</summary>
        public bool Selected { get; private set; }
        public float SelectedNow { get; private set; }

        /// <summary>One frame: is this ship selected (not while it is shot down), eased; returns the smoothstepped share.</summary>
        float SelectedStep(ZObject o, float dt)
        {
            var inp = FindInput();
            bool sel = o != null && !downMode && inp && inp.Selection.Contains(o);
            Selected = sel;
            SelectedNow = Mathf.MoveTowards(SelectedNow, sel ? 1f : 0f, dt / Mathf.Max(0.02f, SelectEaseSec));
            if (sel) SelectedFrames++;
            float k = SelectedNow;
            return k * k * (3f - 2f * k);
        }

        /// <summary>The span the mark's ease covers in <see cref="MarkEaseSec"/>: from the fastest cruise to near a landing while selected.</summary>
        static float MarkEaseSpan() => Mathf.Max(0.05f, MarkNear + SelectedAdd - MarkDay * MarkFastK);

        /// <summary>The strength the rules ask for (before the ease and the cross-fade): day / near / fast, selected, night (the count check).</summary>
        public static float WantFor(float nearK, float fastK, float selectedK, float night) =>
            (Mathf.Lerp(MarkDay, MarkNear, nearK) * Mathf.Lerp(1f, MarkFastK, fastK) + SelectedAdd * selectedK) * Mathf.Lerp(1f, MarkNightK, night);
    }
}

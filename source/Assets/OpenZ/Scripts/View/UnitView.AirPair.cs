using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// The view's spacing and the flying shuttle (view only). The rules leave a shuttle up in the air out of the ground traffic
    /// (ZPathing.Separable: no spacing, no push), and the view's spacing now does the same. Its unit view (a radius of up to 3 m from
    /// its hull) pushed every unit under its flight path aside on the screen whatever its height: the two heroes lying on their
    /// loungers 12 m under it were slid off their spots, their drawn walk started and the antic director cancelled the lie as
    /// "moving". A landed shuttle is in the view's spacing as before.
    /// </summary>
    public partial class UnitView
    {
        /// <summary>Proof: close pairs left alone because one of them is a shuttle up in the air.</summary>
        public static int AirPairsSkipped;

        static bool AirPair(ZObject a, ZObject b)
        {
            bool air = (a != null && a.IsShuttle && a.ShuttleAirborne) || (b != null && b.IsShuttle && b.ShuttleAirborne);
            if (air) AirPairsSkipped++;
            return air;
        }
    }
}

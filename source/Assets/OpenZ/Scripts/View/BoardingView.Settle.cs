namespace OpenZ
{
    /// <summary>
    /// K418 P5 (DV126, view only): every robot saved from a destroyed shuttle's ramp file ends on its rules place. A saved figure that walks
    /// on to its robot (BoardingView.ShuttleDoorway.DoorWalk) and a saved robot that had no figure left to walk (its exit walk already
    /// over) both get <see cref="UnitView.SettleOnSimSpot"/>: the view's spacing offset steps back onto the place the rules gave it.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>How long a saved robot's drawn offset may step back while it stands (s).</summary>
        public static float DoorwaySettleSec = 5f;
        /// <summary>Proof: saved robots told to settle.</summary>
        public static int DoorwaySettles;

        void DoorwaySettle(UnitView live)
        {
            if (!live) return;
            live.SettleOnSimSpot(DoorwaySettleSec); DoorwaySettles++;
        }
    }
}

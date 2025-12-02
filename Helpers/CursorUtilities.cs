using GridlockDesigner.Models;
using System.Windows.Input;

namespace GridlockDesigner.Helpers;

public static class CursorUtilities
{
    public static Cursor GetVehicleCursor(Vehicle vehicle)
    {
        if (vehicle.Orientation == 'H')
        {
            if (vehicle.Length == 2)
                return Cursors.SizeWE;
            else
                return Cursors.SizeWE;
        }
        else
        {
            if (vehicle.Length == 2)
                return Cursors.SizeNS;
            else
                return Cursors.SizeNS;
        }
    }

    public static void SetVehicleCursor(Vehicle vehicle)
    {
        Mouse.OverrideCursor = (vehicle.Orientation == 'H' ? Cursors.SizeWE : Cursors.SizeNS);

        // Special cursor for ambulance
        if (vehicle.Color == "Red")
        {
            Mouse.OverrideCursor = Cursors.Hand; // Hand cursor for ambulance
        }
    }
}

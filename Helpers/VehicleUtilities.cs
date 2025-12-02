using GridlockDesigner.Models;

namespace GridlockDesigner.Helpers;

public static class VehicleUtilities
{
    public static bool CanPlaceVehicle(
        int row,
        int col,
        char orientation,
        int length,
        IEnumerable<Vehicle> allVehicles,  // Pass vehicles as parameter
        Vehicle? excludedVehicle = null)
    {
        var tempVehicle = new Vehicle { Row = row, Col = col, Orientation = orientation, Length = length };
        var occupiedCells = tempVehicle.GetOccupiedCells();

        foreach (var (r, c) in occupiedCells)
        {
            // Check bounds
            if (r < 0 || r >= 6 || c < 0 || c >= 6)
                return false;

            // Filter vehicles to check (excluding the specified one)
            var vehiclesToCheck = allVehicles;
            if (excludedVehicle != null)
            {
                vehiclesToCheck = vehiclesToCheck.Where(v => v != excludedVehicle);
            }

            // Check for collisions
            if (vehiclesToCheck.Any(v => v.GetOccupiedCells().Any(cell => cell.row == r && cell.col == c)))
                return false;
        }

        return true;
    }

    private static List<(int row, int col)> GetOccupiedCells(this Vehicle vehicle)
    {
        var cells = new List<(int, int)>();
        if (vehicle.Orientation == 'H')
        {
            for (int i = 0; i < vehicle.Length; i++)
                cells.Add((vehicle.Row, vehicle.Col + i));
        }
        else
        {
            for (int i = 0; i < vehicle.Length; i++)
                cells.Add((vehicle.Row + i, vehicle.Col));
        }
        return cells;
    }
}

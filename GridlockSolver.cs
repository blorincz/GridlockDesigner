namespace GridlockDesigner;

using GridlockDesigner.Models;
using System.Collections.Generic;
using System.Linq;

public class GridlockSolver
{
    private class SolverVehicle
    {
        public string Id { get; set; } = string.Empty;
        public char Orientation { get; set; }
        public int Length { get; set; }
        public int Row { get; set; }
        public int Col { get; set; }

        public List<(int row, int col)> GetOccupiedCells()
        {
            var cells = new List<(int, int)>();
            if (Orientation == 'H')
            {
                for (int i = 0; i < Length; i++)
                    cells.Add((Row, Col + i));
            }
            else
            {
                for (int i = 0; i < Length; i++)
                    cells.Add((Row + i, Col));
            }
            return cells;
        }

        public SolverVehicle Clone() => new SolverVehicle
        {
            Id = Id,
            Orientation = Orientation,
            Length = Length,
            Row = Row,
            Col = Col
        };
    }

    private class SolverBoardState
    {
        public Dictionary<string, SolverVehicle> Vehicles { get; set; } = new Dictionary<string, SolverVehicle>();
        public string AmbulanceId { get; set; } = string.Empty;

        public string?[,] GetGrid()
        {
            var grid = new string?[6, 6];
            foreach (var vehicle in Vehicles.Values)
            {
                foreach (var (row, col) in vehicle.GetOccupiedCells())
                {
                    if (row >= 0 && row < 6 && col >= 0 && col < 6)
                        grid[row, col] = vehicle.Id;
                }
            }
            return grid;
        }

        public bool IsGoal() => Vehicles.ContainsKey(AmbulanceId) &&
            Vehicles[AmbulanceId].GetOccupiedCells().Any(c => c.col == 5);

        public string GetHash()
        {
            var positions = Vehicles.Keys.Order().Select(vid => $"{vid}:{Vehicles[vid].Row},{Vehicles[vid].Col}");
            return string.Join("|", positions);
        }

        public SolverBoardState Clone()
        {
            var newState = new SolverBoardState { AmbulanceId = AmbulanceId };
            foreach (var kvp in Vehicles)
                newState.Vehicles[kvp.Key] = kvp.Value.Clone();
            return newState;
        }
    }

    public static List<Move>? Solve(List<Vehicle> vehicles, string ambulanceId)
    {
        var initialState = new SolverBoardState { AmbulanceId = ambulanceId };
        foreach (var v in vehicles)
        {
            initialState.Vehicles[v.Id] = new SolverVehicle
            {
                Id = v.Id,
                Orientation = v.Orientation,
                Length = v.Length,
                Row = v.Row,
                Col = v.Col
            };
        }

        var queue = new Queue<(SolverBoardState state, List<Move> path)>();
        var visited = new HashSet<string>();

        queue.Enqueue((initialState, new List<Move>()));
        visited.Add(initialState.GetHash());

        while (queue.Count > 0)
        {
            var (currentState, path) = queue.Dequeue();

            if (currentState.IsGoal())
                return CompressMoves(path);

            foreach (var move in GetPossibleMoves(currentState))
            {
                var newState = ApplyMove(currentState.Clone(), move);
                var stateHash = newState.GetHash();

                if (!visited.Contains(stateHash))
                {
                    visited.Add(stateHash);
                    var newPath = new List<Move>(path) { move };
                    queue.Enqueue((newState, newPath));
                }
            }
        }

        return null;
    }

    private static List<Move> CompressMoves(List<Move> moves)
    {
        if (moves.Count == 0) return moves;

        var compressed = new List<Move>();
        var currentMove = moves[0];

        for (int i = 1; i < moves.Count; i++)
        {
            var nextMove = moves[i];
            if (currentMove.VehicleId == nextMove.VehicleId && currentMove.Direction == nextMove.Direction)
            {
                currentMove = new Move { VehicleId = currentMove.VehicleId, Direction = currentMove.Direction, Spaces = currentMove.Spaces + 1 };
            }
            else
            {
                compressed.Add(currentMove);
                currentMove = nextMove;
            }
        }

        compressed.Add(currentMove);
        return compressed;
    }

    private static List<Move> GetPossibleMoves(SolverBoardState state)
    {
        var moves = new List<Move>();
        var grid = state.GetGrid();

        foreach (var vehicle in state.Vehicles.Values)
        {
            if (vehicle.Orientation == 'H')
            {
                // Left moves
                for (int spaces = 1; spaces <= 5; spaces++)
                {
                    var newCol = vehicle.Col - spaces;
                    if (newCol >= 0 && grid[vehicle.Row, newCol] == null)
                        moves.Add(new Move { VehicleId = vehicle.Id, Direction = "left", Spaces = spaces });
                    else
                        break;
                }
                // Right moves
                for (int spaces = 1; spaces <= 5; spaces++)
                {
                    var rightCol = vehicle.Col + vehicle.Length - 1 + spaces;
                    if (rightCol < 6 && grid[vehicle.Row, rightCol] == null)
                        moves.Add(new Move { VehicleId = vehicle.Id, Direction = "right", Spaces = spaces });
                    else
                        break;
                }
            }
            else
            {
                // Up moves
                for (int spaces = 1; spaces <= 5; spaces++)
                {
                    var newRow = vehicle.Row - spaces;
                    if (newRow >= 0 && grid[newRow, vehicle.Col] == null)
                        moves.Add(new Move { VehicleId = vehicle.Id, Direction = "up", Spaces = spaces });
                    else
                        break;
                }
                // Down moves
                for (int spaces = 1; spaces <= 5; spaces++)
                {
                    var bottomRow = vehicle.Row + vehicle.Length - 1 + spaces;
                    if (bottomRow < 6 && grid[bottomRow, vehicle.Col] == null)
                        moves.Add(new Move { VehicleId = vehicle.Id, Direction = "down", Spaces = spaces });
                    else
                        break;
                }
            }
        }

        return moves;
    }

    private static SolverBoardState ApplyMove(SolverBoardState state, Move move)
    {
        var vehicle = state.Vehicles[move.VehicleId];
        var directionVector = GetDirectionVector(move.Direction, vehicle.Orientation);

        if (vehicle.Orientation == 'H')
            vehicle.Col += directionVector * move.Spaces;
        else
            vehicle.Row += directionVector * move.Spaces;

        return state;
    }

    private static int GetDirectionVector(string direction, char orientation) => direction switch
    {
        "left" when orientation == 'H' => -1,
        "right" when orientation == 'H' => 1,
        "up" when orientation == 'V' => -1,
        "down" when orientation == 'V' => 1,
        _ => throw new ArgumentException($"Invalid direction/orientation combination")
    };
}

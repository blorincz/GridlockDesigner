// MainWindow.xaml.cs
using GridlockDesigner.Data;
using GridlockDesigner.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GridlockDesigner;

public partial class MainWindow : Window
{
    private readonly AppDbContext _dbContext;
    private readonly GridlockSolver _solver;
    private List<Vehicle> _vehicles = [];
    private List<Move>? _currentSolution;
    private int _currentMoveIndex = -1;
    private string _selectedVehicleType = "";
    private Vehicle? _draggedVehicle;

    private static readonly string[] C64Colors = [
        "Black", "White", "Red", "Cyan", "Purple", "Green", "Blue", "Yellow",
        "Orange", "Brown", "LightRed", "DarkGray", "Gray", "LightGreen", "LightBlue", "LightGray"
    ];

    public MainWindow()
    {
        InitializeComponent();

        this.DragEnter += MainWindow_DragEnter;
        this.PreviewDragOver += MainWindow_PreviewDragOver;
        this.QueryCursor += MainWindow_QueryCursor;

        _dbContext = new AppDbContext();

        try
        {
            // This creates the database and tables automatically
            _dbContext.InitializeDatabase();

            // Optional: Add sample data if database is empty
            if (!_dbContext.BoardStates.Any())
            {
                var sampleLevel = new BoardState
                {
                    Title = "Sample Level",
                    CreatedDate = DateTime.Now,
                    Vehicles = new List<Vehicle>
                {
                    new() { Color = "Red", Orientation = 'H', Length = 2, Row = 2, Col = 0 },
                    new() { Color = "Yellow", Orientation = 'H', Length = 3, Row = 0, Col = 0 }
                }
                };
                _dbContext.BoardStates.Add(sampleLevel);
                _dbContext.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Database initialization failed: {ex.Message}\n\nTry using SQLite instead?", "Database Error");
            // Consider falling back to SQLite or file-based storage
        }

        _solver = new GridlockSolver();
        InitializeBoard();
        LoadSavedPuzzles();
    }

    private void InitializeBoard()
    {
        RefreshBoardDisplay();
    }

    private void MainWindow_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        // If we're dragging a vehicle, set the cursor for the entire window
        if (_draggedVehicle != null)
        {
            e.Cursor = GetVehicleCursor(_draggedVehicle);
            e.Handled = true;
        }
    }

    private Cursor GetVehicleCursor(Vehicle vehicle)
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

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        // If we're dragging a vehicle, set the cursor globally
        if (_draggedVehicle != null)
        {
            SetVehicleCursor(_draggedVehicle);
        }
    }

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        // This handler will show where the vehicle would be placed
        if (e.Data.GetData(typeof(Vehicle)) is Vehicle vehicle)
        {
            var position = e.GetPosition(this);

            // You could draw a preview here, but for now we'll just use the cursor
            // The cursor change already gives feedback that dragging is active
        }
    }

    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        // Change cursor to indicate copy operation
        Mouse.OverrideCursor = Cursors.Cross;
    }

    private void MainWindow_DragLeave(object sender, DragEventArgs e)
    {
        // Don't reset cursor here - it will be reset when drop completes
    }

    private void VehicleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            _selectedVehicleType = tag;
        }
    }

    private void BoardGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element &&
            element.DataContext is string cellText)
        {
            int index = BoardGrid.Items.IndexOf(cellText);
            if (index >= 0)
            {
                int row = index / 6;
                int col = index % 6;
                HandleCellClick(row, col);
            }
        }
    }

    private void HandleCellClick(int row, int col)
    {
        if (string.IsNullOrEmpty(_selectedVehicleType)) return;

        var parts = _selectedVehicleType.Split(',');
        char orientation = parts[0][0];
        int length = int.Parse(parts[1]);
        bool isAmbulance = _selectedVehicleType == "H,2" && _vehicles.All(v => v.Color != "Red" || v.Orientation != 'H');

        // Check if ambulance is on row 3 and col < 5
        if (isAmbulance && (row != 2 || col > 3))
        {
            MessageBox.Show("Ambulance must be on row 3 (C) and column less than 5");
            return;
        }

        // Check if placement is valid
        if (!CanPlaceVehicle(row, col, orientation, length))
        {
            MessageBox.Show("Cannot place vehicle here - overlaps with existing vehicle or out of bounds");
            return;
        }

        var vehicle = new Vehicle
        {
            Orientation = orientation,
            Length = length,
            Row = row,
            Col = col,
            Color = isAmbulance ? "Red" : C64Colors[2] // Start with Red for ambulance, Cyan for others
        };

        _vehicles.Add(vehicle);
        RefreshBoardDisplay();
    }

    private bool CanPlaceVehicle(int row, int col, char orientation, int length)
    {
        var tempVehicle = new Vehicle { Row = row, Col = col, Orientation = orientation, Length = length };
        var occupiedCells = GetOccupiedCells(tempVehicle);

        foreach (var (r, c) in occupiedCells)
        {
            if (r < 0 || r >= 6 || c < 0 || c >= 6) return false;
            if (_vehicles.Any(v => GetOccupiedCells(v).Any(cell => cell.row == r && cell.col == c))) return false;
        }

        return true;
    }

    private List<(int row, int col)> GetOccupiedCells(Vehicle vehicle)
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

    private void RefreshBoardDisplay()
    {
        var cells = new List<CellViewModel>();

        // Create all cells
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 6; col++)
            {
                cells.Add(new CellViewModel
                {
                    Row = row,
                    Col = col,
                    IsExit = (row == 2 && col == 5) // C6 is the exit
                });
            }
        }

        // Clear previous vehicle assignments
        foreach (var cell in cells)
        {
            cell.VehicleId = string.Empty;
            cell.Color = "Transparent";
            cell.Content = "";
            cell.RotateText = false;
        }

        // Mark occupied cells with vehicle colors
        foreach (var vehicle in _vehicles)
        {
            var occupiedCells = GetOccupiedCells(vehicle);
            var vehicleColor = GetVehicleColor(vehicle.Color);

            foreach (var (row, col) in occupiedCells)
            {
                var cell = cells.FirstOrDefault(c => c.Row == row && c.Col == col);
                if (cell != null)
                {
                    cell.VehicleId = vehicle.Id;
                    cell.Color = vehicleColor;

                    // Get display content and rotation
                    var (content, rotate) = GetVehicleDisplay(vehicle, row, col);
                    cell.Content = content;
                    cell.RotateText = rotate;
                }
            }
        }

        BoardGrid.ItemsSource = cells;
    }

    private static string GetVehicleColor(string colorName)
    {
        return colorName switch
        {
            "Red" => "#FF0000",
            "Cyan" => "#00FFFF",
            "Yellow" => "#FFFF00",
            "Purple" => "#800080",
            "Green" => "#38F546", // New light green
            "Blue" => "#0000FF",
            "Orange" => "#FFA500",
            "Brown" => "#A52A2A",
            "Black" => "#000000",
            "White" => "#FFFFFF",
            _ => "#CCCCCC"
        };
    }

    private (string content, bool rotate) GetVehicleDisplay(Vehicle vehicle, int row, int col)
    {
        // Only show text on the first cell of the vehicle
        bool isFirstCell = (vehicle.Orientation == 'H' && col == vehicle.Col) ||
                          (vehicle.Orientation == 'V' && row == vehicle.Row);

        if (isFirstCell)
        {
            string text = vehicle.Color == "Red" ? "Ambulance" :
                         (vehicle.Length == 3 ? "Truck" : "Car");

            // Return both content and whether it should be rotated
            return (text, vehicle.Orientation == 'V');
        }

        return ("", false);
    }

    private void BtnSolve_Click(object sender, RoutedEventArgs e)
    {
        var ambulance = _vehicles.FirstOrDefault(v => v.Color == "Red" && v.Orientation == 'H');
        if (ambulance == null)
        {
            MessageBox.Show("No ambulance found on the board");
            return;
        }

        _currentSolution = GridlockSolver.Solve(_vehicles, ambulance.Id);
        _currentMoveIndex = -1;

        if (_currentSolution == null)
        {
            txtSolutionInfo.Text = "No solution found for this puzzle.";
            lstSolution.ItemsSource = null;
            _solutionItems.Clear();
        }
        else
        {
            DisplaySolution(_currentSolution);
            txtSolutionInfo.Text = $"Solution found in {_currentSolution.Count} compressed moves.";
        }
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSolution == null || _currentMoveIndex >= _currentSolution.Count - 1) return;

        _currentMoveIndex++;
        ApplyMove(_currentSolution[_currentMoveIndex], false);
        RefreshBoardDisplay();
        UpdateSolutionHighlight();
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSolution == null || _currentMoveIndex < 0) return;

        ApplyMove(_currentSolution[_currentMoveIndex], true);
        _currentMoveIndex--;
        RefreshBoardDisplay();
        UpdateSolutionHighlight();
    }

    private void UpdateSolutionHighlight()
    {
        if (_solutionItems == null) return;

        for (int i = 0; i < _solutionItems.Count; i++)
        {
            _solutionItems[i].IsCurrent = (i == _currentMoveIndex);
        }
    }

    private void ApplyMove(Move move, bool reverse)
    {
        var vehicle = _vehicles.First(v => v.Id == move.VehicleId);
        int directionMultiplier = reverse ? -1 : 1;
        int spaces = move.Spaces * directionMultiplier;

        if (vehicle.Orientation == 'H')
        {
            if (move.Direction == "left") vehicle.Col -= spaces;
            else if (move.Direction == "right") vehicle.Col += spaces;
        }
        else
        {
            if (move.Direction == "up") vehicle.Row -= spaces;
            else if (move.Direction == "down") vehicle.Row += spaces;
        }
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        // Reload original vehicle positions
        var currentPuzzle = cmbSavedPuzzles.SelectedItem as BoardState;
        if (currentPuzzle != null)
        {
            _vehicles = currentPuzzle.Vehicles.Select(v => v.Clone()).ToList();
            RefreshBoardDisplay();
            _currentMoveIndex = -1;
            UpdateSolutionHighlight(); // Add this line
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var title = txtPuzzleTitle.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show("Please enter a puzzle title");
            return;
        }

        var existing = await _dbContext.BoardStates.FirstOrDefaultAsync(b => b.Title == title);
        if (existing != null)
        {
            existing.Vehicles = _vehicles.Select(v => v.Clone()).ToList();
            existing.CreatedDate = DateTime.Now;
        }
        else
        {
            var newState = new BoardState
            {
                Title = title,
                Vehicles = _vehicles.Select(v => v.Clone()).ToList()
            };
            _dbContext.BoardStates.Add(newState);
        }

        await _dbContext.SaveChangesAsync();
        LoadSavedPuzzles();
        MessageBox.Show("Puzzle saved successfully!");
    }

    private async void LoadSavedPuzzles()
    {
        var puzzles = await _dbContext.BoardStates.OrderBy(b => b.Title).ToListAsync();
        cmbSavedPuzzles.ItemsSource = puzzles;
        cmbSavedPuzzles.DisplayMemberPath = "Title";
    }

    private void CmbSavedPuzzles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbSavedPuzzles.SelectedItem is BoardState selectedState)
        {
            _vehicles = selectedState.Vehicles.Select(v => v.Clone()).ToList();
            RefreshBoardDisplay();
            _currentSolution = null;
            _currentMoveIndex = -1;
            lstSolution.ItemsSource = null;
            txtSolutionInfo.Text = "";
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (cmbSavedPuzzles.SelectedItem is BoardState selectedState)
        {
            _dbContext.BoardStates.Remove(selectedState);
            await _dbContext.SaveChangesAsync();
            LoadSavedPuzzles();
            MessageBox.Show("Puzzle deleted successfully!");
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _vehicles.Clear();
        RefreshBoardDisplay();
        _currentSolution = null;
        _currentMoveIndex = -1;
        lstSolution.ItemsSource = null;
        txtSolutionInfo.Text = "";
    }

    protected override void OnClosed(EventArgs e)
    {
        _dbContext?.Dispose();
        base.OnClosed(e);
    }

    private void VehicleButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            _selectedVehicleType = tag;
            var vehicle = CreateVehicleFromType(tag);

            if (vehicle != null)
            {
                // Store the vehicle for global cursor management
                _draggedVehicle = vehicle;

                DataObject dragData = new DataObject(typeof(Vehicle), vehicle);
                DragDrop.DoDragDrop(border, dragData, DragDropEffects.Copy);

                // Clean up
                _draggedVehicle = null;
                Mouse.OverrideCursor = null;
            }
        }
    }

    private void SetVehicleCursor(Vehicle vehicle)
    {
        if (vehicle.Orientation == 'H')
        {
            if (vehicle.Length == 2)
                Mouse.OverrideCursor = Cursors.SizeWE; // Horizontal double arrow
            else
                Mouse.OverrideCursor = Cursors.SizeWE; // Four-way arrow for trucks
        }
        else
        {
            if (vehicle.Length == 2)
                Mouse.OverrideCursor = Cursors.SizeNS; // Vertical double arrow
            else
                Mouse.OverrideCursor = Cursors.SizeNS; // Four-way arrow for trucks
        }

        // Special cursor for ambulance
        if (vehicle.Color == "Red")
        {
            Mouse.OverrideCursor = Cursors.Hand; // Hand cursor for ambulance
        }
    }

    private void BoardGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Vehicle)))
        {
            e.Effects = DragDropEffects.Copy;

            // When over the board, ensure we have the right cursor
            if (_draggedVehicle != null)
            {
                SetVehicleCursor(_draggedVehicle);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            Mouse.OverrideCursor = Cursors.No;
        }
        e.Handled = true;
    }

    //private void BoardGrid_DragEnter(object sender, DragEventArgs e)
    //{
    //    // This provides visual feedback when entering the board area
    //    if (e.Data.GetData(typeof(Vehicle)) is Vehicle)
    //    {
    //        if (sender is Border border)
    //        {
    //            border.Background = new SolidColorBrush(Colors.LightYellow) { Opacity = 0.3 };
    //        }
    //    }
    //}

    //private void BoardGrid_DragLeave(object sender, DragEventArgs e)
    //{
    //    // Reset background to the game color
    //    var border = sender as Border;
    //    if (border != null)
    //    {
    //        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B7C6C"));
    //    }
    //}

    private void BoardGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Vehicle)) is Vehicle vehicleTemplate)
        {
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && element is not Border)
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;

            if (element is Border border && border.DataContext is CellViewModel cell)
            {
                int row = cell.Row;
                int col = cell.Col;

                // Check ambulance placement rules
                if (vehicleTemplate.Color == "Red" && (row != 2 || col > 3))
                {
                    MessageBox.Show("Ambulance must be placed on row C (3) and column less than 5");
                    return;
                }

                // Check if placement is valid
                if (!CanPlaceVehicle(row, col, vehicleTemplate.Orientation, vehicleTemplate.Length))
                {
                    MessageBox.Show("Cannot place vehicle here - overlaps with existing vehicle or out of bounds");
                    return;
                }

                var newVehicle = vehicleTemplate.Clone();
                newVehicle.Row = row;
                newVehicle.Col = col;

                _vehicles.Add(newVehicle);
                RefreshBoardDisplay();
            }
        }
    }

    private Vehicle? CreateVehicleFromType(string vehicleType)
    {
        var parts = vehicleType.Split(',');
        char orientation = parts[0][0];
        int length = int.Parse(parts[1]);
        bool isAmbulance = parts.Length > 2 && parts[2] == "Ambulance";

        // Check if we're placing an ambulance and one doesn't exist
        if (isAmbulance && _vehicles.Any(v => v.Color == "Red" && v.Orientation == 'H'))
        {
            MessageBox.Show("Only one ambulance allowed on the board!");
            return null;
        }

        return new Vehicle
        {
            Orientation = orientation,
            Length = length,
            Color = isAmbulance ? "Red" : (length == 3 ? "Yellow" : "Cyan") // Trucks are yellow
        };
    }

    private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is CellViewModel cell)
        {
            if (!string.IsNullOrEmpty(cell.VehicleId))
            {
                // Find the vehicle that occupies this cell
                var vehicle = _vehicles.FirstOrDefault(v => v.Id == cell.VehicleId);
                if (vehicle != null)
                {
                    // Cycle to next color
                    vehicle.Color = GetNextColor(vehicle.Color);

                    // Update all cells for this vehicle
                    RefreshBoardDisplay();
                }
            }
        }
        e.Handled = true;
    }

    private static string GetNextColor(string currentColor)
    {
        var colors = new[] { "Red", "Cyan", "Yellow", "Purple", "Green", "Blue", "Orange", "Brown", "Black", "White" };
        var currentIndex = Array.IndexOf(colors, currentColor);
        var nextIndex = (currentIndex + 1) % colors.Length;
        return colors[nextIndex];
    }

    private string GetVehicleDisplayName(string vehicleId)
    {
        var vehicle = _vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle != null)
        {
            string color = vehicle.Color;
            string type = vehicle.Length == 3 ? "Truck" : "Car";
            string orientation = vehicle.Orientation == 'H' ? "(H)" : "(V)";

            if (vehicle.Color == "Red" && vehicle.Orientation == 'H')
            {
                return $"Ambulance {orientation}";
            }

            return $"{color} {type} {orientation}";
        }
        return vehicleId;
    }

    private ObservableCollection<SolutionItem> _solutionItems = new ObservableCollection<SolutionItem>();

    private void DisplaySolution(List<Move> solution)
    {
        _solutionItems.Clear();

        if (solution != null)
        {
            for (int i = 0; i < solution.Count; i++)
            {
                var move = solution[i];
                string vehicleName = GetVehicleDisplayName(move.VehicleId);
                string text = move.Spaces == 1 ?
                    $"{i + 1}. Move {vehicleName} {move.Direction}" :
                    $"{i + 1}. Move {vehicleName} {move.Direction} {move.Spaces}";

                _solutionItems.Add(new SolutionItem
                {
                    Text = text,
                    IsCurrent = (i == _currentMoveIndex),
                    Index = i
                });
            }
        }

        lstSolution.ItemsSource = _solutionItems;
    }
}
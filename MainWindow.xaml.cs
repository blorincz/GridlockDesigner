// MainWindow.xaml.cs
using GridlockDesigner.Data;
using GridlockDesigner.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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

    private bool CanPlaceVehicle(int row, int col, char orientation, int length, Vehicle? excludedVehicle = null)
    {
        var tempVehicle = new Vehicle { Row = row, Col = col, Orientation = orientation, Length = length };
        var occupiedCells = GetOccupiedCells(tempVehicle);

        foreach (var (r, c) in occupiedCells)
        {
            if (r < 0 || r >= 6 || c < 0 || c >= 6) return false;

            // Check for collisions with other vehicles (excluding the specified one)
            var vehiclesToCheck = _vehicles;
            if (excludedVehicle != null)
            {
                vehiclesToCheck = _vehicles.Where(v => v != excludedVehicle).ToList();
            }

            if (vehiclesToCheck.Any(v => GetOccupiedCells(v).Any(cell => cell.row == r && cell.col == c)))
                return false;
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

    private void VehicleCanvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Vehicle)) is Vehicle vehicle)
        {
            var position = e.GetPosition(VehicleCanvas);
            int col = (int)(position.X / 80);
            int row = (int)(position.Y / 80);

            // Check bounds
            if (row < 0 || row >= 6 || col < 0 || col >= 6)
            {
                RestoreDraggedVehicle(vehicle);
                return;
            }

            // Check ambulance placement rules
            if (vehicle.Color == "Red" && vehicle.Orientation == 'H' && (row != 2 || col > 3))
            {
                MessageBox.Show("Ambulance must be placed on row C (3) and column less than 5");
                RestoreDraggedVehicle(vehicle);
                return;
            }

            // Check if placement is valid
            if (!CanPlaceVehicle(row, col, vehicle.Orientation, vehicle.Length, vehicle))
            {
                MessageBox.Show("Cannot place vehicle here - overlaps with existing vehicle or out of bounds");
                RestoreDraggedVehicle(vehicle);
                return;
            }

            // Update vehicle position
            vehicle.Row = row;
            vehicle.Col = col;

            // If this is a new vehicle (not already in collection), add it
            if (!_vehicles.Contains(vehicle))
            {
                _vehicles.Add(vehicle);
            }

            RefreshBoardDisplay();
        }
    }

    private void RestoreDraggedVehicle(Vehicle vehicle)
    {
        if (!_vehicles.Contains(vehicle))
        {
            _vehicles.Add(vehicle);
        }
        RefreshBoardDisplay();
    }

    private void RefreshBoardDisplay()
    {
        // Clear the vehicle canvas
        VehicleCanvas.Children.Clear();

        // Create base grid cells (just borders and exit marker)
        var baseCells = new List<CellViewModel>();
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 6; col++)
            {
                baseCells.Add(new CellViewModel
                {
                    Row = row,
                    Col = col,
                    IsExit = (row == 2 && col == 5)
                });
            }
        }
        BaseGrid.ItemsSource = baseCells;

        // Add vehicles to the canvas
        foreach (var vehicle in _vehicles)
        {
            var vehicleVisual = CreateVehicleVisual(vehicle);
            VehicleCanvas.Children.Add(vehicleVisual);
        }
    }

    private static string GetVehicleColor(string colorName)
    {
        return colorName switch
        {
            "Red" => "#FF0000",
            "Cyan" => "#00FFFF",
            "Yellow" => "#E9FF8C",
            "Purple" => "#9962D8",
            "Green" => "#38F546", // New light green
            "Blue" => "#0000FF",
            "Brown" => "#995E3F",
            "Black" => "#000000",
            "White" => "#FFFFFF",
            _ => "#CCCCCC"
        };
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
            btnPrev.IsEnabled = false;
            btnNext.IsEnabled = false;
        }
        else
        {
            DisplaySolution(_currentSolution);
            txtSolutionInfo.Text = $"Solution found in {_currentSolution.Count} moves.";
            btnPrev.IsEnabled = true;
            btnNext.IsEnabled = _currentSolution.Count > 0;
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
        if (cmbSavedPuzzles.SelectedItem is BoardState currentPuzzle)
        {
            _vehicles = currentPuzzle.Vehicles.Select(v => v.Clone()).ToList();
            RefreshBoardDisplay();
            _currentMoveIndex = -1;

            // Clear the solution - this is the key fix
            _currentSolution = null;
            _solutionItems.Clear();
            lstSolution.ItemsSource = null;
            txtSolutionInfo.Text = "Puzzle reset. Click 'Solve Puzzle' to find a new solution.";

            // Also disable navigation buttons
            btnPrev.IsEnabled = false;
            btnNext.IsEnabled = false;
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
                // Set cursor for drag operation
                Mouse.OverrideCursor = Cursors.Cross;

                try
                {
                    DataObject dragData = new(typeof(Vehicle), vehicle);
                    DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                }
                finally
                {
                    // Always reset cursor, even if drag fails
                    Mouse.OverrideCursor = null;
                }
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
        // This now only handles color cycling on existing vehicles
        // Vehicle placement is handled entirely by drag-and-drop
        e.Handled = true;
    }

    private static string GetNextColor(string currentColor)
    {
        var colors = new[] { "Cyan", "Yellow", "Purple", "Green", "Blue", "Brown", "Black", "White" };
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

    private static Grid CreateCrossIcon()
    {
        // Create a container for the cross
        var crossContainer = new Grid
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Red background circle
        var background = new Ellipse
        {
            Fill = Brushes.Red,
            Width = 20,
            Height = 20
        };

        // Horizontal white bar
        var horizontalBar = new Rectangle
        {
            Fill = Brushes.White,
            Width = 12,
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Vertical white bar
        var verticalBar = new Rectangle
        {
            Fill = Brushes.White,
            Width = 3,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        crossContainer.Children.Add(background);
        crossContainer.Children.Add(horizontalBar);
        crossContainer.Children.Add(verticalBar);

        return crossContainer;
    }
    private Border CreateVehicleVisual(Vehicle vehicle)
    {
        var vehicleBorder = new Border();
        string vehicleColor = GetVehicleColor(vehicle.Color);

        // Set size based on orientation
        if (vehicle.Orientation == 'H')
        {
            vehicleBorder.Width = vehicle.Length * 80;
            vehicleBorder.Height = 80;
        }
        else
        {
            vehicleBorder.Width = 80;
            vehicleBorder.Height = vehicle.Length * 80;
        }

        // Special handling for ambulance - white background
        if (vehicle.Color == "Red" && vehicle.Orientation == 'H')
        {
            vehicleBorder.Background = Brushes.LightGray;
            vehicleColor = "#D3D3D3"; // White for text color determination
        }
        else
        {
            // Normal vehicle appearance
            vehicleBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(vehicleColor));
        }

        vehicleBorder.BorderBrush = Brushes.Black;
        vehicleBorder.BorderThickness = new Thickness(1);
        vehicleBorder.CornerRadius = new CornerRadius(3);

        // Create container for icon and text
        var container = new StackPanel
        {
            Orientation = vehicle.Orientation == 'H' ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Add tooltips and add red cross icon for ambulances
        if (!(vehicle.Color == "Red" && vehicle.Orientation == 'H'))
        {
            vehicleBorder.ToolTip = "Left-click to move, Right-click to change color";
        }
        else
        {
            var crossIcon = CreateCrossIcon();
            container.Children.Add(crossIcon);
            vehicleBorder.ToolTip = "Left-click to move (Color cannot be changed)";
        }

        // Add text label
        var textBlock = new TextBlock
        {
            Text = vehicle.Color == "Red" ? "Ambulance" :
                   (vehicle.Length == 3 ? "Truck" : "Car"),
            Foreground = GetVehicleTextColor(vehicleColor),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = vehicle.Color == "Red" ? new Thickness(5, 0, 0, 0) : new Thickness(0) // Space between icon and text
        };

        container.Children.Add(textBlock);

        // For vertical vehicles, we need to handle rotation differently
        if (vehicle.Orientation == 'V')
        {
            textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
            textBlock.RenderTransform = new RotateTransform(90);
        }

        vehicleBorder.Child = container;

        // Set position
        Canvas.SetLeft(vehicleBorder, vehicle.Col * 80);
        Canvas.SetTop(vehicleBorder, vehicle.Row * 80);

        // Add right-click handler for color cycling (non-ambulance only)
        if (!(vehicle.Color == "Red" && vehicle.Orientation == 'H'))
        {
            vehicleBorder.MouseRightButtonDown += (s, e) =>
            {
                CycleVehicleColor(vehicle);
                e.Handled = true;
            };
        }

        // Add left-click handler for dragging existing vehicles
        vehicleBorder.MouseLeftButtonDown += (s, e) =>
        {
            StartVehicleDrag(vehicle, vehicleBorder);
            e.Handled = true;
        };

        // Store vehicle ID for reference
        vehicleBorder.Tag = vehicle.Id;

        return vehicleBorder;
    }

    private void VehicleCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Vehicle)))
        {
            e.Effects = DragDropEffects.Move;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            Mouse.OverrideCursor = Cursors.No;
        }
        e.Handled = true;
    }

    private void VehicleCanvas_DragLeave(object sender, DragEventArgs e)
    {
        // Reset cursor when leaving the canvas during drag
        Mouse.OverrideCursor = null;
    }

    private void StartVehicleDrag(Vehicle vehicle, FrameworkElement vehicleElement)
    {
        // Store the vehicle being dragged
        _draggedVehicle = vehicle;

        // Store original position in case we need to revert
        var originalRow = vehicle.Row;
        var originalCol = vehicle.Col;

        // Remove the vehicle from the board temporarily so it doesn't block placement
        _vehicles.Remove(vehicle);
        RefreshBoardDisplay();

        try
        {
            // Set cursor for drag operation
            Mouse.OverrideCursor = Cursors.SizeAll;

            // Start drag operation with the vehicle
            DataObject dragData = new DataObject(typeof(Vehicle), vehicle);
            var result = DragDrop.DoDragDrop(vehicleElement, dragData, DragDropEffects.Move);

            // If drag was cancelled or failed, restore the vehicle to original position
            if (result == DragDropEffects.None)
            {
                vehicle.Row = originalRow;
                vehicle.Col = originalCol;
                _vehicles.Add(vehicle);
            }
        }
        catch (Exception ex)
        {
            // If anything goes wrong, restore the vehicle
            vehicle.Row = originalRow;
            vehicle.Col = originalCol;
            _vehicles.Add(vehicle);
            Console.WriteLine($"Drag error: {ex.Message}");
        }
        finally
        {
            // Always reset cursor and cleanup
            Mouse.OverrideCursor = null;
            _draggedVehicle = null;
            RefreshBoardDisplay();
        }
    }

    private void CycleVehicleColor(Vehicle vehicle)
    {
        // Don't allow color cycling for ambulances
        if (vehicle.Color == "Red" && vehicle.Orientation == 'H')
        {
            return;
        }

        vehicle.Color = GetNextColor(vehicle.Color);
        RefreshBoardDisplay();
    }

    private static SolidColorBrush GetVehicleTextColor(string colorHex)
    {
        var darkColors = new[] { "#000000", "#0000FF", "#FF0000", "#800080", "#A52A2A", "#995E3F" };
        return darkColors.Contains(colorHex) ? Brushes.White : Brushes.Black;
    }
}
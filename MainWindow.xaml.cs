using GridlockDesigner.Data;
using GridlockDesigner.Helpers;
using GridlockDesigner.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GridlockDesigner;

public partial class MainWindow : Window
{
    private readonly AppDbContext _dbContext;
    private List<Vehicle> _vehicles = [];
    private List<Move>? _currentSolution;
    private int _currentMoveIndex = -1;
    private Vehicle? _draggedVehicle;
    private readonly ObservableCollection<SolutionItem> _solutionItems = [];
    private bool _isPlayMode = false;
    private int _moveCount = 0;
    private DateTime _gameStartTime;
    private readonly DispatcherTimer _gameTimer = new();
    private bool _isDraggingVehicle = false;
    private Point _dragStartPosition;

    public MainWindow()
    {
        InitializeComponent();

        DragEnter += MainWindow_DragEnter;
        PreviewDragOver += MainWindow_PreviewDragOver;
        QueryCursor += MainWindow_QueryCursor;

        // Initialize game timer (but don't start it yet)
        _gameTimer.Interval = TimeSpan.FromSeconds(1);
        _gameTimer.Tick += GameTimer_Tick;

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
                    Vehicles =
                [
                    new() { Color = "Red", Orientation = 'H', Length = 2, Row = 2, Col = 0 },
                    new() { Color = "Yellow", Orientation = 'H', Length = 3, Row = 0, Col = 0 }
                ]
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

        InitializeBoard();
        LoadSavedPuzzles();
        UpdatePlayModeButtonState();
    }

    private void InitializeBoard()
    {
        RefreshBoardDisplay();
    }

    private async void LoadSavedPuzzles()
    {
        var puzzles = await _dbContext.BoardStates.OrderBy(b => b.Title).ToListAsync();
        cmbSavedPuzzles.ItemsSource = puzzles;
        cmbSavedPuzzles.DisplayMemberPath = "Title";
    }

    // Window/Drag Events

    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        // Change cursor to indicate copy operation
        Mouse.OverrideCursor = Cursors.Cross;
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

    private void MainWindow_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        // If we're dragging a vehicle, set the cursor for the entire window
        if (_draggedVehicle != null)
        {
            e.Cursor = CursorUtilities.GetVehicleCursor(_draggedVehicle);
            e.Handled = true;
        }
    }

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
        if (_isPlayMode)
        {
            var elapsed = DateTime.Now - _gameStartTime;
            txtGameTime.Text = $"Time: {elapsed:mm\\:ss}";
        }
    }

    // Board/Grid Events

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
            if (!VehicleUtilities.CanPlaceVehicle(row, col, vehicle.Orientation, vehicle.Length, _vehicles, vehicle))
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

        UpdatePlayModeButtonState();
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

    private void CmbSavedPuzzles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbSavedPuzzles.SelectedItem is BoardState selectedState)
        {
            _vehicles = selectedState.Vehicles.Select(v => v.Clone()).ToList();
            RefreshBoardDisplay();
            _currentSolution = null;
            _solutionItems.Clear();
            lstSolution.ItemsSource = null;
            txtSolutionInfo.Text = "";
            UpdatePlayModeButtonState();
            ShowMessage($"Loaded: {selectedState.Title}");
        }
    }


    // Button Click Events

    private void VehicleButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
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

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        // Reload original vehicle positions
        if (cmbSavedPuzzles.SelectedItem is BoardState currentPuzzle)
        {
            _vehicles = [.. currentPuzzle.Vehicles.Select(v => v.Clone())];
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
            existing.Vehicles = [.. _vehicles.Select(v => v.Clone())];
            existing.CreatedDate = DateTime.Now;
        }
        else
        {
            var newState = new BoardState
            {
                Title = title,
                Vehicles = [.. _vehicles.Select(v => v.Clone())]
            };
            _dbContext.BoardStates.Add(newState);
        }

        await _dbContext.SaveChangesAsync();
        LoadSavedPuzzles();
        MessageBox.Show("Puzzle saved successfully!");
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
        _solutionItems.Clear();
        lstSolution.ItemsSource = null;
        txtSolutionInfo.Text = "";
        UpdatePlayModeButtonState();
        ShowMessage("Board cleared");
    }

    private void BtnPlayMode_Checked(object sender, RoutedEventArgs e)
    {
        _isPlayMode = true;
        _moveCount = 0;
        _gameStartTime = DateTime.Now;

        // Start timer
        _gameTimer.Start();

        // Update UI panels
        SolutionPanel.Visibility = Visibility.Collapsed;
        PlayModePanel.Visibility = Visibility.Visible;

        // Disable design tools
        VehicleToolsPanel.IsEnabled = false;
        ActionButtonsPanel.IsEnabled = false;
        SaveLoadPanel.IsEnabled = false;

        // Update play mode UI
        txtGameStatus.Text = "Ready";
        txtMoveCount.Text = "Moves: 0";
        txtGameTime.Text = "Time: 00:00";

        ShowMessage("Play mode started! Click and drag vehicles to move.");

        // Ensure all vehicles have correct handlers
        RefreshBoardDisplay();
    }

    private void BtnPlayMode_Unchecked(object? sender, RoutedEventArgs? e)
    {
        _isPlayMode = false;

        // Stop timer
        _gameTimer.Stop();

        // End any ongoing drag
        if (_isDraggingVehicle)
        {
            EndVehicleMove();
        }

        // Update UI panels
        SolutionPanel.Visibility = Visibility.Visible;
        PlayModePanel.Visibility = Visibility.Collapsed;

        // Re-enable design tools
        VehicleToolsPanel.IsEnabled = true;
        ActionButtonsPanel.IsEnabled = true;
        SaveLoadPanel.IsEnabled = true;

        var timeElapsed = DateTime.Now - _gameStartTime;
        ShowMessage($"Play mode ended. Moves: {_moveCount}, Time: {timeElapsed:mm\\:ss}");

        // Restore designer mode handlers
        RefreshBoardDisplay();
    }

    // Vehicle Interaction Methods

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

    private void RestoreDraggedVehicle(Vehicle vehicle)
    {
        if (!_vehicles.Contains(vehicle))
        {
            _vehicles.Add(vehicle);
        }
        RefreshBoardDisplay();
    }

    private void CycleVehicleColor(Vehicle vehicle)
    {
        // Don't allow color cycling for ambulances
        if (vehicle.Color == "Red" && vehicle.Orientation == 'H')
        {
            return;
        }

        vehicle.Color = ColorUtilities.GetNextColor(vehicle.Color);
        RefreshBoardDisplay();
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

    private Border CreateVehicleVisual(Vehicle vehicle)
    {
        var vehicleBorder = new Border();
        string vehicleColor = ColorUtilities.GetVehicleColor(vehicle.Color);

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
            Foreground = ColorUtilities.GetVehicleTextColor(vehicleColor),
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

        if (!_isPlayMode)
        {
            // Designer mode: color cycling and drag-drop
            if (!(vehicle.Color == "Red" && vehicle.Orientation == 'H'))
            {
                vehicleBorder.MouseRightButtonDown += (s, e) =>
                {
                    CycleVehicleColor(vehicle);
                    e.Handled = true;
                };
            }

            vehicleBorder.MouseLeftButtonDown += (s, e) =>
            {
                StartVehicleDrag(vehicle, vehicleBorder);
                e.Handled = true;
            };
        }
        else
        {
            // Play mode: mouse-based movement
            vehicleBorder.MouseLeftButtonDown += (s, e) =>
            {
                StartVehicleMove(vehicle, e.GetPosition(VehicleCanvas));
                e.Handled = true;
            };

            vehicleBorder.Cursor = Cursors.Hand; // Show hand cursor for movable vehicles
        }

        // Store vehicle ID for reference
        vehicleBorder.Tag = vehicle.Id;

        return vehicleBorder;
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

    private void StartVehicleMove(Vehicle vehicle, Point mousePosition)
    {
        if (!_isPlayMode || _isDraggingVehicle) return;

        _isDraggingVehicle = true;
        _draggedVehicle = vehicle;
        _dragStartPosition = mousePosition;

        // Capture mouse to get events even when outside the vehicle
        VehicleCanvas.CaptureMouse();

        // Visual feedback: make vehicle semi-transparent while dragging
        var vehicleBorder = VehicleCanvas.Children
            .OfType<Border>()
            .FirstOrDefault(b => b.Tag as string == vehicle.Id);

        if (vehicleBorder != null)
        {
            vehicleBorder.Opacity = 0.7;
        }

        txtGameStatus.Text = $"Moving: {GetVehicleDisplayName(vehicle.Id)}";
    }

    private void VehicleCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPlayMode || !_isDraggingVehicle || _draggedVehicle == null) return;

        var currentPosition = e.GetPosition(VehicleCanvas);
        var deltaX = currentPosition.X - _dragStartPosition.X;
        var deltaY = currentPosition.Y - _dragStartPosition.Y;

        // Determine movement direction based on vehicle orientation
        if (_draggedVehicle.Orientation == 'H')
        {
            // Horizontal vehicles only move left/right
            int cellDelta = (int)(deltaX / 80); // 80px per cell

            if (cellDelta != 0)
            {
                // Try to move the vehicle
                string direction = cellDelta > 0 ? "right" : "left";
                int spaces = Math.Abs(cellDelta);

                // Snap to grid: only move if we've moved at least half a cell
                if (spaces >= 1)
                {
                    // Check if move is valid
                    if (IsValidMove(_draggedVehicle, direction, spaces))
                    {
                        // Apply move
                        ExecuteMove(_draggedVehicle, direction, spaces);
                        _dragStartPosition = currentPosition; // Reset start position
                    }
                }
            }
        }
        else // Vertical vehicles
        {
            // Vertical vehicles only move up/down
            int cellDelta = (int)(deltaY / 80); // 80px per cell

            if (cellDelta != 0)
            {
                string direction = cellDelta > 0 ? "down" : "up";
                int spaces = Math.Abs(cellDelta);

                if (spaces >= 1)
                {
                    if (IsValidMove(_draggedVehicle, direction, spaces))
                    {
                        ExecuteMove(_draggedVehicle, direction, spaces);
                        _dragStartPosition = currentPosition;
                    }
                }
            }
        }
    }

    private void VehicleCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPlayMode || !_isDraggingVehicle) return;

        EndVehicleMove();
    }

    private void EndVehicleMove()
    {
        _isDraggingVehicle = false;

        // Restore vehicle opacity
        if (_draggedVehicle != null)
        {
            var vehicleBorder = VehicleCanvas.Children
                .OfType<Border>()
                .FirstOrDefault(b => b.Tag as string == _draggedVehicle.Id);

            if (vehicleBorder != null)
            {
                vehicleBorder.Opacity = 1.0;
            }
        }

        _draggedVehicle = null;
        VehicleCanvas.ReleaseMouseCapture();

        txtGameStatus.Text = "Ready";
    }

    // Board Logic Methods

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

        // Update play mode button state after refresh
        if (!_isPlayMode)
        {
            UpdatePlayModeButtonState();
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

    private bool IsValidMove(Vehicle vehicle, string direction, int spaces = 1)
    {
        if (vehicle.Orientation == 'H')
        {
            if (direction == "left")
            {
                // Check if can move left
                int newCol = vehicle.Col - spaces;
                if (newCol < 0) return false;

                // Check for collisions
                var occupiedCells = vehicle.GetOccupiedCells();
                for (int i = 1; i <= spaces; i++)
                {
                    int checkCol = vehicle.Col - i;
                    if (_vehicles.Any(v => v != vehicle && v.GetOccupiedCells().Any(c => c.row == vehicle.Row && c.col == checkCol)))
                        return false;
                }
            }
            else if (direction == "right")
            {
                // Check if can move right
                int newCol = vehicle.Col + spaces;
                if (newCol + vehicle.Length - 1 >= 6) return false;

                // Check for collisions
                var occupiedCells = vehicle.GetOccupiedCells();
                for (int i = 1; i <= spaces; i++)
                {
                    int checkCol = vehicle.Col + vehicle.Length - 1 + i;
                    if (_vehicles.Any(v => v != vehicle && v.GetOccupiedCells().Any(c => c.row == vehicle.Row && c.col == checkCol)))
                        return false;
                }
            }
        }
        else // Vertical
        {
            if (direction == "up")
            {
                // Check if can move up
                int newRow = vehicle.Row - spaces;
                if (newRow < 0) return false;

                // Check for collisions
                for (int i = 1; i <= spaces; i++)
                {
                    int checkRow = vehicle.Row - i;
                    if (_vehicles.Any(v => v != vehicle && v.GetOccupiedCells().Any(c => c.row == checkRow && c.col == vehicle.Col)))
                        return false;
                }
            }
            else if (direction == "down")
            {
                // Check if can move down
                int newRow = vehicle.Row + spaces;
                if (newRow + vehicle.Length - 1 >= 6) return false;

                // Check for collisions
                for (int i = 1; i <= spaces; i++)
                {
                    int checkRow = vehicle.Row + vehicle.Length - 1 + i;
                    if (_vehicles.Any(v => v != vehicle && v.GetOccupiedCells().Any(c => c.row == checkRow && c.col == vehicle.Col)))
                        return false;
                }
            }
        }

        return true;
    }

    private void ExecuteMove(Vehicle vehicle, string direction, int spaces)
    {
        if (!_isPlayMode) return;

        // Apply the move
        if (vehicle.Orientation == 'H')
        {
            if (direction == "left")
                vehicle.Col -= spaces;
            else if (direction == "right")
                vehicle.Col += spaces;
        }
        else // Vertical
        {
            if (direction == "up")
                vehicle.Row -= spaces;
            else if (direction == "down")
                vehicle.Row += spaces;
        }

        _moveCount++;
        txtMoveCount.Text = $"Moves: {_moveCount}";

        RefreshBoardDisplay();

        // Check win condition
        CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        var ambulance = _vehicles.FirstOrDefault(v => v.Color == "Red" && v.Orientation == 'H');
        if (ambulance != null)
        {
            // Check if ambulance is at the exit (occupies C5 and C6)
            var occupiedCells = ambulance.GetOccupiedCells();
            if (occupiedCells.Any(c => c.col == 4) && occupiedCells.Any(c => c.col == 5))
            {
                var timeElapsed = DateTime.Now - _gameStartTime;

                _gameTimer?.Stop();

                MessageBox.Show($"🎉 PUZZLE SOLVED! 🎉\n\nMoves: {_moveCount}\nTime: {timeElapsed:mm\\:ss}\n\nCongratulations!");

                // Exit play mode
                btnPlayMode.IsChecked = false;
                BtnPlayMode_Unchecked(null, null);
            }
        }
    }

    private void ShowMessage(string message, bool isError = false)
    {
        txtMessage.Text = message;
        MessageArea.Background = isError ? Brushes.LightPink : Brushes.LightYellow;
        MessageArea.BorderBrush = isError ? Brushes.Red : Brushes.Goldenrod;
        MessageArea.Visibility = Visibility.Visible;
    }

    private void ClearMessage()
    {
        MessageArea.Visibility = Visibility.Collapsed;
        txtMessage.Text = "";
    }

    private void UpdatePlayModeButtonState()
    {
        // Enable play mode button only if there's an ambulance on the board
        bool hasAmbulance = _vehicles.Any(v => v.Color == "Red" && v.Orientation == 'H');
        btnPlayMode.IsEnabled = hasAmbulance;

        if (!hasAmbulance)
        {
            ShowMessage("Add an ambulance to enable play mode", true);
        }
        else
        {
            ClearMessage();
        }
    }

    // Solution Methods

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

    private void UpdateSolutionHighlight()
    {
        if (_solutionItems == null) return;

        for (int i = 0; i < _solutionItems.Count; i++)
        {
            _solutionItems[i].IsCurrent = (i == _currentMoveIndex);
        }
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

}
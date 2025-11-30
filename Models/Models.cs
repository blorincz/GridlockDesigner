using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GridlockDesigner.Models;

// Models.cs
public class Vehicle
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Color { get; set; } = "Red";
    public char Orientation { get; set; } = 'H';
    public int Length { get; set; } = 2;
    public int Row { get; set; }
    public int Col { get; set; }

    public Vehicle Clone()
    {
        return new Vehicle
        {
            Id = Guid.NewGuid().ToString(),
            Color = Color,
            Orientation = Orientation,
            Length = Length,
            Row = Row,
            Col = Col
        };
    }
}

public class BoardState
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public List<Vehicle> Vehicles { get; set; } = new List<Vehicle>();

    public string AmbulanceId => Vehicles.FirstOrDefault(v => v.Length == 2 && v.Color == "Red" && v.Orientation == 'H')?.Id ?? "";
}

public class Move
{
    public string VehicleId { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int Spaces { get; set; } = 1;

    public override string ToString() => Spaces == 1 ?
        $"Move {VehicleId} {Direction}" :
        $"Move {VehicleId} {Direction} {Spaces}";
}

public class CellViewModel : INotifyPropertyChanged
{
    private string _color = "Transparent";
    private string? _content;
    private bool _rotateText;

    public int Row { get; set; }
    public int Col { get; set; }
    public string? Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }
    public bool RotateText
    {
        get => _rotateText;
        set { _rotateText = value; OnPropertyChanged(); }
    }
    public bool IsExit { get; set; }
    public string VehicleId { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class SolutionItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isCurrent;

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }

    public int Index { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

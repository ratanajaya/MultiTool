using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MultiTool.Presentation.ViewModels;

public sealed class BatchFileItem : INotifyPropertyChanged
{
    private string _currentPath;
    private string _currentName;
    private int _position;
    private string _proposedName = string.Empty;
    private FileDropPosition _dropPosition;

    public BatchFileItem(string path)
    {
        _currentPath = path;
        _currentName = Path.GetFileName(path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetField(ref _currentPath, value);
    }

    public string CurrentName
    {
        get => _currentName;
        private set => SetField(ref _currentName, value);
    }

    public string Extension => Path.GetExtension(CurrentPath);

    public int Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }

    public string ProposedName
    {
        get => _proposedName;
        set => SetField(ref _proposedName, value);
    }

    public FileDropPosition DropPosition
    {
        get => _dropPosition;
        set => SetField(ref _dropPosition, value);
    }

    public void ApplyRename(string path)
    {
        CurrentPath = path;
        CurrentName = Path.GetFileName(path);
        OnPropertyChanged(nameof(Extension));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StevensSupportHelper.Admin.Models;

public sealed class RegistryTreeNode : INotifyPropertyChanged
{
    private bool _isLoaded;
    private bool _isExpanded;
    private bool _isSelected;

    public RegistryTreeNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<RegistryTreeNode> Children { get; } = [];

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetField(ref _isLoaded, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public void EnsurePlaceholder()
    {
        if (Children.Count == 0)
        {
            Children.Add(new RegistryTreeNode("(loading)", FullPath));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

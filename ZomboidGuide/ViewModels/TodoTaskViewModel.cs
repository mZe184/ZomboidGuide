using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZomboidGuide.ViewModels;

public sealed partial class TodoTaskViewModel : ViewModelBase
{
    private static readonly IBrush AutoDoneBrush = Brush.Parse("#2F5A3E");
    private static readonly IBrush ManualDoneBrush = Brush.Parse("#586A35");
    private static readonly IBrush OpenBrush = Brush.Parse("#3A4330");
    private static readonly IBrush GroupBrush = Brush.Parse("#3B4759");

    public TodoTaskViewModel(
        string id,
        string title,
        string detail = "",
        string autoLabel = "Auto",
        string manualLabel = "Manual",
        string openLabel = "Open")
    {
        Id = id;
        Title = title;
        Detail = detail;
        AutoLabel = autoLabel;
        ManualLabel = manualLabel;
        OpenLabel = openLabel;
    }

    public string Id { get; }

    public string Title { get; }

    public string Detail { get; }

    public string AutoLabel { get; }

    public string ManualLabel { get; }

    public string OpenLabel { get; }

    public ObservableCollection<TodoTaskViewModel> Children { get; } = [];

    public TodoTaskViewModel? Parent { get; private set; }

    public bool HasChildren => Children.Count > 0;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool CanManuallyCheck => !HasChildren;

    public int CompletedChildrenCount => Children.Count(child => child.IsCompleted);

    public int TotalChildrenCount => Children.Count;

    public bool IsCompleted =>
        IsAutoCompleted ||
        IsManualChecked ||
        (HasChildren && Children.All(child => child.IsCompleted));

    public string StatusText
    {
        get
        {
            if (HasChildren)
            {
                return $"{CompletedChildrenCount}/{TotalChildrenCount}";
            }

            if (IsAutoCompleted)
            {
                return AutoLabel;
            }

            return IsManualChecked ? ManualLabel : OpenLabel;
        }
    }

    public IBrush StatusBadgeBrush
    {
        get
        {
            if (HasChildren)
            {
                return GroupBrush;
            }

            if (IsAutoCompleted)
            {
                return AutoDoneBrush;
            }

            return IsManualChecked ? ManualDoneBrush : OpenBrush;
        }
    }

    [ObservableProperty]
    private bool isManualChecked;

    [ObservableProperty]
    private bool isAutoCompleted;

    public void AddChild(TodoTaskViewModel child)
    {
        child.Parent = this;
        child.PropertyChanged += OnChildPropertyChanged;
        Children.Add(child);
        RaiseComputedStateChanged();
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IsCompleted) or nameof(StatusText))
        {
            RaiseComputedStateChanged();
        }
    }

    partial void OnIsManualCheckedChanged(bool value)
    {
        RaiseComputedStateChanged();
    }

    partial void OnIsAutoCompletedChanged(bool value)
    {
        RaiseComputedStateChanged();
    }

    private void RaiseComputedStateChanged()
    {
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(CanManuallyCheck));
        OnPropertyChanged(nameof(CompletedChildrenCount));
        OnPropertyChanged(nameof(TotalChildrenCount));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeBrush));
        Parent?.RaiseComputedStateChanged();
    }
}

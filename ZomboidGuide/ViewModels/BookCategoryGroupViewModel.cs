using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZomboidGuide.ViewModels;

public sealed partial class BookCategoryGroupViewModel : ViewModelBase
{
    public string Category { get; init; } = string.Empty;

    public string CategoryGerman { get; init; } = string.Empty;

    public int CheckedCount { get; init; }

    public int TotalCount { get; init; }

    public string DisplayCategory =>
        string.IsNullOrWhiteSpace(CategoryGerman) ||
        CategoryGerman.Equals(Category, System.StringComparison.OrdinalIgnoreCase)
            ? Category
            : $"{Category} ({CategoryGerman})";

    public string Header => $"{DisplayCategory} [{CheckedCount} / {TotalCount}]";

    public string ProgressText => $"{CheckedCount} / {TotalCount}";

    public string Subtitle { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isExpanded = true;

    public ObservableCollection<ChecklistItemViewModel> Items { get; init; } = [];
}

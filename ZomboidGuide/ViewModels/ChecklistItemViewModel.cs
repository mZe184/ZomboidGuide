using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using ZomboidGuide.Models;

namespace ZomboidGuide.ViewModels;

public sealed partial class ChecklistItemViewModel : ViewModelBase
{
    private static readonly IBrush CheckedAccentBrush = Brush.Parse("#8FAF4A");
    private static readonly IBrush UncheckedAccentBrush = Brush.Parse("#4A5337");
    private static readonly IBrush NeutralBadgeBrush = Brush.Parse("#3A4330");
    private static readonly IBrush InventoryBadgeBrush = Brush.Parse("#516936");
    private static readonly IBrush HistoricalInventoryBadgeBrush = Brush.Parse("#4C5840");
    private static readonly IBrush ReadBadgeBrush = Brush.Parse("#2E5A3B");
    private static readonly IBrush ObsoleteBadgeBrush = Brush.Parse("#5C4332");
    private static readonly IBrush LearnedBadgeBrush = Brush.Parse("#2F5A4E");

    public ChecklistItemViewModel(GuideItem item, bool isChecked)
    {
        Id = item.Id;
        Name = item.Name;
        GermanName = item.GermanName;
        GermanNameSource = item.GermanNameSource;
        GermanNameLanguageCode = item.GermanNameLanguageCode;
        Detail = item.Detail;
        Level = item.Level;
        Category = item.Category;
        Source = item.Source;
        Type = item.Type;
        IsChecked = isChecked;
    }

    public string Id { get; }

    public string Name { get; }

    public string GermanName { get; }

    public string GermanNameSource { get; }

    public string GermanNameLanguageCode { get; }

    public string DisplayName
    {
        get
        {
            var marker = GermanTranslationMarker;
            if (string.IsNullOrWhiteSpace(GermanName))
            {
                return Name;
            }

            if (GermanName.Equals(Name, System.StringComparison.OrdinalIgnoreCase))
            {
                if (GermanNameLanguageCode.Equals("EN", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Name;
                }

                return string.IsNullOrWhiteSpace(marker) ? Name : $"{Name} [{marker}]";
            }

            return string.IsNullOrWhiteSpace(marker)
                ? $"{Name} ({GermanName})"
                : $"{Name} ({GermanName}) [{marker}]";
        }
    }

    public string GermanTranslationMarker
    {
        get
        {
            var code = string.IsNullOrWhiteSpace(GermanNameLanguageCode)
                ? "LOC"
                : GermanNameLanguageCode.ToUpperInvariant();
            if (GermanNameSource.Equals("game", System.StringComparison.OrdinalIgnoreCase))
            {
                return $"{code}: Game";
            }

            return GermanNameSource.Equals("app", System.StringComparison.OrdinalIgnoreCase)
                ? $"{code}: App"
                : string.Empty;
        }
    }

    public string Detail { get; }

    public int Level { get; }

    public string Category { get; }

    public string Source { get; }

    public GuideItemType Type { get; }

    public IBrush CardAccentBrush => IsChecked ? CheckedAccentBrush : UncheckedAccentBrush;

    public bool HasSessionState => !string.IsNullOrWhiteSpace(SessionState);

    public string SessionBadgeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SessionState))
            {
                return "Open";
            }

            if (SessionState.StartsWith("In Inventory", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Im Inventar", System.StringComparison.OrdinalIgnoreCase))
            {
                return SessionState.StartsWith("Im Inventar", System.StringComparison.OrdinalIgnoreCase)
                    ? "Inventar"
                    : "Inventory";
            }

            if (SessionState.StartsWith("Seen in Inventory", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Befand sich mal im Inventar", System.StringComparison.OrdinalIgnoreCase))
            {
                return SessionState.StartsWith("Befand sich mal im Inventar", System.StringComparison.OrdinalIgnoreCase)
                    ? "Früher da"
                    : "Seen";
            }

            if (SessionState.StartsWith("No Longer Needed", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Nicht mehr benötigt", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Nicht mehr benoetigt", System.StringComparison.OrdinalIgnoreCase))
            {
                return SessionState.StartsWith("Nicht mehr", System.StringComparison.OrdinalIgnoreCase)
                    ? "Nicht nötig"
                    : "Obsolete";
            }

            if (SessionState.StartsWith("Read", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Gelesen", System.StringComparison.OrdinalIgnoreCase))
            {
                return SessionState.StartsWith("Gelesen", System.StringComparison.OrdinalIgnoreCase)
                    ? "Gelesen"
                    : "Read";
            }

            if (SessionState.StartsWith("Learned", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Gelernt", System.StringComparison.OrdinalIgnoreCase))
            {
                return SessionState.StartsWith("Gelernt", System.StringComparison.OrdinalIgnoreCase)
                    ? "Gelernt"
                    : "Learned";
            }

            return SessionState;
        }
    }

    public IBrush SessionBadgeBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SessionState))
            {
                return NeutralBadgeBrush;
            }

            if (SessionState.StartsWith("In Inventory", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Im Inventar", System.StringComparison.OrdinalIgnoreCase))
            {
                return InventoryBadgeBrush;
            }

            if (SessionState.StartsWith("Seen in Inventory", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Befand sich mal im Inventar", System.StringComparison.OrdinalIgnoreCase))
            {
                return HistoricalInventoryBadgeBrush;
            }

            if (SessionState.StartsWith("No Longer Needed", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Nicht mehr benötigt", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Nicht mehr benoetigt", System.StringComparison.OrdinalIgnoreCase))
            {
                return ObsoleteBadgeBrush;
            }

            if (SessionState.StartsWith("Read", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Gelesen", System.StringComparison.OrdinalIgnoreCase))
            {
                return ReadBadgeBrush;
            }

            if (SessionState.StartsWith("Learned", System.StringComparison.OrdinalIgnoreCase) ||
                SessionState.StartsWith("Gelernt", System.StringComparison.OrdinalIgnoreCase))
            {
                return LearnedBadgeBrush;
            }

            return NeutralBadgeBrush;
        }
    }

    [ObservableProperty]
    private bool isChecked;

    [ObservableProperty]
    private string sessionState = string.Empty;

    partial void OnIsCheckedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardAccentBrush));
    }

    partial void OnSessionStateChanged(string value)
    {
        OnPropertyChanged(nameof(HasSessionState));
        OnPropertyChanged(nameof(SessionBadgeText));
        OnPropertyChanged(nameof(SessionBadgeBrush));
    }
}

using System;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using ZomboidGuide.Models;

namespace ZomboidGuide.ViewModels;

public sealed class CompanionTodoItemRowViewModel
{
    private static readonly IBrush PriorityCriticalBrush = Brush.Parse("#8A2D2D");
    private static readonly IBrush PriorityHighBrush = Brush.Parse("#8A5C2D");
    private static readonly IBrush PriorityMedBrush = Brush.Parse("#6C7A2E");
    private static readonly IBrush PriorityLowBrush = Brush.Parse("#355A3D");

    public required string Id { get; init; }

    public required string Title { get; init; }

    public required TodoPriority Priority { get; init; }

    public required string Category { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public bool IsPinned { get; init; }

    public bool IsDone { get; init; }

    public required ICommand PinCommand { get; init; }

    public required ICommand DoneCommand { get; init; }

    public required ICommand DismissCommand { get; init; }

    public required string PriorityText { get; init; }

    public IBrush PriorityBadgeBrush => Priority switch
    {
        TodoPriority.CRITICAL => PriorityCriticalBrush,
        TodoPriority.HIGH => PriorityHighBrush,
        TodoPriority.MED => PriorityMedBrush,
        _ => PriorityLowBrush,
    };

    public required string MetaText { get; init; }

    public required string PinButtonText { get; init; }

    public required string DoneButtonText { get; init; }

    public required string DismissButtonText { get; init; }

    public double RowOpacity => IsDone ? 0.6 : 1.0;

    public static ICommand BuildCommand(Action action)
    {
        return new RelayCommand(action);
    }
}

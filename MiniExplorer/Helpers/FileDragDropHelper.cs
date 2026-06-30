using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.Helpers;

public static class FileDragDropHelper
{
    private sealed class DragState
    {
        public Point StartPoint;
        public bool IsActive;
        public FileSystemEntry? OriginEntry;
        public List<string>? DragPaths;
    }

    public static void Attach(
        ListView listView,
        Func<ListView, Point, FileSystemEntry?> getEntryUnderMouse,
        Func<IReadOnlyList<string>> getSelectedPaths)
    {
        var state = new DragState();

        listView.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount > 1)
            {
                return;
            }

            state.StartPoint = e.GetPosition(null);
            state.IsActive = true;
            state.OriginEntry = getEntryUnderMouse(listView, e.GetPosition(listView));
            state.DragPaths = ResolveDragPaths(state.OriginEntry, getSelectedPaths());
        };

        listView.PreviewMouseLeftButtonUp += (_, _) =>
        {
            state.IsActive = false;
            state.DragPaths = null;
        };

        listView.PreviewMouseMove += (_, e) =>
        {
            if (!state.IsActive || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = e.GetPosition(null);
            if (Math.Abs(current.X - state.StartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - state.StartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            state.IsActive = false;

            var paths = state.DragPaths;
            state.DragPaths = null;

            if (paths is null || paths.Count == 0)
            {
                return;
            }

            var data = ClipboardService.CreateFileDataObject(paths);
            DragDrop.DoDragDrop(listView, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };
    }

    private static List<string>? ResolveDragPaths(FileSystemEntry? origin, IReadOnlyList<string> selectedPaths)
    {
        if (origin is null)
        {
            return null;
        }

        if (selectedPaths.Count > 0 &&
            selectedPaths.Any(path => string.Equals(path, origin.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return selectedPaths.ToList();
        }

        return [origin.FullPath];
    }
}

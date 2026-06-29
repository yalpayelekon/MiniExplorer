using System.Windows;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.Views;

public partial class OpenWithDialog : Window
{
    public OpenWithDialog(FileSystemEntry entry, IReadOnlyList<AssociatedApp> apps, ShellService shellService)
    {
        InitializeComponent();
        Title = $"Birlikte aç — {entry.Name}";
        AppsList.ItemsSource = apps;
        _entry = entry;
        _shellService = shellService;
    }

    private readonly FileSystemEntry _entry;
    private readonly ShellService _shellService;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (AppsList.SelectedItem is AssociatedApp app)
        {
            _shellService.OpenWith(_entry.FullPath, app.ExecutablePath);
            DialogResult = true;
        }
    }

    private void ChooseOther_Click(object sender, RoutedEventArgs e)
    {
        _shellService.ShowOpenWithDialog(_entry.FullPath);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

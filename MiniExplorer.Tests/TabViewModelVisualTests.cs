using System.Windows.Media;
using MiniExplorer.Models;
using MiniExplorer.ViewModels;

namespace MiniExplorer.Tests;

public sealed class TabViewModelVisualTests
{
    [Fact]
    public void TransferVisuals_PreservesAssetsWhenMetadataIsUnchanged()
    {
        var icon = new DrawingImage();
        var tileIcon = new DrawingImage();
        var thumbnail = new DrawingImage();
        var existing = CreateEntry(10, DateTime.UnixEpoch, icon, tileIcon, thumbnail);
        var updated = CreateEntry(10, DateTime.UnixEpoch);
        var loaderCalled = false;

        TabViewModel.TransferVisuals(existing, updated, _ =>
        {
            loaderCalled = true;
            return new DrawingImage();
        });

        Assert.False(loaderCalled);
        Assert.Same(icon, updated.Icon);
        Assert.Same(tileIcon, updated.TileIcon);
        Assert.Same(thumbnail, updated.Thumbnail);
    }

    [Fact]
    public void TransferVisuals_RefreshesIconAndClearsLargeAssetsWhenMetadataChanges()
    {
        var refreshedIcon = new DrawingImage();
        var existing = CreateEntry(
            10,
            DateTime.UnixEpoch,
            new DrawingImage(),
            new DrawingImage(),
            new DrawingImage());
        var updated = CreateEntry(20, DateTime.UnixEpoch.AddSeconds(1));
        bool? forceRefresh = null;

        TabViewModel.TransferVisuals(existing, updated, force =>
        {
            forceRefresh = force;
            return refreshedIcon;
        });

        Assert.True(forceRefresh);
        Assert.Same(refreshedIcon, updated.Icon);
        Assert.Null(updated.TileIcon);
        Assert.Null(updated.Thumbnail);
    }

    private static FileSystemEntry CreateEntry(
        long size,
        DateTime modified,
        ImageSource? icon = null,
        ImageSource? tileIcon = null,
        ImageSource? thumbnail = null) => new()
        {
            FullPath = @"C:\file.ico",
            Name = "file.ico",
            Extension = ".ico",
            Size = size,
            Modified = modified,
            Icon = icon,
            TileIcon = tileIcon,
            Thumbnail = thumbnail
        };
}

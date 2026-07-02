using MiniExplorer.Helpers;

namespace MiniExplorer.Tests;

public class FilePathHelperTests
{
    [Theory]
    [InlineData(@"C:\parent", @"C:\parent\child.txt", true)]
    [InlineData(@"C:\parent", @"C:\parent\child", true)]
    [InlineData(@"C:\parent", @"C:\parentother\child.txt", false)]
    [InlineData(@"C:\foo", @"C:\foobar\child.txt", false)]
    public void IsDirectChildPath_DetectsDirectChildren(string parent, string candidate, bool expected)
    {
        Assert.Equal(expected, FilePathHelper.IsDirectChildPath(parent, candidate));
    }

    [Theory]
    [InlineData(@"C:\folder\child.txt", @"C:\folder", true)]
    [InlineData(@"C:\folder\sub\child.txt", @"C:\folder", true)]
    [InlineData(@"C:\folder", @"C:\folder", true)]
    [InlineData(@"C:\foobar", @"C:\foo", false)]
    [InlineData(@"C:\other\file.txt", @"C:\folder", false)]
    public void IsInsideDirectory_DetectsContainmentWithoutPrefixFalsePositives(
        string candidate,
        string directory,
        bool expected)
    {
        Assert.Equal(expected, FilePathHelper.IsInsideDirectory(candidate, directory));
    }
}

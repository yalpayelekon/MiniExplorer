namespace MiniExplorer.Models;

public sealed class TabSession
{
    public List<string> TabPaths { get; set; } = [];
    public int ActiveTabIndex { get; set; }
}

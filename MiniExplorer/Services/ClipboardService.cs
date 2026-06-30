using System.IO;
using System.Text;
using System.Windows;

namespace MiniExplorer.Services;

public enum ClipboardOperation
{
    None,
    Copy,
    Cut
}

public sealed class ClipboardService
{
  public ClipboardOperation Operation { get; private set; } = ClipboardOperation.None;
  public IReadOnlyList<string> Paths { get; private set; } = Array.Empty<string>();

  public void Cut(IEnumerable<string> paths)
  {
    var list = paths.ToList();
    Paths = list;
    Operation = ClipboardOperation.Cut;
    SetWindowsClipboard(list, isCut: true);
  }

  public void Copy(IEnumerable<string> paths)
  {
    var list = paths.ToList();
    Paths = list;
    Operation = ClipboardOperation.Copy;
    SetWindowsClipboard(list, isCut: false);
  }

  public bool HasContent => Paths.Count > 0 && Operation != ClipboardOperation.None;

  public void Clear()
  {
    Paths = Array.Empty<string>();
    Operation = ClipboardOperation.None;
  }

  private static void SetWindowsClipboard(IReadOnlyList<string> paths, bool isCut)
  {
    if (paths.Count == 0)
    {
      return;
    }

    Clipboard.SetDataObject(CreateFileDataObject(paths, isCut), copy: true);
  }

  public static DataObject CreateFileDataObject(IReadOnlyList<string> paths, bool isCut = false)
  {
    if (paths.Count == 0)
    {
      throw new ArgumentException("At least one path is required.", nameof(paths));
    }

    var data = new DataObject();
    data.SetData("Preferred DropEffect", isCut ? new MemoryStream(Encoding.Unicode.GetBytes("2\0")) : new MemoryStream(Encoding.Unicode.GetBytes("5\0")));
    data.SetData(DataFormats.FileDrop, paths.ToArray());
    data.SetData("FileNameW", paths.ToArray());
    return data;
  }
}

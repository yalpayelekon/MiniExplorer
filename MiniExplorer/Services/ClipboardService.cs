using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
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

    var dropFiles = BuildDropFilesStructure(paths);
    var data = new System.Windows.DataObject();
    data.SetData("Preferred DropEffect", isCut ? new MemoryStream(Encoding.Unicode.GetBytes("2\0")) : new MemoryStream(Encoding.Unicode.GetBytes("5\0")));
    data.SetData(System.Windows.DataFormats.FileDrop, paths.ToArray());
    data.SetData("FileNameW", paths.ToArray());
    Clipboard.SetDataObject(data, copy: true);
  }

  private static byte[] BuildDropFilesStructure(IReadOnlyList<string> paths)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write(20);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);

    foreach (var path in paths)
    {
      var bytes = Encoding.Unicode.GetBytes(path + '\0');
      writer.Write(bytes);
    }

    writer.Write((ushort)0);
    return stream.ToArray();
  }
}

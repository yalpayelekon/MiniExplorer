# MiniExplorer

MiniExplorer is a lightweight, tabbed file explorer for Windows built with WPF
and .NET 10.

## Features

- Browse folders in multiple tabs with back, forward, up, and refresh navigation
- Restore open tabs between sessions
- Pin folders to Quick Access
- Filter the contents of the current folder
- Preview pictures in a thumbnail layout
- Copy, cut, paste, rename, and move items to the Recycle Bin
- Open files with their associated applications
- Open folders in Windows Explorer or Visual Studio Code
- Open supported files in Notepad++

## Requirements

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Getting started

Clone the repository and run the application:

```powershell
git clone https://github.com/yalpayelekon/MiniExplorer.git
cd MiniExplorer
dotnet run --project .\MiniExplorer\MiniExplorer.csproj
```

To build it without running:

```powershell
dotnet build .\MiniExplorer.slnx
```

## Project structure

MiniExplorer follows an MVVM-oriented structure:

- `Models` contains file-system and session data types.
- `ViewModels` contains navigation and UI state.
- `Views` contains application dialogs.
- `Services` handles file-system, shell, clipboard, session, and thumbnail work.
- `Helpers` and `Converters` contain Windows integration and WPF utilities.

## Contributing

Bug reports and pull requests are welcome. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.

## License

MiniExplorer is available under the [MIT License](LICENSE).

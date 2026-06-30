# Contributing to MiniExplorer

Thanks for taking the time to contribute.

## Development setup

MiniExplorer requires Windows and the .NET 10 SDK.

1. Fork and clone the repository.
2. Create a branch for your change.
3. Restore and build the solution:

   ```powershell
   dotnet restore .\MiniExplorer.slnx
   dotnet build .\MiniExplorer.slnx
   ```

4. Run the application and manually verify the affected file operations:

   ```powershell
   dotnet run --project .\MiniExplorer\MiniExplorer.csproj
   ```

5. Open a pull request with a concise explanation of the change.

## Guidelines

- Keep changes focused and consistent with the existing MVVM-oriented design.
- Test destructive file operations with disposable files and folders.
- Do not commit build output, local settings, or generated publish artifacts.
- Include reproduction steps for bug fixes and screenshots for visible UI
  changes when useful.

## Reporting bugs

When opening an issue, include the Windows version, .NET version, steps to
reproduce, expected behavior, and actual behavior. Remove personal file paths
or other sensitive information from screenshots and logs.

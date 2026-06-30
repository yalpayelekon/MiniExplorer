using System.Globalization;
using System.Resources;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class LocalizationService
{
    private static readonly ResourceManager ResourceManager = new(
        "MiniExplorer.Resources.Strings",
        typeof(LocalizationService).Assembly);

    public static LocalizationService Instance { get; } = new();

    public event EventHandler? LanguageChanged;

    public CultureInfo CurrentCulture { get; private set; } = new("tr");

    public static string Get(string key, params object[] args)
    {
        var format = ResourceManager.GetString(key, Instance.CurrentCulture) ?? key;
        return args.Length == 0 ? format : string.Format(Instance.CurrentCulture, format, args);
    }

    public void SetLanguage(LanguagePreset language)
    {
        CurrentCulture = language switch
        {
            LanguagePreset.English => new CultureInfo("en"),
            _ => new CultureInfo("tr")
        };

        CultureInfo.CurrentUICulture = CurrentCulture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsThisPcDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return string.Equals(text, Get("Common_ThisPc"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, ResourceManager.GetString("Common_ThisPc", new CultureInfo("tr")), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, ResourceManager.GetString("Common_ThisPc", new CultureInfo("en")), StringComparison.OrdinalIgnoreCase);
    }
}

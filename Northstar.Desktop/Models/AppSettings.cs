namespace Northstar.Desktop.Models;

public sealed class AppSettings
{
    public bool ShowHiddenFiles { get; set; }

    public string DefaultStartFolder { get; set; } = string.Empty;

    public string ThemeName { get; set; } = "Midnight";
}

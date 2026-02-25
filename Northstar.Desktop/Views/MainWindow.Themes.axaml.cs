using Avalonia;
using Avalonia.Media;
using System;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private void ApplyColorTheme(string? themeName)
    {
        var palette = BuildThemePalette(themeName);

        SetGradientResource("WindowBackgroundBrush", palette.WindowGradientStart, palette.WindowGradientEnd);

        SetSolidResource("ChromeBorderBrush", palette.ChromeBorder);
        SetSolidResource("ChromeBackgroundBrush", palette.ChromeBackground);
        SetSolidResource("ToolbarBackgroundBrush", palette.ToolbarBackground);
        SetSolidResource("ToolbarBorderBrush", palette.ToolbarBorder);

        SetSolidResource("NavBackgroundBrush", palette.NavBackground);
        SetSolidResource("NavHoverBackgroundBrush", palette.NavHover);
        SetSolidResource("NavPressedBackgroundBrush", palette.NavPressed);
        SetSolidResource("NavForegroundBrush", palette.NavForeground);
        SetSolidResource("NavBorderBrush", palette.NavBorder);

        SetSolidResource("SideItemForegroundBrush", palette.SideItemForeground);
        SetSolidResource("SideItemHoverBackgroundBrush", palette.SideItemHoverBackground);
        SetSolidResource("SideItemHoverBorderBrush", palette.SideItemHoverBorder);

        SetSolidResource("CrumbForegroundBrush", palette.CrumbForeground);
        SetSolidResource("CrumbHoverBackgroundBrush", palette.CrumbHoverBackground);
        SetSolidResource("CrumbSeparatorBrush", palette.CrumbSeparator);

        SetSolidResource("PathSurfaceBackgroundBrush", palette.PathSurfaceBackground);
        SetSolidResource("PathSurfaceBorderBrush", palette.PathSurfaceBorder);

        SetSolidResource("ColumnHeaderForegroundBrush", palette.ColumnHeaderForeground);
        SetSolidResource("ColumnHeaderHoverForegroundBrush", palette.ColumnHeaderHoverForeground);

        SetSolidResource("TerminalToggleBackgroundBrush", palette.TerminalToggleBackground);
        SetSolidResource("TerminalToggleHoverBackgroundBrush", palette.TerminalToggleHoverBackground);
        SetSolidResource("TerminalToggleForegroundBrush", palette.TerminalToggleForeground);
        SetSolidResource("TerminalToggleBorderBrush", palette.TerminalToggleBorder);

        SetSolidResource("EmbeddedTerminalBackgroundBrush", palette.EmbeddedTerminalBackground);
        SetSolidResource("EmbeddedTerminalBorderBrush", palette.EmbeddedTerminalBorder);
        SetSolidResource("TerminalOutputForegroundBrush", palette.TerminalOutputForeground);
        SetSolidResource("TerminalInputBackgroundBrush", palette.TerminalInputBackground);
        SetSolidResource("TerminalInputForegroundBrush", palette.TerminalInputForeground);
        SetSolidResource("TerminalInputBorderBrush", palette.TerminalInputBorder);

        SetSolidResource("PreviewPaneBackgroundBrush", palette.PreviewPaneBackground);
        SetSolidResource("PreviewPaneBorderBrush", palette.PreviewPaneBorder);
        SetSolidResource("PreviewTextBackgroundBrush", palette.PreviewTextBackground);
        SetSolidResource("PreviewTextBorderBrush", palette.PreviewTextBorder);
        SetSolidResource("PreviewTextForegroundBrush", palette.PreviewTextForeground);

        SetSolidResource("ListItemBackgroundBrush", palette.ListItemBackground);
        SetSolidResource("ListItemHoverBackgroundBrush", palette.ListItemHoverBackground);
        SetSolidResource("ListItemSelectedBackgroundBrush", palette.ListItemSelectedBackground);
        SetSolidResource("ListItemHoverNameForegroundBrush", palette.ListItemHoverNameForeground);

        SetSolidResource("SidebarPaneBackgroundBrush", palette.SidebarPaneBackground);
        SetSolidResource("SidebarPaneBorderBrush", palette.SidebarPaneBorder);
        SetSolidResource("ExplorerPaneBackgroundBrush", palette.ExplorerPaneBackground);
        SetSolidResource("StatusBarBackgroundBrush", palette.StatusBarBackground);
        SetSolidResource("StatusBarBorderBrush", palette.StatusBarBorder);
        SetSolidResource("StatusTextForegroundBrush", palette.StatusTextForeground);
    }

    private void SetSolidResource(string key, string color)
    {
        Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    private void SetGradientResource(string key, string startColor, string endColor)
    {
        Resources[key] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(startColor), 0),
                new GradientStop(Color.Parse(endColor), 1),
            },
        };
    }

    private static ThemePalette BuildThemePalette(string? themeName)
    {
        return (themeName ?? string.Empty).Trim() switch
        {
            "Graphite" => ThemePalette.Graphite,
            "One Dark" => ThemePalette.OneDark,
            "Emerald Night" => ThemePalette.EmeraldNight,
            "Ocean Deep" => ThemePalette.OceanDeep,
            "Daylight" => ThemePalette.Daylight,
            _ => ThemePalette.Midnight,
        };
    }

    private readonly record struct ThemePalette(
        string WindowGradientStart,
        string WindowGradientEnd,
        string ChromeBorder,
        string ChromeBackground,
        string ToolbarBackground,
        string ToolbarBorder,
        string NavBackground,
        string NavHover,
        string NavPressed,
        string NavForeground,
        string NavBorder,
        string SideItemForeground,
        string SideItemHoverBackground,
        string SideItemHoverBorder,
        string CrumbForeground,
        string CrumbHoverBackground,
        string CrumbSeparator,
        string PathSurfaceBackground,
        string PathSurfaceBorder,
        string ColumnHeaderForeground,
        string ColumnHeaderHoverForeground,
        string TerminalToggleBackground,
        string TerminalToggleHoverBackground,
        string TerminalToggleForeground,
        string TerminalToggleBorder,
        string EmbeddedTerminalBackground,
        string EmbeddedTerminalBorder,
        string TerminalOutputForeground,
        string TerminalInputBackground,
        string TerminalInputForeground,
        string TerminalInputBorder,
        string PreviewPaneBackground,
        string PreviewPaneBorder,
        string PreviewTextBackground,
        string PreviewTextBorder,
        string PreviewTextForeground,
        string ListItemBackground,
        string ListItemHoverBackground,
        string ListItemSelectedBackground,
        string ListItemHoverNameForeground,
        string SidebarPaneBackground,
        string SidebarPaneBorder,
        string ExplorerPaneBackground,
        string StatusBarBackground,
        string StatusBarBorder,
        string StatusTextForeground)
    {
        public static ThemePalette Midnight => new(
            "#10131A", "#0A0D12", "#2A3140", "#131923", "#1A2231", "#283246",
            "#222E42", "#2A3750", "#32415E", "#CED8E8", "#3A465D",
            "#D4DDF0", "#202B3D", "#2E3B54", "#DCE6F8", "#223048", "#71839E",
            "#0E131D", "#2A344A", "#97A2B7", "#C7D4EB",
            "#1C2738", "#233149", "#CFE0F6", "#35445D",
            "#08101A", "#283246", "#D7E6FF", "#0A111D", "#E8F2FF", "#1F3047",
            "#0D1522", "#2A344A", "#0A111C", "#2A344A", "#D6E4FA",
            "#121A28", "#1C273A", "#28405B", "#FFFFFF",
            "#161E2D", "#29334A", "#101823", "#0F1623", "#283246", "#6F84A4");

        public static ThemePalette Graphite => new(
            "#121212", "#0D0D0D", "#404040", "#1E1E1E", "#2A2A2A", "#3A3A3A",
            "#333333", "#404040", "#4D4D4D", "#E0E0E0", "#575757",
            "#D2D2D2", "#313131", "#454545", "#E6E6E6", "#3B3B3B", "#8F8F8F",
            "#161616", "#3A3A3A", "#A8A8A8", "#DDDDDD",
            "#2C2C2C", "#383838", "#F2F2F2", "#4A4A4A",
            "#141414", "#3A3A3A", "#E8E8E8", "#1C1C1C", "#F3F3F3", "#474747",
            "#1B1B1B", "#3A3A3A", "#181818", "#3A3A3A", "#ECECEC",
            "#232323", "#303030", "#3C3C3C", "#FFFFFF",
            "#262626", "#3A3A3A", "#202020", "#1E1E1E", "#3A3A3A", "#A0A0A0");

        public static ThemePalette OneDark => new(
            "#2B303B", "#21252B", "#3B4048", "#282C34", "#2D3139", "#3A404A",
            "#313640", "#3A404A", "#434A56", "#ABB2BF", "#4B5360",
            "#C0C7D4", "#343A45", "#424A57", "#D2DAE8", "#3A404A", "#5C6370",
            "#21252B", "#404754", "#7F8797", "#ABB2BF",
            "#3A404A", "#434A56", "#D7DEE9", "#4D5563",
            "#1F2329", "#3A404A", "#D7DEE9", "#21252B", "#E2E8F2", "#4B5360",
            "#232933", "#3B434F", "#1F252E", "#3B434F", "#D7DEE9",
            "#2C313A", "#353B45", "#3E4451", "#FFFFFF",
            "#282C34", "#3A404A", "#252A32", "#21252B", "#3A404A", "#6B7380");

        public static ThemePalette EmeraldNight => new(
            "#08140F", "#050C09", "#1F4537", "#0B1A14", "#11251D", "#204638",
            "#163528", "#1D4636", "#245744", "#CFEAD9", "#2A5C48",
            "#D3EEDC", "#143226", "#24503E", "#DFF6E7", "#1B4A37", "#6FA58F",
            "#0A1712", "#24503E", "#94C3AF", "#CBEBDD",
            "#153428", "#1D4636", "#D8F2E3", "#2A5C48",
            "#07130E", "#214536", "#D7F4E7", "#0D1D16", "#E7FAEF", "#24503E",
            "#0B1A14", "#204638", "#09150F", "#204638", "#DBF6E8",
            "#0F2119", "#173628", "#1E4635", "#FFFFFF",
            "#10231B", "#204638", "#0B1D16", "#0A1913", "#204638", "#75AB93");

        public static ThemePalette OceanDeep => new(
            "#081322", "#050A15", "#244868", "#0B1A2D", "#11243C", "#244A6C",
            "#183554", "#204466", "#2A547A", "#D2E6F8", "#2F6088",
            "#D5EAFE", "#17314D", "#275075", "#E1F0FF", "#21476A", "#7EA4C9",
            "#0A1628", "#2B5275", "#98B7D5", "#D4E7FB",
            "#1A3A5A", "#22476C", "#D9ECFF", "#2D5C85",
            "#081626", "#2A4F72", "#D8ECFF", "#0C1D31", "#E9F4FF", "#2B5479",
            "#0D1F34", "#2A4F72", "#0A1A2B", "#2A4F72", "#DAECFF",
            "#11253E", "#193654", "#0E2137", "#FFFFFF",
            "#102540", "#1B3E62", "#0E2137", "#0C1D31", "#2A4F72", "#7CA5CB");

        public static ThemePalette Daylight => new(
            "#F4F7FD", "#E8EEF9", "#B9C7DD", "#FDFEFF", "#F1F5FD", "#CBD7EA",
            "#E5ECF8", "#D8E3F4", "#CCD9EF", "#1F2C3E", "#B6C4DD",
            "#23344A", "#DCE7F8", "#C0D0E8", "#1F2F45", "#D6E4F7", "#6C7F9E",
            "#F5F9FF", "#C5D4EA", "#5A6E8B", "#344A67",
            "#DFE8F7", "#D1DDF2", "#274062", "#B7C9E4",
            "#EEF4FC", "#C7D5EA", "#1E314D", "#F8FBFF", "#1C314F", "#C1D3EB",
            "#F4F8FF", "#C8D6EB", "#F9FCFF", "#C8D6EB", "#2A3D57",
            "#EEF4FE", "#DBE6F6", "#D0DEF2", "#0F223D",
            "#F1F6FF", "#CAD8EE", "#EBF2FF", "#E2EBFA", "#C5D4EA", "#526B8B");
    }
}

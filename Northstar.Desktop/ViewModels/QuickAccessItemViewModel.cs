namespace Northstar.Desktop.ViewModels;

public sealed class QuickAccessItemViewModel
{
    public QuickAccessItemViewModel(string label, string fullPath, string glyph)
    {
        Label = label;
        FullPath = fullPath;
        Glyph = glyph;
    }

    public string Label { get; }

    public string FullPath { get; }

    public string Glyph { get; }
}

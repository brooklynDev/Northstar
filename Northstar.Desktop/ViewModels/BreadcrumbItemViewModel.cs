namespace Northstar.Desktop.ViewModels;

public sealed class BreadcrumbItemViewModel
{
    public BreadcrumbItemViewModel(string label, string fullPath)
    {
        Label = label;
        FullPath = fullPath;
    }

    public string Label { get; }

    public string FullPath { get; }
}

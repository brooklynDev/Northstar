using CommunityToolkit.Mvvm.ComponentModel;

namespace Northstar.Desktop.ViewModels;

public sealed partial class ExplorerTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string path;

    public ExplorerTabViewModel(string title, string path)
    {
        this.title = title;
        this.path = path;
    }
}

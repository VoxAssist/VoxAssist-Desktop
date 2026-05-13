using ReactiveUI;

namespace VoxAssist.Desktop.ViewModels;

public class AiProviderViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    private string _hostUrl = "";
    public string HostUrl { get => _hostUrl; set => this.RaiseAndSetIfChanged(ref _hostUrl, value); }

    private string _apiKey = "";
    public string ApiKey { get => _apiKey; set => this.RaiseAndSetIfChanged(ref _apiKey, value); }
}

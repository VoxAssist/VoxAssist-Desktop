using ReactiveUI;

namespace VoxAssist.Desktop.ViewModels;

public class LlmViewModel : ViewModelBase
{
    private string _providerName = "";
    public string ProviderName { get => _providerName; set => this.RaiseAndSetIfChanged(ref _providerName, value); }

    private string _model = "";
    public string Model { get => _model; set => this.RaiseAndSetIfChanged(ref _model, value); }

    private bool _isDefault;
    public bool IsDefault { get => _isDefault; set => this.RaiseAndSetIfChanged(ref _isDefault, value); }

    public string DisplayName => Model + (IsDefault ? " (default)" : "");
}

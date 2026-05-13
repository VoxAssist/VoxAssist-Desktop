namespace VoxAssist.Desktop.ViewModels;

public class MicDevice
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public override string ToString() => Description;
}

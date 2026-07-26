namespace CustomLauncher.Shared.Models;

public sealed class ShaderPackInfo
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsServerManaged { get; set; }
    public bool IsSelected { get; set; }
}

public sealed class ResourcePackInfo
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsServerManaged { get; set; }
    public bool IsEnabled { get; set; }
    public int Order { get; set; }
}

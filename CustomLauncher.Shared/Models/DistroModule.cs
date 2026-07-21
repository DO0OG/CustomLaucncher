namespace CustomLauncher.Shared.Models;

public enum ModuleType
{
    RequiredMod,
    OptionalMod,
    DropInMod,
    ShaderPack,
    ResourcePack,
}

/// <summary>Describes how the downloaded artifact is applied.</summary>
public enum ModulePackaging
{
    File,
    Archive,
}

public sealed class DistroModule
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public ModuleType Type { get; set; }
    public ModulePackaging Packaging { get; set; }
    public string? ParentId { get; set; }
    public int? LoadOrder { get; set; }
    public bool IsServerManaged { get; set; }
}

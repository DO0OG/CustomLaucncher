using System.ComponentModel;
using System.Runtime.CompilerServices;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ManifestTool;

/// <summary>
/// One server row. <see cref="ServerEntry"/> does not raise change notifications, so the list name
/// and the edit fields bind to this wrapper, which writes straight through to the entry.
/// </summary>
public sealed class ServerItem : INotifyPropertyChanged
{
    public ServerItem(ServerEntry entry)
    {
        Entry = entry;
        Entry.ModLoader ??= new ModLoaderSpec();
        Entry.Java ??= new JavaRequirement();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ServerEntry Entry { get; }

    public IReadOnlyList<string> LoaderTypes => ServerCatalog.LoaderTypes;

    public string Display => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";

    public string Id
    {
        get => Entry.Id;
        set { Entry.Id = value ?? string.Empty; Raise(); Raise(nameof(Display)); }
    }

    public string Name
    {
        get => Entry.Name;
        set { Entry.Name = value ?? string.Empty; Raise(); Raise(nameof(Display)); }
    }

    public string Address
    {
        get => Entry.Address;
        set { Entry.Address = value ?? string.Empty; Raise(); }
    }

    public int Port
    {
        get => Entry.Port;
        set { Entry.Port = value; Raise(); }
    }

    public string MinecraftVersion
    {
        get => Entry.MinecraftVersion;
        set { Entry.MinecraftVersion = value ?? string.Empty; Raise(); }
    }

    public string LoaderType
    {
        get => Entry.ModLoader.Type;
        set { Entry.ModLoader.Type = value ?? string.Empty; Raise(); }
    }

    public string LoaderVersion
    {
        get => Entry.ModLoader.Version;
        set { Entry.ModLoader.Version = value ?? string.Empty; Raise(); }
    }

    public string JavaRecommendedVersion
    {
        get => Entry.Java.RecommendedVersion;
        set { Entry.Java.RecommendedVersion = value ?? string.Empty; Raise(); }
    }

    public string JavaMinVersion
    {
        get => Entry.Java.MinVersion;
        set { Entry.Java.MinVersion = value ?? string.Empty; Raise(); }
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

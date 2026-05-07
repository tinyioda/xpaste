using CommunityToolkit.Mvvm.ComponentModel;
using xpaste.Models;

namespace xpaste.ViewModels;

/// <summary>
/// View-model representation of a single snippet, used by the management UI.
/// Holds the decrypted plain-text content in memory; content is only available
/// while the <see cref="xpaste.Services.SnippetStore"/> is unlocked.
/// </summary>
public partial class SnippetViewModel : ObservableObject
{
    /// <summary>Unique identifier matching the underlying <see cref="Snippet.Id"/>.</summary>
    [ObservableProperty] private Guid _id;

    /// <summary>Human-readable label displayed in the snippet list.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Hotkey slot (1–10, or 0 for unassigned).</summary>
    [ObservableProperty] private int _slot;

    /// <summary>Decrypted snippet content held in memory. Never written to disk in plain form.</summary>
    [ObservableProperty] private string _plainContent = string.Empty;

    /// <summary>Creates a <see cref="SnippetViewModel"/> from its persisted model and decrypted content.</summary>
    public static SnippetViewModel FromModel(Snippet meta, string plain)
        => new() { Id = meta.Id, Name = meta.Name, Slot = meta.Slot, PlainContent = plain };

    /// <summary>Projects back to a <see cref="Snippet"/> model (without encrypted fields — those are set by the store).</summary>
    public Snippet ToModel() => new() { Id = Id, Name = Name, Slot = Slot };
}

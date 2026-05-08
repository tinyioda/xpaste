using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xpaste.Services;

namespace xpaste.ViewModels;

/// <summary>
/// Represents a single entry in the hotkey-slot ComboBox.
/// </summary>
/// <param name="Value">Numeric slot value (0 = Unassigned, 1–10).</param>
/// <param name="Label">Human-readable label shown in the UI (e.g. <c>"Ctrl+Shift+2"</c>).</param>
public record SlotOption(int Value, string Label);

/// <summary>
/// Primary view-model for <see cref="xpaste.MainWindow"/>.
/// Manages the snippet list, inline edit form, auto-start toggle,
/// inline delete-confirmation overlay, and info/help overlay.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SnippetStore _store;

    /// <summary>Live collection of snippets bound to the management list.</summary>
    public ObservableCollection<SnippetViewModel> Snippets { get; } = new();

    /// <summary>All available hotkey slots for the edit-form ComboBox (0 = Unassigned, 1–10).</summary>
    public List<SlotOption> SlotOptions { get; } = Enumerable.Range(0, 11)
        .Select(i => new SlotOption(i, i == 0 ? "Unassigned" : $"Ctrl+Shift+{(i == 10 ? "0" : i.ToString())}"))
        .ToList();

    /// <summary>Whether xpaste is registered to launch automatically at Windows login.</summary>
    [ObservableProperty] private bool _autoStart;

    /// <summary>Controls whether the inline edit panel is visible instead of the snippet list.</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>Header text shown at the top of the edit panel ("New Snippet" or "Edit Snippet").</summary>
    [ObservableProperty] private string _editTitle = "New Snippet";

    /// <summary>ID of the snippet currently being edited (new GUID for a new snippet).</summary>
    [ObservableProperty] private Guid _editId;

    /// <summary>Bound to the Name field in the edit form.</summary>
    [ObservableProperty] private string _editName = "";

    /// <summary>Bound to the Content field in the edit form.</summary>
    [ObservableProperty] private string _editContent = "";

    /// <summary>Bound to the Slot ComboBox in the edit form.</summary>
    [ObservableProperty] private int _editSlot;

    /// <summary>Validation error message shown below the edit form fields. Empty when there is no error.</summary>
    [ObservableProperty] private string _editError = "";

    /// <summary>Inverse of <see cref="IsEditing"/>; used by the list panel's Visibility binding.</summary>
    public bool IsNotEditing => !IsEditing;

    /// <summary>Keeps <see cref="IsNotEditing"/> in sync whenever <see cref="IsEditing"/> changes.</summary>
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    /// <summary>True while the inline delete-confirmation overlay is visible.</summary>
    [ObservableProperty] private bool _isConfirmingDelete;

    /// <summary>Name of the snippet pending deletion, shown in the confirmation overlay.</summary>
    [ObservableProperty] private string _pendingDeleteName = "";

    private SnippetViewModel? _pendingDeleteTarget;

    /// <summary>True while the info/help overlay is visible.</summary>
    [ObservableProperty] private bool _isShowingInfo;

    /// <summary>True while the exit-confirmation overlay is visible.</summary>
    [ObservableProperty] private bool _isConfirmingExit;

    private bool _initComplete;

    /// <summary>
    /// Creates the view-model, reads the auto-start registry state, and loads all snippets.
    /// </summary>
    /// <param name="store">Unlocked snippet store to read from and write to.</param>
    public MainViewModel(SnippetStore store)
    {
        _store = store;
        AutoStart = StartupService.IsEnabled();
        Refresh();
        _initComplete = true;
    }

    /// <summary>Reloads the <see cref="Snippets"/> collection from the store (e.g. after save or delete).</summary>
    public void Refresh()
    {
        Snippets.Clear();
        foreach (var (meta, plain) in _store.Snippets)
            Snippets.Add(SnippetViewModel.FromModel(meta, plain));
    }

    /// <summary>Opens the edit panel pre-populated for a brand-new snippet.</summary>
    [RelayCommand]
    public void AddSnippet()
    {
        EditId = Guid.NewGuid();
        EditName = "";
        EditContent = "";
        EditSlot = 0;
        EditError = "";
        EditTitle = "New Snippet";
        IsEditing = true;
    }

    /// <summary>Opens the edit panel pre-populated with the fields of an existing snippet.</summary>
    [RelayCommand]
    public void EditSnippet(SnippetViewModel vm)
    {
        EditId = vm.Id;
        EditName = vm.Name;
        EditContent = vm.PlainContent;
        EditSlot = vm.Slot;
        EditError = "";
        EditTitle = "Edit Snippet";
        IsEditing = true;
    }

    /// <summary>Shows the inline confirmation overlay for deleting <paramref name="vm"/>.</summary>
    [RelayCommand]
    public void DeleteSnippet(SnippetViewModel vm)
    {
        _pendingDeleteTarget = vm;
        PendingDeleteName = vm.Name;
        IsConfirmingDelete = true;
    }

    /// <summary>Confirms the pending deletion and removes the snippet.</summary>
    [RelayCommand]
    public void ConfirmDelete()
    {
        if (_pendingDeleteTarget == null) { IsConfirmingDelete = false; return; }
        _store.Remove(_pendingDeleteTarget.Id);
        Snippets.Remove(_pendingDeleteTarget);
        _pendingDeleteTarget = null;
        IsConfirmingDelete = false;
    }

    /// <summary>Cancels the pending deletion and hides the overlay.</summary>
    [RelayCommand]
    public void CancelDelete()
    {
        _pendingDeleteTarget = null;
        IsConfirmingDelete = false;
    }

    /// <summary>
    /// Validates and saves the current edit-form fields. Shows an inline error if validation fails;
    /// otherwise persists the snippet and closes the edit panel.
    /// </summary>
    [RelayCommand]
    public void SaveEdit()
    {
        if (string.IsNullOrWhiteSpace(EditName))  { EditError = "Name is required."; return; }
        if (string.IsNullOrWhiteSpace(EditContent)) { EditError = "Content is required."; return; }

        var slotLabel = EditSlot == 10 ? "0" : EditSlot.ToString();
        if (EditSlot > 0 && Snippets.Any(s => s.Slot == EditSlot && s.Id != EditId))
        {
            EditError = $"Ctrl+Shift+{slotLabel} is already assigned.";
            return;
        }

        var vm = new SnippetViewModel { Id = EditId, Name = EditName.Trim(), PlainContent = EditContent, Slot = EditSlot };
        _store.AddOrUpdate(vm.ToModel(), vm.PlainContent);
        Refresh();
        IsEditing = false;
    }

    /// <summary>Discards any unsaved changes and returns to the snippet list.</summary>
    [RelayCommand]
    public void CancelEdit() => IsEditing = false;

    /// <summary>Toggles the info/help overlay.</summary>
    [RelayCommand]
    public void ToggleInfo() => IsShowingInfo = !IsShowingInfo;

    /// <summary>Shows the exit-confirmation overlay.</summary>
    [RelayCommand]
    public void RequestExit() => IsConfirmingExit = true;

    /// <summary>Confirmed exit — shuts down the application.</summary>
    [RelayCommand]
    public void ConfirmExit() => System.Windows.Application.Current.Shutdown();

    /// <summary>Cancels the exit request and hides the overlay.</summary>
    [RelayCommand]
    public void CancelExit() => IsConfirmingExit = false;

    /// <summary>Writes or removes the Windows registry run-key entry when the auto-start toggle changes.</summary>
    partial void OnAutoStartChanged(bool value)
    {
        if (!_initComplete) return;
        if (value) StartupService.Enable();
        else StartupService.Disable();
        (Application.Current as App)?.SyncStartupTrayItem(value);
    }
}

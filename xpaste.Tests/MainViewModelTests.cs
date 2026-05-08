using System.IO;
using xpaste.Models;
using xpaste.Services;
using xpaste.ViewModels;

namespace xpaste.Tests;

/// <summary>
/// Tests for MainViewModel. Each test uses a real SnippetStore backed by a temp file,
/// so no production data is touched and tests are fully isolated.
///
/// Not tested here (require live Windows APIs or WPF Application instance):
///   - AutoStart toggle (touches real registry via StartupService)
///   - ConfirmExitCommand (calls Application.Current.Shutdown())
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _tempFile;
    private readonly SnippetStore _store;
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"xpaste_vmtest_{Guid.NewGuid()}.json");
        _store = new SnippetStore(_tempFile);
        _store.Initialize("testpassword");
        _vm = new MainViewModel(_store);
    }

    public void Dispose() => File.Delete(_tempFile);

    // ── AddSnippet ───────────────────────────────────────────────────────────

    [Fact]
    public void AddSnippet_SetsIsEditingTrue()
    {
        _vm.AddSnippetCommand.Execute(null);
        Assert.True(_vm.IsEditing);
    }

    [Fact]
    public void AddSnippet_SetsEditTitleToNewSnippet()
    {
        _vm.AddSnippetCommand.Execute(null);
        Assert.Equal("New Snippet", _vm.EditTitle);
    }

    [Fact]
    public void AddSnippet_ClearsEditFields()
    {
        _vm.AddSnippetCommand.Execute(null);
        Assert.Equal("", _vm.EditName);
        Assert.Equal("", _vm.EditContent);
        Assert.Equal(0, _vm.EditSlot);
        Assert.Equal("", _vm.EditError);
    }

    // ── EditSnippet ──────────────────────────────────────────────────────────

    [Fact]
    public void EditSnippet_PopulatesFormFields()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "My PW", Slot = 2 }, "secret");
        _vm.Refresh();

        var snippetVm = _vm.Snippets.First(s => s.Id == id);
        _vm.EditSnippetCommand.Execute(snippetVm);

        Assert.True(_vm.IsEditing);
        Assert.Equal("Edit Snippet", _vm.EditTitle);
        Assert.Equal("My PW", _vm.EditName);
        Assert.Equal("secret", _vm.EditContent);
        Assert.Equal(2, _vm.EditSlot);
    }

    // ── SaveEdit validation ──────────────────────────────────────────────────

    [Fact]
    public void SaveEdit_EmptyName_SetsErrorAndStaysEditing()
    {
        _vm.AddSnippetCommand.Execute(null);
        _vm.EditName = "";
        _vm.EditContent = "content";
        _vm.SaveEditCommand.Execute(null);

        Assert.NotEmpty(_vm.EditError);
        Assert.True(_vm.IsEditing);
    }

    [Fact]
    public void SaveEdit_EmptyContent_SetsErrorAndStaysEditing()
    {
        _vm.AddSnippetCommand.Execute(null);
        _vm.EditName = "name";
        _vm.EditContent = "";
        _vm.SaveEditCommand.Execute(null);

        Assert.NotEmpty(_vm.EditError);
        Assert.True(_vm.IsEditing);
    }

    [Fact]
    public void SaveEdit_DuplicateSlot_SetsError()
    {
        // Add first snippet on slot 1
        _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "First", Slot = 1 }, "a");
        _vm.Refresh();

        // Try to add a second snippet on the same slot
        _vm.AddSnippetCommand.Execute(null);
        _vm.EditName = "Second";
        _vm.EditContent = "b";
        _vm.EditSlot = 1;
        _vm.SaveEditCommand.Execute(null);

        Assert.NotEmpty(_vm.EditError);
        Assert.True(_vm.IsEditing);
    }

    [Fact]
    public void SaveEdit_Valid_ClosesEditPanel()
    {
        _vm.AddSnippetCommand.Execute(null);
        _vm.EditName = "New Snippet";
        _vm.EditContent = "my content";
        _vm.EditSlot = 0;
        _vm.SaveEditCommand.Execute(null);

        Assert.False(_vm.IsEditing);
    }

    [Fact]
    public void SaveEdit_Valid_AddsToSnippetsList()
    {
        _vm.AddSnippetCommand.Execute(null);
        _vm.EditName = "Added";
        _vm.EditContent = "value";
        _vm.EditSlot = 0;
        _vm.SaveEditCommand.Execute(null);

        Assert.Contains(_vm.Snippets, s => s.Name == "Added");
    }

    [Fact]
    public void SaveEdit_Updating_DoesNotDuplicateSnippet()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Original", Slot = 1 }, "v");
        _vm.Refresh();
        var snippetVm = _vm.Snippets.First(s => s.Id == id);
        _vm.EditSnippetCommand.Execute(snippetVm);
        _vm.EditName = "Renamed";
        _vm.SaveEditCommand.Execute(null);

        Assert.Single(_vm.Snippets, s => s.Id == id);
    }

    // ── CancelEdit ───────────────────────────────────────────────────────────

    [Fact]
    public void CancelEdit_ClosesEditPanel()
    {
        _vm.AddSnippetCommand.Execute(null);
        _vm.CancelEditCommand.Execute(null);
        Assert.False(_vm.IsEditing);
    }

    // ── Delete overlay ───────────────────────────────────────────────────────

    [Fact]
    public void DeleteSnippet_ShowsConfirmOverlay()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "ToDelete", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));

        Assert.True(_vm.IsConfirmingDelete);
    }

    [Fact]
    public void DeleteSnippet_SetsPendingDeleteName()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Important PW", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));

        Assert.Equal("Important PW", _vm.PendingDeleteName);
    }

    [Fact]
    public void ConfirmDelete_RemovesSnippet()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "ToDelete", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));
        _vm.ConfirmDeleteCommand.Execute(null);

        Assert.DoesNotContain(_vm.Snippets, s => s.Id == id);
    }

    [Fact]
    public void ConfirmDelete_HidesOverlay()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "x", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));
        _vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(_vm.IsConfirmingDelete);
    }

    [Fact]
    public void CancelDelete_PreservesSnippet()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Keeper", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));
        _vm.CancelDeleteCommand.Execute(null);

        Assert.Contains(_vm.Snippets, s => s.Id == id);
    }

    [Fact]
    public void CancelDelete_HidesOverlay()
    {
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "x", Slot = 1 }, "x");
        _vm.Refresh();
        _vm.DeleteSnippetCommand.Execute(_vm.Snippets.First(s => s.Id == id));
        _vm.CancelDeleteCommand.Execute(null);

        Assert.False(_vm.IsConfirmingDelete);
    }

    // ── Info overlay ─────────────────────────────────────────────────────────

    [Fact]
    public void ToggleInfo_ShowsOverlay()
    {
        _vm.ToggleInfoCommand.Execute(null);
        Assert.True(_vm.IsShowingInfo);
    }

    [Fact]
    public void ToggleInfo_TogglesOverlayOff()
    {
        _vm.ToggleInfoCommand.Execute(null);
        _vm.ToggleInfoCommand.Execute(null);
        Assert.False(_vm.IsShowingInfo);
    }

    // ── Exit overlay ─────────────────────────────────────────────────────────

    [Fact]
    public void RequestExit_ShowsConfirmOverlay()
    {
        _vm.RequestExitCommand.Execute(null);
        Assert.True(_vm.IsConfirmingExit);
    }

    [Fact]
    public void CancelExit_HidesConfirmOverlay()
    {
        _vm.RequestExitCommand.Execute(null);
        _vm.CancelExitCommand.Execute(null);
        Assert.False(_vm.IsConfirmingExit);
    }

    // ── IsNotEditing ─────────────────────────────────────────────────────────

    [Fact]
    public void IsNotEditing_IsTrueByDefault()
    {
        Assert.True(_vm.IsNotEditing);
    }

    [Fact]
    public void IsNotEditing_IsFalseWhileEditing()
    {
        _vm.AddSnippetCommand.Execute(null);
        Assert.False(_vm.IsNotEditing);
    }
}

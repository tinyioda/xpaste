using System.IO;
using xpaste.Models;
using xpaste.Services;

namespace xpaste.Tests;

/// <summary>
/// Tests for SnippetStore. Each test gets its own temp file so tests are fully isolated
/// and never touch the real %AppData%\xpaste\snippets.json.
/// </summary>
public class SnippetStoreTests : IDisposable
{
    private readonly string _tempFile;
    private readonly SnippetStore _store;

    public SnippetStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"xpaste_test_{Guid.NewGuid()}.json");
        _store = new SnippetStore(_tempFile);
    }

    public void Dispose() => File.Delete(_tempFile);

    // ── HasStore ─────────────────────────────────────────────────────────────

    [Fact]
    public void HasStore_BeforeInit_ReturnsFalse()
    {
        Assert.False(_store.HasStore);
    }

    [Fact]
    public void HasStore_AfterInit_ReturnsTrue()
    {
        _store.Initialize("master");
        Assert.True(_store.HasStore);
    }

    // ── Initialize / Unlock ──────────────────────────────────────────────────

    [Fact]
    public void Initialize_StoreIsUnlocked()
    {
        _store.Initialize("master");
        Assert.True(_store.IsUnlocked);
    }

    [Fact]
    public void Unlock_CorrectPassword_ReturnsTrue()
    {
        _store.Initialize("master");
        var fresh = new SnippetStore(_tempFile);
        Assert.True(fresh.Unlock("master"));
    }

    [Fact]
    public void Unlock_WrongPassword_ReturnsFalse()
    {
        _store.Initialize("master");
        var fresh = new SnippetStore(_tempFile);
        Assert.False(fresh.Unlock("wrong"));
    }

    [Fact]
    public void Unlock_WrongPassword_DoesNotUnlock()
    {
        _store.Initialize("master");
        var fresh = new SnippetStore(_tempFile);
        fresh.Unlock("wrong");
        Assert.False(fresh.IsUnlocked);
    }

    // ── AddOrUpdate / Remove ─────────────────────────────────────────────────

    [Fact]
    public void AddOrUpdate_AddsNewSnippet()
    {
        _store.Initialize("master");
        _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "Test", Slot = 1 }, "content");
        Assert.Single(_store.Snippets);
    }

    [Fact]
    public void AddOrUpdate_UpdatesExistingSnippet()
    {
        _store.Initialize("master");
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Old", Slot = 1 }, "old");
        _store.AddOrUpdate(new Snippet { Id = id, Name = "New", Slot = 2 }, "new");
        Assert.Single(_store.Snippets);
        Assert.Equal("new", _store.Snippets[0].PlainContent);
        Assert.Equal("New", _store.Snippets[0].Meta.Name);
    }

    [Fact]
    public void AddOrUpdate_WhenLocked_Throws()
    {
        _store.Initialize("master");
        _store.Lock();
        Assert.Throws<InvalidOperationException>(() =>
            _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "x", Slot = 1 }, "y"));
    }

    [Fact]
    public void Remove_RemovesSnippet()
    {
        _store.Initialize("master");
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Test", Slot = 1 }, "content");
        _store.Remove(id);
        Assert.Empty(_store.Snippets);
    }

    [Fact]
    public void Remove_NonExistentId_DoesNotThrow()
    {
        _store.Initialize("master");
        _store.Remove(Guid.NewGuid()); // should not throw
    }

    // ── GetContentBySlot ─────────────────────────────────────────────────────

    [Fact]
    public void GetContentBySlot_AssignedSlot_ReturnsContent()
    {
        _store.Initialize("master");
        _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "pw", Slot = 3 }, "secret123");
        Assert.Equal("secret123", _store.GetContentBySlot(3));
    }

    [Fact]
    public void GetContentBySlot_UnassignedSlot_ReturnsNull()
    {
        _store.Initialize("master");
        Assert.Null(_store.GetContentBySlot(7));
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public void Snippets_SurviveRestartCycle()
    {
        _store.Initialize("master");
        var id = Guid.NewGuid();
        _store.AddOrUpdate(new Snippet { Id = id, Name = "Saved", Slot = 5 }, "my secret");

        // Simulate restart: new instance, same file
        var fresh = new SnippetStore(_tempFile);
        fresh.Unlock("master");

        Assert.Single(fresh.Snippets);
        Assert.Equal("my secret", fresh.Snippets[0].PlainContent);
        Assert.Equal("Saved", fresh.Snippets[0].Meta.Name);
        Assert.Equal(5, fresh.Snippets[0].Meta.Slot);
    }

    [Fact]
    public void MultipleSnippets_AllSurviveRoundtrip()
    {
        _store.Initialize("master");
        for (int i = 1; i <= 5; i++)
            _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = $"Snippet {i}", Slot = i }, $"content{i}");

        var fresh = new SnippetStore(_tempFile);
        fresh.Unlock("master");

        Assert.Equal(5, fresh.Snippets.Count);
        for (int i = 1; i <= 5; i++)
            Assert.Contains(fresh.Snippets, s => s.PlainContent == $"content{i}");
    }

    // ── ChangePassword ───────────────────────────────────────────────────────

    [Fact]
    public void ChangePassword_CorrectCurrentPassword_ReturnsTrue()
    {
        _store.Initialize("old");
        Assert.True(_store.ChangePassword("old", "new"));
    }

    [Fact]
    public void ChangePassword_WrongCurrentPassword_ReturnsFalse()
    {
        _store.Initialize("old");
        Assert.False(_store.ChangePassword("wrong", "new"));
    }

    [Fact]
    public void ChangePassword_SnippetsSurviveReencryption()
    {
        _store.Initialize("old");
        _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "pw", Slot = 1 }, "supersecret");
        _store.ChangePassword("old", "newpass");

        var fresh = new SnippetStore(_tempFile);
        Assert.True(fresh.Unlock("newpass"));
        Assert.Single(fresh.Snippets);
        Assert.Equal("supersecret", fresh.Snippets[0].PlainContent);
    }

    [Fact]
    public void ChangePassword_OldPasswordNoLongerWorks()
    {
        _store.Initialize("old");
        _store.ChangePassword("old", "new");

        var fresh = new SnippetStore(_tempFile);
        Assert.False(fresh.Unlock("old"));
    }

    // ── Lock ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Lock_ClearsIsUnlocked()
    {
        _store.Initialize("master");
        _store.Lock();
        Assert.False(_store.IsUnlocked);
    }

    [Fact]
    public void Lock_ClearsSnippetsFromMemory()
    {
        _store.Initialize("master");
        _store.AddOrUpdate(new Snippet { Id = Guid.NewGuid(), Name = "x", Slot = 1 }, "secret");
        _store.Lock();
        Assert.Empty(_store.Snippets);
    }
}

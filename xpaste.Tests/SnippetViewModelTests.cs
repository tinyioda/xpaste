using xpaste.Models;
using xpaste.ViewModels;

namespace xpaste.Tests;

public class SnippetViewModelTests
{
    [Fact]
    public void FromModel_MapsAllFields()
    {
        var id = Guid.NewGuid();
        var meta = new Snippet { Id = id, Name = "My Password", Slot = 3 };
        var vm = SnippetViewModel.FromModel(meta, "secret");

        Assert.Equal(id, vm.Id);
        Assert.Equal("My Password", vm.Name);
        Assert.Equal(3, vm.Slot);
        Assert.Equal("secret", vm.PlainContent);
    }

    [Fact]
    public void ToModel_MapsIdNameSlot()
    {
        var id = Guid.NewGuid();
        var vm = new SnippetViewModel { Id = id, Name = "Test", Slot = 7, PlainContent = "plain" };
        var model = vm.ToModel();

        Assert.Equal(id, model.Id);
        Assert.Equal("Test", model.Name);
        Assert.Equal(7, model.Slot);
    }

    [Fact]
    public void ToModel_DoesNotCopyEncryptedFields()
    {
        // Encrypted fields (CipherText, IV, Tag) are set by SnippetStore, not by the VM
        var vm = new SnippetViewModel { Id = Guid.NewGuid(), Name = "x", Slot = 1, PlainContent = "y" };
        var model = vm.ToModel();

        Assert.Equal(string.Empty, model.CipherText);
        Assert.Equal(string.Empty, model.IV);
        Assert.Equal(string.Empty, model.Tag);
    }
}

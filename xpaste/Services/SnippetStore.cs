using System.IO;
using System.Text.Json;
using xpaste.Models;

namespace xpaste.Services;

/// <summary>
/// Manages the encrypted snippet store on disk and exposes decrypted snippet content in memory
/// while the store is unlocked.
/// <para>
/// Data is persisted to <c>%AppData%\xpaste\snippets.json</c> as a <see cref="SnippetStoreFile"/>
/// containing the PBKDF2 salt, a password-verification blob, and AES-256-GCM encrypted snippets.
/// </para>
/// <para>
/// The master password and derived AES key are never written to disk. Calling <see cref="Lock"/>
/// zeroes the in-memory key reference, making decrypted content inaccessible until the next
/// successful <see cref="Unlock"/>.
/// </para>
/// </summary>
public class SnippetStore
{
    private static readonly string DefaultDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xpaste");
    private static readonly string DefaultDataFile =
        Path.Combine(DefaultDataDir, "snippets.json");

    private readonly string _dataDir;
    private readonly string _dataFile;

    /// <summary>Production constructor — uses <c>%AppData%\xpaste\snippets.json</c>.</summary>
    public SnippetStore() : this(DefaultDataFile) { }

    /// <summary>Testing constructor — uses the supplied file path instead of AppData.</summary>
    internal SnippetStore(string dataFile)
    {
        _dataFile = dataFile;
        _dataDir  = Path.GetDirectoryName(dataFile)!;
    }

    private byte[]? _key;
    private SnippetStoreFile _storeFile = new();

    /// <summary>In-memory decrypted snippets. Only populated while <see cref="IsUnlocked"/> is <c>true</c>.</summary>
    public List<(Snippet Meta, string PlainContent)> Snippets { get; private set; } = new();

    /// <summary><c>true</c> when the AES key is loaded in memory and snippets can be read/written.</summary>
    public bool IsUnlocked => _key != null;

    /// <summary><c>true</c> when a store file already exists on disk.</summary>
    public bool HasStore => File.Exists(_dataFile);

    /// <summary>
    /// First-time setup: derives a new AES key from <paramref name="masterPassword"/>,
    /// creates an empty store with a verification blob, and saves it to disk.
    /// </summary>
    public void Initialize(string masterPassword)
    {
        var salt = EncryptionService.GenerateSalt();
        _key = EncryptionService.DeriveKey(masterPassword, salt);
        var (vc, vi, vt) = EncryptionService.CreateVerification(_key);
        _storeFile = new SnippetStoreFile
        {
            Salt = Convert.ToBase64String(salt),
            VerifyCipherText = vc,
            VerifyIV = vi,
            VerifyTag = vt
        };
        Snippets = new();
        Save();
    }

    /// <summary>
    /// Attempts to unlock the store with <paramref name="masterPassword"/>.
    /// Derives the AES key, validates it against the stored verification blob,
    /// and decrypts all snippets into memory on success.
    /// </summary>
    /// <returns><c>true</c> on success; <c>false</c> if the password is incorrect.</returns>
    public bool Unlock(string masterPassword)
    {
        var json = File.ReadAllText(_dataFile);
        _storeFile = JsonSerializer.Deserialize<SnippetStoreFile>(json)!;
        var salt = Convert.FromBase64String(_storeFile.Salt);
        var key = EncryptionService.DeriveKey(masterPassword, salt);
        if (!EncryptionService.VerifyPassword(key, _storeFile.VerifyCipherText, _storeFile.VerifyIV, _storeFile.VerifyTag))
        {
            return false;
        }

        _key = key;
        Snippets = _storeFile.Snippets
            .Select(s => (s, EncryptionService.Decrypt(_key, s.CipherText, s.IV, s.Tag)))
            .ToList();
        return true;
    }

    /// <summary>
    /// Changes the master password by verifying <paramref name="currentPassword"/>, then
    /// re-deriving a new AES key from <paramref name="newPassword"/> and re-encrypting all
    /// snippets under that key. Saves immediately on success.
    /// </summary>
    /// <param name="currentPassword">The user's existing master password for verification.</param>
    /// <param name="newPassword">The new master password to apply.</param>
    /// <returns><c>true</c> on success; <c>false</c> if <paramref name="currentPassword"/> is incorrect.</returns>
    public bool ChangePassword(string currentPassword, string newPassword)
    {
        // Verify the current password against the stored verification blob
        var oldSalt = Convert.FromBase64String(_storeFile.Salt);
        var oldKey = EncryptionService.DeriveKey(currentPassword, oldSalt);
        if (!EncryptionService.VerifyPassword(oldKey, _storeFile.VerifyCipherText, _storeFile.VerifyIV, _storeFile.VerifyTag))
            return false;

        // Derive new key with a fresh salt
        var newSalt = EncryptionService.GenerateSalt();
        var newKey = EncryptionService.DeriveKey(newPassword, newSalt);
        var (vc, vi, vt) = EncryptionService.CreateVerification(newKey);

        // Re-encrypt every snippet under the new key
        _key = newKey;
        _storeFile.Salt = Convert.ToBase64String(newSalt);
        _storeFile.VerifyCipherText = vc;
        _storeFile.VerifyIV = vi;
        _storeFile.VerifyTag = vt;

        var reencrypted = new List<(Snippet Meta, string PlainContent)>();
        foreach (var (meta, plain) in Snippets)
        {
            var (c, i, t) = EncryptionService.Encrypt(newKey, plain);
            meta.CipherText = c;
            meta.IV = i;
            meta.Tag = t;
            reencrypted.Add((meta, plain));
        }
        Snippets = reencrypted;
        Save();
        return true;
    }

    /// <summary>Clears the in-memory AES key and decrypted snippet list, effectively locking the store.</summary>
    public void Lock()
    {
        _key = null;
        Snippets = new();
    }

    /// <summary>
    /// Adds a new snippet or updates an existing one (matched by <see cref="Snippet.Id"/>),
    /// encrypting <paramref name="plainContent"/> before writing to disk.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the store is locked.</exception>
    public void AddOrUpdate(Snippet meta, string plainContent)
    {
        if (_key == null) throw new InvalidOperationException("Store locked");
        var (c, i, t) = EncryptionService.Encrypt(_key, plainContent);
        meta.CipherText = c;
        meta.IV = i;
        meta.Tag = t;
        var idx = Snippets.FindIndex(x => x.Meta.Id == meta.Id);
        if (idx >= 0)
        {
            Snippets[idx] = (meta, plainContent);
        }
        else
        {
            Snippets.Add((meta, plainContent));
        }

        Save();
    }

    /// <summary>Removes the snippet with the given <paramref name="id"/> and saves the store.</summary>
    public void Remove(Guid id)
    {
        Snippets.RemoveAll(x => x.Meta.Id == id);
        Save();
    }

    /// <summary>
    /// Returns the decrypted content for the snippet assigned to <paramref name="slot"/>,
    /// or <c>null</c> if no snippet is assigned to that slot.
    /// </summary>
    public string? GetContentBySlot(int slot)
        => Snippets.FirstOrDefault(x => x.Meta.Slot == slot).PlainContent;

    private void Save()
    {
        if (_key == null) return;
        Directory.CreateDirectory(_dataDir);
        _storeFile.Snippets = Snippets.Select(x => x.Meta).ToList();
        File.WriteAllText(_dataFile, JsonSerializer.Serialize(_storeFile, new JsonSerializerOptions { WriteIndented = true }));
    }
}


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CiscoUpgradeOrchestrator.Gui.Models;

namespace CiscoUpgradeOrchestrator.Gui.Services;

/// <summary>
/// Cross-platform AES-256-GCM encrypted SSH credential vault adapted from Bulk Switch Backup.
/// Uses PBKDF2-SHA256 (600,000 iterations), atomic writes with file locking, and explicit memory zeroing.
/// Neither the master password nor the derived key is ever persisted to disk.
/// </summary>
public sealed class CredentialStore : IDisposable
{
    private const int Iterations = 600_000;
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("CiscoUpgradeOrchestrator vault v1");
    private readonly string _storagePath;
    private readonly object _gate = new();
    private byte[]? _key, _salt, _lastFileHash;
    private List<CredentialEntry> _entries = new();

    private string VaultPath => Path.Combine(_storagePath, "vault.json");

    public bool IsUnlocked { get { lock (_gate) return _key != null; } }
    public bool Exists => File.Exists(VaultPath);

    public CredentialStore(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CiscoUpgradeOrchestrator",
            "Credentials");
    }

    public void Create(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12)
            throw new ArgumentException("Master password must be at least 12 characters.");

        lock (_gate)
        {
            if (Exists)
                throw new InvalidOperationException("A vault already exists. Unlock it instead.");

            Lock();
            _salt = RandomNumberGenerator.GetBytes(32);
            _key = Derive(password, _salt);
            try
            {
                Persist(create: true);
            }
            catch
            {
                Lock();
                throw;
            }
        }
    }

    public void Unlock(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Master password is required.");

        lock (_gate)
        {
            Lock();
            byte[]? key = null, plain = null;
            try
            {
                if (!Exists)
                    throw new FileNotFoundException("Credential vault does not exist yet.");
                if (new FileInfo(VaultPath).Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("Vault file exceeds safety size limit.");

                var bytes = File.ReadAllBytes(VaultPath);
                var file = JsonSerializer.Deserialize<VaultFile>(bytes)
                    ?? throw new InvalidDataException("Invalid vault format.");

                if (file.Version != 1 || file.Iterations != Iterations ||
                    file.Salt?.Length != 32 || file.Nonce?.Length != 12 ||
                    file.Tag?.Length != 16 || file.Data == null)
                {
                    throw new InvalidDataException("Unsupported or damaged vault format.");
                }

                key = Derive(password, file.Salt);
                plain = new byte[file.Data.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(file.Nonce, file.Data, file.Tag, plain, Header);

                var entries = JsonSerializer.Deserialize<List<CredentialEntry>>(plain)
                    ?? throw new InvalidDataException("Invalid vault contents.");

                if (entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Name) ||
                                     e.Username == null || e.Password == null || e.Secret == null))
                {
                    throw new InvalidDataException("Invalid credential record inside vault.");
                }

                _entries = entries;
                _salt = file.Salt;
                _lastFileHash = SHA256.HashData(bytes);
                _key = key;
                key = null;
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Wrong master password or damaged vault. Existing data was preserved.");
            }
            finally
            {
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (plain != null) CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    public void Lock()
    {
        lock (_gate)
        {
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = null;
            _salt = null;
            _lastFileHash = null;
            _entries.Clear();
        }
    }

    public List<CredentialEntry> LoadAllCredentials()
    {
        lock (_gate)
        {
            return _key == null ? new List<CredentialEntry>() : _entries.Select(Copy).ToList();
        }
    }

    public void SaveCredential(string name, string username, string password, string secret = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Profile name and username are required.");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("SSH password is required.");

        secret ??= string.Empty;

        lock (_gate)
        {
            RequireUnlocked();
            var previous = _entries;
            _entries = _entries
                .Where(e => !string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(Copy)
                .ToList();
            _entries.Add(new CredentialEntry
            {
                Name = name.Trim(),
                Username = username.Trim(),
                Password = password,
                Secret = secret
            });

            try
            {
                Persist();
            }
            catch
            {
                _entries = previous;
                throw;
            }
        }
    }

    public void DeleteCredential(string name)
    {
        lock (_gate)
        {
            RequireUnlocked();
            var previous = _entries;
            _entries = _entries
                .Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(Copy)
                .ToList();

            try
            {
                Persist();
            }
            catch
            {
                _entries = previous;
                throw;
            }
        }
    }

    private void RequireUnlocked()
    {
        if (_key == null)
            throw new InvalidOperationException("Unlock the credential vault first.");
    }

    private void Persist(bool create = false)
    {
        RequireUnlocked();
        Directory.CreateDirectory(_storagePath);

        using var lease = new FileStream(
            Path.Combine(_storagePath, "vault.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        if (create ? Exists : !Exists || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(VaultPath)), _lastFileHash!))
        {
            throw new IOException("Vault changed in another session. Lock and unlock it before saving.");
        }

        var plain = JsonSerializer.SerializeToUtf8Bytes(_entries);
        var temp = Path.Combine(_storagePath, $"vault.{Guid.NewGuid():N}.tmp");
        try
        {
            var file = new VaultFile
            {
                Salt = _salt!,
                Nonce = RandomNumberGenerator.GetBytes(12),
                Tag = new byte[16],
                Data = new byte[plain.Length]
            };

            using var aes = new AesGcm(_key!, 16);
            aes.Encrypt(file.Nonce, plain, file.Data, file.Tag, Header);

            var bytes = JsonSerializer.SerializeToUtf8Bytes(file);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var output = new FileStream(temp, options))
            {
                output.Write(bytes);
                output.Flush(true);
            }

            File.Move(temp, VaultPath, overwrite: !create);
            _lastFileHash = SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static CredentialEntry Copy(CredentialEntry e) => new()
    {
        Name = e.Name,
        Username = e.Username,
        Password = e.Password,
        Secret = e.Secret
    };

    public void Dispose() => Lock();

    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public int Iterations { get; set; } = CredentialStore.Iterations;
        public byte[] Salt { get; set; } = Array.Empty<byte>();
        public byte[] Nonce { get; set; } = Array.Empty<byte>();
        public byte[] Tag { get; set; } = Array.Empty<byte>();
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}

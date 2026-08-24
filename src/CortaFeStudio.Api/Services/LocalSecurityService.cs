using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace CortaFeStudio.Api.Services;

public sealed class LocalSecurityService(IWebHostEnvironment environment, IDataProtectionProvider protection)
{
    private readonly string _storage = Path.Combine(environment.ContentRootPath, "storage");
    private readonly string _pinFile = Path.Combine(environment.ContentRootPath, "storage", "security.pin");
    private readonly IDataProtector _sessions = protection.CreateProtector("CortaFeStudio.Session.v1");
    public bool Enabled => File.Exists(_pinFile);
    public async Task ConfigurePinAsync(string pin) { if (pin.Length is < 4 or > 12 || !pin.All(char.IsDigit)) throw new InvalidOperationException("Use um PIN numérico de 4 a 12 dígitos."); var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 120_000, HashAlgorithmName.SHA256, 32); await File.WriteAllTextAsync(_pinFile, $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}"); }
    public bool VerifyPin(string pin) { if (!Enabled) return true; var parts = File.ReadAllText(_pinFile).Split(':'); if (parts.Length != 2) return false; var salt = Convert.FromBase64String(parts[0]); var expected = Convert.FromBase64String(parts[1]); var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 120_000, HashAlgorithmName.SHA256, 32); return CryptographicOperations.FixedTimeEquals(expected, actual); }
    public string CreateSession() => _sessions.Protect(DateTimeOffset.UtcNow.AddHours(12).ToString("O"));
    public bool ValidSession(string? value) { try { return value is not null && DateTimeOffset.Parse(_sessions.Unprotect(value)) > DateTimeOffset.UtcNow; } catch { return false; } }
    public async Task<string> CreateBackupAsync(string password)
    {
        if (password.Length < 8) throw new InvalidOperationException("A senha do backup precisa ter pelo menos 8 caracteres."); var backupDirectory = Path.Combine(_storage, "backups"); Directory.CreateDirectory(backupDirectory); var zip = Path.Combine(Path.GetTempPath(), $"amado-jesus-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { foreach (var file in new[] { "catalog.db", "editorial-feedback.json", "social/credentials.protected", "social/credentials.protected.history" }) { var source = Path.Combine(_storage, file.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(source)) archive.CreateEntryFromFile(source, file, CompressionLevel.Optimal); } foreach (var project in Directory.EnumerateFiles(Path.Combine(_storage, "projects"), "project.json", SearchOption.AllDirectories)) archive.CreateEntryFromFile(project, $"projects/{Path.GetFileName(Path.GetDirectoryName(project))}/project.json", CompressionLevel.Optimal); }
        var plain = await File.ReadAllBytesAsync(zip); File.Delete(zip); var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 180_000, HashAlgorithmName.SHA256, 32); var cipher = new byte[plain.Length]; var tag = new byte[16]; using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        var output = Path.Combine(backupDirectory, $"amado-jesus-backup-{DateTime.Now:yyyyMMdd-HHmmss}.cfbackup"); await using var stream = File.Create(output); await stream.WriteAsync(Encoding.ASCII.GetBytes("CORTAFE1")); await stream.WriteAsync(salt); await stream.WriteAsync(nonce); await stream.WriteAsync(tag); await stream.WriteAsync(cipher); return output;
    }
}

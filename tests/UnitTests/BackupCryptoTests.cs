using System.IO;
using System.Text;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class BackupCryptoTests
{
    private const int Fast = 1000; // the real setting is far slower on purpose; tests don't need that

    private static readonly byte[] Plain = Encoding.UTF8.GetBytes("my dog is called Biscuit and my notes are private");

    [Fact]
    public void RoundTripsWithTheRightPassword()
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        Assert.True(BackupCrypto.IsEncrypted(locked));
        Assert.Equal(Plain, BackupCrypto.Decrypt(locked, "correct horse"));
    }

    [Fact]
    public void TheLockedFileHoldsNoReadableText()
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        Assert.DoesNotContain("Biscuit", Encoding.UTF8.GetString(locked));
    }

    [Theory]
    [InlineData("wrong password")]
    [InlineData("")]
    [InlineData("Correct horse")]
    public void AWrongPasswordIsRefused(string password) =>
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(BackupCrypto.Encrypt(Plain, "correct horse", Fast), password));

    [Fact]
    public void EveryLockIsDifferentEvenForTheSameDataAndPassword()
    {
        var a = BackupCrypto.Encrypt(Plain, "pw-pw-pw", Fast);
        var b = BackupCrypto.Encrypt(Plain, "pw-pw-pw", Fast);
        Assert.NotEqual(a, b); // a fresh salt and nonce each time
    }

    [Theory]
    [InlineData(0)]   // the magic
    [InlineData(4)]   // the version
    [InlineData(6)]   // the iteration count
    [InlineData(12)]  // the salt
    [InlineData(30)]  // the nonce
    [InlineData(-1)]  // the last byte: the tag
    public void ChangingAnyPartOfTheFileIsDetected(int index)
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        locked[index < 0 ? locked.Length + index : index] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(locked, "correct horse"));
    }

    [Fact]
    public void ChangingTheEncryptedDataIsDetected()
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        locked[locked.Length / 2] ^= 0x80;
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(locked, "correct horse"));
    }

    [Fact]
    public void ATruncatedFileIsRefused()
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(locked[..(locked.Length - 5)], "correct horse"));
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(locked[..20], "correct horse"));
    }

    [Fact]
    public void AnAbsurdIterationCountInAForgedFileIsNotHonoured()
    {
        var locked = BackupCrypto.Encrypt(Plain, "correct horse", Fast);
        locked[5] = 0x7F; // a huge count: must be refused before any slow work starts
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(locked, "correct horse"));
        Assert.True(watch.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void ANonLockedFileIsNotMistakenForOne()
    {
        Assert.False(BackupCrypto.IsEncrypted("PK\u0003\u0004 just a zip, long enough to pass a length check......"u8));
        Assert.Throws<InvalidDataException>(() => BackupCrypto.Decrypt(new byte[100], "x"));
    }

    [Fact]
    public void ShortPasswordsAreRefusedButEmptyMeansNoLock()
    {
        Assert.NotNull(BackupCrypto.CheckPassword("abc"));
        Assert.Null(BackupCrypto.CheckPassword("abcdef"));
        Assert.Null(BackupCrypto.CheckPassword(""));
        Assert.Throws<ArgumentException>(() => BackupCrypto.Encrypt(Plain, "", Fast));
    }

    [Fact]
    public void UnicodePasswordsWork()
    {
        var locked = BackupCrypto.Encrypt(Plain, "கடவுச்சொல் 🔑", Fast);
        Assert.Equal(Plain, BackupCrypto.Decrypt(locked, "கடவுச்சொல் 🔑"));
    }
}

public class LockedBackupTests : DatabaseTestBase
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tessa-locked").FullName;

    private string Make(string? password)
    {
        var path = Path.Combine(_folder, password is null ? "plain.zip" : "locked.tessabak");
        var memory = new MemoryStore();
        memory.Remember("My dog is called Biscuit");
        var notes = Path.Combine(_folder, "notes.md");
        File.WriteAllText(notes, "- [ ] a private note\n");
        Backup.Create(path, new AppSettings { UserName = "Sam" }, memory, new RoutineStore(), new ReminderStore(), notes, new PlanStore(), password, iterations: 1000);
        return path;
    }

    [Fact]
    public void ALockedBackupNeedsItsPasswordAndHidesItsContents()
    {
        var path = Make("a good password");
        Assert.True(Backup.IsLocked(path));
        Assert.DoesNotContain("Biscuit", File.ReadAllText(path, Encoding.Latin1));

        Assert.Null(Backup.Inspect(path, out var error));
        Assert.Equal(Backup.PasswordRequired, error);

        Assert.Null(Backup.Inspect(path, out error, "wrong one"));
        Assert.Contains("Wrong password", error);

        var summary = Backup.Inspect(path, out _, "a good password")!;
        Assert.Equal(1, summary.Memories);
    }

    [Fact]
    public void ALockedBackupRestoresWithThePassword()
    {
        var path = Make("a good password");
        Database.Path = Path.Combine(_folder, "fresh.db");
        File.Delete(Path.Combine(_folder, "notes.md"));
        var memory = new MemoryStore();
        var settings = new AppSettings();

        Assert.Throws<InvalidDataException>(() => Backup.Restore(path, settings, memory, new RoutineStore(), new ReminderStore(), Path.Combine(_folder, "notes.md")));
        Assert.Empty(memory.List());

        Backup.Restore(path, settings, memory, new RoutineStore(), new ReminderStore(), Path.Combine(_folder, "notes.md"), null, new PlanStore(), "a good password");
        Assert.Equal("Sam", settings.UserName);
        Assert.Single(memory.List());
        Assert.Contains("private note", File.ReadAllText(Path.Combine(_folder, "notes.md")));
    }

    [Fact]
    public void APlainBackupStillOpensWithNoPassword()
    {
        var path = Make(null);
        Assert.False(Backup.IsLocked(path));
        Assert.NotNull(Backup.Inspect(path, out _));
    }

    [Fact]
    public void ATamperedLockedBackupIsRefused()
    {
        var path = Make("a good password");
        var bytes = File.ReadAllBytes(path);
        bytes[^20] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        Assert.Null(Backup.Inspect(path, out var error, "a good password"));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void AnEmptyPasswordSavesAPlainZip()
    {
        var path = Make("");
        Assert.False(Backup.IsLocked(path));
    }
}

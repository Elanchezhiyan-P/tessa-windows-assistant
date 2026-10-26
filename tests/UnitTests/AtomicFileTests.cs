using System.IO;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atomic").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void WritesAndReadsBack()
    {
        var path = Path.Combine(_dir, "a.json");
        AtomicFile.WriteAllText(path, "[1]");
        Assert.Equal("[1]", AtomicFile.ReadAllText(path));
    }

    [Fact]
    public void KeepsThePreviousVersionAsBackup()
    {
        var path = Path.Combine(_dir, "a.json");
        AtomicFile.WriteAllText(path, "[1]");
        AtomicFile.WriteAllText(path, "[2]");
        Assert.Equal("[1]", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void FallsBackToBackupWhenMainFileIsCorrupt()
    {
        var path = Path.Combine(_dir, "a.json");
        AtomicFile.WriteAllText(path, "[1]");
        AtomicFile.WriteAllText(path, "[2]");
        File.WriteAllText(path, "[2,"); // simulate a torn write
        Assert.Equal("[1]", AtomicFile.ReadAllText(path, AtomicFile.IsJson));
    }

    [Fact]
    public void MissingFileReadsAsNull() =>
        Assert.Null(AtomicFile.ReadAllText(Path.Combine(_dir, "none.json")));
}

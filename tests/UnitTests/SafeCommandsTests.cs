using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

public class SafeCommandsTests
{
    [Theory]
    [InlineData("Get-Date")]
    [InlineData("Get-Process | Sort-Object CPU | Select-Object -First 5")]
    [InlineData("Get-Volume | Select-Object DriveLetter, @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,1)}}")]
    [InlineData("hostname")]
    [InlineData("ipconfig /all")]
    public void ReadOnlyCommandsNeedNoApproval(string command)
    {
        Assert.True(SafeCommands.IsPowerShellSafe(command));
        Assert.False(SafeCommands.RequiresApproval(command, trusted: false));
    }

    [Theory]
    [InlineData("Get-ChildItem //evil/share")]
    [InlineData(@"Get-ChildItem \\evil\share")]
    [InlineData("Get-Date; Remove-Item C:/x")]
    [InlineData("Get-Date > out.txt")]
    [InlineData("Get-Process | ForEach-Object { $_ }")]
    [InlineData("ipconfig /release")]
    [InlineData("")]
    public void TrickyOrEmptyCommandsAreNotSafe(string command) =>
        Assert.False(SafeCommands.IsPowerShellSafe(command));

    [Theory]
    [InlineData("Move-Item a b")]
    [InlineData("Copy-Item a b")]
    [InlineData("New-Item x -ItemType File")]
    [InlineData("Rename-Item a b")]
    [InlineData("Add-Content a.txt hi")]
    [InlineData("Set-ItemProperty -Path HKCU:/Software/X -Name a -Value 1")]
    [InlineData("Start-Process notepad")]
    [InlineData("Invoke-Command -ScriptBlock x")]
    [InlineData("[IO.File]::Delete('a')")]
    [InlineData("Remove-Item a")]
    [InlineData("winget install foo")]
    [InlineData("cmd /c del a")]
    public void TrustedModeStillAsksForMutatingCommands(string command) =>
        Assert.True(SafeCommands.RequiresApproval(command, trusted: true));

    [Fact]
    public void UntrustedModeAsksForAnythingNotReadOnly() =>
        Assert.True(SafeCommands.RequiresApproval("Get-Content a.txt", trusted: false));

    [Fact]
    public void TrustedModeRunsHarmlessUnknownCommandsWithoutAsking() =>
        Assert.False(SafeCommands.RequiresApproval("Get-Content a.txt", trusted: true));
}

using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class SystemInfoTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(8 * Gb, 0, "qwen2.5:3b", false)]          // 4 GB usable on the processor
    [InlineData(16 * Gb, 0, "qwen2.5:7b", false)]         // 8 GB usable
    [InlineData(4 * Gb, 0, "qwen2.5:1.5b", false)]
    [InlineData(64 * Gb, 0, "qwen2.5:7b", false)]         // plenty of memory, but no graphics card: stay at 7B
    [InlineData(16 * Gb, 8 * Gb, "qwen2.5:7b", true)]     // fits in the graphics card
    [InlineData(32 * Gb, 12 * Gb, "qwen2.5:14b", true)]
    [InlineData(64 * Gb, 24 * Gb, "qwen2.5:32b", true)]
    [InlineData(16 * Gb, 2 * Gb, "qwen2.5:7b", false)]    // too little video memory to count
    public void PicksAModelThatFits(long ram, long vram, string model, bool onGpu)
    {
        var advice = ModelAdvice.Recommend(ram, vram);
        Assert.Equal(model, advice.Model);
        Assert.Equal(onGpu, advice.OnGpu);
    }

    [Fact]
    public void WarnsWhenThePcIsTooSmallForALocalModel() =>
        Assert.Contains("Gemini", ModelAdvice.Recommend(4 * Gb, 0).Text);

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5L * 1024 * 1024 + 512 * 1024, "5.5 MB")]
    [InlineData(16L * 1024 * 1024 * 1024, "16 GB")]
    public void FormatsSizes(long bytes, string expected) => Assert.Equal(expected, SystemInfo.FormatBytes(bytes));

    [Fact]
    public void DescribesEveryPart()
    {
        var specs = new SystemSpecs("Windows 11 24H2 (build 26100), 64-bit", "Test CPU", 8, 16, 640 * 1024, 10L * 1024 * 1024, 24L * 1024 * 1024,
            16 * Gb, 6 * Gb, [new GpuSpec("Test GPU", 8 * Gb), new GpuSpec("Integrated", 0)], [new DriveSpec("C:", 500 * Gb, 120 * Gb)]);

        var text = SystemInfo.Describe(specs);
        Assert.Contains("Test CPU (8 cores, 16 threads)", text);
        Assert.Contains("L1 640 KB · L2 10 MB · L3 24 MB", text);
        Assert.Contains("16 GB total, 6 GB free", text);
        Assert.Contains("Test GPU (8 GB)", text);
        Assert.Contains("Integrated (shares system memory)", text);
        Assert.Contains("Drive C: 500 GB, 120 GB free", text);
        Assert.Equal("Test GPU", SystemInfo.MainGpu(specs)!.Name);
    }

    [Fact]
    public void ReadsThisMachine()
    {
        var specs = SystemInfo.Collect();
        Assert.True(specs.RamTotalBytes > 0);
        Assert.True(specs.Threads >= 1);
        Assert.True(specs.Cores >= 1 && specs.Cores <= specs.Threads);
        Assert.False(string.IsNullOrWhiteSpace(specs.Os));
        Assert.NotEmpty(SystemInfo.Describe(specs));
    }
}

namespace WinCompanion.Services;

/// <summary>Per-user data folder (%AppData%\WinCompanion) for settings, history and logs.</summary>
public static class AppPaths
{
    public static string Dir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCompanion");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FileIn(string name) => Path.Combine(Dir, name);
}

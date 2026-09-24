using System.IO;

namespace Leap;

/// <summary>Log de diagnóstico em %APPDATA%\Leap\leap.log (limitado a ~1 MB).</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(ConfigStore.Dir, "leap.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.Dir);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 1_000_000)
                    file.Delete();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}

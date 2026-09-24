namespace Leap;

/// <summary>Consulta as janelas abertas do Windows Explorer via COM (Shell.Application).</summary>
public static class ExplorerWindows
{
    /// <summary>Pasta aberta na janela do Explorer com o HWND dado, ou null.</summary>
    public static string? PathOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    if (new IntPtr((long)window.HWND) != hwnd) continue;
                    var url = (string)window.LocationURL;
                    if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                        return new Uri(url).LocalPath;
                }
                catch
                {
                    // janela pode ter fechado no meio da iteração
                }
            }
        }
        catch
        {
        }
        return null;
    }
}

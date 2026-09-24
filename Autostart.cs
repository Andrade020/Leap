using System.IO;

namespace Leap;

/// <summary>Atalho na pasta de inicialização do usuário (shell:startup).</summary>
public static class Autostart
{
    private static string LnkPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Leap.lnk");

    public static bool IsEnabled => File.Exists(LnkPath);

    public static void Toggle()
    {
        if (IsEnabled)
        {
            File.Delete(LnkPath);
            return;
        }

        var exe = Environment.ProcessPath!;
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(LnkPath);
        lnk.TargetPath = exe;
        lnk.WorkingDirectory = Path.GetDirectoryName(exe);
        lnk.Description = "Leap — navegação rápida estilo NeoVim";
        lnk.Save();
    }
}

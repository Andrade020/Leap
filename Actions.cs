using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Leap;

public static class Actions
{
    // Terminal/editor reutilizam processos já em execução (wt.exe e code são
    // lançadores): a janela nova é criada por um processo antigo, que não herda
    // o direito de vir para o primeiro plano. ASFW_ANY concede esse direito a
    // qualquer processo enquanto o Leap ainda é a janela ativa.
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int AsfwAny = -1;

    /// <summary>Abre diretório no Explorer; arquivos/URLs/executáveis vão para o app padrão.</summary>
    public static void Open(string path)
    {
        AllowSetForegroundWindow(AsfwAny);
        if (Directory.Exists(path))
        {
            Log.Write($"abrindo no Explorer: {path}");
            Process.Start("explorer.exe", $"\"{Normalize(path)}\"");
            return;
        }
        // arquivo, URL ou nome de app (ex.: "notepad.exe", "wt.exe") — o shell resolve
        if (File.Exists(path) || path.Contains("://") || !path.Contains('\\'))
        {
            Log.Write($"abrindo com app padrão: {path}");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        throw new FileNotFoundException($"o caminho não existe mais:\n{path}");
    }

    public static void OpenTerminal(Config config, string path)
    {
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir))
            throw new DirectoryNotFoundException($"pasta não encontrada:\n{path}");
        dir = Normalize(dir);

        AllowSetForegroundWindow(AsfwAny);
        try
        {
            Log.Write($"abrindo terminal: {config.Terminal} {string.Format(config.TerminalArgs, dir)}");
            Process.Start(new ProcessStartInfo
            {
                FileName = config.Terminal,
                Arguments = string.Format(config.TerminalArgs, dir),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // terminal configurado não existe — cai para o PowerShell na pasta
            Log.Write($"terminal falhou ({ex.Message}); usando PowerShell");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = dir,
                UseShellExecute = true,
            });
        }
    }

    public static void OpenEditor(Config config, string path)
    {
        AllowSetForegroundWindow(AsfwAny);

        // pastas/projetos → editor de projeto (padrão: Neovim no Windows Terminal)
        if (Directory.Exists(path))
        {
            var args = string.Format(config.ProjectEditorArgs, Normalize(path));
            Log.Write($"abrindo projeto no editor: {config.ProjectEditor} {args}");
            Process.Start(new ProcessStartInfo
            {
                FileName = config.ProjectEditor,
                Arguments = args,
                UseShellExecute = true,
            });
            return;
        }

        // arquivos → editor nativo instantâneo do Leap
        if (config.Editor.Equals("leap", StringComparison.OrdinalIgnoreCase))
        {
            Log.Write($"abrindo no Leap Edit: {path}");
            QuickEditWindow.Open(path);
            return;
        }

        // ou o editor externo configurado (ShellExecute nem sempre resolve
        // comandos .cmd sem extensão, caso do "code" — resolvemos na mão)
        var exe = ResolveOnPath(config.Editor) ?? config.Editor;
        Log.Write($"abrindo editor: {exe} {string.Format(config.EditorArgs, Normalize(path))}");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = string.Format(config.EditorArgs, Normalize(path)),
            UseShellExecute = true,
        });
    }

    /// <summary>Executa um programa com argumentos (itens com "args" no config).</summary>
    public static void Run(string file, string args)
    {
        AllowSetForegroundWindow(AsfwAny);
        Log.Write($"executando: {file} {args}");
        Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = true,
        });
    }

    /// <summary>Executa um alias do modo comando ":" num PowerShell (terminal aberto por padrão).
    /// Dir do comando > pasta de contexto (navegador) > pasta do usuário.</summary>
    public static void RunCommand(LeapCommand cmd, string? contextDir = null)
    {
        AllowSetForegroundWindow(AsfwAny);
        var dir = Environment.ExpandEnvironmentVariables(
            cmd.Dir ?? contextDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var script = cmd.Run.Replace("\"", "\\\"");
        Log.Write($"comando '{cmd.Name}': {cmd.Run} (em {dir})");

        if (cmd.KeepOpen)
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{dir}\" powershell -NoExit -Command \"{script}\"",
                UseShellExecute = true,
            });
        else
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-WindowStyle Hidden -Command \"{script}\"",
                WorkingDirectory = dir,
                UseShellExecute = true,
            });
    }

    /// <summary>Barra final escaparia a aspa de fechamento nos argumentos ("C:\dir\").</summary>
    private static string Normalize(string path) =>
        path.Length > 3 ? path.TrimEnd('\\', '/') : path;

    private static string? ResolveOnPath(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains('\\')) return null;
        var exts = new[] { "", ".exe", ".cmd", ".bat" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

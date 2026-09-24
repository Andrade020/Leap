using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leap;

public class Config
{
    public string Hotkey { get; set; } = "Alt+Space";

    public string Terminal { get; set; } = "wt.exe";
    public string TerminalArgs { get; set; } = "-d \"{0}\"";

    /// <summary>Editor de ARQUIVOS. "leap" = editor nativo instantâneo do Leap.</summary>
    public string Editor { get; set; } = "leap";
    public string EditorArgs { get; set; } = "\"{0}\"";

    /// <summary>Editor de PASTAS/projetos (padrão: Neovim dentro do Windows Terminal).</summary>
    public string ProjectEditor { get; set; } = "wt.exe";
    public string ProjectEditorArgs { get; set; } = "-d \"{0}\" nvim .";

    /// <summary>Tecla simples numa pasta: "browse" navega dentro do Leap; "open" abre o Explorer.</summary>
    public string FolderAction { get; set; } = "browse";

    /// <summary>Raízes do índice de busca (aceita %VARIAVEIS%). Vazio = todos os drives fixos.</summary>
    public List<string> IndexRoots { get; set; } = new();

    public int IndexMaxDepth { get; set; } = 12;
    public int IndexMaxEntries { get; set; } = 2_000_000;
    public int IndexRefreshHours { get; set; } = 12;

    /// <summary>Nomes de pasta ignorados pelo índice, em qualquer nível.</summary>
    public List<string> IndexExclude { get; set; } = new()
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "AppData",
        "$Recycle.Bin", "System Volume Information", "node_modules", "bin", "obj",
        "__pycache__", "venv", ".venv", "packages", "dist", "target", "Temp", "cache",
    };

    /// <summary>CLI do Everything (voidtools); usado automaticamente se existir.</summary>
    public string EverythingCli { get; set; } = "es.exe";

    /// <summary>Zonas da tela, de cima para baixo (Pastas, Código, Apps...).</summary>
    public List<Section> Sections { get; set; } = new();

    /// <summary>Legado (v1): lista única de itens; migrada para Sections ao carregar.</summary>
    public List<LeapItem> Items { get; set; } = new();

    /// <summary>Marcas estilo vim: letra → caminho (criadas com m+letra, abertas com '+letra).</summary>
    public Dictionary<string, string> Marks { get; set; } = new();

    /// <summary>Aliases/macros do modo comando ":" — linhas de comando prontas.</summary>
    public List<LeapCommand> Commands { get; set; } = new();

    /// <summary>Versão do esquema do config (para migrações automáticas).</summary>
    public int Version { get; set; }
}

public class LeapCommand
{
    public string Name { get; set; } = "";    // alias curto, ex.: "gs"
    public string Label { get; set; } = "";   // descrição no painel
    public string Run { get; set; } = "";     // linha de comando (PowerShell)
    public string? Dir { get; set; }          // pasta de trabalho (aceita %VARS%)
    public bool KeepOpen { get; set; } = true; // terminal fica aberto mostrando o resultado
}

public class Section
{
    public string Title { get; set; } = "";
    public List<LeapItem> Items { get; set; } = new();
}

public class LeapItem
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Path { get; set; }

    /// <summary>Argumentos opcionais: a tecla executa Path com estes argumentos.</summary>
    public string? Args { get; set; }

    /// <summary>Ação da tecla sem modificador: null/"open" (padrão), "editor" ou "terminal".</summary>
    public string? Action { get; set; }

    public List<LeapItem>? Children { get; set; }

    [JsonIgnore]
    public bool IsGroup => Children is { Count: > 0 };
}

public static class ConfigStore
{
    public static string Dir =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leap");

    public static string FilePath => System.IO.Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Config LoadOrCreate()
    {
        Config cfg;
        if (File.Exists(FilePath))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), JsonOpts) ?? Default();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Erro ao ler {FilePath}:\n\n{ex.Message}\n\nUsando a configuração padrão por enquanto.",
                    "Leap", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return Default();
            }
        }
        else
        {
            cfg = Default();
            Save(cfg);
        }

        Migrate(cfg);
        return cfg;
    }

    /// <summary>Config v1 (lista única "items") vira o layout em seções.</summary>
    private static void Migrate(Config cfg)
    {
        var changed = false;

        if (cfg.Sections.Count == 0)
        {
            cfg.Sections.Add(new Section
            {
                Title = "Pastas",
                Items = cfg.Items.Count > 0 ? cfg.Items : DefaultFolderItems(),
            });
            cfg.Sections.Add(new Section { Title = "Código — direto no editor", Items = DefaultCodeItems() });
            cfg.Sections.Add(new Section { Title = "Apps", Items = DefaultAppItems() });
            cfg.Items = new();
            changed = true;
        }

        // v2 → v3: VS Code dá lugar ao editor nativo (arquivos) + Neovim (projetos)
        if (cfg.Editor == "code")
        {
            cfg.Editor = "leap";
            changed = true;
        }
        var nvim = FindNvim();
        if (nvim is not null && cfg.ProjectEditorArgs.Contains(" nvim ") )
        {
            cfg.ProjectEditorArgs = $"-d \"{{0}}\" \"{nvim}\" .";
            changed = true;
        }

        // v2: VS Code nos Apps, grupo virtual "Repos principais" e comandos padrão
        if (cfg.Version < 2)
        {
            var used = cfg.Sections.SelectMany(s => s.Items)
                .Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var reposRoot = System.IO.Path.Combine(desktop, "Repositorios");

            var codeExe = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe");
            var apps = cfg.Sections.FirstOrDefault(s =>
                s.Title.StartsWith("Apps", StringComparison.OrdinalIgnoreCase));
            if (File.Exists(codeExe) && apps is not null && !used.Contains("v"))
                apps.Items.Add(new LeapItem { Key = "v", Label = "VS Code", Path = codeExe });

            // pasta virtual: agrupa repos de qualquer lugar do disco num só painel
            if (Directory.Exists(reposRoot) && !used.Contains("r") && cfg.Sections.Count > 0)
            {
                var children = Directory.EnumerateDirectories(reposRoot)
                    .Take(9)
                    .Select((d, i) => new LeapItem
                    {
                        Key = (i + 1).ToString(),
                        Label = System.IO.Path.GetFileName(d),
                        Path = d,
                    })
                    .ToList();
                if (children.Count > 0)
                    cfg.Sections[0].Items.Add(new LeapItem
                    {
                        Key = "r",
                        Label = "Repos principais",
                        Children = children,
                    });
            }

            if (cfg.Commands.Count == 0)
            {
                var gitRepo = Directory.Exists(reposRoot)
                    ? Directory.EnumerateDirectories(reposRoot)
                        .FirstOrDefault(d => Directory.Exists(System.IO.Path.Combine(d, ".git")))
                    : null;
                if (gitRepo is not null)
                {
                    var repoName = System.IO.Path.GetFileName(gitRepo);
                    cfg.Commands.Add(new LeapCommand { Name = "gs", Label = $"git status — {repoName}", Run = "git status", Dir = gitRepo });
                    cfg.Commands.Add(new LeapCommand { Name = "gl", Label = $"git log — {repoName}", Run = "git log --oneline -15", Dir = gitRepo });
                    cfg.Commands.Add(new LeapCommand { Name = "gp", Label = $"git pull — {repoName}", Run = "git pull", Dir = gitRepo });
                }
                cfg.Commands.Add(new LeapCommand { Name = "ip", Label = "IP da máquina", Run = "ipconfig | Select-String IPv4" });
            }

            cfg.Version = 2;
            changed = true;
        }

        // v3: comandos git viram contextuais (rodam na pasta em que você está)
        if (cfg.Version < 3)
        {
            foreach (var c in cfg.Commands)
            {
                if (c.Name is "gs" or "gl" or "gp")
                {
                    c.Dir = null; // null = pasta do contexto atual
                    c.Label = c.Name switch
                    {
                        "gs" => "git status (pasta atual)",
                        "gl" => "git log (pasta atual)",
                        _ => "git pull (pasta atual)",
                    };
                }
            }
            cfg.Version = 3;
            changed = true;
        }

        if (changed) Save(cfg);
    }

    /// <summary>Caminho do nvim.exe (PATH ou instalação user-local feita pelo Leap).</summary>
    public static string? FindNvim()
    {
        var local = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "nvim", "nvim-win64", "bin", "nvim.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir.Trim(), "nvim.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOpts));
    }

    private static Config Default() => new() { Items = DefaultFolderItems() };

    private static List<LeapItem> DefaultFolderItems()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return new List<LeapItem>
        {
            new() { Key = "1", Label = "Repositórios", Path = System.IO.Path.Combine(desktop, "Repositorios") },
            new() { Key = "2", Label = "Downloads", Path = System.IO.Path.Combine(home, "Downloads") },
            new() { Key = "3", Label = "Área de Trabalho", Path = desktop },
            new() { Key = "4", Label = "Documentos", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        };
    }

    private static List<LeapItem> DefaultCodeItems()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return new List<LeapItem>
        {
            new()
            {
                Key = "e", Label = "Explorer (projeto Leap)",
                Path = System.IO.Path.Combine(desktop, "Repositorios", "Explorer"),
                Action = "editor",
            },
        };
    }

    private static List<LeapItem> DefaultAppItems() => new()
    {
        new() { Key = "n", Label = "Bloco de Notas", Path = "notepad.exe" },
        new() { Key = "t", Label = "Terminal", Path = "wt.exe" },
        new() { Key = "g", Label = "Google", Path = "https://google.com" },
    };
}

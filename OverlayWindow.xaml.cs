using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Leap;

public record CardVm(string KeyDisplay, string Label, string Sub, string BadgeColor, LeapItem Item);
/// <summary>Kind: 0 = arquivo, 1 = diretório, 2 = "abrir esta pasta no Explorer".</summary>
public record ResultVm(string Name, string Path, bool Selected, long Hwnd = 0, int Kind = 0);
public record SectionVm(string Title, List<CardVm> Cards);
public record TipVm(string Keys, string Desc);

public partial class OverlayWindow : Window
{
    private const string ChordHints =
        "tecla: navegar / abrir   ·   Ctrl: terminal   ·   Alt: editor   ·   Shift: copiar   ·   /: buscar   ·   b: janelas   ·   :: comandos   ·   ?: ajuda completa   ·   Esc: fechar";
    private const string SearchHints =
        "↑↓ ou Ctrl+J/K: navegar   ·   Enter: abrir   ·   Ctrl+Enter: terminal   ·   Alt+Enter: editor   ·   Shift+Enter: copiar   ·   Esc: voltar";
    private const string WindowHints =
        "↑↓ ou Ctrl+J/K: navegar   ·   Enter: focar a janela   ·   Esc: voltar";
    private const string BrowseHints =
        "digite: filtrar   ·   Enter: entrar na pasta / abrir arquivo   ·   Backspace: subir   ·   Ctrl+Enter: terminal   ·   Alt+Enter: editor   ·   Shift+Enter: copiar   ·   Ctrl+P: fixar esta pasta   ·   Esc: voltar";
    private const string CommandHints =
        "digite: filtrar   ·   Enter: executar no terminal   ·   Esc: voltar   —   crie os seus no config.json (campo \"commands\")";

    private enum SearchTarget { Files, Windows, Browse, Commands }
    private bool _helpMode;

    private static readonly TipVm[] HelpRows =
    {
        new("Alt+Espaço", "abre/fecha o Leap de qualquer lugar do Windows"),
        new("tecla do cartão", "pasta: navega dentro do Leap · app/URL: abre · verde: abre no editor"),
        new("Ctrl + tecla", "abre no terminal (Windows Terminal na pasta)"),
        new("Alt + tecla", "abre no editor — pasta: Neovim · arquivo: Leap Edit"),
        new("Shift + tecla", "copia o caminho para a área de transferência"),
        new("Backspace", "volta um nível (submenu ou pasta)"),
        new("/", "busca fuzzy no PC inteiro (índice próprio + favoritos + frecência)"),
        new("b", "troca de janela: lista tudo aberto, filtra fuzzy, Enter foca"),
        new(":", "comandos: aliases (gs, gl, gp) OU linha livre (\":code .\") — dentro de uma pasta, roda nela"),
        new("x + tecla", "remove o atalho ou a marca do painel"),
        new("m + letra", "marca a pasta do Explorer em primeiro plano (vim marks)"),
        new("' + letra", "abre o painel de marcas e pula (modificadores valem)"),
        new(";", "repete a última ação (mesmo destino, mesmo modificador)"),
        new("+", "fixa a pasta do Explorer em primeiro plano num slot livre"),
        new("?", "esta ajuda"),
        new("— na busca / navegador —", ""),
        new("↑↓ ou Ctrl+J/K", "navega nos resultados"),
        new("Enter", "abre · Ctrl+Enter terminal · Alt+Enter editor · Shift+Enter copia"),
        new("Ctrl+P (navegador)", "fixa a pasta atual como novo atalho"),
        new("⏎ primeira linha", "no navegador: abre a pasta atual no Windows Explorer"),
        new("— bandeja (ícone) —", ""),
        new("Editar configuração", "abre o config.json (seções, grupos virtuais com \"children\", comandos)"),
        new("Reconstruir índice", "força a re-varredura do disco para a busca /"),
        new("Iniciar com o Windows", "liga/desliga a inicialização automática"),
        new("— no Neovim —", ""),
        new(":Tutor", "tutorial oficial · Espaço = líder · Espaço+w salva · Espaço+q sai"),
    };

    private Config _config = new();
    private readonly List<(string Crumb, List<LeapItem> Items)> _stack = new();
    private IntPtr _prevForeground;

    private readonly EverythingSearch _everything = new();
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _searchCts;
    private bool _searchMode;
    private SearchTarget _searchTarget = SearchTarget.Files;
    private string _browseDir = "";
    private List<WindowInfo> _windows = new();
    private List<ResultVm> _results = new();
    private int _selected;

    private bool _pendingMark;
    private bool _pendingDelete;
    private string? _pendingMarkPath;
    private string? _commandDir;
    private static (string Path, ModifierKeys Mods, string? Action)? _lastAction;

    // ---------- escola de vim ----------

    private static int _tipOffset;
    private const int TipsPerOpen = 4;
    private static readonly TipVm[] VimTips =
    {
        new("hjkl", "← ↓ ↑ → sem sair da home row — as setas do vim"),
        new("w · b · e", "pula palavra a palavra (3w pula três de uma vez)"),
        new("gg · G", "vai ao topo · ao fim do arquivo"),
        new("0 · $", "início · fim da linha"),
        new("f{c} · ;", "voa até o caractere c na linha; ; repete o salto"),
        new("ciw", "change inner word — reescreve a palavra sob o cursor"),
        new("di( · da\"", "apaga dentro dos ( ) · em volta das aspas"),
        new("yy · p", "copia a linha · cola abaixo"),
        new("dd", "apaga a linha inteira"),
        new("u · Ctrl+r", "desfaz · refaz"),
        new(".", "repete a última edição — o feitiço mais poderoso do vim"),
        new("o · O", "nova linha abaixo · acima, já em modo inserção"),
        new("%", "salta entre ( ) { } correspondentes"),
        new("* · #", "busca a palavra sob o cursor para frente · para trás"),
        new(":%s/a/b/g", "substitui a por b no arquivo inteiro"),
        new("V · Ctrl+v", "seleção por linha · por bloco (colunas)"),
        new("zz", "centraliza a tela no cursor"),
        new("Ctrl+o · Ctrl+i", "volta · avança no histórico de saltos"),
    };

    public OverlayWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => { if (IsVisible) HideOverlay(); };

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };
    }

    public void ShowOverlay(Config config, IntPtr prevForeground)
    {
        _config = config;
        _prevForeground = prevForeground;
        _pendingMark = false;
        _pendingDelete = false;
        _helpMode = false;
        HelpScroll.Visibility = Visibility.Collapsed;
        FileIndex.Shared.EnsureFresh(config);

        _stack.Clear();
        _stack.Add(("Leap", AllRootItems()));
        _tipOffset = (_tipOffset + TipsPerOpen) % VimTips.Length; // leva nova de dicas
        ExitSearch(render: false);
        Render();

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        Show();
        Activate();
        ForceForeground();
        Keyboard.Focus(this);

        // se mesmo assim o Windows negar o foco, teclas vazariam para o app de
        // trás (Esc fechando outras janelas!) — nesse caso é mais seguro recuar
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!IsVisible) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (GetForegroundWindow() != hwnd)
            {
                ForceForeground();
                if (GetForegroundWindow() != hwnd)
                {
                    Log.Write("AVISO: não consegui tomar o foco do teclado — fechando o overlay para não vazar teclas");
                    HideOverlay();
                }
            }
        });
    }

    public void HideOverlay() => Hide();

    // ---------- foco à força (Win32) ----------

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>SetForegroundWindow com o truque de AttachThreadInput, para o
    /// overlay SEMPRE ter o teclado — senão as teclas vazam para o app de trás.</summary>
    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var fg = GetForegroundWindow();
        if (fg == hwnd || hwnd == IntPtr.Zero) return;

        var fgThread = GetWindowThreadProcessId(fg, out _);
        var ourThread = GetCurrentThreadId();
        var attached = fgThread != ourThread && AttachThreadInput(fgThread, ourThread, true);
        try
        {
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(fgThread, ourThread, false);
        }
    }

    // ---------- modo acordes ----------

    private List<LeapItem> AllRootItems() =>
        _config.Sections.SelectMany(s => s.Items).ToList();

    private void Render()
    {
        Breadcrumb.Text = string.Join("  ▸  ", _stack.Select(s => s.Crumb));
        Footer.Text = ChordHints;
        SearchPanel.Visibility = Visibility.Collapsed;

        if (_stack.Count == 1)
        {
            // raiz: zonas espalhadas de cima para baixo + escola de vim embaixo
            SectionsScroll.Visibility = Visibility.Visible;
            ChordScroll.Visibility = Visibility.Collapsed;
            TipsPanel.Visibility = Visibility.Visible;
            SectionsHost.ItemsSource = _config.Sections
                .Select(s => new SectionVm(s.Title.ToUpperInvariant(), s.Items.Select(ToVm).ToList()))
                .ToList();
            TipsHost.ItemsSource = Enumerable.Range(0, TipsPerOpen)
                .Select(i => VimTips[(_tipOffset + i) % VimTips.Length])
                .ToList();
        }
        else
        {
            // submenu: painel único centralizado, estilo which-key
            SectionsScroll.Visibility = Visibility.Collapsed;
            ChordScroll.Visibility = Visibility.Visible;
            TipsPanel.Visibility = Visibility.Collapsed;
            ItemsHost.ItemsSource = _stack[^1].Items.Select(ToVm).ToList();
        }
    }

    private static CardVm ToVm(LeapItem item) => new(
        KeyDisplay: item.Key.ToUpperInvariant(),
        Label: item.Label,
        Sub: item.IsGroup ? $"▸ {item.Children!.Count} atalhos"
            : item.Action == "editor" ? $"abre no editor · {item.Path}"
            : item.Action == "terminal" ? $"abre no terminal · {item.Path}"
            : item.Path ?? "",
        BadgeColor: item.IsGroup ? "#8957E5"
            : item.Action == "editor" ? "#2EA043"
            : item.Action == "terminal" ? "#2EA043"
            : IsAppLike(item.Path) ? "#D29922"
            : "#1F6FEB",
        Item: item);

    private static bool IsAppLike(string? path) =>
        path is not null && (path.Contains("://") || !path.Contains('\\'));

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // com Alt pressionado o WPF entrega Key.System; a tecla real fica em SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt))
            Log.Write($"tecla: {key} mods=[{Keyboard.Modifiers}] busca={_searchMode}");

        if (_helpMode)
        {
            if (key is Key.Escape or Key.Back) CloseHelp();
            e.Handled = true;
            return;
        }

        if (_searchMode)
        {
            HandleSearchKey(key, e);
            return;
        }

        e.Handled = true;
        switch (key)
        {
            case Key.Escape:
                if (_pendingMark || _pendingDelete) { _pendingMark = _pendingDelete = false; Render(); }
                else HideOverlay();
                return;
            case Key.Back:
                if (_stack.Count > 1) { _stack.RemoveAt(_stack.Count - 1); Render(); }
                else HideOverlay();
                return;
        }

        var pressed = KeyToString(key);
        if (pressed is null)
        {
            e.Handled = false; // deixa chegar no PreviewTextInput ("/", "+", "'", ";")
            return;
        }

        if (_pendingMark)
        {
            _pendingMark = false;
            SetMark(pressed);
            return;
        }

        if (_pendingDelete)
        {
            _pendingDelete = false;
            DeleteByKey(pressed);
            return;
        }

        var item = _stack[^1].Items
            .FirstOrDefault(i => string.Equals(i.Key, pressed, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            ActivateItem(item, Keyboard.Modifiers);
            return;
        }

        // letra sem item: pode ser gatilho especial (b, m) — segue para o TextInput
        e.Handled = false;
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_searchMode)
        {
            // ":" no navegador de pastas vira prompt de comando NAQUELA pasta
            // (":" nunca é válido em nomes de arquivo do Windows)
            if (_searchTarget == SearchTarget.Browse && e.Text == ":")
            {
                _commandDir = _browseDir;
                EnterSearch(SearchTarget.Commands);
                e.Handled = true;
            }
            return;
        }
        if (_pendingMark || _pendingDelete) { e.Handled = true; return; } // letras tratadas no KeyDown

        switch (e.Text)
        {
            case "/": EnterSearch(SearchTarget.Files); break;
            case "b": EnterSearch(SearchTarget.Windows); break;
            case ":": _commandDir = null; EnterSearch(SearchTarget.Commands); break;
            case "?": ShowHelp(); break;
            case "+" or "=": PinForegroundExplorerFolder(); break;
            case "m": BeginMark(); break;
            case "x": BeginDelete(); break;
            case "'" or "`" or "´": ShowMarks(); break;
            case ";": RepeatLast(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- remoção (x + tecla) ----------

    private void BeginDelete()
    {
        _pendingDelete = true;
        Breadcrumb.Text = "remover  —  aperte a tecla do atalho ou da marca (Esc cancela)";
    }

    private void DeleteByKey(string key)
    {
        // painel de marcas: remove a marca
        if (_stack.Count > 1 && _stack[^1].Crumb == "marcas")
        {
            var mark = _config.Marks.Keys.FirstOrDefault(k =>
                k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (mark is null) { Render(); return; }
            _config.Marks.Remove(mark);
            ConfigStore.Save(_config);
            _stack.RemoveAt(_stack.Count - 1);
            if (_config.Marks.Count > 0) ShowMarks(); else Render();
            Breadcrumb.Text = $"✓ marca '{mark} removida";
            Log.Write($"marca removida: {mark}");
            return;
        }

        // raiz: procura nas seções; submenu: remove da lista atual (referência do config)
        LeapItem? removed = null;
        if (_stack.Count == 1)
        {
            foreach (var section in _config.Sections)
            {
                removed = section.Items.FirstOrDefault(i =>
                    i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (removed is not null) { section.Items.Remove(removed); break; }
            }
        }
        else
        {
            var items = _stack[^1].Items;
            removed = items.FirstOrDefault(i =>
                i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (removed is not null) items.Remove(removed);
        }

        if (removed is null) { Render(); return; }
        ConfigStore.Save(_config);
        if (_stack.Count == 1)
        {
            _stack.Clear();
            _stack.Add(("Leap", AllRootItems()));
        }
        Render();
        Breadcrumb.Text = $"✓ removido  —  {removed.Label}";
        Log.Write($"item removido: {removed.Key} ({removed.Label})");
    }

    // ---------- ajuda ----------

    private void ShowHelp()
    {
        _helpMode = true;
        SectionsScroll.Visibility = Visibility.Collapsed;
        ChordScroll.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Collapsed;
        TipsPanel.Visibility = Visibility.Collapsed;
        HelpScroll.Visibility = Visibility.Visible;
        Breadcrumb.Text = "Leap  ▸  ajuda";
        Footer.Text = "Esc: voltar";
        HelpHost.ItemsSource = HelpRows;
        Keyboard.Focus(this);
    }

    private void CloseHelp()
    {
        _helpMode = false;
        HelpScroll.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CardVm vm)
            ActivateItem(vm.Item, Keyboard.Modifiers);
    }

    private void ActivateItem(LeapItem item, ModifierKeys modifiers)
    {
        if (item.IsGroup)
        {
            _stack.Add((item.Label, item.Children!));
            Render();
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Path)) return;

        // item com "args": executa o programa com os argumentos dados
        if (item.Args is not null && modifiers == ModifierKeys.None)
        {
            try { Actions.Run(item.Path, item.Args); }
            catch (Exception ex) { Log.Write($"ERRO em args: {ex}"); }
            HideOverlay();
            return;
        }

        // tecla simples numa pasta: navega DENTRO do Leap (Enter lá abre o Explorer)
        if (modifiers == ModifierKeys.None && item.Action is null
            && _config.FolderAction != "open" && Directory.Exists(item.Path))
        {
            EnterBrowse(item.Path);
            return;
        }

        ExecutePath(item.Path, modifiers, item.Action);
    }

    private void Tip_Click(object sender, MouseButtonEventArgs e)
    {
        // referência completa de motions do vim
        ExecutePath("https://vim.rtorr.com/lang/pt_br", ModifierKeys.None);
    }

    /// <summary>Fixa a pasta da janela do Explorer que estava em primeiro plano (harpoon).</summary>
    private void PinForegroundExplorerFolder()
    {
        var path = ExplorerWindows.PathOf(_prevForeground);
        if (path is null)
        {
            Breadcrumb.Text = "Leap — deixe uma janela do Explorer em primeiro plano e aperte +";
            return;
        }
        if (Flatten(AllRootItems()).Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Breadcrumb.Text = $"Leap — \"{DisplayName(path)}\" já está fixada";
            return;
        }

        PinPath(path);
    }

    /// <summary>Adiciona a pasta à seção "Pastas" na primeira tecla livre.</summary>
    private void PinPath(string path)
    {
        var used = AllRootItems().Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var key = "123456789abcdefghijklmnopqrstuvwxyz"
            .Select(c => c.ToString())
            .FirstOrDefault(k => !used.Contains(k));
        if (key is null)
        {
            Breadcrumb.Text = "Leap — sem teclas livres no nível raiz";
            return;
        }

        if (_config.Sections.Count == 0)
            _config.Sections.Add(new Section { Title = "Pastas" });
        _config.Sections[0].Items.Add(new LeapItem { Key = key, Label = DisplayName(path), Path = path });
        ConfigStore.Save(_config);
        _stack.Clear();
        _stack.Add(("Leap", AllRootItems()));
        Log.Write($"pasta fixada: {key} -> {path}");
        if (!_searchMode)
        {
            Render();
            Breadcrumb.Text = $"Leap — \"{DisplayName(path)}\" fixada na tecla {key.ToUpperInvariant()}";
        }
        else
        {
            Breadcrumb.Text = $"✓ fixada na tecla {key.ToUpperInvariant()}  —  {_browseDir}";
        }
    }

    // ---------- marcas (vim marks) ----------

    private void BeginMark()
    {
        var path = ExplorerWindows.PathOf(_prevForeground);
        if (path is null)
        {
            Breadcrumb.Text = "Leap — deixe uma janela do Explorer em primeiro plano para marcar";
            return;
        }
        _pendingMark = true;
        _pendingMarkPath = path;
        Breadcrumb.Text = $"marcar \"{DisplayName(path)}\"  —  aperte uma letra (Esc cancela)";
    }

    private void SetMark(string letter)
    {
        _config.Marks[letter] = _pendingMarkPath!;
        ConfigStore.Save(_config);
        Render();
        Breadcrumb.Text = $"Leap — marca  '{letter}  →  {DisplayName(_pendingMarkPath!)}";
        Log.Write($"marca criada: {letter} -> {_pendingMarkPath}");
    }

    private void ShowMarks()
    {
        if (_config.Marks.Count == 0)
        {
            Breadcrumb.Text = "Leap — nenhuma marca ainda (crie com m + letra)";
            return;
        }
        var items = _config.Marks
            .OrderBy(kv => kv.Key)
            .Select(kv => new LeapItem { Key = kv.Key, Label = DisplayName(kv.Value), Path = kv.Value })
            .ToList();
        _stack.Add(("marcas", items));
        Render();
    }

    private void RepeatLast()
    {
        if (_lastAction is not { } last)
        {
            Breadcrumb.Text = "Leap — nenhuma ação para repetir ainda";
            return;
        }
        ExecutePath(last.Path, last.Mods, last.Action);
    }

    // ---------- modo busca (arquivos e janelas) ----------

    private void EnterSearch(SearchTarget target)
    {
        _searchTarget = target;
        if (target == SearchTarget.Windows)
            _windows = WindowSwitcher.List();

        _searchMode = true;
        ChordScroll.Visibility = Visibility.Collapsed;
        SectionsScroll.Visibility = Visibility.Collapsed;
        TipsPanel.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Visible;
        Breadcrumb.Text = target switch
        {
            SearchTarget.Files => "Leap  ▸  busca",
            SearchTarget.Windows => "Leap  ▸  janelas",
            SearchTarget.Commands => _commandDir is null
                ? "Leap  ▸  comandos"
                : $"Leap  ▸  comandos  —  em {_commandDir}",
            _ => _browseDir,
        };
        Footer.Text = target switch
        {
            SearchTarget.Files => SearchHints,
            SearchTarget.Windows => WindowHints,
            SearchTarget.Commands => CommandHints,
            _ => BrowseHints,
        };
        SearchBox.Clear();
        _results = new();
        _selected = 0;
        RunSearch(); // caixa vazia: frecência, janelas ou conteúdo da pasta
        SearchBox.Focus();
    }

    /// <summary>Navegador de pastas embutido: filtra, desce, sobe — sem sair do Leap.</summary>
    private void EnterBrowse(string dir)
    {
        _browseDir = dir;
        EnterSearch(SearchTarget.Browse);
    }

    private void ExitSearch(bool render = true)
    {
        _searchMode = false;
        _searchCts?.Cancel();
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchBox.Clear();
        if (render)
        {
            Render(); // restaura seções/submenu e as dicas de vim
            Keyboard.Focus(this);
        }
    }

    private void HandleSearchKey(Key key, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (key)
        {
            case Key.Escape:
                ExitSearch();
                e.Handled = true;
                return;
            case Key.Back when SearchBox.Text.Length == 0:
                // no navegador de pastas, Backspace sobe um nível
                if (_searchTarget == SearchTarget.Browse
                    && Path.GetDirectoryName(_browseDir.TrimEnd('\\')) is { } parent)
                    EnterBrowse(parent);
                else
                    ExitSearch();
                e.Handled = true;
                return;
            case Key.P when ctrl && _searchTarget == SearchTarget.Browse:
                PinPath(_browseDir);
                e.Handled = true;
                return;
            case Key.Down:
            case Key.J when ctrl:
                MoveSelection(+1);
                e.Handled = true;
                return;
            case Key.Up:
            case Key.K when ctrl:
                MoveSelection(-1);
                e.Handled = true;
                return;
            case Key.Enter:
                if (_results.Count > 0)
                    ActivateResult(_results[_selected]);
                e.Handled = true;
                return;
        }
        // demais teclas seguem para o TextBox
    }

    private void ActivateResult(ResultVm vm)
    {
        if (_searchTarget == SearchTarget.Windows)
        {
            Log.Write($"focando janela: {vm.Name} ({vm.Path})");
            HideOverlay();
            WindowSwitcher.Focus(new IntPtr(vm.Hwnd));
            return;
        }

        if (_searchTarget == SearchTarget.Browse)
        {
            var mods = Keyboard.Modifiers;
            if (mods != ModifierKeys.None)
            {
                ExecutePath(vm.Path, mods); // Ctrl/Alt/Shift agem direto no item
                return;
            }
            if (vm.Kind == 1) { EnterBrowse(vm.Path); return; } // desce na pasta
            ExecutePath(vm.Path, ModifierKeys.None);            // Explorer ou app padrão
            return;
        }

        if (_searchTarget == SearchTarget.Commands)
        {
            var cmd = vm.Kind == 4
                ? new LeapCommand { Name = "run", Label = vm.Path, Run = vm.Path }
                : (int)vm.Hwnd is var index && index >= 0 && index < _config.Commands.Count
                    ? _config.Commands[index]
                    : null;
            if (cmd is null) return;
            try
            {
                Actions.RunCommand(cmd, _commandDir);
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO no comando '{cmd.Name}': {ex}");
            }
            HideOverlay();
            return;
        }

        ExecutePath(vm.Path, Keyboard.Modifiers);
    }

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _results.Count - 1);
        RenderResults();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void RunSearch()
    {
        if (!_searchMode) return;

        var query = SearchBox.Text.Trim();
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;

        List<ResultVm> results;

        if (_searchTarget == SearchTarget.Browse)
        {
            if (query.Length == 0)
            {
                // caixa vazia: conteúdo direto da pasta (diretórios primeiro)
                results = new List<ResultVm>();
                try
                {
                    results = Directory.EnumerateDirectories(_browseDir)
                            .Select(d => (Path: d, IsDir: true))
                        .Concat(Directory.EnumerateFiles(_browseDir)
                            .Select(f => (Path: f, IsDir: false)))
                        .Where(t => !Path.GetFileName(t.Path).StartsWith('.'))
                        .OrderByDescending(t => t.IsDir)
                        .ThenBy(t => Path.GetFileName(t.Path), StringComparer.OrdinalIgnoreCase)
                        .Take(13)
                        .Select(t => new ResultVm(
                            (t.IsDir ? "▸  " : "") + Path.GetFileName(t.Path), t.Path, false,
                            Kind: t.IsDir ? 1 : 0))
                        .ToList();
                }
                catch (Exception ex)
                {
                    Log.Write($"erro navegando {_browseDir}: {ex.Message}");
                }
                results.Insert(0, new ResultVm("⏎  abrir esta pasta no Windows Explorer",
                    _browseDir, false, Kind: 2));
            }
            else
            {
                // digitou: busca RECURSIVA na subárvore (acha arquivos em subpastas)
                var root = _browseDir;
                var exclude = new HashSet<string>(_config.IndexExclude, StringComparer.OrdinalIgnoreCase);
                try
                {
                    results = await Task.Run(() =>
                    {
                        var found = new List<(string Path, bool IsDir, int Score)>();
                        var pending = new Queue<(string Dir, int Depth)>();
                        pending.Enqueue((root, 0));
                        var visited = 0;

                        while (pending.Count > 0 && visited < 5000)
                        {
                            ct.ThrowIfCancellationRequested();
                            var (dir, depth) = pending.Dequeue();
                            IEnumerable<string> entries;
                            try { entries = Directory.EnumerateFileSystemEntries(dir); }
                            catch { continue; }

                            foreach (var entry in entries)
                            {
                                if (++visited >= 5000) break;
                                var name = Path.GetFileName(entry);
                                if (name.StartsWith('.')) continue;
                                var isDir = Directory.Exists(entry);
                                if (isDir && exclude.Contains(name)) continue;

                                var rel = entry[root.Length..].TrimStart('\\');
                                if (FuzzyMatcher.Score(query, rel) is int s)
                                    found.Add((entry, isDir, s + (depth == 0 ? 10 : 0)));
                                if (isDir && depth < 6)
                                    pending.Enqueue((entry, depth + 1));
                            }
                        }

                        return found
                            .OrderByDescending(t => t.Score)
                            .Take(13)
                            .Select(t => new ResultVm(
                                (t.IsDir ? "▸  " : "") + Path.GetFileName(t.Path), t.Path, false,
                                Kind: t.IsDir ? 1 : 0))
                            .ToList();
                    }, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (ct.IsCancellationRequested || !_searchMode) return;
            }

            _results = results;
            _selected = 0;
            RenderResults();
            return;
        }

        if (_searchTarget == SearchTarget.Commands)
        {
            results = _config.Commands
                .Select((c, i) => (Cmd: c, Index: i, Score: query.Length == 0
                    ? 0
                    : FuzzyMatcher.Score(query, c.Name + " " + c.Label) ?? int.MinValue))
                .Where(t => t.Score != int.MinValue)
                .OrderByDescending(t => t.Score)
                .Take(11)
                .Select(t => new ResultVm(
                    $"{t.Cmd.Name}   —   {t.Cmd.Label}",
                    t.Cmd.Run + (t.Cmd.Dir is null ? "" : $"    ({t.Cmd.Dir})"),
                    false, t.Index, Kind: 3))
                .ToList();

            // sem alias que sirva? a linha digitada vira comando (":code ." etc.)
            if (query.Length > 0)
                results.Add(new ResultVm($"⚡  executar:  {query}", query, false, -1, Kind: 4));

            _results = results;
            _selected = 0;
            RenderResults();
            return;
        }

        if (_searchTarget == SearchTarget.Windows)
        {
            results = _windows
                .Select(w => (w, Score: query.Length == 0
                    ? 0
                    : FuzzyMatcher.Score(query, w.Title + " " + w.ProcessName) ?? int.MinValue))
                .Where(t => t.Score != int.MinValue)
                .OrderByDescending(t => t.Score)
                .Take(12)
                .Select(t => new ResultVm(t.w.Title, t.w.ProcessName, false, t.w.Hwnd.ToInt64()))
                .ToList();
            _results = results;
            _selected = 0;
            RenderResults();
            return;
        }

        try
        {
            if (query.Length == 0)
            {
                results = Frecency.Top(12)
                    .Select(p => new ResultVm(DisplayName(p), p, false))
                    .ToList();
            }
            else
            {
                var config = _config;
                var favorites = Flatten(AllRootItems()).ToList();
                var esResults = await _everything.SearchAsync(config, query, ct);

                results = await Task.Run(() =>
                {
                    var queryLower = query.ToLowerInvariant();
                    var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    void Add(string p, int s) =>
                        merged[p] = merged.TryGetValue(p, out var old) ? Math.Max(old, s) : s;

                    // favoritos do config valem mais
                    foreach (var (label, path) in favorites)
                        if (FuzzyMatcher.Score(query, label + " " + path) is int s)
                            Add(path, s + 60);

                    // índice do PC (pré-filtro barato antes da pontuação)
                    var index = FileIndex.Shared.Paths;
                    for (int i = 0; i < index.Count; i++)
                    {
                        if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                        var p = index[i];
                        if (!FuzzyMatcher.IsSubsequence(queryLower, p)) continue;
                        if (FuzzyMatcher.Score(query, p) is int s)
                            Add(p, s);
                    }

                    // Everything, se disponível
                    foreach (var p in esResults)
                        Add(p, FuzzyMatcher.Score(query, p) is int s ? s + 10 : 5);

                    return merged
                        .Select(kv => (kv.Key, Score: kv.Value + Frecency.Boost(kv.Key)))
                        .OrderByDescending(t => t.Score)
                        .Take(12)
                        .Select(t => new ResultVm(DisplayName(t.Key), t.Key, false))
                        .ToList();
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested || !_searchMode) return;
        _results = results;
        _selected = 0;
        RenderResults();
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void RenderResults()
    {
        ResultsHost.ItemsSource = _results.Select((r, i) => r with { Selected = i == _selected }).ToList();
        NoResults.Visibility = _results.Count == 0 && SearchBox.Text.Trim().Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        // defensivo: a caixa de busca nunca pode perder o foco do teclado
        if (_searchMode && !SearchBox.IsKeyboardFocusWithin)
            SearchBox.Focus();
    }

    private void Result_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ResultVm vm)
            ActivateResult(vm);
    }

    private static IEnumerable<(string Label, string Path)> Flatten(IEnumerable<LeapItem> items)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Path))
                yield return (item.Label, item.Path);
            if (item.Children is not null)
                foreach (var child in Flatten(item.Children))
                    yield return child;
        }
    }

    // ---------- execução ----------

    private void ExecutePath(string path, ModifierKeys modifiers, string? defaultAction = null)
    {
        // IMPORTANTE: lançar ANTES de esconder o overlay. Enquanto o Leap é a
        // janela em primeiro plano, o processo filho herda o direito de assumir
        // o foco; esconder primeiro fazia o terminal/editor abrir atrás de tudo.
        Log.Write($"ação: mods=[{modifiers}] action={defaultAction ?? "open"} path={path}");
        try
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                Clipboard.SetText(path);
                Frecency.Record(path);
                _lastAction = (path, modifiers, defaultAction);
                ShowFeedbackAndHide($"✓  caminho copiado — {path}");
                return;
            }

            if (modifiers.HasFlag(ModifierKeys.Control))
                Actions.OpenTerminal(_config, path);
            else if (modifiers.HasFlag(ModifierKeys.Alt))
                Actions.OpenEditor(_config, path);
            else switch (defaultAction)
            {
                case "editor": Actions.OpenEditor(_config, path); break;
                case "terminal": Actions.OpenTerminal(_config, path); break;
                default: Actions.Open(path); break;
            }
            Frecency.Record(path);
            _lastAction = (path, modifiers, defaultAction);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO na ação: {ex}");
            HideOverlay();
            MessageBox.Show(
                $"Não consegui abrir \"{path}\":\n\n{ex.Message}\n\n" +
                $"Confira os campos \"terminal\"/\"editor\" em {ConfigStore.FilePath} se o erro persistir.",
                "Leap", MessageBoxButton.OK, MessageBoxImage.Warning,
                MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            return;
        }
        HideOverlay();
    }

    /// <summary>Mostra uma confirmação breve no overlay e então o esconde.</summary>
    private void ShowFeedbackAndHide(string message)
    {
        Breadcrumb.Text = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) => { timer.Stop(); HideOverlay(); };
        timer.Start();
    }

    private static string? KeyToString(Key k)
    {
        if (k is >= Key.A and <= Key.Z) return ((char)('a' + (k - Key.A))).ToString();
        if (k is >= Key.D0 and <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k is >= Key.NumPad0 and <= Key.NumPad9) return ((char)('0' + (k - Key.NumPad0))).ToString();
        return null;
    }
}

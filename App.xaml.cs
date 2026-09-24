using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Leap;

public partial class App : System.Windows.Application
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private Mutex? _mutex;
    private HotkeyManager? _hotkey;
    private WinForms.NotifyIcon? _tray;
    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Write("=== Leap iniciando ===");
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"EXCEÇÃO NÃO TRATADA: {args.Exception}");
            args.Handled = true;
        };

        _mutex = new Mutex(initiallyOwned: true, "Leap_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("O Leap já está em execução (veja o ícone na bandeja).", "Leap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var config = ConfigStore.LoadOrCreate();
        FileIndex.Shared.Initialize(config); // carrega/constrói o índice em background
        _overlay = new OverlayWindow();

        try
        {
            _hotkey = HotkeyManager.Register(config.Hotkey);
            _hotkey.Pressed += ToggleOverlay;
            Log.Write($"hotkey registrado: {config.Hotkey}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível registrar o atalho global \"{config.Hotkey}\": {ex.Message}\n\n" +
                $"Edite o campo \"hotkey\" em {ConfigStore.FilePath} e reinicie o Leap.",
                "Leap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SetupTray(config.Hotkey);
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible)
        {
            _overlay.HideOverlay();
        }
        else
        {
            // captura a janela ativa ANTES do overlay tomar o foco (usada pelo "+")
            var prevForeground = GetForegroundWindow();
            _overlay.ShowOverlay(ConfigStore.LoadOrCreate(), prevForeground);
        }
    }

    private void SetupTray(string hotkey)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir painel", null, (_, _) => ToggleOverlay());
        menu.Items.Add("Editar configuração", null, (_, _) =>
            Process.Start("notepad.exe", ConfigStore.FilePath));
        menu.Items.Add("Reconstruir índice de busca", null, (_, _) =>
            FileIndex.Shared.Rebuild(ConfigStore.LoadOrCreate()));

        var startup = new WinForms.ToolStripMenuItem("Iniciar com o Windows")
        {
            Checked = Autostart.IsEnabled,
        };
        startup.Click += (_, _) =>
        {
            try
            {
                Autostart.Toggle();
                startup.Checked = Autostart.IsEnabled;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não consegui alterar a inicialização automática:\n{ex.Message}",
                    "Leap", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        menu.Items.Add(startup);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _tray = new WinForms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = $"Leap — {hotkey}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ToggleOverlay();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

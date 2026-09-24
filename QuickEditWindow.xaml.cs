using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Leap;

/// <summary>
/// Editor nativo instantâneo do Leap (AvalonEdit): como o processo já está
/// residente, o arquivo abre em milissegundos, com coloração por linguagem.
/// </summary>
public partial class QuickEditWindow : Window
{
    private readonly string _path;
    private bool _dirty;

    public QuickEditWindow(string path)
    {
        InitializeComponent();
        _path = path;

        Editor.Load(path);
        Editor.SyntaxHighlighting =
            HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path));
        Editor.TextChanged += (_, _) =>
        {
            if (_dirty) return;
            _dirty = true;
            UpdateTitle();
        };

        UpdateTitle();
        StatusBar.Text = "Ctrl+S: salvar   ·   Esc: fechar   ·   dica: abra a pasta com Alt para editar no nvim e treinar motions";
        PreviewKeyDown += OnKey;
    }

    public static void Open(string path)
    {
        var window = new QuickEditWindow(path);
        window.Show();
        window.Activate();
    }

    private void UpdateTitle() =>
        Title = $"{(_dirty ? "● " : "")}{Path.GetFileName(_path)} — Leap Edit";

    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveFile();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void SaveFile()
    {
        Editor.Save(_path);
        _dirty = false;
        UpdateTitle();
        StatusBar.Text = $"salvo às {DateTime.Now:HH:mm:ss}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty)
        {
            var choice = MessageBox.Show(
                $"Salvar as alterações em {Path.GetFileName(_path)}?", "Leap Edit",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes) SaveFile();
            else if (choice == MessageBoxResult.Cancel) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}

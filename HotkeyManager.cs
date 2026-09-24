using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Leap;

/// <summary>
/// Registra um atalho global via RegisterHotKey numa janela message-only.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;

    public event Action? Pressed;

    private HotkeyManager(uint modifiers, uint vk)
    {
        var p = new HwndSourceParameters("LeapHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            // HWND_MESSAGE: janela invisível que só recebe mensagens
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);

        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, vk))
        {
            _source.Dispose();
            throw new InvalidOperationException("a combinação já está em uso por outro programa");
        }
    }

    public static HotkeyManager Register(string spec)
    {
        var (mods, vk) = Parse(spec);
        return new HotkeyManager(mods, vk);
    }

    /// <summary>Interpreta especificações como "Alt+Space", "Ctrl+Shift+K", "Win+E".</summary>
    private static (uint Mods, uint Vk) Parse(string spec)
    {
        uint mods = 0;
        var key = Key.None;

        foreach (var raw in spec.Split('+'))
        {
            var token = raw.Trim().ToLowerInvariant();
            switch (token)
            {
                case "ctrl" or "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win" or "windows" or "super": mods |= ModWin; break;
                default:
                    var name = token.Length == 1 && char.IsDigit(token[0]) ? "D" + token : token;
                    if (!Enum.TryParse(name, ignoreCase: true, out key))
                        throw new FormatException($"tecla desconhecida: '{raw.Trim()}'");
                    break;
            }
        }

        if (key == Key.None)
            throw new FormatException("nenhuma tecla principal definida (ex.: \"Alt+Space\")");

        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}

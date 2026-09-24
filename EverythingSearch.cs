using System.Diagnostics;
using System.Text;

namespace Leap;

/// <summary>
/// Integração opcional com o Everything (voidtools) via CLI es.exe.
/// Se o es.exe não existir ou o serviço não estiver rodando, desativa-se sozinha.
/// </summary>
public sealed class EverythingSearch
{
    private bool? _available;

    public async Task<List<string>> SearchAsync(Config config, string query, CancellationToken ct)
    {
        if (_available == false || query.Length < 3) return new();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = config.EverythingCli,
                Arguments = $"-n 30 \"{query.Replace("\"", "")}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);

            if (p.ExitCode != 0)
            {
                _available = false; // IPC indisponível ou erro — para de tentar
                return new();
            }

            _available = true;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList();
        }
        catch (OperationCanceledException)
        {
            return new();
        }
        catch
        {
            _available = false; // es.exe não encontrado
            return new();
        }
    }
}

using System.IO;
using System.Text.Json;

namespace Leap;

/// <summary>
/// Frequência + recência de uso (estilo zoxide): cada abertura conta, e usos
/// recentes pesam mais. Persistido em %APPDATA%\Leap\frecency.json.
/// </summary>
public static class Frecency
{
    private sealed record Entry(int Count, DateTime LastUsedUtc);

    private static readonly object Gate = new();
    private static Dictionary<string, Entry>? _data;

    private static string FilePath => Path.Combine(ConfigStore.Dir, "frecency.json");

    public static void Record(string path)
    {
        lock (Gate)
        {
            var data = _data ??= Load();
            data[path] = data.TryGetValue(path, out var e)
                ? new Entry(e.Count + 1, DateTime.UtcNow)
                : new Entry(1, DateTime.UtcNow);
            Save(data);
        }
    }

    /// <summary>Bônus (0–60) somado à pontuação fuzzy nos resultados de busca.</summary>
    public static int Boost(string path)
    {
        lock (Gate)
        {
            var data = _data ??= Load();
            return data.TryGetValue(path, out var e) ? (int)Math.Min(60, ScoreOf(e) * 4) : 0;
        }
    }

    /// <summary>Caminhos mais "frecentes" — mostrados na busca com a caixa vazia.</summary>
    public static List<string> Top(int n)
    {
        lock (Gate)
        {
            var data = _data ??= Load();
            return data
                .OrderByDescending(kv => ScoreOf(kv.Value))
                .Take(n)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    private static double ScoreOf(Entry e)
    {
        var age = DateTime.UtcNow - e.LastUsedUtc;
        var mult = age < TimeSpan.FromHours(1) ? 4.0
                 : age < TimeSpan.FromDays(1) ? 2.0
                 : age < TimeSpan.FromDays(7) ? 1.0
                 : 0.5;
        return e.Count * mult;
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath))
                       ?? new();
        }
        catch
        {
        }
        return new();
    }

    private static void Save(Dictionary<string, Entry> data)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
        }
    }
}

using System.IO;
using System.IO.Compression;

namespace Leap;

/// <summary>
/// Índice de arquivos e diretórios do PC inteiro (drives fixos), construído em
/// background e persistido comprimido em %APPDATA%\Leap\index.txt.gz para que a
/// busca funcione imediatamente no próximo início.
/// </summary>
public sealed class FileIndex
{
    public static FileIndex Shared { get; } = new();

    private volatile List<string> _paths = new();
    private int _building;

    public IReadOnlyList<string> Paths => _paths;
    public DateTime BuiltAtUtc { get; private set; } = DateTime.MinValue;
    public bool IsBuilding => _building == 1;

    private static string CacheFile => Path.Combine(ConfigStore.Dir, "index.txt.gz");

    /// <summary>Carrega o cache do disco e reconstrói se estiver velho. Nunca bloqueia.</summary>
    public void Initialize(Config config)
    {
        Task.Run(() =>
        {
            LoadCache();
            if (IsStale(config)) Rebuild(config);
        });
    }

    public void EnsureFresh(Config config)
    {
        if (IsStale(config)) Rebuild(config);
    }

    public void Rebuild(Config config)
    {
        if (Interlocked.Exchange(ref _building, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var list = Scan(config);
                _paths = list;
                BuiltAtUtc = DateTime.UtcNow;
                SaveCache(list);
            }
            finally
            {
                Interlocked.Exchange(ref _building, 0);
            }
        });
    }

    private bool IsStale(Config config) =>
        DateTime.UtcNow - BuiltAtUtc > TimeSpan.FromHours(config.IndexRefreshHours);

    private static List<string> Scan(Config config)
    {
        var roots = config.IndexRoots.Count > 0
            ? config.IndexRoots.Select(Environment.ExpandEnvironmentVariables).ToList()
            : DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();

        var exclude = new HashSet<string>(config.IndexExclude, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(1 << 20);
        var pending = new Stack<(string Dir, int Depth)>();

        foreach (var root in roots)
            if (Directory.Exists(root))
                pending.Push((root, 0));

        while (pending.Count > 0 && result.Count < config.IndexMaxEntries)
        {
            var (dir, depth) = pending.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.') || exclude.Contains(name)) continue;
                    result.Add(sub);
                    if (depth + 1 <= config.IndexMaxDepth)
                        pending.Push((sub, depth + 1));
                    if (result.Count >= config.IndexMaxEntries) return result;
                }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    result.Add(file);
                    if (result.Count >= config.IndexMaxEntries) return result;
                }
            }
            catch
            {
                // sem permissão, caminho longo demais etc. — segue o baile
            }
        }
        return result;
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return;
            using var fs = File.OpenRead(CacheFile);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gz);
            var list = new List<string>(1 << 20);
            while (reader.ReadLine() is { } line)
                if (line.Length > 0)
                    list.Add(line);
            _paths = list;
            BuiltAtUtc = File.GetLastWriteTimeUtc(CacheFile);
        }
        catch
        {
            // cache corrompido — será reconstruído
        }
    }

    private static void SaveCache(List<string> paths)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Dir);
            using var fs = File.Create(CacheFile);
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            using var writer = new StreamWriter(gz);
            foreach (var p in paths)
                writer.WriteLine(p);
        }
        catch
        {
        }
    }
}

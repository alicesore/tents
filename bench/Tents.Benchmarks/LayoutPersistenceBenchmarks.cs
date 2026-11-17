using BenchmarkDotNet.Attributes;
using Tents.Core;
using Tents.Core.Persistence;

namespace Tents.Benchmarks;

/// <summary>
/// Load and save of the layout file (MILESTONES.md M1 step 4), in memory and through the file store.
/// </summary>
/// <remarks>
/// The file benchmarks use a new folder under the system temp directory. <c>SaveFile</c> is the full atomic save:
/// write the temp file, flush it to disk, and <c>File.Replace</c> with a backup, so it mostly measures the disk.
/// <c>LoadFile</c> is <c>LayoutFileStore.Load</c> on a valid file: read, version probe, parse and validate.
/// </remarks>
[MemoryDiagnoser]
public class LayoutPersistenceBenchmarks
{
    private LayoutSet _layouts = LayoutSet.Empty;
    private byte[] _json = [];
    private string _folder = string.Empty;
    private LayoutFileStore? _store;

    [Params(LayoutSize.Typical, LayoutSize.Stress)]
    public LayoutSize Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _layouts = BenchmarkLayouts.Create(Size);
        _json = LayoutSerializer.SerializeToUtf8Bytes(_layouts);
        _folder = Path.Combine(Path.GetTempPath(), "Tents.Benchmarks", Guid.NewGuid().ToString("N"));
        _store = new LayoutFileStore(Path.Combine(_folder, "layout.json"));
        _store.Save(_layouts);
        _store.Save(_layouts);

        var (tents, items) = BenchmarkLayouts.Dimensions(Size);
        Console.WriteLine($"// {Size}: {tents} tents, {items} items, layout.json is {_json.Length:N0} bytes");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Benchmark(Description = "Serialize (memory)")]
    public byte[] Serialize() => LayoutSerializer.SerializeToUtf8Bytes(_layouts);

    [Benchmark(Description = "Deserialize (memory)")]
    public LayoutSet Deserialize() => LayoutSerializer.Default.Deserialize(_json);

    [Benchmark(Description = "Save (file, atomic)")]
    public void SaveFile() => _store!.Save(_layouts);

    [Benchmark(Description = "Load (file)")]
    public LayoutSet LoadFile() => _store!.Load().Layouts;
}

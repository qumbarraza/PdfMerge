namespace PdfMerge.Core.Objects;

/// <summary>Indirect object reference ("N G R").</summary>
public readonly struct PdfRef : IEquatable<PdfRef>
{
    public int Num { get; }
    public int Gen { get; }

    public PdfRef(int num, int gen = 0)
    {
        Num = num;
        Gen = gen;
    }

    public bool Equals(PdfRef other) => Num == other.Num && Gen == other.Gen;
    public override bool Equals(object? obj) => obj is PdfRef r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Num, Gen);
    public override string ToString() => $"{Num} {Gen} R";
}

/// <summary>PDF name object, e.g. /Type.</summary>
public sealed class PdfName
{
    public string Value { get; }
    public PdfName(string value) => Value = value;
    public override bool Equals(object? obj) => obj is PdfName n && n.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => "/" + Value;

    public static readonly PdfName Type = new("Type");
    public static readonly PdfName Pages = new("Pages");
    public static readonly PdfName Page = new("Page");
    public static readonly PdfName Kids = new("Kids");
    public static readonly PdfName Count = new("Count");
    public static readonly PdfName MediaBox = new("MediaBox");
    public static readonly PdfName Resources = new("Resources");
    public static readonly PdfName Contents = new("Contents");
    public static readonly PdfName Parent = new("Parent");
    public static readonly PdfName Filter = new("Filter");
    public static readonly PdfName Length = new("Length");
}

/// <summary>Ordered PDF dictionary preserving insertion order.</summary>
public sealed class PdfDict
{
    private readonly Dictionary<string, object?> _map = new();
    private readonly List<string> _order = new();

    public object? this[string key]
    {
        get => _map.TryGetValue(key, out var v) ? v : null;
        set
        {
            if (!_map.ContainsKey(key)) _order.Add(key);
            _map[key] = value;
        }
    }

    public bool ContainsKey(string key) => _map.ContainsKey(key);
    public bool TryGetValue(string key, out object? value) => _map.TryGetValue(key, out value);
    public void Remove(string key)
    {
        if (_map.Remove(key)) _order.Remove(key);
    }

    public IEnumerable<KeyValuePair<string, object?>> Entries()
    {
        foreach (var k in _order) yield return new KeyValuePair<string, object?>(k, _map[k]);
    }
}

/// <summary>PDF stream object: a dictionary plus raw (already filter-encoded) bytes.</summary>
public sealed class PdfStreamObj
{
    public PdfDict Dict { get; } = new();
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

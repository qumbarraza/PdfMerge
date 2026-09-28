using PdfMerge.Core.Objects;

namespace PdfMerge.Core.Reader;

public sealed class PageInfo
{
    /// <summary>Content stream value as found on the page (PdfRef, list of PdfRef, or null) — refs are relative to the source document.</summary>
    public object? Contents { get; init; }
    public PdfDict Resources { get; init; } = new();
    public List<object?> MediaBox { get; init; } = new() { 0, 0, 612, 792 };
    public int Rotate { get; init; }
}

/// <summary>
/// Parses an existing PDF file well enough to enumerate its pages and resolve any object
/// (classic xref tables, cross-reference streams, and compressed object streams).
/// </summary>
public sealed class PdfDocumentReader
{
    private readonly byte[] _buf;
    private readonly Dictionary<int, XRefEntry> _xref = new();
    private readonly Dictionary<int, object?> _cache = new();
    private readonly Dictionary<int, (byte[] Data, int First, List<(int Num, int Offset)> Header)> _objStmCache = new();

    public PdfDict Trailer { get; private set; } = new();

    public PdfDocumentReader(byte[] bytes)
    {
        _buf = bytes;
        BuildXRef();
    }

    public object? Resolve(PdfRef r)
    {
        if (_cache.TryGetValue(r.Num, out var cached)) return cached;
        _cache[r.Num] = null; // guard against cycles during resolution
        object? value = null;

        if (_xref.TryGetValue(r.Num, out var entry))
        {
            value = entry.Kind == XRefEntryKind.Direct
                ? ParseIndirectObjectAt(entry.Offset)
                : ResolveFromObjectStream(entry.ObjStmNum, entry.IndexInObjStm);
        }

        _cache[r.Num] = value;
        return value;
    }

    public object? Deref(object? v)
    {
        int guard = 0;
        while (v is PdfRef r && guard++ < 32) v = Resolve(r);
        return v;
    }

    public PdfDict? Root => Deref(Trailer["Root"]) as PdfDict;

    public List<PageInfo> GetPages()
    {
        var result = new List<PageInfo>();
        var root = Root;
        var pagesRoot = Deref(root?["Pages"]) as PdfDict;
        if (pagesRoot != null) Walk(pagesRoot, new PdfDict(), new List<object?> { 0, 0, 612, 792 }, 0, result, new HashSet<PdfDict>());
        return result;
    }

    private void Walk(PdfDict node, PdfDict inheritedResources, List<object?> inheritedMediaBox, int inheritedRotate,
        List<PageInfo> result, HashSet<PdfDict> visited)
    {
        if (!visited.Add(node)) return; // guard against malformed cyclic trees

        var resources = Deref(node["Resources"]) as PdfDict ?? inheritedResources;
        var mediaBoxRaw = Deref(node["MediaBox"]) as List<object?> ?? inheritedMediaBox;
        int rotate = node["Rotate"] != null ? ToInt(Deref(node["Rotate"])) : inheritedRotate;

        var typeName = (Deref(node[PdfName.Type.Value]) as PdfName)?.Value;
        var kids = Deref(node["Kids"]) as List<object?>;

        if (typeName == "Pages" || kids != null)
        {
            if (kids != null)
            {
                foreach (var kidRaw in kids)
                {
                    if (Deref(kidRaw) is PdfDict kidDict)
                        Walk(kidDict, resources, mediaBoxRaw, rotate, result, visited);
                }
            }
            return;
        }

        // Leaf page.
        result.Add(new PageInfo
        {
            Contents = node["Contents"],
            Resources = resources,
            MediaBox = mediaBoxRaw,
            Rotate = rotate,
        });
    }

    private static int ToInt(object? v) => v switch { int i => i, double d => (int)d, _ => 0 };

    // ---- Indirect object parsing -------------------------------------------------

    private object? ParseIndirectObjectAt(long offset)
    {
        int pos = (int)offset;
        PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
        // "N G obj"
        while (pos < _buf.Length && _buf[pos] is >= (byte)'0' and <= (byte)'9') pos++;
        PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
        while (pos < _buf.Length && _buf[pos] is >= (byte)'0' and <= (byte)'9') pos++;
        PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
        PdfObjectParser.MatchKeyword(_buf, ref pos, "obj");
        return PdfObjectParser.ParseTopLevelValue(_buf, ref pos);
    }

    private object? ResolveFromObjectStream(int objStmNum, int index)
    {
        if (!_objStmCache.TryGetValue(objStmNum, out var cached))
        {
            var stmValue = Resolve(new PdfRef(objStmNum));
            if (stmValue is not PdfStreamObj stm) return null;
            byte[] decoded = Filters.DecodeStream(stm.Dict, stm.Data, Deref);
            int first = ToInt(Deref(stm.Dict["First"]));
            int n = ToInt(Deref(stm.Dict["N"]));

            var header = new List<(int, int)>();
            int hpos = 0;
            for (int i = 0; i < n; i++)
            {
                PdfObjectParser.SkipWhitespaceAndComments(decoded, ref hpos);
                int numStart = hpos;
                while (hpos < decoded.Length && decoded[hpos] is >= (byte)'0' and <= (byte)'9') hpos++;
                int num = int.Parse(System.Text.Encoding.ASCII.GetString(decoded, numStart, hpos - numStart));
                PdfObjectParser.SkipWhitespaceAndComments(decoded, ref hpos);
                int offStart = hpos;
                while (hpos < decoded.Length && decoded[hpos] is >= (byte)'0' and <= (byte)'9') hpos++;
                int off = int.Parse(System.Text.Encoding.ASCII.GetString(decoded, offStart, hpos - offStart));
                header.Add((num, off));
            }
            cached = (decoded, first, header);
            _objStmCache[objStmNum] = cached;
        }

        if (index < 0 || index >= cached.Header.Count) return null;
        // Objects inside an ObjStm are bare values relative to /First, not "N G obj ... endobj".
        int p = cached.First + cached.Header[index].Offset;
        return PdfObjectParser.ParseValue(cached.Data, ref p);
    }

    // ---- XRef construction ---------------------------------------------------

    private void BuildXRef()
    {
        int startxrefIdx = PdfObjectParser.LastIndexOf(_buf, "startxref");
        if (startxrefIdx < 0) throw new InvalidDataException("Not a valid PDF (missing startxref).");
        int p = startxrefIdx + "startxref".Length;
        PdfObjectParser.SkipWhitespaceAndComments(_buf, ref p);
        int numStart = p;
        while (p < _buf.Length && _buf[p] is >= (byte)'0' and <= (byte)'9') p++;
        long offset = long.Parse(System.Text.Encoding.ASCII.GetString(_buf, numStart, p - numStart));

        var visitedOffsets = new HashSet<long>();
        bool trailerSet = false;
        long? next = offset;

        while (next is long off && visitedOffsets.Add(off) && off >= 0 && off < _buf.Length)
        {
            next = ParseXRefSectionAt(off, ref trailerSet);
        }
    }

    /// <summary>Parses one xref section (classic table or xref stream) at the given offset, merging entries
    /// (earlier/most-recent entries win) and returns the /Prev offset to follow, if any.</summary>
    private long? ParseXRefSectionAt(long offset, ref bool trailerSet)
    {
        int pos = (int)offset;
        PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);

        if (PdfObjectParser.MatchKeyword(_buf, ref pos, "xref"))
        {
            // Classic table: one or more "start count" subsections.
            while (true)
            {
                PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
                if (PdfObjectParser.MatchKeyword(_buf, ref pos, "trailer")) break;
                if (pos >= _buf.Length || !(_buf[pos] is >= (byte)'0' and <= (byte)'9')) break;

                int s1 = pos;
                while (_buf[pos] is >= (byte)'0' and <= (byte)'9') pos++;
                int start = int.Parse(System.Text.Encoding.ASCII.GetString(_buf, s1, pos - s1));
                PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
                int s2 = pos;
                while (_buf[pos] is >= (byte)'0' and <= (byte)'9') pos++;
                int count = int.Parse(System.Text.Encoding.ASCII.GetString(_buf, s2, pos - s2));

                for (int i = 0; i < count; i++)
                {
                    PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
                    // Each entry is exactly 20 bytes: "nnnnnnnnnn ggggg n \r\n" (or f).
                    string entryOff = System.Text.Encoding.ASCII.GetString(_buf, pos, 10);
                    string entryType = System.Text.Encoding.ASCII.GetString(_buf, pos + 17, 1);
                    pos += 20;
                    int objNum = start + i;
                    if (entryType == "n" && !_xref.ContainsKey(objNum))
                    {
                        _xref[objNum] = new XRefEntry { Kind = XRefEntryKind.Direct, Offset = long.Parse(entryOff) };
                    }
                }
            }

            PdfObjectParser.SkipWhitespaceAndComments(_buf, ref pos);
            var trailerDict = PdfObjectParser.ParseValue(_buf, ref pos) as PdfDict ?? new PdfDict();
            if (!trailerSet) { Trailer = trailerDict; trailerSet = true; }
            else MergeTrailer(trailerDict);

            // Hybrid-reference files also carry a cross-reference stream via /XRefStm.
            if (trailerDict["XRefStm"] is int xrefStmOff)
            {
                bool dummy = true;
                ParseXRefSectionAt(xrefStmOff, ref dummy);
            }

            return trailerDict["Prev"] is int prev ? prev : null;
        }

        // Cross-reference stream: "N G obj << ... /Type /XRef ... >> stream ... endstream".
        var value = ParseIndirectObjectAt(offset);
        if (value is not PdfStreamObj xrefStm) return null;

        if (!trailerSet) { Trailer = xrefStm.Dict; trailerSet = true; }
        else MergeTrailer(xrefStm.Dict);

        byte[] decoded = Filters.DecodeStream(xrefStm.Dict, xrefStm.Data, v => v is PdfRef ? null : v);
        var wRaw = xrefStm.Dict["W"] as List<object?> ?? new List<object?> { 1, 1, 1 };
        int w0 = ToIntLiteral(wRaw[0]), w1 = ToIntLiteral(wRaw[1]), w2 = ToIntLiteral(wRaw[2]);
        int entrySize = w0 + w1 + w2;

        var index = xrefStm.Dict["Index"] as List<object?>;
        var ranges = new List<(int Start, int Count)>();
        if (index != null)
        {
            for (int i = 0; i + 1 < index.Count; i += 2)
                ranges.Add((ToIntLiteral(index[i]), ToIntLiteral(index[i + 1])));
        }
        else
        {
            int size = xrefStm.Dict["Size"] is int sz ? sz : 0;
            ranges.Add((0, size));
        }

        int dp = 0;
        foreach (var (start, count) in ranges)
        {
            for (int i = 0; i < count && dp + entrySize <= decoded.Length; i++)
            {
                long f0 = w0 == 0 ? 1 : ReadBE(decoded, dp, w0);
                long f1 = ReadBE(decoded, dp + w0, w1);
                long f2 = w2 == 0 ? 0 : ReadBE(decoded, dp + w0 + w1, w2);
                dp += entrySize;

                int objNum = start + i;
                if (_xref.ContainsKey(objNum)) continue;

                if (f0 == 1)
                    _xref[objNum] = new XRefEntry { Kind = XRefEntryKind.Direct, Offset = f1 };
                else if (f0 == 2)
                    _xref[objNum] = new XRefEntry { Kind = XRefEntryKind.InObjectStream, ObjStmNum = (int)f1, IndexInObjStm = (int)f2 };
            }
        }

        return xrefStm.Dict["Prev"] is int prevOff ? prevOff : null;
    }

    private void MergeTrailer(PdfDict d)
    {
        foreach (var (k, v) in d.Entries())
        {
            if (Trailer[k] == null) Trailer[k] = v;
        }
    }

    private static int ToIntLiteral(object? v) => v switch { int i => i, double d => (int)d, _ => 0 };

    private static long ReadBE(byte[] data, int offset, int len)
    {
        long v = 0;
        for (int i = 0; i < len; i++) v = (v << 8) | data[offset + i];
        return v;
    }
}

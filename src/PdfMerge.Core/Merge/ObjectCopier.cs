using PdfMerge.Core.Objects;
using PdfMerge.Core.Reader;

namespace PdfMerge.Core.Merge;

/// <summary>
/// Deep-copies PDF object graphs (pages, resources, content streams, fonts, images, ...)
/// from a source document into a destination PdfWriter, renumbering objects and
/// de-duplicating shared references within that one source document.
/// </summary>
public sealed class ObjectCopier
{
    private readonly PdfDocumentReader _source;
    private readonly PdfWriter _writer;
    private readonly Dictionary<int, PdfRef> _memo = new();

    public ObjectCopier(PdfDocumentReader source, PdfWriter writer)
    {
        _source = source;
        _writer = writer;
    }

    /// <summary>Copies a value that may directly embed the object graph (dicts/arrays/names/etc.),
    /// rewriting any PdfRef it contains into new indirect objects in the destination document.</summary>
    public object? CopyValue(object? value)
    {
        switch (value)
        {
            case null:
            case bool:
            case int:
            case double:
            case string:
            case PdfName:
                return value;
            case PdfRef r:
                return CopyIndirect(r);
            case List<object?> arr:
                return arr.Select(CopyValue).ToList();
            case PdfDict dict:
            {
                var copy = new PdfDict();
                foreach (var (k, v) in dict.Entries()) copy[k] = CopyValue(v);
                return copy;
            }
            default:
                throw new NotSupportedException($"Cannot copy PDF value of type {value.GetType()}");
        }
    }

    public PdfRef CopyIndirect(PdfRef sourceRef)
    {
        if (_memo.TryGetValue(sourceRef.Num, out var existing)) return existing;

        var destRef = _writer.Allocate();
        _memo[sourceRef.Num] = destRef; // register before recursing, guards against reference cycles

        var value = _source.Resolve(sourceRef);
        if (value is PdfStreamObj stream)
        {
            var newStream = new PdfStreamObj { Data = stream.Data };
            foreach (var (k, v) in stream.Dict.Entries()) newStream.Dict[k] = CopyValue(v);
            _writer.WriteObject(destRef, newStream);
        }
        else
        {
            _writer.WriteObject(destRef, CopyValue(value));
        }

        return destRef;
    }
}

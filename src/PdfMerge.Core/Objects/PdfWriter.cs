using System.Globalization;

namespace PdfMerge.Core.Objects;

/// <summary>
/// Low-level writer for a new PDF file: allocates object numbers, writes indirect
/// objects sequentially while tracking byte offsets, then emits the xref table and trailer.
/// </summary>
public sealed class PdfWriter
{
    private readonly Stream _out;
    private readonly List<long> _offsets = new() { -1 }; // index 0 is the reserved free entry
    private int _nextNum = 1;

    public PdfWriter(Stream output)
    {
        _out = output;
        PdfSerializer.WriteAscii(_out, "%PDF-1.7\n%");
        _out.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4);
        PdfSerializer.WriteAscii(_out, "\n");
    }

    /// <summary>Reserve the next object number without writing it yet.</summary>
    public PdfRef Allocate()
    {
        var r = new PdfRef(_nextNum++, 0);
        _offsets.Add(-1);
        return r;
    }

    /// <summary>Write a value as the body of a previously allocated (or fresh) indirect object.</summary>
    public void WriteObject(PdfRef reference, object? value)
    {
        while (_offsets.Count <= reference.Num) _offsets.Add(-1);
        _offsets[reference.Num] = _out.Position;
        PdfSerializer.WriteAscii(_out, $"{reference.Num} {reference.Gen} obj\n");
        PdfSerializer.Write(value, _out);
        PdfSerializer.WriteAscii(_out, "\nendobj\n");
    }

    /// <summary>Allocate a fresh object number and write it in one step.</summary>
    public PdfRef WriteNewObject(object? value)
    {
        var r = Allocate();
        WriteObject(r, value);
        return r;
    }

    /// <summary>Write the xref table, trailer, and startxref/EOF footer. Call last.</summary>
    public void Finish(PdfRef root, PdfRef? info = null)
    {
        long xrefOffset = _out.Position;
        int size = _offsets.Count;

        PdfSerializer.WriteAscii(_out, $"xref\n0 {size}\n");
        PdfSerializer.WriteAscii(_out, "0000000000 65535 f \n");
        for (int i = 1; i < size; i++)
        {
            long off = _offsets[i];
            if (off < 0)
            {
                // Object number was allocated but never written; mark free to keep the table valid.
                PdfSerializer.WriteAscii(_out, "0000000000 00000 f \n");
            }
            else
            {
                PdfSerializer.WriteAscii(_out, off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            }
        }

        var id = new PdfHexString(Guid.NewGuid().ToByteArray());
        var trailer = new PdfDict();
        trailer["Size"] = size;
        trailer["Root"] = root;
        if (info != null) trailer["Info"] = info.Value;
        trailer["ID"] = new List<object?> { id, id };

        PdfSerializer.WriteAscii(_out, "trailer\n");
        PdfSerializer.Write(trailer, _out);
        PdfSerializer.WriteAscii(_out, $"\nstartxref\n{xrefOffset}\n%%EOF");
    }
}

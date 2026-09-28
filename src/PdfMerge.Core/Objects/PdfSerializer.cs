using System.Globalization;
using System.Text;

namespace PdfMerge.Core.Objects;

/// <summary>Serializes the PDF object model (PdfDict, List, PdfRef, PdfName, PdfStreamObj, ...) to bytes.</summary>
public static class PdfSerializer
{
    public static void Write(object? value, Stream s)
    {
        switch (value)
        {
            case null:
                WriteAscii(s, "null");
                break;
            case bool b:
                WriteAscii(s, b ? "true" : "false");
                break;
            case int i:
                WriteAscii(s, i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                WriteAscii(s, l.ToString(CultureInfo.InvariantCulture));
                break;
            case double d:
                WriteAscii(s, d.ToString("0.######", CultureInfo.InvariantCulture));
                break;
            case string str:
                WriteLiteralString(s, str);
                break;
            case PdfHexString hex:
                WriteAscii(s, "<");
                foreach (var b in hex.Bytes) WriteAscii(s, b.ToString("x2"));
                WriteAscii(s, ">");
                break;
            case PdfName name:
                WriteName(s, name.Value);
                break;
            case PdfRef r:
                WriteAscii(s, $"{r.Num} {r.Gen} R");
                break;
            case List<object?> arr:
                WriteAscii(s, "[ ");
                foreach (var item in arr)
                {
                    Write(item, s);
                    WriteAscii(s, " ");
                }
                WriteAscii(s, "]");
                break;
            case PdfDict dict:
                WriteDict(dict, s);
                break;
            case PdfStreamObj stream:
                // Ensure /Length reflects actual data size.
                stream.Dict[PdfName.Length.Value] = stream.Data.Length;
                WriteDict(stream.Dict, s);
                WriteAscii(s, "\nstream\n");
                s.Write(stream.Data, 0, stream.Data.Length);
                WriteAscii(s, "\nendstream");
                break;
            default:
                throw new NotSupportedException($"Cannot serialize PDF value of type {value.GetType()}");
        }
    }

    private static void WriteDict(PdfDict dict, Stream s)
    {
        WriteAscii(s, "<< ");
        foreach (var (key, val) in dict.Entries())
        {
            WriteName(s, key);
            WriteAscii(s, " ");
            Write(val, s);
            WriteAscii(s, " ");
        }
        WriteAscii(s, ">>");
    }

    private static void WriteName(Stream s, string name)
    {
        var sb = new StringBuilder("/");
        foreach (var ch in name)
        {
            if (ch <= ' ' || ch > '~' || "()<>[]{}/%#".IndexOf(ch) >= 0)
                sb.Append('#').Append(((int)ch).ToString("x2"));
            else
                sb.Append(ch);
        }
        WriteAscii(s, sb.ToString());
    }

    private static void WriteLiteralString(Stream s, string str)
    {
        // Characters outside Latin-1 can't round-trip as PDFDocEncoding; write UTF-16BE with a BOM instead
        // (a literal string may contain raw bytes - the PDF spec explicitly allows this for Unicode text).
        bool needsUnicode = str.Any(c => c > 255);
        IEnumerable<byte> bytes = needsUnicode
            ? new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(str))
            : str.Select(c => (byte)c);

        s.WriteByte((byte)'(');
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(': WriteAscii(s, "\\("); break;
                case (byte)')': WriteAscii(s, "\\)"); break;
                case (byte)'\\': WriteAscii(s, "\\\\"); break;
                case (byte)'\r': WriteAscii(s, "\\r"); break;
                case (byte)'\n': WriteAscii(s, "\\n"); break;
                default: s.WriteByte(b); break;
            }
        }
        s.WriteByte((byte)')');
    }

    internal static void WriteAscii(Stream s, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }
}

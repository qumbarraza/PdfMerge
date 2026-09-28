using System.Text;

namespace PdfMerge.Core.Objects;

/// <summary>
/// Recursive-descent parser for the PDF object syntax (dicts, arrays, names, strings,
/// numbers, references). Operates directly on a byte buffer with a cursor.
/// </summary>
public static class PdfObjectParser
{
    public static bool IsWhitespace(byte b) => b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;
    public static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
        or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public static void SkipWhitespaceAndComments(byte[] buf, ref int pos)
    {
        while (pos < buf.Length)
        {
            if (IsWhitespace(buf[pos])) { pos++; continue; }
            if (buf[pos] == (byte)'%')
            {
                while (pos < buf.Length && buf[pos] != '\n' && buf[pos] != '\r') pos++;
                continue;
            }
            break;
        }
    }

    /// <summary>Parses one value; if it turns out to be a top-level dict followed by "stream", returns a PdfStreamObj.</summary>
    public static object? ParseTopLevelValue(byte[] buf, ref int pos)
    {
        var value = ParseValue(buf, ref pos);
        if (value is PdfDict dict)
        {
            int save = pos;
            SkipWhitespaceAndComments(buf, ref pos);
            if (MatchKeyword(buf, ref pos, "stream"))
            {
                // Per spec: "stream" is followed by CRLF or LF (not a lone CR).
                if (pos < buf.Length && buf[pos] == '\r') pos++;
                if (pos < buf.Length && buf[pos] == '\n') pos++;
                int dataStart = pos;
                int endIdx = IndexOf(buf, "endstream", dataStart);
                if (endIdx < 0) throw new InvalidDataException("Unterminated stream (no endstream found).");
                int dataEnd = endIdx;
                // Trim a single trailing EOL that precedes "endstream" (not part of the data per spec intent).
                if (dataEnd > dataStart && buf[dataEnd - 1] == '\n')
                {
                    dataEnd--;
                    if (dataEnd > dataStart && buf[dataEnd - 1] == '\r') dataEnd--;
                }
                else if (dataEnd > dataStart && buf[dataEnd - 1] == '\r')
                {
                    dataEnd--;
                }

                var streamObj = new PdfStreamObj();
                foreach (var kv in dict.Entries()) streamObj.Dict[kv.Key] = kv.Value;
                streamObj.Data = new byte[dataEnd - dataStart];
                Array.Copy(buf, dataStart, streamObj.Data, 0, dataEnd - dataStart);
                pos = endIdx + "endstream".Length;
                return streamObj;
            }
            pos = save;
        }
        return value;
    }

    public static object? ParseValue(byte[] buf, ref int pos)
    {
        SkipWhitespaceAndComments(buf, ref pos);
        if (pos >= buf.Length) throw new InvalidDataException("Unexpected end of data while parsing PDF object.");

        byte b = buf[pos];
        if (b == '/') return ParseName(buf, ref pos);
        if (b == '(') return ParseLiteralString(buf, ref pos);
        if (b == '[') return ParseArray(buf, ref pos);
        if (b == '<')
        {
            if (pos + 1 < buf.Length && buf[pos + 1] == '<') return ParseDict(buf, ref pos);
            return ParseHexString(buf, ref pos);
        }
        if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9')) return ParseNumberOrRef(buf, ref pos);

        if (MatchKeyword(buf, ref pos, "true")) return true;
        if (MatchKeyword(buf, ref pos, "false")) return false;
        if (MatchKeyword(buf, ref pos, "null")) return null;

        throw new InvalidDataException($"Unexpected byte 0x{b:X2} at position {pos} while parsing PDF object.");
    }

    private static object? ParseNumberOrRef(byte[] buf, ref int pos)
    {
        int start = pos;
        double first = ReadNumber(buf, ref pos);
        bool isInt = !HasDecimalPoint(buf, start, pos);

        if (isInt && first >= 0)
        {
            int save = pos;
            SkipWhitespaceAndComments(buf, ref pos);
            int genStart = pos;
            if (pos < buf.Length && (buf[pos] == '+' || buf[pos] == '-' || (buf[pos] >= '0' && buf[pos] <= '9')))
            {
                double gen = ReadNumber(buf, ref pos);
                bool genIsInt = !HasDecimalPoint(buf, genStart, pos);
                if (genIsInt && gen >= 0)
                {
                    int save2 = pos;
                    SkipWhitespaceAndComments(buf, ref pos);
                    if (pos < buf.Length && buf[pos] == 'R' && (pos + 1 >= buf.Length || IsWhitespace(buf[pos + 1]) || IsDelimiter(buf[pos + 1])))
                    {
                        pos++;
                        return new PdfRef((int)first, (int)gen);
                    }
                    pos = save2;
                }
            }
            pos = save;
        }

        return isInt ? (int)first : first;
    }

    private static bool HasDecimalPoint(byte[] buf, int start, int end)
    {
        for (int i = start; i < end; i++) if (buf[i] == '.') return true;
        return false;
    }

    private static double ReadNumber(byte[] buf, ref int pos)
    {
        int start = pos;
        if (pos < buf.Length && (buf[pos] == '+' || buf[pos] == '-')) pos++;
        while (pos < buf.Length && ((buf[pos] >= '0' && buf[pos] <= '9') || buf[pos] == '.')) pos++;
        string s = Encoding.ASCII.GetString(buf, start, pos - start);
        if (s.Length == 0 || s == "-" || s == "+") return 0;
        return double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static PdfName ParseName(byte[] buf, ref int pos)
    {
        pos++; // skip '/'
        var sb = new StringBuilder();
        while (pos < buf.Length && !IsWhitespace(buf[pos]) && !IsDelimiter(buf[pos]))
        {
            if (buf[pos] == '#' && pos + 2 < buf.Length)
            {
                string hex = Encoding.ASCII.GetString(buf, pos + 1, 2);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                {
                    sb.Append((char)code);
                    pos += 3;
                    continue;
                }
            }
            sb.Append((char)buf[pos]);
            pos++;
        }
        return new PdfName(sb.ToString());
    }

    private static string ParseLiteralString(byte[] buf, ref int pos)
    {
        pos++; // skip '('
        var sb = new StringBuilder();
        int depth = 1;
        while (pos < buf.Length && depth > 0)
        {
            byte c = buf[pos];
            if (c == '\\')
            {
                pos++;
                if (pos >= buf.Length) break;
                byte e = buf[pos];
                switch (e)
                {
                    case (byte)'n': sb.Append('\n'); pos++; break;
                    case (byte)'r': sb.Append('\r'); pos++; break;
                    case (byte)'t': sb.Append('\t'); pos++; break;
                    case (byte)'b': sb.Append('\b'); pos++; break;
                    case (byte)'f': sb.Append('\f'); pos++; break;
                    case (byte)'(': sb.Append('('); pos++; break;
                    case (byte)')': sb.Append(')'); pos++; break;
                    case (byte)'\\': sb.Append('\\'); pos++; break;
                    case (byte)'\r':
                        pos++;
                        if (pos < buf.Length && buf[pos] == '\n') pos++;
                        break;
                    case (byte)'\n': pos++; break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int val = 0, n = 0;
                            while (n < 3 && pos < buf.Length && buf[pos] >= '0' && buf[pos] <= '7')
                            {
                                val = val * 8 + (buf[pos] - '0');
                                pos++; n++;
                            }
                            sb.Append((char)(val & 0xFF));
                        }
                        else { sb.Append((char)e); pos++; }
                        break;
                }
                continue;
            }
            if (c == '(') { depth++; sb.Append('('); pos++; continue; }
            if (c == ')') { depth--; pos++; if (depth > 0) sb.Append(')'); continue; }
            sb.Append((char)c);
            pos++;
        }
        return sb.ToString();
    }

    private static string ParseHexString(byte[] buf, ref int pos)
    {
        pos++; // skip '<'
        var sb = new StringBuilder();
        var hex = new StringBuilder();
        while (pos < buf.Length && buf[pos] != '>')
        {
            if (!IsWhitespace(buf[pos])) hex.Append((char)buf[pos]);
            pos++;
        }
        if (pos < buf.Length) pos++; // skip '>'
        if (hex.Length % 2 == 1) hex.Append('0');
        for (int i = 0; i < hex.Length; i += 2)
        {
            sb.Append((char)Convert.ToInt32(hex.ToString(i, 2), 16));
        }
        return sb.ToString();
    }

    private static List<object?> ParseArray(byte[] buf, ref int pos)
    {
        pos++; // skip '['
        var list = new List<object?>();
        while (true)
        {
            SkipWhitespaceAndComments(buf, ref pos);
            if (pos >= buf.Length) throw new InvalidDataException("Unterminated array.");
            if (buf[pos] == ']') { pos++; break; }
            list.Add(ParseValue(buf, ref pos));
        }
        return list;
    }

    private static PdfDict ParseDict(byte[] buf, ref int pos)
    {
        pos += 2; // skip '<<'
        var dict = new PdfDict();
        while (true)
        {
            SkipWhitespaceAndComments(buf, ref pos);
            if (pos + 1 < buf.Length && buf[pos] == '>' && buf[pos + 1] == '>') { pos += 2; break; }
            if (pos >= buf.Length) throw new InvalidDataException("Unterminated dictionary.");
            if (buf[pos] != '/') throw new InvalidDataException($"Expected dictionary key at position {pos}.");
            var key = ParseName(buf, ref pos);
            var val = ParseValue(buf, ref pos);
            dict[key.Value] = val;
        }
        return dict;
    }

    public static bool MatchKeyword(byte[] buf, ref int pos, string keyword)
    {
        if (pos + keyword.Length > buf.Length) return false;
        for (int i = 0; i < keyword.Length; i++)
            if (buf[pos + i] != (byte)keyword[i]) return false;
        int after = pos + keyword.Length;
        if (after < buf.Length && !IsWhitespace(buf[after]) && !IsDelimiter(buf[after]))
        {
            // e.g. "trueXYZ" is not the keyword "true"
            if (char.IsLetterOrDigit((char)buf[after])) return false;
        }
        pos = after;
        return true;
    }

    public static int IndexOf(byte[] buf, string needle, int start)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);
        for (int i = start; i <= buf.Length - bytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < bytes.Length; j++)
            {
                if (buf[i + j] != bytes[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    public static int LastIndexOf(byte[] buf, string needle)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);
        for (int i = buf.Length - bytes.Length; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < bytes.Length; j++)
            {
                if (buf[i + j] != bytes[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}

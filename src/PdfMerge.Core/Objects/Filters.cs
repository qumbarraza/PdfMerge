using System.IO.Compression;

namespace PdfMerge.Core.Objects;

public static class Filters
{
    public static byte[] FlateDecode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Applies PNG-style predictor un-filtering used by xref streams and some image/object streams.</summary>
    public static byte[] ApplyPngPredictor(byte[] data, int columns, int colors, int bitsPerComponent)
    {
        int bpp = Math.Max(1, (colors * bitsPerComponent + 7) / 8);
        int stride = (columns * colors * bitsPerComponent + 7) / 8;
        int rowStride = stride + 1; // +1 for the leading filter-type byte
        int rows = data.Length / rowStride;

        var output = new byte[stride * rows];
        var prev = new byte[stride];
        var cur = new byte[stride];

        int pos = 0;
        for (int r = 0; r < rows; r++)
        {
            byte filter = data[pos++];
            Array.Copy(data, pos, cur, 0, stride);
            pos += stride;

            switch (filter)
            {
                case 0: break;
                case 1:
                    for (int i = bpp; i < stride; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]);
                    break;
                case 2:
                    for (int i = 0; i < stride; i++) cur[i] = (byte)(cur[i] + prev[i]);
                    break;
                case 3:
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + (a + prev[i]) / 2);
                    }
                    break;
                case 4:
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0;
                        int b = prev[i];
                        int c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        int pred = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                        cur[i] = (byte)(cur[i] + pred);
                    }
                    break;
                default:
                    throw new InvalidDataException($"Unknown predictor filter type {filter}.");
            }

            Array.Copy(cur, 0, output, r * stride, stride);
            (prev, cur) = (cur, prev);
        }
        return output;
    }

    /// <summary>Decodes stream data according to its dictionary's /Filter and /DecodeParms (Flate + optional PNG predictor only).</summary>
    public static byte[] DecodeStream(PdfDict dict, byte[] raw, Func<object?, object?> resolve)
    {
        var filterVal = resolve(dict[PdfName.Filter.Value]);
        var parmsVal = resolve(dict["DecodeParms"] ?? dict["DP"]);

        List<string> filters = filterVal switch
        {
            PdfName n => new List<string> { n.Value },
            List<object?> arr => arr.Select(x => (resolve(x) as PdfName)?.Value ?? "").ToList(),
            _ => new List<string>(),
        };
        List<PdfDict?> parmsList = parmsVal switch
        {
            PdfDict d => new List<PdfDict?> { d },
            List<object?> arr => arr.Select(x => resolve(x) as PdfDict).ToList(),
            _ => new List<PdfDict?>(),
        };

        byte[] data = raw;
        for (int i = 0; i < filters.Count; i++)
        {
            if (filters[i] != "FlateDecode" && filters[i] != "Fl") continue;
            data = FlateDecode(data);
            var parms = i < parmsList.Count ? parmsList[i] : null;
            if (parms != null)
            {
                int predictor = ToInt(resolve(parms["Predictor"]), 1);
                if (predictor >= 10)
                {
                    int columns = ToInt(resolve(parms["Columns"]), 1);
                    int colors = ToInt(resolve(parms["Colors"]), 1);
                    int bpc = ToInt(resolve(parms["BitsPerComponent"]), 8);
                    data = ApplyPngPredictor(data, columns, colors, bpc);
                }
            }
        }
        return data;
    }

    private static int ToInt(object? v, int def) => v switch
    {
        int i => i,
        double d => (int)d,
        _ => def,
    };
}

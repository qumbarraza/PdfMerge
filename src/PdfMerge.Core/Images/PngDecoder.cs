using System.IO.Compression;

namespace PdfMerge.Core.Images;

public enum PngColorType
{
    Gray = 0,
    Rgb = 2,
    Indexed = 3,
    GrayAlpha = 4,
    Rgba = 6,
}

/// <summary>
/// Minimal PNG decoder: supports 8-bit depth, non-interlaced images (color types
/// Gray/RGB/Indexed/GrayAlpha/RGBA). Enough for typical exported screenshots/photos.
/// </summary>
public sealed class DecodedPng
{
    public int Width { get; init; }
    public int Height { get; init; }
    public PngColorType ColorType { get; init; }

    /// <summary>Raw, de-filtered pixel bytes (channels interleaved, no PNG filter bytes).</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();

    /// <summary>Palette entries (R,G,B) for indexed images, else empty.</summary>
    public byte[] Palette { get; init; } = Array.Empty<byte>();

    public int Channels => ColorType switch
    {
        PngColorType.Gray => 1,
        PngColorType.Rgb => 3,
        PngColorType.Indexed => 1,
        PngColorType.GrayAlpha => 2,
        PngColorType.Rgba => 4,
        _ => throw new NotSupportedException(),
    };

    public static DecodedPng Decode(byte[] data)
    {
        if (data.Length < 8 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            throw new InvalidDataException("Not a valid PNG (missing signature).");

        int pos = 8;
        int width = 0, height = 0, bitDepth = 0, interlace = 0;
        PngColorType colorType = PngColorType.Rgb;
        byte[] palette = Array.Empty<byte>();
        using var idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int bodyStart = pos + 8;

            if (type == "IHDR")
            {
                width = ReadBigEndianInt32(data, bodyStart);
                height = ReadBigEndianInt32(data, bodyStart + 4);
                bitDepth = data[bodyStart + 8];
                colorType = (PngColorType)data[bodyStart + 9];
                interlace = data[bodyStart + 12];
            }
            else if (type == "PLTE")
            {
                palette = new byte[len];
                Array.Copy(data, bodyStart, palette, 0, len);
            }
            else if (type == "IDAT")
            {
                idat.Write(data, bodyStart, len);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos = bodyStart + len + 4; // skip CRC
        }

        if (width == 0 || height == 0)
            throw new InvalidDataException("PNG missing IHDR.");
        if (bitDepth != 8)
            throw new NotSupportedException($"PNG bit depth {bitDepth} is not supported (only 8-bit PNGs); re-save the image as 8-bit.");
        if (interlace != 0)
            throw new NotSupportedException("Interlaced (Adam7) PNGs are not supported; re-save the image without interlacing.");

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }
        byte[] raw = inflated.ToArray();

        int channels = colorType switch
        {
            PngColorType.Gray => 1,
            PngColorType.Rgb => 3,
            PngColorType.Indexed => 1,
            PngColorType.GrayAlpha => 2,
            PngColorType.Rgba => 4,
            _ => throw new NotSupportedException($"Unsupported PNG color type {colorType}."),
        };

        int bpp = channels; // bit depth is always 8 here
        int stride = width * channels;
        var pixels = new byte[stride * height];
        int rawPos = 0;
        byte[] prev = new byte[stride];
        byte[] cur = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            byte filter = raw[rawPos++];
            Array.Copy(raw, rawPos, cur, 0, stride);
            rawPos += stride;
            Unfilter(filter, cur, prev, bpp);
            Array.Copy(cur, 0, pixels, y * stride, stride);
            (prev, cur) = (cur, prev);
        }

        return new DecodedPng
        {
            Width = width,
            Height = height,
            ColorType = colorType,
            Pixels = pixels,
            Palette = palette,
        };
    }

    private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        int len = cur.Length;
        switch (filter)
        {
            case 0: // None
                break;
            case 1: // Sub
                for (int i = bpp; i < len; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]);
                break;
            case 2: // Up
                for (int i = 0; i < len; i++) cur[i] = (byte)(cur[i] + prev[i]);
                break;
            case 3: // Average
                for (int i = 0; i < len; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    cur[i] = (byte)(cur[i] + (a + b) / 2);
                }
                break;
            case 4: // Paeth
                for (int i = 0; i < len; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    int c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] = (byte)(cur[i] + PaethPredictor(a, b, c));
                }
                break;
            default:
                throw new InvalidDataException($"Unknown PNG filter type {filter}.");
        }
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

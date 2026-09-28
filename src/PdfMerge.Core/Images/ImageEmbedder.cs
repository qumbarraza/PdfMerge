using System.IO.Compression;
using PdfMerge.Core.Objects;

namespace PdfMerge.Core.Images;

public readonly struct EmbeddedImage
{
    public PdfRef XObjectRef { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>EXIF orientation (1-8, JPEG only; always 1 for PNG).</summary>
    public int Orientation { get; init; } = 1;

    public EmbeddedImage() { }
}

/// <summary>Encodes JPEG/PNG files as PDF image XObjects and writes them into the target document.</summary>
public static class ImageEmbedder
{
    public static EmbeddedImage Embed(PdfWriter writer, byte[] fileBytes, string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => EmbedJpeg(writer, fileBytes),
            ".png" => EmbedPng(writer, fileBytes),
            _ => throw new NotSupportedException($"Unsupported image extension '{extension}'. Only .jpg/.jpeg/.png are supported."),
        };
    }

    private static EmbeddedImage EmbedJpeg(PdfWriter writer, byte[] fileBytes)
    {
        var info = JpegInfo.Parse(fileBytes);
        string colorSpace = info.Components switch
        {
            1 => "DeviceGray",
            4 => "DeviceCMYK",
            _ => "DeviceRGB",
        };

        var stream = new PdfStreamObj();
        stream.Dict[PdfName.Type.Value] = new PdfName("XObject");
        stream.Dict["Subtype"] = new PdfName("Image");
        stream.Dict["Width"] = info.Width;
        stream.Dict["Height"] = info.Height;
        stream.Dict["BitsPerComponent"] = 8;
        stream.Dict["ColorSpace"] = new PdfName(colorSpace);
        stream.Dict[PdfName.Filter.Value] = new PdfName("DCTDecode");
        if (info.IsAdobeInvertedCmyk)
        {
            // Adobe writes 4-component JPEGs with inverted CMYK samples; without this, they render as a negative.
            stream.Dict["Decode"] = new List<object?> { 1, 0, 1, 0, 1, 0, 1, 0 };
        }
        stream.Data = fileBytes;

        var xref = writer.WriteNewObject(stream);
        return new EmbeddedImage { XObjectRef = xref, Width = info.Width, Height = info.Height, Orientation = info.Orientation };
    }

    private static EmbeddedImage EmbedPng(PdfWriter writer, byte[] fileBytes)
    {
        var png = DecodedPng.Decode(fileBytes);

        PdfRef? smaskRef = null;
        List<object?>? colorKeyMask = null;
        byte[] colorBytes;
        object colorSpace;
        int bitsPerComponent = png.BitDepth;

        switch (png.ColorType)
        {
            case PngColorType.Gray:
                colorBytes = png.Pixels;
                colorSpace = new PdfName("DeviceGray");
                if (png.Transparency.Length >= 2)
                {
                    int v = (png.Transparency[0] << 8) | png.Transparency[1];
                    colorKeyMask = new List<object?> { v, v };
                }
                break;

            case PngColorType.Rgb:
                colorBytes = png.Pixels;
                colorSpace = new PdfName("DeviceRGB");
                if (png.Transparency.Length >= 6)
                {
                    int r = (png.Transparency[0] << 8) | png.Transparency[1];
                    int g = (png.Transparency[2] << 8) | png.Transparency[3];
                    int b = (png.Transparency[4] << 8) | png.Transparency[5];
                    colorKeyMask = new List<object?> { r, r, g, g, b, b };
                }
                break;

            case PngColorType.Indexed:
                colorBytes = png.Pixels;
                {
                    int hival = Math.Max(0, png.Palette.Length / 3 - 1);
                    var lookup = new string(png.Palette.Select(b => (char)b).ToArray());
                    colorSpace = new List<object?> { new PdfName("Indexed"), new PdfName("DeviceRGB"), hival, lookup };
                }
                if (png.Transparency.Length > 0)
                {
                    var alpha = ExpandIndexedAlpha(png);
                    smaskRef = WriteSMask(writer, alpha, png.Width, png.Height, 8);
                }
                break;

            case PngColorType.GrayAlpha:
            {
                int bytesPerSample = png.BitDepth / 8;
                (colorBytes, var alpha) = SplitChannels(png.Pixels, 2, 1, bytesPerSample);
                smaskRef = WriteSMask(writer, alpha, png.Width, png.Height, png.BitDepth);
                colorSpace = new PdfName("DeviceGray");
                break;
            }

            case PngColorType.Rgba:
            {
                int bytesPerSample = png.BitDepth / 8;
                (colorBytes, var alpha) = SplitChannels(png.Pixels, 4, 3, bytesPerSample);
                smaskRef = WriteSMask(writer, alpha, png.Width, png.Height, png.BitDepth);
                colorSpace = new PdfName("DeviceRGB");
                break;
            }

            default:
                throw new NotSupportedException($"Unsupported PNG color type {png.ColorType}.");
        }

        var stream = new PdfStreamObj();
        stream.Dict[PdfName.Type.Value] = new PdfName("XObject");
        stream.Dict["Subtype"] = new PdfName("Image");
        stream.Dict["Width"] = png.Width;
        stream.Dict["Height"] = png.Height;
        stream.Dict["BitsPerComponent"] = bitsPerComponent;
        stream.Dict["ColorSpace"] = colorSpace;
        stream.Dict[PdfName.Filter.Value] = new PdfName("FlateDecode");
        if (smaskRef != null) stream.Dict["SMask"] = smaskRef.Value;
        else if (colorKeyMask != null) stream.Dict["Mask"] = colorKeyMask;
        stream.Data = FlateCompress(colorBytes);

        var xref = writer.WriteNewObject(stream);
        return new EmbeddedImage { XObjectRef = xref, Width = png.Width, Height = png.Height };
    }

    /// <summary>Builds a full-resolution 8-bit alpha channel for an indexed PNG from its tRNS palette-alpha table.</summary>
    private static byte[] ExpandIndexedAlpha(DecodedPng png)
    {
        var alpha = new byte[png.Width * png.Height];
        int stride = (png.Width * png.BitDepth + 7) / 8;
        int p = 0;
        for (int y = 0; y < png.Height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < png.Width; x++)
            {
                int index = ReadPackedSample(png.Pixels, rowStart, x, png.BitDepth);
                alpha[p++] = index < png.Transparency.Length ? png.Transparency[index] : (byte)255;
            }
        }
        return alpha;
    }

    private static int ReadPackedSample(byte[] data, int rowStart, int x, int bitDepth)
    {
        if (bitDepth == 8) return data[rowStart + x];
        if (bitDepth == 16) return data[rowStart + x * 2]; // high byte is enough for a palette index (<= 255)

        int samplesPerByte = 8 / bitDepth;
        int byteIndex = rowStart + x / samplesPerByte;
        int shift = 8 - bitDepth - (x % samplesPerByte) * bitDepth;
        int mask = (1 << bitDepth) - 1;
        return (data[byteIndex] >> shift) & mask;
    }

    private static PdfRef WriteSMask(PdfWriter writer, byte[] alphaBytes, int width, int height, int bitsPerComponent)
    {
        var stream = new PdfStreamObj();
        stream.Dict[PdfName.Type.Value] = new PdfName("XObject");
        stream.Dict["Subtype"] = new PdfName("Image");
        stream.Dict["Width"] = width;
        stream.Dict["Height"] = height;
        stream.Dict["BitsPerComponent"] = bitsPerComponent;
        stream.Dict["ColorSpace"] = new PdfName("DeviceGray");
        stream.Dict[PdfName.Filter.Value] = new PdfName("FlateDecode");
        stream.Data = FlateCompress(alphaBytes);
        return writer.WriteNewObject(stream);
    }

    /// <summary>Splits interleaved pixel bytes into (colorChannels, alphaChannel), preserving each sample's byte width (1 for 8-bit, 2 for 16-bit).</summary>
    private static (byte[] Color, byte[] Alpha) SplitChannels(byte[] pixels, int totalChannels, int colorChannels, int bytesPerSample)
    {
        int pixelStride = totalChannels * bytesPerSample;
        int colorStride = colorChannels * bytesPerSample;
        int pixelCount = pixels.Length / pixelStride;
        var color = new byte[pixelCount * colorStride];
        var alpha = new byte[pixelCount * bytesPerSample];
        for (int p = 0; p < pixelCount; p++)
        {
            Array.Copy(pixels, p * pixelStride, color, p * colorStride, colorStride);
            Array.Copy(pixels, p * pixelStride + colorStride, alpha, p * bytesPerSample, bytesPerSample);
        }
        return (color, alpha);
    }

    private static byte[] FlateCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }
}

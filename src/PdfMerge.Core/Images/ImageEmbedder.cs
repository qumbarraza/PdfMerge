using System.IO.Compression;
using PdfMerge.Core.Objects;

namespace PdfMerge.Core.Images;

public readonly struct EmbeddedImage
{
    public PdfRef XObjectRef { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
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
        stream.Data = fileBytes;

        var xref = writer.WriteNewObject(stream);
        return new EmbeddedImage { XObjectRef = xref, Width = info.Width, Height = info.Height };
    }

    private static EmbeddedImage EmbedPng(PdfWriter writer, byte[] fileBytes)
    {
        var png = DecodedPng.Decode(fileBytes);

        PdfRef? smaskRef = null;
        byte[] colorBytes;
        object colorSpace;

        switch (png.ColorType)
        {
            case PngColorType.Gray:
                colorBytes = png.Pixels;
                colorSpace = new PdfName("DeviceGray");
                break;

            case PngColorType.Rgb:
                colorBytes = png.Pixels;
                colorSpace = new PdfName("DeviceRGB");
                break;

            case PngColorType.Indexed:
                colorBytes = png.Pixels;
                {
                    int hival = Math.Max(0, png.Palette.Length / 3 - 1);
                    var lookup = new string(png.Palette.Select(b => (char)b).ToArray());
                    colorSpace = new List<object?> { new PdfName("Indexed"), new PdfName("DeviceRGB"), hival, lookup };
                }
                break;

            case PngColorType.GrayAlpha:
            {
                (colorBytes, var alpha) = SplitChannels(png.Pixels, 2, 1);
                smaskRef = WriteSMask(writer, alpha, png.Width, png.Height);
                colorSpace = new PdfName("DeviceGray");
                break;
            }

            case PngColorType.Rgba:
            {
                (colorBytes, var alpha) = SplitChannels(png.Pixels, 4, 3);
                smaskRef = WriteSMask(writer, alpha, png.Width, png.Height);
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
        stream.Dict["BitsPerComponent"] = 8;
        stream.Dict["ColorSpace"] = colorSpace;
        stream.Dict[PdfName.Filter.Value] = new PdfName("FlateDecode");
        if (smaskRef != null) stream.Dict["SMask"] = smaskRef.Value;
        stream.Data = FlateCompress(colorBytes);

        var xref = writer.WriteNewObject(stream);
        return new EmbeddedImage { XObjectRef = xref, Width = png.Width, Height = png.Height };
    }

    private static PdfRef WriteSMask(PdfWriter writer, byte[] alphaBytes, int width, int height)
    {
        var stream = new PdfStreamObj();
        stream.Dict[PdfName.Type.Value] = new PdfName("XObject");
        stream.Dict["Subtype"] = new PdfName("Image");
        stream.Dict["Width"] = width;
        stream.Dict["Height"] = height;
        stream.Dict["BitsPerComponent"] = 8;
        stream.Dict["ColorSpace"] = new PdfName("DeviceGray");
        stream.Dict[PdfName.Filter.Value] = new PdfName("FlateDecode");
        stream.Data = FlateCompress(alphaBytes);
        return writer.WriteNewObject(stream);
    }

    /// <summary>Splits interleaved pixel bytes into (colorChannels, alphaChannel) given total channel count and color-channel count.</summary>
    private static (byte[] Color, byte[] Alpha) SplitChannels(byte[] pixels, int totalChannels, int colorChannels)
    {
        int pixelCount = pixels.Length / totalChannels;
        var color = new byte[pixelCount * colorChannels];
        var alpha = new byte[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            Array.Copy(pixels, p * totalChannels, color, p * colorChannels, colorChannels);
            alpha[p] = pixels[p * totalChannels + colorChannels];
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

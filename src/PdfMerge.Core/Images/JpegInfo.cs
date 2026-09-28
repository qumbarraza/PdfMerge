namespace PdfMerge.Core.Images;

public sealed class JpegInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Components { get; init; } // 1 = gray, 3 = RGB (YCbCr), 4 = CMYK

    /// <summary>Parses just enough of the JPEG header (SOF marker) to get dimensions and color depth.</summary>
    public static JpegInfo Parse(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Not a valid JPEG (missing SOI marker).");

        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF) { pos++; continue; }
            byte marker = data[pos + 1];

            // Standalone markers with no length/payload.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2;
                continue;
            }

            if (pos + 4 > data.Length) break;
            int segLen = (data[pos + 2] << 8) | data[pos + 3];

            bool isSof = marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
                                  or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
            if (isSof)
            {
                int height = (data[pos + 5] << 8) | data[pos + 6];
                int width = (data[pos + 7] << 8) | data[pos + 8];
                int components = data[pos + 9];
                return new JpegInfo { Width = width, Height = height, Components = components };
            }

            if (marker == 0xDA) break; // Start of scan: no SOF found before compressed data.
            pos += 2 + segLen;
        }

        throw new InvalidDataException("Could not locate JPEG SOF marker.");
    }
}

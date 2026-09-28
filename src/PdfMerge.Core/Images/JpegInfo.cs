namespace PdfMerge.Core.Images;

public sealed class JpegInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Components { get; init; } // 1 = gray, 3 = RGB (YCbCr), 4 = CMYK

    /// <summary>EXIF orientation tag (1-8), or 1 (normal) if absent/unrecognized.</summary>
    public int Orientation { get; init; } = 1;

    /// <summary>True when a 4-component JPEG carries an Adobe APP14 marker, meaning the CMYK data is inverted.</summary>
    public bool IsAdobeInvertedCmyk { get; init; }

    /// <summary>Parses JPEG markers to get dimensions, color depth, EXIF orientation, and Adobe CMYK inversion.</summary>
    public static JpegInfo Parse(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Not a valid JPEG (missing SOI marker).");

        int width = 0, height = 0, components = 0;
        bool sofFound = false;
        int orientation = 1;
        bool hasAdobeMarker = false;

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
            int bodyStart = pos + 4;

            bool isSof = marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
                                  or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
            if (isSof && !sofFound)
            {
                height = (data[pos + 5] << 8) | data[pos + 6];
                width = (data[pos + 7] << 8) | data[pos + 8];
                components = data[pos + 9];
                sofFound = true;
            }
            else if (marker == 0xE1 && segLen >= 8) // APP1: possibly EXIF
            {
                orientation = TryReadExifOrientation(data, bodyStart, segLen - 2) ?? orientation;
            }
            else if (marker == 0xEE) // APP14: Adobe
            {
                hasAdobeMarker = true;
            }

            if (marker == 0xDA) break; // Start of scan: stop, we have everything markers-wise.
            pos = bodyStart + (segLen - 2);
        }

        if (!sofFound) throw new InvalidDataException("Could not locate JPEG SOF marker.");

        return new JpegInfo
        {
            Width = width,
            Height = height,
            Components = components,
            Orientation = orientation,
            IsAdobeInvertedCmyk = hasAdobeMarker && components == 4,
        };
    }

    private static int? TryReadExifOrientation(byte[] data, int start, int len)
    {
        // Expect "Exif\0\0" followed by a TIFF header.
        if (len < 14) return null;
        if (data[start] != 'E' || data[start + 1] != 'x' || data[start + 2] != 'i' || data[start + 3] != 'f') return null;
        int tiffStart = start + 6;
        if (tiffStart + 8 > data.Length) return null;

        bool little = data[tiffStart] == 0x49 && data[tiffStart + 1] == 0x49; // "II"
        bool big = data[tiffStart] == 0x4D && data[tiffStart + 1] == 0x4D;    // "MM"
        if (!little && !big) return null;

        int ReadU16(int off) => little ? data[off] | (data[off + 1] << 8) : (data[off] << 8) | data[off + 1];
        int ReadU32(int off) => little
            ? data[off] | (data[off + 1] << 8) | (data[off + 2] << 16) | (data[off + 3] << 24)
            : (data[off] << 24) | (data[off + 1] << 16) | (data[off + 2] << 8) | data[off + 3];

        int ifdOffset = ReadU32(tiffStart + 4);
        int ifdStart = tiffStart + ifdOffset;
        if (ifdStart + 2 > data.Length) return null;

        int entryCount = ReadU16(ifdStart);
        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = ifdStart + 2 + i * 12;
            if (entryOffset + 12 > data.Length) break;
            int tag = ReadU16(entryOffset);
            if (tag == 0x0112) // Orientation
            {
                int value = ReadU16(entryOffset + 8);
                return value is >= 1 and <= 8 ? value : null;
            }
        }
        return null;
    }
}

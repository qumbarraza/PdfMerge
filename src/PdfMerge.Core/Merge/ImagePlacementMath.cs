namespace PdfMerge.Core.Merge;

internal readonly record struct PlacementMatrix(double A, double B, double C, double D, double E, double F);

/// <summary>
/// Computes the page size and image-placement matrix for a single image page, honoring
/// EXIF orientation (1,2,3,4,6,8 - the common cases; 5/7, transpose/mirror, fall back to normal)
/// and the FitToPage/NativeSize placement modes.
/// </summary>
internal static class ImagePlacementMath
{
    public static (double PageWidth, double PageHeight, PlacementMatrix Matrix) Compute(
        int pixelWidth, int pixelHeight, int orientation, MergeOptions options)
    {
        bool swapped = orientation is 5 or 6 or 7 or 8;
        double displayWidth = swapped ? pixelHeight : pixelWidth;
        double displayHeight = swapped ? pixelWidth : pixelHeight;

        double dw, dh, x, y, pageWidth, pageHeight;
        if (options.Images == ImagePlacement.FitToPage)
        {
            var (pw, ph) = options.GetPageSizePoints();
            double availW = Math.Max(1, pw - 2 * options.MarginPoints);
            double availH = Math.Max(1, ph - 2 * options.MarginPoints);
            double scale = Math.Min(availW / displayWidth, availH / displayHeight);
            dw = displayWidth * scale;
            dh = displayHeight * scale;
            x = (pw - dw) / 2;
            y = (ph - dh) / 2;
            pageWidth = pw;
            pageHeight = ph;
        }
        else
        {
            dw = displayWidth;
            dh = displayHeight;
            x = 0;
            y = 0;
            pageWidth = displayWidth;
            pageHeight = displayHeight;
        }

        var matrix = orientation switch
        {
            2 => new PlacementMatrix(-dw, 0, 0, dh, dw + x, y),
            3 => new PlacementMatrix(-dw, 0, 0, -dh, dw + x, dh + y),
            4 => new PlacementMatrix(dw, 0, 0, -dh, x, dh + y),
            6 => new PlacementMatrix(0, -dh, dw, 0, x, dh + y),
            8 => new PlacementMatrix(0, dh, -dw, 0, dw + x, y),
            _ => new PlacementMatrix(dw, 0, 0, dh, x, y), // 1 (normal), and 5/7 (rare transpose cases: not corrected)
        };

        return (pageWidth, pageHeight, matrix);
    }
}

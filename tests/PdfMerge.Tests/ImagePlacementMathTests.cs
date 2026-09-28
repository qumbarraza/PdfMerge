using PdfMerge.Core.Merge;
using Xunit;

namespace PdfMerge.Tests;

public class ImagePlacementMathTests
{
    [Fact]
    public void NativeSize_Orientation1_PageMatchesPixelDimensions()
    {
        var options = new MergeOptions { Images = ImagePlacement.NativeSize };
        var (w, h, m) = ImagePlacementMath.Compute(300, 200, 1, options);

        Assert.Equal(300, w);
        Assert.Equal(200, h);
        Assert.Equal(300, m.A, 3);
        Assert.Equal(200, m.D, 3);
        Assert.Equal(0, m.E, 3);
        Assert.Equal(0, m.F, 3);
    }

    [Fact]
    public void NativeSize_Orientation6_SwapsPageDimensions()
    {
        // Orientation 6 = "rotate 90 CW to display correctly": a 300x200 (w x h) source
        // photo displays as a 200x300 portrait page.
        var options = new MergeOptions { Images = ImagePlacement.NativeSize };
        var (w, h, _) = ImagePlacementMath.Compute(300, 200, 6, options);

        Assert.Equal(200, w);
        Assert.Equal(300, h);
    }

    [Fact]
    public void FitToPage_LargePhoto_FitsWithinA4WithMargins()
    {
        var options = new MergeOptions { Images = ImagePlacement.FitToPage, ImagePageSize = PageSize.A4, MarginPoints = 36 };
        var (w, h, m) = ImagePlacementMath.Compute(4032, 3024, 1, options);

        Assert.Equal(595, w);
        Assert.Equal(842, h);
        double availW = 595 - 72, availH = 842 - 72;
        Assert.True(m.A <= availW + 0.01);
        Assert.True(m.D <= availH + 0.01);
        // Centered: left margin should roughly equal right margin.
        Assert.Equal((w - m.A) / 2, m.E, 1);
    }
}

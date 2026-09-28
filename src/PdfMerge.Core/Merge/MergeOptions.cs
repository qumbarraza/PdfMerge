namespace PdfMerge.Core.Merge;

public enum ImagePlacement
{
    /// <summary>Each image becomes its own page sized to the image's pixel dimensions (1 px = 1 pt).</summary>
    NativeSize,

    /// <summary>Each image is scaled to fit within a standard page size, centered, with a margin.</summary>
    FitToPage,
}

public enum PageSize
{
    A4,
    Letter,
}

public enum MergeErrorPolicy
{
    /// <summary>An unreadable/unsupported input aborts the whole merge (default).</summary>
    Throw,

    /// <summary>An unreadable/unsupported input is skipped; the rest of the inputs are still merged.</summary>
    Skip,
}

public sealed class MergeOptions
{
    public ImagePlacement Images { get; init; } = ImagePlacement.FitToPage;
    public PageSize ImagePageSize { get; init; } = PageSize.A4;
    public double MarginPoints { get; init; } = 36;
    public bool ApplyExifOrientation { get; init; } = true;
    public MergeErrorPolicy OnError { get; init; } = MergeErrorPolicy.Throw;

    public static readonly MergeOptions Default = new();

    internal (double Width, double Height) GetPageSizePoints() => ImagePageSize switch
    {
        PageSize.Letter => (612, 792),
        _ => (595, 842), // A4
    };
}

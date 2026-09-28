namespace PdfMerge.Core.Merge;

public enum MergeInputKind
{
    Pdf,
    Image,
}

/// <summary>One input to a merge: a name (for error reporting) and a factory that opens its bytes.
/// <paramref name="Open"/> may be called more than once and each returned stream is disposed after use.</summary>
public sealed record MergeInput(string Name, Func<Stream> Open, MergeInputKind Kind)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public static MergeInput FromFile(string path)
    {
        string ext = Path.GetExtension(path);
        var kind = string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) ? MergeInputKind.Pdf
            : ImageExtensions.Contains(ext) ? MergeInputKind.Image
            : throw new NotSupportedException($"Unsupported input file '{path}'. Only .pdf, .jpg/.jpeg, and .png are supported.");
        return new MergeInput(Path.GetFileName(path), () => File.OpenRead(path), kind);
    }
}

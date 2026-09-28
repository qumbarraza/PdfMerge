# PdfMerge

A small, **dependency-free** PDF engine for .NET — a PDF reader, writer, and merger built
from scratch (no PDFsharp, no iText, no native dependencies). It can:

- Write valid PDF files from scratch (objects, xref table, trailer, `/Info`, `/ID`).
- Parse existing PDFs, including modern producers that use cross-reference streams,
  compressed object streams, and incremental updates (`/Prev` chains) — not just classic,
  single-revision xref tables. Falls back to rebuilding the xref by scanning the file when
  it's damaged or non-conforming.
- Embed JPEG images directly (no re-encoding, with Adobe CMYK inversion handled and EXIF
  orientation applied) and PNG images (bit depths 1/2/4/8/16, indexed/gray/RGB/alpha,
  `tRNS` transparency; non-interlaced only).
- Merge any mix of PDF files and JPEG/PNG images into a single output PDF, preserving
  each source PDF's fonts, images, annotations, and other resources.

## Install

```bash
dotnet add package Shark.PDFMerge
```

## Usage

```csharp
using PdfMerge.Core.Merge;

MergeReport report = PdfMerger.Merge(
    new[] { "cover.pdf", "photo.jpg", "scan.png", "appendix.pdf" },
    "combined.pdf");
```

Each PDF input contributes its pages as-is. Each image input becomes its own page; by
default it's scaled to fit an A4 page with a margin (`MergeOptions.Images = FitToPage`).

For in-memory files (e.g. a web app with blob storage instead of local disk), use the
stream-based overload:

```csharp
var inputs = new[]
{
    new MergeInput("cover.pdf", () => blobStore.OpenRead("cover.pdf"), MergeInputKind.Pdf),
    new MergeInput("photo.jpg", () => blobStore.OpenRead("photo.jpg"), MergeInputKind.Image),
};
var options = new MergeOptions
{
    Images = ImagePlacement.FitToPage,
    ImagePageSize = PageSize.A4,
    OnError = MergeErrorPolicy.Skip, // keep going past a bad input instead of aborting
};
MergeReport report = PdfMerger.Merge(inputs, outputStream, options);

foreach (var result in report.Inputs)
{
    if (!result.Success) Console.WriteLine($"{result.Name}: {result.Error}");
}
```

`Merge(paths, outputPath, options)` writes to a temporary file and only replaces the
destination on success, so a failed merge never leaves a truncated PDF behind.

## CLI

A companion console tool is included in this repo (not published to NuGet):

```bash
dotnet run --project src/PdfMerge.Cli -- output.pdf input1.pdf input2.jpg input3.png
```

## Current limitations

- PNG support does not include Adam7 interlacing (throws with a clear message; re-save
  the image without interlacing).
- No decryption support — encrypted PDFs are detected and rejected with a clear
  `NotSupportedException` rather than silently producing garbage pages.
- Mirrored EXIF orientations (2, 4, 5, 7) are not corrected; only the common rotation
  cases (1, 3, 6, 8) are. Orientation 5/7 (transpose/anti-transpose) is rendered as-is.
- Fonts/images used by more than one source PDF are not de-duplicated across documents
  (only within a single source document), so merging many PDFs that share embedded fonts
  produces a larger file than strictly necessary.
- Only `.pdf`, `.jpg`/`.jpeg`, and `.png` inputs are supported.

## License

MIT — see [LICENSE](LICENSE).

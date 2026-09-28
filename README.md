# PdfMerge

A small, **dependency-free** PDF engine for .NET — a PDF reader, writer, and merger built
from scratch (no PDFsharp, no iText, no native dependencies). It can:

- Write valid PDF files from scratch (objects, xref table, trailer).
- Parse existing PDFs, including modern producers that use cross-reference streams and
  compressed object streams (not just classic xref tables).
- Embed JPEG images directly (no re-encoding) and PNG images (8-bit, non-interlaced,
  including alpha via soft masks).
- Merge any mix of PDF files and JPEG/PNG images into a single output PDF, preserving
  each source PDF's fonts, images, and other resources.

## Install

```bash
dotnet add package Shark.PDFMerge
```

## Usage

```csharp
using PdfMerge.Core.Merge;

PdfMerger.Merge(
    new[] { "cover.pdf", "photo.jpg", "scan.png", "appendix.pdf" },
    "combined.pdf");
```

Each PDF input contributes its pages as-is; each image input becomes its own page sized
to the image's pixel dimensions.

## CLI

A companion console tool is included in this repo (not published to NuGet):

```bash
dotnet run --project src/PdfMerge.Cli -- output.pdf input1.pdf input2.jpg input3.png
```

## Current limitations

- PNG support is limited to 8-bit, non-interlaced images.
- No encryption/password support (reading or writing).
- Images are embedded as a full page at their native pixel size — no automatic
  scale-to-fit onto an existing page size.

## License

MIT — see [LICENSE](LICENSE).

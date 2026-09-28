# Changelog

## 0.2.0

Fixes and hardening from an external review of 0.1.0 ([details](https://github.com/qumbarraza/PdfMerge)).

### Fixed
- **Critical:** a C# ternary-unification bug boxed every parsed integer as `double`, so
  `is int` checks on `/Prev`, `/XRefStm`, and `/Size` never matched. This silently broke
  reading any PDF with more than one xref section — incrementally-updated, linearized,
  signed, or annotated files came out with zero pages and no error.
- A PDF whose `/Root` didn't resolve, or whose page tree yielded zero pages, now throws
  instead of silently producing an empty (but "successful") merge.
- Stream data is now read via `/Length` (resolving an indirect reference, verifying
  `endstream` follows) instead of always scanning for the first `endstream` byte
  sequence, which could cut a stream short if its own binary data happened to contain
  those bytes. Falls back to scanning when `/Length` is missing or wrong.
- Classic xref table entries are now parsed by field instead of assuming an exact
  20-byte width, and a damaged/non-conforming xref (bad `startxref` offset, truncated
  file, ...) is recovered by rebuilding the table from a full-file object scan.

### Added
- Encrypted PDFs (`/Encrypt` in the trailer) are now detected and rejected with a clear
  `NotSupportedException` instead of silently copying still-encrypted content.
- `Merge(paths, outputPath, ...)` now writes to a temp file and only replaces the
  destination on success — a failed merge no longer leaves a truncated PDF on disk.
- New stream-based, options-aware API: `Merge(IEnumerable<MergeInput>, Stream, MergeOptions?)`,
  returning a `MergeReport`. `MergeOptions.OnError` can be set to `Skip` so one bad input
  doesn't abort the whole batch; each input's outcome is reported individually.
- Images can now be scaled to fit a standard page (`MergeOptions.Images = FitToPage`,
  the new default) instead of always becoming a page sized to their raw pixel dimensions.
- EXIF orientation (the common rotation cases) is read from JPEGs and applied to
  placement.
- Adobe-inverted CMYK JPEGs (APP14 marker) get a `/Decode` array so they no longer render
  as a photographic negative.
- PNG support extended to bit depths 1/2/4/16 (previously 8-bit only) and `tRNS`
  transparency (color-key masking for gray/RGB, a derived soft mask for indexed).
- `CropBox`, `UserUnit`, and transparency `/Group` are now copied from source pages;
  `/Annots` is copied too (with back-references to the source document's page/field tree
  stripped, since they'd otherwise dangle or pull in unrelated objects).
- The output now includes an `/Info` dictionary (`Producer`) and a trailer `/ID`.
- A recursion depth guard in the parser and object copier turns pathological/hostile
  nesting into a catchable exception instead of an unrecoverable
  `StackOverflowException`.
- A test project (`tests/PdfMerge.Tests`) covering the number-parsing regression, an
  incremental-update (`/Prev` chain) file built and merged end-to-end, encrypted-PDF
  detection, the atomic-write and skip-on-error behavior, and the image placement math.
  Runs in CI.

### Changed
- Default image placement changed from `NativeSize` (1 pixel = 1 point, e.g. a phone
  photo became a 56×42 inch page) to `FitToPage` (A4, centered, with a margin).

## 0.1.0

Initial release.

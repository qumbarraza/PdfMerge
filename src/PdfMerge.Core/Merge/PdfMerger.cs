using System.Globalization;
using System.Text;
using PdfMerge.Core.Images;
using PdfMerge.Core.Objects;
using PdfMerge.Core.Reader;

namespace PdfMerge.Core.Merge;

public static class PdfMerger
{
    /// <summary>Merges PDF files and images (in the given order) into a single output PDF at <paramref name="outputPath"/>.
    /// Writes to a temporary file first and only replaces the destination on success, so a failed merge never
    /// leaves a truncated/corrupt file behind.</summary>
    public static MergeReport Merge(IReadOnlyList<string> inputPaths, string outputPath, MergeOptions? options = null)
    {
        if (inputPaths.Count == 0) throw new ArgumentException("At least one input file is required.", nameof(inputPaths));

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        string tempPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            MergeReport report;
            using (var outStream = File.Create(tempPath))
            {
                report = Merge(inputPaths.Select(MergeInput.FromFile), outStream, options);
            }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tempPath, outputPath);
            return report;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }

    /// <summary>Merges the given inputs into <paramref name="output"/>. The caller owns and disposes the stream.</summary>
    public static MergeReport Merge(IEnumerable<MergeInput> inputs, Stream output, MergeOptions? options = null)
    {
        var inputList = inputs.ToList();
        if (inputList.Count == 0) throw new ArgumentException("At least one input file is required.", nameof(inputs));
        options ??= MergeOptions.Default;

        var writer = new PdfWriter(output);
        var pagesRef = writer.Allocate();
        var catalogRef = writer.Allocate();
        var pageRefs = new List<object?>();
        var results = new List<MergeInputResult>();

        foreach (var input in inputList)
        {
            try
            {
                int added = input.Kind == MergeInputKind.Pdf
                    ? AppendPdf(writer, input, pagesRef, pageRefs)
                    : AppendImage(writer, input, pagesRef, pageRefs, options);
                results.Add(new MergeInputResult(input.Name, true, added, null));
            }
            catch (Exception ex) when (options.OnError == MergeErrorPolicy.Skip)
            {
                results.Add(new MergeInputResult(input.Name, false, 0, ex.Message));
            }
        }

        var pagesDict = new PdfDict();
        pagesDict[PdfName.Type.Value] = PdfName.Pages;
        pagesDict["Kids"] = pageRefs;
        pagesDict["Count"] = pageRefs.Count;
        writer.WriteObject(pagesRef, pagesDict);

        var catalog = new PdfDict();
        catalog[PdfName.Type.Value] = new PdfName("Catalog");
        catalog["Pages"] = pagesRef;
        writer.WriteObject(catalogRef, catalog);

        var info = new PdfDict();
        info["Producer"] = "Shark.PDFMerge";
        var infoRef = writer.WriteNewObject(info);

        writer.Finish(catalogRef, infoRef);

        return new MergeReport(results, pageRefs.Count);
    }

    private static int AppendPdf(PdfWriter writer, MergeInput input, PdfRef pagesRef, List<object?> pageRefs)
    {
        byte[] bytes;
        using (var s = input.Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }

        PdfDocumentReader reader;
        try
        {
            reader = new PdfDocumentReader(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"'{input.Name}' could not be parsed as a PDF: {ex.Message}", ex);
        }

        if (reader.IsEncrypted)
            throw new NotSupportedException($"'{input.Name}' is encrypted; encrypted PDFs are not supported.");

        if (reader.Root is not PdfDict)
            throw new InvalidDataException($"'{input.Name}' has no resolvable /Root (the file may be corrupt or use an unsupported xref layout).");

        var pages = reader.GetPages();
        if (pages.Count == 0)
            throw new InvalidDataException($"'{input.Name}' has a /Root but its page tree yielded no pages.");

        var copier = new ObjectCopier(reader, writer);

        foreach (var page in pages)
        {
            object? contents = page.Contents == null
                ? writer.WriteNewObject(new PdfStreamObj())
                : copier.CopyValue(page.Contents);

            var resources = copier.CopyValue(page.Resources) as PdfDict ?? new PdfDict();

            var pageDict = new PdfDict();
            pageDict[PdfName.Type.Value] = PdfName.Page;
            pageDict["Parent"] = pagesRef;
            pageDict["MediaBox"] = copier.CopyValue(page.MediaBox);
            if (page.CropBox != null) pageDict["CropBox"] = copier.CopyValue(page.CropBox);
            if (page.Rotate != 0) pageDict["Rotate"] = page.Rotate;
            if (page.UserUnit != null) pageDict["UserUnit"] = copier.CopyValue(page.UserUnit);
            if (page.Group != null) pageDict["Group"] = copier.CopyValue(page.Group);
            pageDict["Resources"] = resources;
            pageDict["Contents"] = contents;

            var annots = CopyAnnotations(reader, copier, writer, page.Annots);
            if (annots is { Count: > 0 }) pageDict["Annots"] = annots;

            pageRefs.Add(writer.WriteNewObject(pageDict));
        }

        return pages.Count;
    }

    /// <summary>Copies each annotation dict, dropping /P and /Parent (back-references to the source document's
    /// page/field tree that would otherwise pull in - and dangle against - unrelated source-document objects).</summary>
    private static List<object?>? CopyAnnotations(PdfDocumentReader reader, ObjectCopier copier, PdfWriter writer, object? annotsValue)
    {
        if (reader.Deref(annotsValue) is not List<object?> annotsList) return null;

        var result = new List<object?>();
        foreach (var item in annotsList)
        {
            var resolved = item is PdfRef r ? reader.Resolve(r) : item;
            if (resolved is not PdfDict annotDict) continue;

            var newDict = new PdfDict();
            foreach (var (k, v) in annotDict.Entries())
            {
                if (k is "P" or "Parent") continue;
                newDict[k] = copier.CopyValue(v);
            }
            result.Add(writer.WriteNewObject(newDict));
        }
        return result;
    }

    private static int AppendImage(PdfWriter writer, MergeInput input, PdfRef pagesRef, List<object?> pageRefs, MergeOptions options)
    {
        byte[] fileBytes;
        using (var s = input.Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            fileBytes = ms.ToArray();
        }

        string ext = Path.GetExtension(input.Name);
        EmbeddedImage embedded;
        try
        {
            embedded = ImageEmbedder.Embed(writer, fileBytes, ext);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException($"'{input.Name}': {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"'{input.Name}' could not be read as an image: {ex.Message}", ex);
        }

        int orientation = options.ApplyExifOrientation ? embedded.Orientation : 1;
        var (pageWidth, pageHeight, matrix) = ImagePlacementMath.Compute(embedded.Width, embedded.Height, orientation, options);

        string content = string.Create(CultureInfo.InvariantCulture,
            $"q {matrix.A:0.###} {matrix.B:0.###} {matrix.C:0.###} {matrix.D:0.###} {matrix.E:0.###} {matrix.F:0.###} cm /Im0 Do Q");
        var contentStream = new PdfStreamObj { Data = Encoding.ASCII.GetBytes(content) };
        var contentRef = writer.WriteNewObject(contentStream);

        var xobjects = new PdfDict();
        xobjects["Im0"] = embedded.XObjectRef;
        var resources = new PdfDict();
        resources["XObject"] = xobjects;

        var pageDict = new PdfDict();
        pageDict[PdfName.Type.Value] = PdfName.Page;
        pageDict["Parent"] = pagesRef;
        pageDict["MediaBox"] = new List<object?> { 0, 0, pageWidth, pageHeight };
        pageDict["Resources"] = resources;
        pageDict["Contents"] = contentRef;

        pageRefs.Add(writer.WriteNewObject(pageDict));
        return 1;
    }
}

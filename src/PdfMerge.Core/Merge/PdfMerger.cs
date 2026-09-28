using System.Globalization;
using System.Text;
using PdfMerge.Core.Images;
using PdfMerge.Core.Objects;
using PdfMerge.Core.Reader;

namespace PdfMerge.Core.Merge;

public static class PdfMerger
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    /// <summary>Merges PDF files and images (in the given order) into a single output PDF.</summary>
    public static void Merge(IReadOnlyList<string> inputPaths, string outputPath)
    {
        if (inputPaths.Count == 0) throw new ArgumentException("At least one input file is required.", nameof(inputPaths));

        using var outStream = File.Create(outputPath);
        var writer = new PdfWriter(outStream);
        var pagesRef = writer.Allocate();
        var catalogRef = writer.Allocate();
        var pageRefs = new List<object?>();

        foreach (var path in inputPaths)
        {
            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                AppendPdf(writer, path, pagesRef, pageRefs);
            }
            else if (ImageExtensions.Contains(ext))
            {
                AppendImage(writer, path, ext, pagesRef, pageRefs);
            }
            else
            {
                throw new NotSupportedException($"Unsupported input file '{path}'. Only .pdf, .jpg/.jpeg, and .png are supported.");
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

        writer.Finish(catalogRef);
    }

    private static void AppendPdf(PdfWriter writer, string path, PdfRef pagesRef, List<object?> pageRefs)
    {
        var reader = new PdfDocumentReader(File.ReadAllBytes(path));
        var copier = new ObjectCopier(reader, writer);

        foreach (var page in reader.GetPages())
        {
            object? contents = page.Contents == null
                ? writer.WriteNewObject(new PdfStreamObj())
                : copier.CopyValue(page.Contents);

            var resources = copier.CopyValue(page.Resources) as PdfDict ?? new PdfDict();

            var pageDict = new PdfDict();
            pageDict[PdfName.Type.Value] = PdfName.Page;
            pageDict["Parent"] = pagesRef;
            pageDict["MediaBox"] = page.MediaBox;
            if (page.Rotate != 0) pageDict["Rotate"] = page.Rotate;
            pageDict["Resources"] = resources;
            pageDict["Contents"] = contents;

            pageRefs.Add(writer.WriteNewObject(pageDict));
        }
    }

    private static void AppendImage(PdfWriter writer, string path, string ext, PdfRef pagesRef, List<object?> pageRefs)
    {
        byte[] fileBytes = File.ReadAllBytes(path);
        var embedded = ImageEmbedder.Embed(writer, fileBytes, ext);

        string content = string.Create(CultureInfo.InvariantCulture,
            $"q {embedded.Width} 0 0 {embedded.Height} 0 0 cm /Im0 Do Q");
        var contentStream = new PdfStreamObj { Data = Encoding.ASCII.GetBytes(content) };
        var contentRef = writer.WriteNewObject(contentStream);

        var xobjects = new PdfDict();
        xobjects["Im0"] = embedded.XObjectRef;
        var resources = new PdfDict();
        resources["XObject"] = xobjects;

        var pageDict = new PdfDict();
        pageDict[PdfName.Type.Value] = PdfName.Page;
        pageDict["Parent"] = pagesRef;
        pageDict["MediaBox"] = new List<object?> { 0, 0, embedded.Width, embedded.Height };
        pageDict["Resources"] = resources;
        pageDict["Contents"] = contentRef;

        pageRefs.Add(writer.WriteNewObject(pageDict));
    }
}

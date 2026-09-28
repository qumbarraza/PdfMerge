using System.Text;
using PdfMerge.Core.Merge;
using PdfMerge.Core.Objects;
using Xunit;

namespace PdfMerge.Tests;

public class MergeBehaviorTests
{
    private static byte[] BuildMinimalPdf()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pagesRef = writer.Allocate();
        var catalogRef = writer.Allocate();

        var contentStream = new PdfStreamObj { Data = Encoding.ASCII.GetBytes("0 0 0 rg 0 0 10 10 re f") };
        var contentRef = writer.WriteNewObject(contentStream);
        var page = new PdfDict();
        page[PdfName.Type.Value] = PdfName.Page;
        page["Parent"] = pagesRef;
        page["MediaBox"] = new List<object?> { 0, 0, 100, 100 };
        page["Resources"] = new PdfDict();
        page["Contents"] = contentRef;
        var pageRef = writer.WriteNewObject(page);

        var pagesDict = new PdfDict();
        pagesDict[PdfName.Type.Value] = PdfName.Pages;
        pagesDict["Kids"] = new List<object?> { pageRef };
        pagesDict["Count"] = 1;
        writer.WriteObject(pagesRef, pagesDict);

        var catalog = new PdfDict();
        catalog[PdfName.Type.Value] = new PdfName("Catalog");
        catalog["Pages"] = pagesRef;
        writer.WriteObject(catalogRef, catalog);
        writer.Finish(catalogRef);
        return ms.ToArray();
    }

    private static MergeInput ValidPdfInput(string name) =>
        new(name, () => new MemoryStream(BuildMinimalPdf()), MergeInputKind.Pdf);

    [Fact]
    public void Merge_EncryptedPdf_ThrowsNotSupported()
    {
        byte[] pdf = BuildMinimalPdf();
        string text = Encoding.ASCII.GetString(pdf);
        // Splice a minimal /Encrypt marker into the trailer to simulate an encrypted file.
        int trailerIdx = text.LastIndexOf("trailer", StringComparison.Ordinal);
        string patched = text.Insert(text.IndexOf("<<", trailerIdx, StringComparison.Ordinal) + 2, " /Encrypt 99 0 R");
        byte[] patchedBytes = Encoding.ASCII.GetBytes(patched);

        var input = new MergeInput("encrypted.pdf", () => new MemoryStream(patchedBytes), MergeInputKind.Pdf);
        using var output = new MemoryStream();

        var ex = Assert.Throws<NotSupportedException>(() => PdfMerger.Merge(new[] { input }, output));
        Assert.Contains("encrypted.pdf", ex.Message);
    }

    [Fact]
    public void Merge_SkipPolicy_ContinuesPastABadInputAndReportsIt()
    {
        var badInput = new MergeInput("broken.pdf", () => new MemoryStream(Encoding.ASCII.GetBytes("not a pdf")), MergeInputKind.Pdf);
        var goodInput = ValidPdfInput("good.pdf");
        using var output = new MemoryStream();

        var report = PdfMerger.Merge(new[] { badInput, goodInput }, output, new MergeOptions { OnError = MergeErrorPolicy.Skip });

        Assert.Equal(1, report.PageCount);
        Assert.False(report.Inputs[0].Success);
        Assert.NotNull(report.Inputs[0].Error);
        Assert.True(report.Inputs[1].Success);
    }

    [Fact]
    public void Merge_ThrowPolicy_AbortsOnBadInput()
    {
        var badInput = new MergeInput("broken.pdf", () => new MemoryStream(Encoding.ASCII.GetBytes("not a pdf")), MergeInputKind.Pdf);
        using var output = new MemoryStream();

        Assert.ThrowsAny<Exception>(() => PdfMerger.Merge(new[] { badInput }, output));
    }

    [Fact]
    public void Merge_ToPath_FailureLeavesNoOutputFile()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"pdfmerge-test-{Guid.NewGuid():N}.pdf");
        string badPath = Path.Combine(Path.GetTempPath(), $"pdfmerge-test-bad-{Guid.NewGuid():N}.notreal");
        File.WriteAllText(badPath, "nope");

        try
        {
            Assert.ThrowsAny<Exception>(() => PdfMerger.Merge(new[] { badPath }, outputPath));
            Assert.False(File.Exists(outputPath));
            // No stray temp file left behind either.
            var leftovers = Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(outputPath)}.*.tmp");
            Assert.Empty(leftovers);
        }
        finally
        {
            if (File.Exists(badPath)) File.Delete(badPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void Merge_EmptyPdf_ThrowsWithFileNameInMessage()
    {
        // A syntactically valid-ish file whose /Root can't be resolved at all.
        byte[] bogus = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
        var input = new MergeInput("no-root.pdf", () => new MemoryStream(bogus), MergeInputKind.Pdf);
        using var output = new MemoryStream();

        var ex = Assert.Throws<InvalidDataException>(() => PdfMerger.Merge(new[] { input }, output));
        Assert.Contains("no-root.pdf", ex.Message);
    }
}

using System.Text;
using System.Text.RegularExpressions;
using PdfMerge.Core.Objects;
using PdfMerge.Core.Reader;
using Xunit;

namespace PdfMerge.Tests;

/// <summary>
/// Regression coverage for the C1 finding: every plain integer parsed as `double`, so
/// `/Prev`-chain following (incremental updates, linearized/signed/annotated PDFs) silently
/// read only the newest xref section and dropped everything defined earlier in the file.
/// </summary>
public class IncrementalUpdateTests
{
    private static byte[] BuildSingleRevisionPdf()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);

        var pagesRef = writer.Allocate();
        var catalogRef = writer.Allocate();

        var contentStream = new PdfStreamObj { Data = Encoding.ASCII.GetBytes("1 0 0 rg 10 10 50 50 re f") };
        var contentRef = writer.WriteNewObject(contentStream);

        var page = new PdfDict();
        page[PdfName.Type.Value] = PdfName.Page;
        page["Parent"] = pagesRef;
        page["MediaBox"] = new List<object?> { 0, 0, 200, 300 };
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

    private static int ExtractStartXRefOffset(byte[] pdfBytes)
    {
        string text = Encoding.ASCII.GetString(pdfBytes);
        var match = Regex.Match(text, @"startxref\s+(\d+)");
        Assert.True(match.Success, "Test fixture PDF must contain a startxref offset.");
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public void GetPages_FollowsPrevChain_AcrossAnIncrementalUpdate()
    {
        byte[] revision1 = BuildSingleRevisionPdf();
        int revision1XRefOffset = ExtractStartXRefOffset(revision1);

        // Simulate an incremental update (e.g. Acrobat annotate/sign/save): append a second,
        // trivial xref section whose trailer chains back via /Prev to the first revision's xref.
        // Objects 1-4 (Pages/Catalog/Contents/Page - see BuildSingleRevisionPdf's allocation
        // order) are defined ONLY in revision 1; object 2 is the Catalog.
        string revision2 = $"\nxref\n0 1\r\n0000000000 65535 f \r\n" +
                            $"trailer\n<< /Size 5 /Root 2 0 R /Prev {revision1XRefOffset} >>\n";
        int revision2XRefOffset = revision1.Length + 1; // +1 for the leading '\n'
        revision2 += $"startxref\n{revision2XRefOffset}\n%%EOF";

        byte[] fullFile = revision1.Concat(Encoding.ASCII.GetBytes(revision2)).ToArray();

        var reader = new PdfDocumentReader(fullFile);

        Assert.NotNull(reader.Root); // /Root only resolves by following /Prev into revision 1.
        var pages = reader.GetPages();
        Assert.Single(pages);
        Assert.Equal(new List<object?> { 0, 0, 200, 300 }, pages[0].MediaBox);
    }

    [Fact]
    public void GetPages_SingleRevisionFile_StillWorks()
    {
        var reader = new PdfDocumentReader(BuildSingleRevisionPdf());
        var pages = reader.GetPages();
        Assert.Single(pages);
    }
}

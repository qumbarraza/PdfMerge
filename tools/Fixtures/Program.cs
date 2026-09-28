using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using PdfMerge.Core.Objects;

if (args.Length > 0 && args[0] == "verify")
{
    Verify.Run(args[1]);
    return;
}

string dir = args.Length > 0 ? args[0] : "sample";
Directory.CreateDirectory(dir);

// --- A two-page PDF built with our own writer, to round-trip through our own reader. ---
using (var fs = File.Create(Path.Combine(dir, "test.pdf")))
{
    var writer = new PdfWriter(fs);
    var pagesRef = writer.Allocate();
    var catalogRef = writer.Allocate();

    var page1Content = new PdfStreamObj { Data = Encoding.ASCII.GetBytes("1 0 0 rg 50 50 200 150 re f") };
    var content1Ref = writer.WriteNewObject(page1Content);
    var page1 = new PdfDict();
    page1[PdfName.Type.Value] = PdfName.Page;
    page1["Parent"] = pagesRef;
    page1["MediaBox"] = new List<object?> { 0, 0, 612, 792 };
    page1["Resources"] = new PdfDict();
    page1["Contents"] = content1Ref;
    var page1Ref = writer.WriteNewObject(page1);

    var page2Content = new PdfStreamObj { Data = Encoding.ASCII.GetBytes("0 0 1 rg 100 100 300 100 re f") };
    var content2Ref = writer.WriteNewObject(page2Content);
    var page2 = new PdfDict();
    page2[PdfName.Type.Value] = PdfName.Page;
    page2["Parent"] = pagesRef;
    page2["MediaBox"] = new List<object?> { 0, 0, 612, 792 };
    page2["Resources"] = new PdfDict();
    page2["Contents"] = content2Ref;
    var page2Ref = writer.WriteNewObject(page2);

    var pagesDict = new PdfDict();
    pagesDict[PdfName.Type.Value] = PdfName.Pages;
    pagesDict["Kids"] = new List<object?> { page1Ref, page2Ref };
    pagesDict["Count"] = 2;
    writer.WriteObject(pagesRef, pagesDict);

    var catalog = new PdfDict();
    catalog[PdfName.Type.Value] = new PdfName("Catalog");
    catalog["Pages"] = pagesRef;
    writer.WriteObject(catalogRef, catalog);

    writer.Finish(catalogRef);
}

// --- Sample raster images. ---
using (var bmp = new Bitmap(300, 200))
{
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.FromArgb(255, 20, 140, 220));
    g.FillEllipse(Brushes.Yellow, 60, 40, 180, 120);
    bmp.Save(Path.Combine(dir, "photo.jpg"), ImageFormat.Jpeg);
}

using (var bmp = new Bitmap(300, 200, PixelFormat.Format24bppRgb))
{
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.FromArgb(30, 180, 90));
    g.FillRectangle(Brushes.White, 20, 20, 100, 100);
    bmp.Save(Path.Combine(dir, "solid.png"), ImageFormat.Png);
}

using (var bmp = new Bitmap(300, 200, PixelFormat.Format32bppArgb))
{
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Transparent);
    using var brush = new SolidBrush(Color.FromArgb(160, 220, 30, 30));
    g.FillEllipse(brush, 30, 30, 240, 140);
    bmp.Save(Path.Combine(dir, "alpha.png"), ImageFormat.Png);
}

Console.WriteLine($"Fixtures written to {Path.GetFullPath(dir)}");

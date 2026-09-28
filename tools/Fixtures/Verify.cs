using PdfMerge.Core.Reader;

static class Verify
{
    public static void Run(string path)
    {
        var reader = new PdfDocumentReader(File.ReadAllBytes(path));
        var pages = reader.GetPages();
        Console.WriteLine($"{path}: {pages.Count} page(s)");
        foreach (var (p, i) in pages.Select((p, i) => (p, i)))
        {
            var mb = string.Join(",", p.MediaBox.Select(x => x?.ToString()));
            Console.WriteLine($"  page {i}: MediaBox=[{mb}] Rotate={p.Rotate} HasContents={p.Contents != null}");
        }
    }
}

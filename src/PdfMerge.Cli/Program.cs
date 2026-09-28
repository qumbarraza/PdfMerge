using PdfMerge.Core.Merge;

if (args.Length < 2)
{
    Console.WriteLine("Usage: pdfmerge <output.pdf> <input1.pdf|jpg|png> [input2 ...]");
    return 1;
}

string output = args[0];
var inputs = args.Skip(1).ToList();

foreach (var input in inputs)
{
    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }
}

try
{
    PdfMerger.Merge(inputs, output);
    Console.WriteLine($"Wrote {output} ({inputs.Count} input file(s) merged).");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Merge failed: {ex.Message}");
    return 1;
}

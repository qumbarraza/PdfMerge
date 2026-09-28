namespace PdfMerge.Core.Merge;

public sealed record MergeInputResult(string Name, bool Success, int PagesAdded, string? Error);

public sealed record MergeReport(IReadOnlyList<MergeInputResult> Inputs, int PageCount);

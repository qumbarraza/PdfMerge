namespace PdfMerge.Core.Reader;

public enum XRefEntryKind { Direct, InObjectStream }

public readonly struct XRefEntry
{
    public XRefEntryKind Kind { get; init; }
    public long Offset { get; init; }          // Direct
    public int ObjStmNum { get; init; }         // InObjectStream
    public int IndexInObjStm { get; init; }     // InObjectStream
}

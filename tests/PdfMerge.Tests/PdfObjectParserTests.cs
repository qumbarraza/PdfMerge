using System.Text;
using PdfMerge.Core.Objects;
using Xunit;

namespace PdfMerge.Tests;

public class PdfObjectParserTests
{
    [Fact]
    public void ParseValue_IntegerLiteral_ReturnsBoxedInt_NotDouble()
    {
        // Regression for a ternary-unification bug: `isInt ? (int)first : first` boxed
        // every integer as a double, so every `is int` check downstream silently never matched.
        byte[] buf = Encoding.ASCII.GetBytes("116");
        int pos = 0;
        object? value = PdfObjectParser.ParseValue(buf, ref pos);

        Assert.IsType<int>(value);
        Assert.Equal(116, value);
    }

    [Fact]
    public void ParseValue_RealNumberLiteral_ReturnsDouble()
    {
        byte[] buf = Encoding.ASCII.GetBytes("3.14");
        int pos = 0;
        object? value = PdfObjectParser.ParseValue(buf, ref pos);

        Assert.IsType<double>(value);
        Assert.Equal(3.14, (double)value!, 3);
    }

    [Fact]
    public void ParseValue_IndirectReference_ReturnsPdfRef()
    {
        byte[] buf = Encoding.ASCII.GetBytes("17 0 R");
        int pos = 0;
        object? value = PdfObjectParser.ParseValue(buf, ref pos);

        var r = Assert.IsType<PdfRef>(value);
        Assert.Equal(17, r.Num);
        Assert.Equal(0, r.Gen);
    }

    [Fact]
    public void ParseValue_DeeplyNestedArray_ThrowsInsteadOfCrashing()
    {
        // Guards against a StackOverflowException (uncatchable, kills the process) on hostile input.
        var sb = new StringBuilder();
        for (int i = 0; i < 5000; i++) sb.Append('[');
        byte[] buf = Encoding.ASCII.GetBytes(sb.ToString());
        int pos = 0;

        Assert.Throws<InvalidDataException>(() => PdfObjectParser.ParseValue(buf, ref pos));
    }
}

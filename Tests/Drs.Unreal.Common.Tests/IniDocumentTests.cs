using Drs.Unreal.Common.Ini;

namespace Drs.Unreal.Common.Tests;

public class IniDocumentTests
{
    // Raw string literals take the source file's line endings, which differ between checkouts
    // (Git converts to CRLF on Windows runners), so every comparison normalises to LF via Lf().
    private static string Lf(string s) => s.Replace("\r\n", "\n");

    private static readonly string Sample = Lf("""
        ; Header comment
        [First]
        A=1
        ; inline comment
        B = two words

        ; Describes Second
        [Second]
        C=3
        Unknown=kept
        """);

    [Fact]
    public void RoundTripsUnchanged()
    {
        var text = Sample.Replace("\n", "\r\n") + "\r\n";   // Sample is LF; this makes a CRLF file
        Assert.Equal(text, IniDocument.Parse(text).ToString());
    }

    [Fact]
    public void Reads_CaseInsensitive_Trimmed()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.Equal("two words", doc.Get("first", "b"));
        Assert.Null(doc.Get("First", "Missing"));
        Assert.Equal(["First", "Second"], doc.SectionNames);
        Assert.Equal(["C", "Unknown"], doc.GetAll("Second").Select(kv => kv.Key));
    }

    [Fact]
    public void Set_UpdatesInPlace_AddsAfterLastKey_AddsSectionAtEnd()
    {
        var doc = IniDocument.Parse(Sample);
        doc.Set("First", "A", "one");
        doc.Set("First", "New", "x");
        doc.Set("Third", "D", "4");

        Assert.Equal(Lf("""
            ; Header comment
            [First]
            A=one
            ; inline comment
            B = two words
            New=x

            ; Describes Second
            [Second]
            C=3
            Unknown=kept

            [Third]
            D=4

            """), Lf(doc.ToString()));
    }

    [Fact]
    public void RemoveSection_TakesItsCommentWithIt_AndTidiesBlankLines()
    {
        var doc = IniDocument.Parse(Sample + "\n\n[Third]\nD=4\n");
        Assert.True(doc.RemoveSection("Second"));
        Assert.Equal(Lf("""
            ; Header comment
            [First]
            A=1
            ; inline comment
            B = two words

            [Third]
            D=4

            """), Lf(doc.ToString()));
        Assert.False(doc.RemoveSection("Second"));
    }

    [Fact]
    public void RemoveLastSection_LeavesNoTrailingBlankLines()
    {
        var doc = IniDocument.Parse(Sample);
        doc.RemoveSection("Second");
        Assert.EndsWith("B = two words\n", Lf(doc.ToString()));
    }

    [Fact]
    public void RejectsInvalidNames()
    {
        var doc = IniDocument.Empty();
        Assert.Throws<ArgumentException>(() => doc.Set("A]B", "k", "v"));
        Assert.Throws<ArgumentException>(() => doc.Set("S", "k=x", "v"));
        Assert.Throws<ArgumentException>(() => doc.Set("S", "k", "two\nlines"));
    }

    [Fact]
    public void SaveAndLoad_PreservesBom()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, @"Sub\File.ini");
        File.WriteAllText(Path.Combine(tmp.File(@"Sub\File.ini")), "[S]\r\nK=V\r\n", new System.Text.UTF8Encoding(true));

        var doc = IniDocument.Load(path);
        Assert.Equal("V", doc.Get("S", "K"));
        doc.Set("S", "K", "W");
        doc.Save(path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("W", IniDocument.Load(path).Get("S", "K"));
        Assert.False(File.Exists(path + ".tmp"));
    }
}

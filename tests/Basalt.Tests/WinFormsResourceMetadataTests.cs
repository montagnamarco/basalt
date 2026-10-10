using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The &lt;metadata&gt; elements of a neutral .resx: the "$this.Localizable" flag and the designer metadata Visual
/// Studio keeps beside the values. Metadata is read apart from the values, and writing it changes no other byte.
/// </summary>
/// <remarks>
/// The flag's element and type are the owner's knowledge of Visual Studio output. They were not checked against a
/// file Visual Studio wrote for this change, so these tests prove the writer's layout, not Visual Studio's.
/// Each defect below was reintroduced once and its test failed:
/// <list type="bullet">
/// <item>SetMetadata wrote the flag ahead of the closing root instead of the first resource element:
/// SetMetadataAddsTheFlagAheadOfTheFirstResourceAndChangesNothingElse failed (strings differ at position 178).</item>
/// <item>RemoveMetadata left the blank line: RemoveMetadataDropsTheFlagsLineAndLeavesEveryOtherByte failed (strings differ at position 156).</item>
/// <item>Metadata was also counted as values: MetadataIsReadApartFromTheValues failed (IndexOutOfRangeException).</item>
/// <item>An existing flag was appended again instead of rewritten: SetMetadataRewritesAnExistingElementInPlaceAndIgnoresAnEqualValue failed.</item>
/// <item>A document with no resource element had no insertion point: AMetadataElementWithoutAResourceToPrecedeGoesAtTheClosingRoot failed (InvalidOperationException).</item>
/// </list>
/// </remarks>
public sealed class WinFormsResourceMetadataTests
{
    private const string FlagType = "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    /// <summary>
    /// The fixture as LF text. A raw literal keeps the checkout's line endings, so a CRLF checkout would otherwise
    /// compare CRLF text against the LF expectations below; the document's own newline is tested separately.
    /// </summary>
    private static readonly string Source = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <metadata name="$this.TrayLargeIcon" type="System.Drawing.Point, System.Drawing">
            <value>17, 17</value>
          </metadata>
          <data name="$this.Text" xml:space="preserve">
            <value>Main form</value>
          </data>
          <metadata name="ToolTip1.TrayLocation" type="System.Drawing.Point, System.Drawing">
            <value>100, 50</value>
          </metadata>
        </root>
        """.ReplaceLineEndings("\n");

    private const string FlagElement = "  <metadata name=\"$this.Localizable\" type=\"System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089\"><value>True</value></metadata>\n";

    [Fact]
    public void MetadataIsReadApartFromTheValues()
    {
        var document = new WinFormsResourceDocument(Source);

        Assert.Equal(["$this.Text"], document.Entries.Select(entry => entry.Name));
        Assert.Equal(["$this.TrayLargeIcon", "ToolTip1.TrayLocation"], document.Metadata.Select(entry => entry.Name));
        Assert.Equal("17, 17", document.TryGetMetadata("$this.TrayLargeIcon")!.Value);
        Assert.Null(document.TryGet("$this.TrayLargeIcon"));
        Assert.Null(document.TryGetMetadata("$this.Localizable"));
    }

    [Fact]
    public void SetMetadataAddsTheFlagAheadOfTheFirstResourceAndChangesNothingElse()
    {
        var expected = Source.Replace("  <metadata name=\"$this.TrayLargeIcon\"",
            FlagElement + "  <metadata name=\"$this.TrayLargeIcon\"", StringComparison.Ordinal);

        var written = new WinFormsResourceDocument(Source).SetMetadata("$this.Localizable", "True", FlagType);

        Assert.Equal(expected, written);
        var reopened = new WinFormsResourceDocument(written);
        Assert.Equal("True", reopened.TryGetMetadata("$this.Localizable")!.Value);
        Assert.Equal(FlagType, reopened.TryGetMetadata("$this.Localizable")!.TypeName);
        Assert.Equal(["$this.Text"], reopened.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void SetMetadataRewritesAnExistingElementInPlaceAndIgnoresAnEqualValue()
    {
        var flagged = new WinFormsResourceDocument(Source).SetMetadata("$this.Localizable", "True", FlagType);

        Assert.Equal(flagged, new WinFormsResourceDocument(flagged).SetMetadata("$this.Localizable", "True", FlagType));

        var changed = new WinFormsResourceDocument(flagged).SetMetadata("$this.Localizable", "False", FlagType);
        Assert.Equal(flagged.Replace("<value>True</value></metadata>", "<value>False</value></metadata>",
            StringComparison.Ordinal), changed);
    }

    [Fact]
    public void RemoveMetadataDropsTheFlagsLineAndLeavesEveryOtherByte()
    {
        var flagged = new WinFormsResourceDocument(Source).SetMetadata("$this.Localizable", "True", FlagType);

        var removed = new WinFormsResourceDocument(flagged).RemoveMetadata("$this.Localizable");

        Assert.Equal(Source, removed);
    }

    /// <remarks>
    /// Defect reintroduced once (Markup wrote the line feeds the XML reader left behind): the rewritten data element of a
    /// CRLF file held bare LF, and the assertion on the bare line feeds failed.
    /// </remarks>
    [Fact]
    public void ARewrittenElementInACrlfResxKeepsCrlfLineBreaks()
    {
        var crlf = Source.ReplaceLineEndings("\r\n");

        var written = new WinFormsResourceDocument(crlf).SetValue("$this.Text", "Main form 2", null, null);

        Assert.DoesNotContain("\n", written.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal("Main form 2", new WinFormsResourceDocument(written).TryGet("$this.Text")!.Value);
    }

    [Fact]
    public void AMetadataElementWithoutAResourceToPrecedeGoesAtTheClosingRoot()
    {
        var bare = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="version">2.0</resheader>
            </root>
            """.ReplaceLineEndings("\n");

        var written = new WinFormsResourceDocument(bare).SetMetadata("$this.Localizable", "True", FlagType);

        Assert.Equal(bare.Replace("</root>", FlagElement + "</root>", StringComparison.Ordinal), written);
        Assert.Equal("True", new WinFormsResourceDocument(written).TryGetMetadata("$this.Localizable")!.Value);
    }
}

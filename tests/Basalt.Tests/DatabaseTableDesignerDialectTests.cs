using Basalt.Data.Sessions;

namespace Basalt.Tests;

/// <summary>
/// The CREATE and ALTER text each engine gets from a table design. SQL Server, PostgreSQL and MySQL need a server,
/// so these check the script itself: the quoting, the statements for each kind of change, and the transaction around them.
/// </summary>
public sealed class DatabaseTableDesignerDialectTests
{
    private static DesignColumn Col(string name, string type, bool nullable = true, bool key = false, string? fallback = null, bool identity = false, string? was = null) =>
        new(name, type, nullable, key, fallback, identity, was);

    private static TableDesign Design(DbObjectName name, params DesignColumn[] columns) =>
        TableDesign.Empty(name) with { Columns = columns };

    /// <summary>A stored table: every column is tied to itself, and the key has the name the engine would have given it.</summary>
    private static TableDesign Stored(DbObjectName name, string keyName, params DesignColumn[] columns)
    {
        var tied = columns.Select(c => c with { OriginalName = c.Name }).ToArray();
        var key = tied.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        return Design(name, tied) with { Indexes = key.Count == 0 ? [] : [new IndexInfo(keyName, key, true, true)] };
    }

    private static readonly DesignColumn[] Orders =
    [
        Col("Id", "int", nullable: false, key: true, identity: true),
        Col("Total", "decimal(10,2)", nullable: false, fallback: "0"),
        Col("Note", "nvarchar(50)"),
    ];

    // ------------------------------------------------------------------
    // SQL Server
    // ------------------------------------------------------------------

    [Fact]
    public void SqlServerCreateNamesTheKeyIdentityAndDefault()
    {
        var script = TableScriptGenerator.For(DatabaseKind.SqlServer).Create(Design(new DbObjectName("dbo", "Orders"), Orders));

        Assert.Equal(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            CREATE TABLE [dbo].[Orders] (
                [Id] int IDENTITY(1,1) NOT NULL,
                [Total] decimal(10,2) NOT NULL DEFAULT 0,
                [Note] nvarchar(50) NULL,
                CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
            );
            COMMIT TRANSACTION;
            SET XACT_ABORT OFF;
            """.ReplaceLineEndings("\n"),
            script.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SqlServerAlterRenamesRetypesAddsAndDropsFromTheDifferenceOnly()
    {
        var name = new DbObjectName("dbo", "Orders");
        var stored = Stored(name, "PK_Orders", Orders);

        var edited = stored with
        {
            Columns =
            [
                stored.Columns[0],
                stored.Columns[1] with { DataType = "decimal(12,2)", DefaultValue = "1" },
                stored.Columns[2] with { Name = "Remark" },
                Col("Paid", "bit", nullable: false, fallback: "0"),
            ],
        };

        var text = TableScriptGenerator.For(DatabaseKind.SqlServer).Alter(stored, edited).Text;

        Assert.Contains("EXEC sp_rename N'[dbo].[Orders].[Note]', N'Remark', N'COLUMN';", text);
        Assert.Contains("ALTER TABLE [dbo].[Orders] ALTER COLUMN [Total] decimal(12,2) NOT NULL;", text);
        Assert.Contains("ADD CONSTRAINT [DF_Orders_Total] DEFAULT 1 FOR [Total];", text);
        Assert.Contains("ALTER TABLE [dbo].[Orders] ADD [Paid] bit NOT NULL DEFAULT 0;", text);

        // The untouched column and the key produce nothing.
        Assert.DoesNotContain("[Id]", text);
        Assert.StartsWith("SET XACT_ABORT ON;\nBEGIN TRANSACTION;", text.ReplaceLineEndings("\n"));
        Assert.EndsWith("COMMIT TRANSACTION;\nSET XACT_ABORT OFF;", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SqlServerAlterDropsAColumnAfterItsDefaultAndChangesTheKey()
    {
        var name = new DbObjectName("dbo", "Orders");
        var stored = Stored(name, "PK_Orders", Orders);

        var edited = stored with
        {
            Columns =
            [
                stored.Columns[0] with { IsPrimaryKey = false, IsIdentity = false },
                stored.Columns[1] with { IsPrimaryKey = true, IsNullable = false },
            ],
        };

        var script = TableScriptGenerator.For(DatabaseKind.SqlServer).Alter(stored, edited);
        var text = script.Text;

        var dropKey = text.IndexOf("DROP CONSTRAINT [PK_Orders]", StringComparison.Ordinal);
        var dropColumn = text.IndexOf("DROP COLUMN [Note]", StringComparison.Ordinal);
        var addKey = text.IndexOf("ADD CONSTRAINT [PK_Orders] PRIMARY KEY ([Total])", StringComparison.Ordinal);

        Assert.True(dropKey >= 0 && dropColumn > dropKey && addKey > dropColumn, text);
        Assert.Contains("sys.default_constraints", text);

        // An identity cannot be switched off on an existing column: the script says so.
        Assert.Single(script.Warnings);
    }

    [Fact]
    public void SqlServerBracketsInANameAreDoubled()
    {
        var script = TableScriptGenerator.For(DatabaseKind.SqlServer).Create(
            Design(new DbObjectName("dbo", "a]b"), Col("c]d", "int", key: true, nullable: false)));

        Assert.Contains("[dbo].[a]]b]", script.Text);
        Assert.Contains("[c]]d] int NOT NULL", script.Text);
    }

    // ------------------------------------------------------------------
    // PostgreSQL
    // ------------------------------------------------------------------

    [Fact]
    public void PostgreSqlCreateUsesAnIdentityColumnAndANamedKey()
    {
        var script = TableScriptGenerator.For(DatabaseKind.PostgreSql).Create(Design(
            new DbObjectName("public", "Orders"),
            Col("Id", "integer", nullable: false, key: true, identity: true),
            Col("Total", "numeric(10,2)", nullable: false, fallback: "0")));

        Assert.Equal(
            """
            BEGIN;
            CREATE TABLE "public"."Orders" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                "Total" numeric(10,2) NOT NULL DEFAULT 0,
                CONSTRAINT "Orders_pkey" PRIMARY KEY ("Id")
            );
            COMMIT;
            """.ReplaceLineEndings("\n"),
            script.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void PostgreSqlAlterWritesOneStatementPerChange()
    {
        var name = new DbObjectName("public", "Orders");
        var stored = Stored(name, "Orders_pkey",
            Col("Id", "integer", nullable: false, key: true, identity: true),
            Col("Total", "numeric(10,2)", nullable: false, fallback: "0"),
            Col("Note", "text"));

        var edited = stored with
        {
            Columns =
            [
                stored.Columns[0],
                stored.Columns[1] with { Name = "Amount", DataType = "bigint", IsNullable = true, DefaultValue = null },
                Col("Tag", "varchar(10)"),
            ],
        };

        var text = TableScriptGenerator.For(DatabaseKind.PostgreSql).Alter(stored, edited).Text;

        Assert.Contains("ALTER TABLE \"public\".\"Orders\" DROP COLUMN \"Note\";", text);
        Assert.Contains("RENAME COLUMN \"Total\" TO \"Amount\";", text);
        Assert.Contains("ALTER COLUMN \"Amount\" TYPE bigint USING \"Amount\"::bigint;", text);
        Assert.Contains("ALTER COLUMN \"Amount\" DROP NOT NULL;", text);
        Assert.Contains("ALTER COLUMN \"Amount\" DROP DEFAULT;", text);
        Assert.Contains("ADD COLUMN \"Tag\" varchar(10);", text);
        Assert.DoesNotContain("\"Id\"", text);
        Assert.StartsWith("BEGIN;", text);
    }

    [Fact]
    public void PostgreSqlQuotesNamesWithTheQuoteCharacter()
    {
        var script = TableScriptGenerator.For(DatabaseKind.PostgreSql).Create(
            Design(new DbObjectName("s\"x", "t\"y"), Col("c\"d", "text")));

        Assert.Contains("\"s\"\"x\".\"t\"\"y\"", script.Text);
        Assert.Contains("\"c\"\"d\" text", script.Text);
    }

    // ------------------------------------------------------------------
    // MySQL
    // ------------------------------------------------------------------

    [Fact]
    public void MySqlCreateUsesAutoIncrementAndHasNoTransaction()
    {
        var script = TableScriptGenerator.For(DatabaseKind.MySql).Create(Design(
            new DbObjectName(null, "Orders"),
            Col("Id", "int", nullable: false, key: true, identity: true),
            Col("Total", "decimal(10,2)", nullable: false, fallback: "0")));

        Assert.Null(script.Transaction);
        Assert.Equal(
            """
            CREATE TABLE `Orders` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Total` decimal(10,2) NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`)
            );
            """.ReplaceLineEndings("\n"),
            script.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MySqlAlterRenamesWithChangeAndRestatesWithModify()
    {
        var stored = Stored(new DbObjectName(null, "Orders"), "PRIMARY",
            Col("Id", "int", nullable: false, key: true, identity: true),
            Col("Total", "decimal(10,2)", nullable: false),
            Col("Note", "varchar(50)"));

        var edited = stored with
        {
            Columns =
            [
                stored.Columns[0],
                stored.Columns[1] with { DataType = "decimal(12,2)" },
                stored.Columns[2] with { Name = "Remark" },
            ],
        };

        var text = TableScriptGenerator.For(DatabaseKind.MySql).Alter(stored, edited).Text;

        Assert.Contains("MODIFY COLUMN `Total` decimal(12,2) NOT NULL;", text);
        Assert.Contains("CHANGE COLUMN `Note` `Remark` varchar(50) NULL;", text);
        Assert.DoesNotContain("BEGIN", text);
        Assert.DoesNotContain("`Id`", text);
    }

    [Fact]
    public void MySqlReplacesThePrimaryKeyInOneStatement()
    {
        var stored = Stored(new DbObjectName(null, "T"), "PRIMARY",
            Col("A", "int", nullable: false, key: true),
            Col("B", "int", nullable: false));

        var edited = stored with { Columns = [stored.Columns[0] with { IsPrimaryKey = false }, stored.Columns[1] with { IsPrimaryKey = true }] };
        var text = TableScriptGenerator.For(DatabaseKind.MySql).Alter(stored, edited).Text;

        Assert.Contains("ALTER TABLE `T` DROP PRIMARY KEY, ADD PRIMARY KEY (`B`);", text);
    }

    [Fact]
    public void MySqlQuotesNamesWithTheBacktick()
    {
        var script = TableScriptGenerator.For(DatabaseKind.MySql).Create(
            Design(new DbObjectName(null, "a`b"), Col("c`d", "int")));

        Assert.Contains("`a``b`", script.Text);
        Assert.Contains("`c``d` int NULL", script.Text);
    }

    // ------------------------------------------------------------------
    // Every engine
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(DatabaseKind.Sqlite)]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    [InlineData(DatabaseKind.MySql)]
    public void AnUntouchedDesignHasNothingToScriptAndEveryEngineOffersTypes(DatabaseKind kind)
    {
        var generator = TableScriptGenerator.For(kind);
        var stored = Stored(new DbObjectName(kind == DatabaseKind.Sqlite || kind == DatabaseKind.MySql ? null : "dbo", "T"), "PK",
            Col("A", "int", nullable: false, key: true), Col("B", "text"));

        Assert.True(generator.Alter(stored, stored).IsEmpty);
        Assert.Equal(kind, generator.Kind);
        Assert.Contains(generator.CommonTypes, t => t.StartsWith("int", StringComparison.OrdinalIgnoreCase));
        Assert.True(generator.CommonTypes.Count >= 8);
    }

    [Fact]
    public void ASqlServerDefaultReportedInParenthesesIsNotAChange()
    {
        var name = new DbObjectName("dbo", "T");
        var stored = Stored(name, "PK_T", Col("A", "int", nullable: false, key: true), Col("B", "int", fallback: "((0))"));
        var edited = stored with { Columns = [stored.Columns[0], stored.Columns[1] with { DefaultValue = "0" }] };

        Assert.True(TableScriptGenerator.For(DatabaseKind.SqlServer).Alter(stored, edited).IsEmpty);
    }

    [Fact]
    public void SqlServerRetypingAColumnWithADefaultDropsTheDefaultFirstAndPutsItBackAfter()
    {
        var name = new DbObjectName("dbo", "Orders");
        var stored = Stored(name, "PK_Orders", Orders);

        // Total has DEFAULT 0 and is retyped; its default is untouched.
        var edited = stored with { Columns = [stored.Columns[0], stored.Columns[1] with { DataType = "decimal(12,2)" }, stored.Columns[2]] };
        var text = TableScriptGenerator.For(DatabaseKind.SqlServer).Alter(stored, edited).Text;

        var drop = text.IndexOf("sys.default_constraints", StringComparison.Ordinal);
        var alter = text.IndexOf("ALTER COLUMN [Total] decimal(12,2) NOT NULL;", StringComparison.Ordinal);
        var restore = text.IndexOf("ADD CONSTRAINT [DF_Orders_Total] DEFAULT 0 FOR [Total];", StringComparison.Ordinal);

        Assert.True(drop >= 0 && alter > drop && restore > alter, text);

        // A column without a default needs none of that.
        var plain = stored with { Columns = [stored.Columns[0], stored.Columns[1], stored.Columns[2] with { DataType = "nvarchar(80)" }] };
        Assert.DoesNotContain("sys.default_constraints", TableScriptGenerator.For(DatabaseKind.SqlServer).Alter(stored, plain).Text);
    }

    [Fact]
    public void MySqlModifyWritesBackWhatTheCatalogueGaveAndNotAcrossAChangeOfType()
    {
        var extras = new ColumnExtras("utf8mb4", "utf8mb4_bin", "it's a note", "CURRENT_TIMESTAMP");
        var stored = Stored(new DbObjectName(null, "T"), "PRIMARY",
            Col("A", "int", nullable: false, key: true),
            Col("B", "varchar(50)") with { Extras = extras });

        var widened = stored with { Columns = [stored.Columns[0], stored.Columns[1] with { Name = "C" }] };
        var script = TableScriptGenerator.For(DatabaseKind.MySql).Alter(stored, widened);

        Assert.Contains("CHANGE COLUMN `B` `C` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL ON UPDATE CURRENT_TIMESTAMP COMMENT 'it''s a note';", script.Text);
        Assert.Empty(script.Warnings);

        // A new type is not text with that collation any more.
        var retyped = stored with { Columns = [stored.Columns[0], stored.Columns[1] with { DataType = "int" }] };
        var retypedText = TableScriptGenerator.For(DatabaseKind.MySql).Alter(stored, retyped).Text;

        Assert.Contains("MODIFY COLUMN `B` int NULL;", retypedText);
    }

    [Fact]
    public void MySqlModifyOfAColumnWhoseExtrasWereNotReadSaysSoInTheScript()
    {
        var stored = Stored(new DbObjectName(null, "T"), "PRIMARY", Col("A", "int", nullable: false, key: true), Col("B", "varchar(50)"));
        var edited = stored with { Columns = [stored.Columns[0], stored.Columns[1] with { DataType = "varchar(80)" }] };

        var script = TableScriptGenerator.For(DatabaseKind.MySql).Alter(stored, edited);

        Assert.StartsWith("-- ", script.Text);
        Assert.Single(script.Warnings);
    }
}

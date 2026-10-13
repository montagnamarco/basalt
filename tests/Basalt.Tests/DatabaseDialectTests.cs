using System.Data.Common;
using Basalt.Data.Sessions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using NpgsqlTypes;

namespace Basalt.Tests;

/// <summary>
/// The SQL each engine's dialect writes, the parameters its sessions make, and the
/// display names its providers give. None of it needs a server: the sessions are
/// built on connections that are never opened.
/// </summary>
public sealed class DatabaseDialectTests
{
    private static readonly ColumnInfo Key = new("Id", "int", false, true, true, null, 1);
    private static readonly ColumnInfo Total = new("Total", "decimal(10,2)", true, false, false, null, 2);

    private static TableDetails Orders(DbObjectName name, bool keyed = true) => new(
        name,
        DbObjectKind.Table,
        keyed ? [Key, Total] : [Total],
        [],
        []);

    // ---- Quoting ----

    [Fact]
    public void SqlServerBracketsNamesAndDoublesTheCloser()
    {
        var dialect = new SqlServerDialect();

        Assert.Equal("[we\"ird ]]name]", dialect.Quote("we\"ird ]name"));
        Assert.Equal("[Order Lines]", dialect.Quote("Order Lines"));
        Assert.Equal("[dbo].[Order Lines]", dialect.QualifiedName(new DbObjectName("dbo", "Order Lines")));
        Assert.Equal("[Order Lines]", dialect.QualifiedName(new DbObjectName(null, "Order Lines")));
    }

    [Fact]
    public void PostgreSqlDoublesTheDoubleQuote()
    {
        var dialect = new PostgreSqlDialect();

        Assert.Equal("\"we\"\"ird name\"", dialect.Quote("we\"ird name"));
        Assert.Equal("\"it's ]\"", dialect.Quote("it's ]"));
        Assert.Equal("\"public\".\"Order Lines\"", dialect.QualifiedName(new DbObjectName("public", "Order Lines")));
    }

    [Fact]
    public void MySqlDoublesTheBacktickAndLeavesQuotesAlone()
    {
        var dialect = new MySqlDialect();

        Assert.Equal("`we``ird name`", dialect.Quote("we`ird name"));
        Assert.Equal("`a\"b'c`", dialect.Quote("a\"b'c"));
        Assert.Equal("`Order Lines`", dialect.QualifiedName(new DbObjectName(null, "Order Lines")));
    }

    // ---- SelectPage and SelectTop ----

    [Fact]
    public void SqlServerPagesWithOffsetAndFetchOrderedByTheSortKeyOrFirstColumn()
    {
        var dialect = new SqlServerDialect();
        var name = new DbObjectName("dbo", "Orders");

        Assert.Equal(
            "SELECT * FROM [dbo].[Orders] WHERE (Total > 10\n) ORDER BY [Total] DESC OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY",
            dialect.SelectPage(Orders(name), 50, 25, [new SortSpec("Total", true)], "Total > 10"));

        // With no sort the key orders the page; with no filter there is no WHERE.
        Assert.Equal(
            "SELECT * FROM [dbo].[Orders] ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY",
            dialect.SelectPage(Orders(name), 0, 25, [], null));

        // A table with no key orders by its first column, and OFFSET/FETCH always has an ORDER BY.
        Assert.Equal(
            "SELECT * FROM [dbo].[Orders] ORDER BY [Total] OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
            dialect.SelectPage(Orders(name, keyed: false), 0, 5, [], null));

        Assert.Equal(
            "SELECT * FROM [dbo].[Orders] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
            dialect.SelectPage(new TableDetails(name, DbObjectKind.View, [], [], []), 0, 5, [], null));
    }

    [Fact]
    public void SqlServerStartsTheSelectWithTop()
    {
        Assert.Equal("SELECT TOP (5) * FROM [dbo].[Orders]", new SqlServerDialect().SelectTop(new DbObjectName("dbo", "Orders"), 5));
    }

    [Fact]
    public void PostgreSqlPagesWithLimitAndOffset()
    {
        var dialect = new PostgreSqlDialect();
        var name = new DbObjectName("public", "Orders");

        Assert.Equal(
            "SELECT * FROM \"public\".\"Orders\" WHERE (Total > 10\n) ORDER BY \"Total\" DESC LIMIT 25 OFFSET 50",
            dialect.SelectPage(Orders(name), 50, 25, [new SortSpec("Total", true)], "Total > 10"));

        Assert.Equal(
            "SELECT * FROM \"public\".\"Orders\" ORDER BY \"Id\" LIMIT 25 OFFSET 0",
            dialect.SelectPage(Orders(name), 0, 25, [], null));

        Assert.Equal("SELECT * FROM \"public\".\"Orders\" LIMIT 5", dialect.SelectTop(name, 5));
    }

    [Fact]
    public void MySqlPagesWithLimitAndOffset()
    {
        var dialect = new MySqlDialect();
        var name = new DbObjectName(null, "Orders");

        Assert.Equal(
            "SELECT * FROM `Orders` WHERE (Total > 10\n) ORDER BY `Total` DESC LIMIT 25 OFFSET 50",
            dialect.SelectPage(Orders(name), 50, 25, [new SortSpec("Total", true)], "Total > 10"));

        Assert.Equal(
            "SELECT * FROM `Orders` ORDER BY `Id` LIMIT 25 OFFSET 0",
            dialect.SelectPage(Orders(name), 0, 25, [], null));

        Assert.Equal("SELECT * FROM `Orders` LIMIT 5", dialect.SelectTop(name, 5));
    }

    [Fact]
    public void AFilterEndingInALineCommentKeepsThePagingInForce()
    {
        // The newline before the closing parenthesis ends the comment, so the ORDER BY and LIMIT still apply.
        var name = new DbObjectName("dbo", "Orders");

        var sqlServer = new SqlServerDialect().SelectPage(Orders(name), 0, 25, [], "Total > 10 --");
        Assert.Contains("WHERE (Total > 10 --\n) ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY", sqlServer);

        var postgres = new PostgreSqlDialect().SelectPage(Orders(new DbObjectName("public", "Orders")), 0, 25, [], "Total > 10 --");
        Assert.Contains("WHERE (Total > 10 --\n) ORDER BY \"Id\" LIMIT 25 OFFSET 0", postgres);

        var mysql = new MySqlDialect().SelectPage(Orders(new DbObjectName(null, "Orders")), 0, 25, [], "Total > 10 --");
        Assert.Contains("WHERE (Total > 10 --\n) ORDER BY `Id` LIMIT 25 OFFSET 0", mysql);
    }

    [Fact]
    public void EveryDialectRefusesAPageThatIsNotOne()
    {
        var name = new DbObjectName("dbo", "Orders");
        var table = Orders(name);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerDialect().SelectPage(table, 0, 0, [], null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlDialect().SelectPage(table, -1, 5, [], null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MySqlDialect().SelectPage(table, 0, 0, [], null));
    }

    // ---- Parameter naming, typing and conversion, through sessions that never connect ----

    private sealed class SqlServerProbe(DbConnection connection) : SqlServerSession(connection, "probe")
    {
        public string Name(int index) => ParameterName(index);

        public string EmptyInsert => EmptyInsertClause;
    }

    private sealed class PostgreSqlProbe(DbConnection connection) : PostgreSqlSession(connection, "probe")
    {
        public string Name(int index) => ParameterName(index);

        public string EmptyInsert => EmptyInsertClause;

        public DbParameter Parameter(string dataType, object? value)
        {
            using var command = Connection.CreateCommand();
            return CreateParameter(command, "@p0", value, new ColumnInfo("c", dataType, true, false, false, null, 1));
        }

        public object? Convert(string? text, string dataType) =>
            ConvertValue(text, new ColumnInfo("c", dataType, true, false, false, null, 1));
    }

    private sealed class MySqlProbe(DbConnection connection) : MySqlSession(connection, "probe")
    {
        public string EmptyInsert => EmptyInsertClause;

        public string Name(int index) => ParameterName(index);

        public object? Convert(string? text, string dataType) =>
            ConvertValue(text, new ColumnInfo("c", dataType, true, false, false, null, 1));
    }

    [Fact]
    public void EveryEngineNamesItsParametersAtSignAndIndex()
    {
        Assert.Equal("@p0", new SqlServerProbe(new SqlConnection("Server=unused")).Name(0));
        Assert.Equal("@p7", new PostgreSqlProbe(new NpgsqlConnection("Host=unused")).Name(7));
        Assert.Equal("@p2", new MySqlProbe(new MySqlConnection("Server=unused")).Name(2));
    }

    [Fact]
    public void AnEmptyInsertIsWrittenAsEachEngineTakesIt()
    {
        // MySQL has no DEFAULT VALUES; it writes an insert with no values as "() VALUES ()".
        Assert.Equal("DEFAULT VALUES", new SqlServerProbe(new SqlConnection("Server=unused")).EmptyInsert);
        Assert.Equal("DEFAULT VALUES", new PostgreSqlProbe(new NpgsqlConnection("Host=unused")).EmptyInsert);
        Assert.Equal("() VALUES ()", new MySqlProbe(new MySqlConnection("Server=unused")).EmptyInsert);
    }

    [Fact]
    public void PostgreSqlTypesAParameterFromTheColumnSoItIsNotSentAsText()
    {
        var probe = new PostgreSqlProbe(new NpgsqlConnection("Host=unused"));

        // PostgreSQL refuses a text parameter for an integer column, so the parameter's own type is the column's.
        Assert.Equal(NpgsqlDbType.Integer, Assert.IsType<NpgsqlParameter>(probe.Parameter("integer", 42)).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Bigint, Assert.IsType<NpgsqlParameter>(probe.Parameter("bigint", 42L)).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Text, Assert.IsType<NpgsqlParameter>(probe.Parameter("character varying(20)", "x")).NpgsqlDbType);

        // A type the list does not know takes a string as PostgreSQL's own untyped literal, which it converts to the column's type.
        Assert.Equal(NpgsqlDbType.Unknown, Assert.IsType<NpgsqlParameter>(probe.Parameter("mood", "happy")).NpgsqlDbType);
    }

    [Fact]
    public void PostgreSqlConvertsEachTypedTextToItsClrType()
    {
        var probe = new PostgreSqlProbe(new NpgsqlConnection("Host=unused"));

        Assert.Equal(42, probe.Convert("42", "integer"));
        Assert.Equal(9_000_000_000L, probe.Convert("9000000000", "bigint"));
        Assert.Equal((short)7, probe.Convert("7", "smallint"));
        Assert.Equal(1.25m, probe.Convert("1.25", "numeric(10,2)"));
        Assert.Equal(2.5, probe.Convert("2.5", "double precision"));
        Assert.Equal(2.5f, probe.Convert("2.5", "real"));
        Assert.Equal(true, probe.Convert("true", "boolean"));
        Assert.Equal(new DateTime(2024, 3, 5, 10, 20, 30), probe.Convert("2024-03-05 10:20:30", "timestamp without time zone"));
        Assert.Equal(Guid.Parse("8f3c2a1e-0000-4000-8000-000000000001"), probe.Convert("8f3c2a1e-0000-4000-8000-000000000001", "uuid"));
        Assert.Equal(new byte[] { 0xDE, 0xAD }, probe.Convert("0xDEAD", "bytea"));
        Assert.Equal("text", probe.Convert("text", "character varying(20)"));
    }

    [Fact]
    public void PostgreSqlStoresATimestampWithAZoneAtItsUtcInstant()
    {
        var probe = new PostgreSqlProbe(new NpgsqlConnection("Host=unused"));

        // An explicit offset is converted: 10:20:30 at +02:00 is 08:20:30 UTC, and Npgsql takes only offset zero.
        var zoned = Assert.IsType<DateTimeOffset>(probe.Convert("2024-03-05 10:20:30+02:00", "timestamp(3) with time zone"));
        Assert.Equal(TimeSpan.Zero, zoned.Offset);
        Assert.Equal(new DateTime(2024, 3, 5, 8, 20, 30), zoned.UtcDateTime);

        // Text with no zone is local time: the same reading at this machine's offset, converted to UTC.
        var local = new DateTime(2024, 3, 5, 10, 20, 30);
        var plain = Assert.IsType<DateTimeOffset>(probe.Convert("2024-03-05 10:20:30", "timestamp with time zone"));
        Assert.Equal(TimeSpan.Zero, plain.Offset);
        Assert.Equal(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).UtcDateTime, plain.UtcDateTime);
    }

    [Fact]
    public void PostgreSqlFindsEachStatementOfABatchAsItsClientDoes()
    {
        // A semicolon inside a string, a quoted name, a comment or a dollar-quoted body does not end a statement;
        // a semicolon alone, or white space, is not a statement.
        const string batch = "SELECT 'a;b'; SELECT \"x;y\" -- c;d\n; /* e;f */ SELECT $$g;h$$; SELECT $fn$ i; $fn$;\n;  SELECT 9";

        var starts = PostgreSqlSession.StatementStarts(batch);

        Assert.Equal(5, starts.Count);
        Assert.StartsWith("SELECT 'a;b'", batch[starts[0]..], StringComparison.Ordinal);
        Assert.StartsWith("SELECT \"x;y\"", batch[starts[1]..], StringComparison.Ordinal);
        Assert.StartsWith("/* e;f */", batch[starts[2]..], StringComparison.Ordinal);
        Assert.StartsWith("SELECT $fn$", batch[starts[3]..], StringComparison.Ordinal);
        Assert.StartsWith("SELECT 9", batch[starts[4]..], StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSqlRefusesAnIntegerThatDoesNotFitItsColumn()
    {
        var probe = new PostgreSqlProbe(new NpgsqlConnection("Host=unused"));

        Assert.Throws<FormatException>(() => probe.Convert("2147483648", "integer"));
        Assert.Throws<FormatException>(() => probe.Convert("40000", "smallint"));
    }

    [Fact]
    public void MySqlReadsUnsignedIntegersAndTheTinyIntBooleanAsTheirKinds()
    {
        var probe = new MySqlProbe(new MySqlConnection("Server=unused"));

        Assert.Equal(5L, probe.Convert("5", "int unsigned"));
        Assert.Equal(5L, probe.Convert("5", "int(10) unsigned"));
        Assert.Equal(true, probe.Convert("true", "tinyint(1)"));
        Assert.Equal(7L, probe.Convert("7", "tinyint(4)"));
    }

    // ---- Display names: server and database, never the password ----

    [Fact]
    public void SqlServerDisplaysServerAndDatabaseWithoutThePassword()
    {
        const string connection = "Data Source=tcp:db.example.com,1433;Initial Catalog=Sales;User ID=app;Password=s3cret;";

        Assert.Equal("tcp:db.example.com,1433/Sales", SqlServerProvider.DisplayNameOf(connection));
        Assert.DoesNotContain("s3cret", SqlServerProvider.DisplayNameOf(connection));
        Assert.Equal("db.example.com", SqlServerProvider.DisplayNameOf("Data Source=db.example.com;Integrated Security=true"));
    }

    [Fact]
    public void PostgreSqlDisplaysHostPortAndDatabaseWithoutThePassword()
    {
        const string connection = "Host=db.example.com;Database=sales;Username=app;Password=s3cret";

        Assert.Equal("db.example.com/sales", PostgreSqlProvider.DisplayNameOf(connection));
        Assert.Equal("db.example.com:5433/sales", PostgreSqlProvider.DisplayNameOf(connection + ";Port=5433"));
        Assert.DoesNotContain("s3cret", PostgreSqlProvider.DisplayNameOf(connection));
    }

    [Fact]
    public void MySqlDisplaysServerAndDatabaseWithoutThePassword()
    {
        const string connection = "Server=db.example.com;Database=sales;User ID=app;Password=s3cret";

        Assert.Equal("db.example.com/sales", MySqlProvider.DisplayNameOf(connection));
        Assert.Equal("db.example.com:3307/sales", MySqlProvider.DisplayNameOf(connection + ";Port=3307"));
        Assert.DoesNotContain("s3cret", MySqlProvider.DisplayNameOf(connection));
    }

    [Fact]
    public void EveryEngineHasAProviderOfItsKind()
    {
        Assert.Equal(DatabaseKind.SqlServer, DatabaseProviders.Get(DatabaseKind.SqlServer).Kind);
        Assert.Equal(DatabaseKind.PostgreSql, DatabaseProviders.Get(DatabaseKind.PostgreSql).Kind);
        Assert.Equal(DatabaseKind.MySql, DatabaseProviders.Get(DatabaseKind.MySql).Kind);
    }
}

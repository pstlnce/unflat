using Unflat;
using BenchmarkDotNet.Running;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text;
using Bogus;
using Microsoft.Data.Sqlite;
using Dapper;
using System.Globalization;
using UnflatTester;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using System.Collections;
using System.Diagnostics.CodeAnalysis;


//UnflatTester.SqliteData.GenerateMeasured();
//return;

var con = new SqlConnection();


#if false

var preDapper = await new UnflatTester.SqliteBench().Dapper();
GC.Collect();
GC.Collect(1);
GC.Collect(2);
GC.Collect(3);

var start = Stopwatch.GetTimestamp();

try
{
    var dapper = await new UnflatTester.SqliteBench().Dapper();
}
finally
{
    var elapsed = Stopwatch.GetElapsedTime(start);
    Console.WriteLine("Elapsed time - {0}", elapsed);
}
return;
#elif false
var preUnflat = await new UnflatTester.SqliteBench().Unflat();
GC.Collect();
GC.Collect(1);
GC.Collect(2);
GC.Collect(3);

var start = Stopwatch.GetTimestamp();

try
{
    var unflat = await new UnflatTester.SqliteBench().Unflat();
}
finally
{
    var elapsed = Stopwatch.GetElapsedTime(start);
    Console.WriteLine("Elapsed time - {0}", elapsed);
}
return;
#elif true

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var config = BenchmarkDotNet.Configs.DefaultConfig.Instance
    .AddJob(BenchmarkDotNet.Jobs.Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));

/*
Console.WriteLine(UnflatTester.SqliteBench._query);

Console.WriteLine(UnflatTester.SqliteData.ConnectionString);

using var a = new SqliteConnection(UnflatTester.SqliteData.ConnectionString);
var b = a.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table';").AsList();
var bb = a.Query<UnflatTester.Movie>("SELECT * FROM movies").AsList();

for(var i = 0; i < bb.Count && i < 100; i++)
{
    Console.WriteLine(bb[i].Id);
}

Console.WriteLine(string.Join("\n", b));

return;
*/

//UnflatTester.SqliteBench.Init();

BenchmarkRunner.Run<UnflatTester.SqliteBench>(config);
#elif true
BenchmarkRunner.Run<UnflatTester.BigModelBenchmark>();
#elif true
BenchmarkRunner.Run<UnflatTester.Benchy>();
#elif true
BenchmarkRunner.Run<UnflatTester.ParsingBenchmarks>();
#elif false
#endif

return;

[
    UnflatMarker(Case = MatchCase.IgnoreCase, GenerateDbReader = false),
    Some(Map = ["", 1, 3, null!]),
]
public sealed class Mapper
{
    public static string[] _p;

    public int? NullableValueType { get; set; }

    [UnflatSource([])]
    public int Num1 { get; set; }

    public string Name { get; set; }

    [SettableParser("Convert.ToString({0})")]
    public string Description { get; set; }

    [UnflatIgnore, Unflat.SettableParser("Convert.ToDateTime({0})")]
    public DateTime Time { get; set; }

    public Uri Uri { get; set; }

    [Unflat.UnflatParser]
    public static Uri ParseToUri(object value)
    {
        return new Uri(value as string);
    }

    //[UnflatParser(IsDefault = false)]
    public static int Parse(object v)
    {
        return 0;
    }


    //[UnflatRun(1)]
    public static string CompileTimeCall()
    {
        var sb = new StringBuilder();

        var bb = new object[3] { 1, 2, 3 };
        sb.AppendFormat("1: {0}", bb);
        //sb.Append("1: ").Append(bb);

        //sb
        //.Append(repeatCount: 1, value: '\n')
        //.Append('-', 10)
        //.Append('\n')
        //.Append('-', 10)
        //.AppendFormat("Hello {0}!", new object[] { "Sailor!", "NotSailor", "Skip", "Params" });

        /*
        for (int i = 0; i <= 100; i++)
        {
            if(i % 2 == 0)
            {
                sb.Append('\n').Append(i).Append(" - is even");
            }
            else
            {
                sb.Append('\n').Append(i).Append(" - is not even");
            }
        }
        */

        var result = sb.ToString();

        return result;
    }
}

public sealed class NestedClass
{
    public required DateTime Property1 { get; set; }
    public required decimal Property2 { get; set; }
    public int Property3 { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
internal sealed class SomeAttribute : Attribute
{
    public required object[] Map { get; init; }

    public SomeAttribute() { }

    public SomeAttribute(object[] map)
    {
        Map = map;
    }
}

public sealed class FDbDataReader : DbDataReader
{
    public override object this[int ordinal] => throw new NotImplementedException();

    public override object this[string name] => throw new NotImplementedException();

    public override int Depth => throw new NotImplementedException();

    public override int FieldCount => throw new NotImplementedException();

    public override bool HasRows => throw new NotImplementedException();

    public override bool IsClosed => throw new NotImplementedException();

    public override int RecordsAffected => throw new NotImplementedException();

    public override bool GetBoolean(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override byte GetByte(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override char GetChar(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override string GetDataTypeName(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override DateTime GetDateTime(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override decimal GetDecimal(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override double GetDouble(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override float GetFloat(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override Guid GetGuid(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override short GetInt16(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetInt32(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetInt64(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override string GetName(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetOrdinal(string name)
    {
        throw new NotImplementedException();
    }

    public override string GetString(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override object GetValue(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetValues(object[] values)
    {
        throw new NotImplementedException();
    }

    public override bool IsDBNull(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override bool NextResult()
    {
        throw new NotImplementedException();
    }

    public override bool Read()
    {
        throw new NotImplementedException();
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        
        return base.ReadAsync(cancellationToken);
    }
}

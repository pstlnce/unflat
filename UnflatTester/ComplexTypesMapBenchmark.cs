using BenchmarkDotNet.Attributes;
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unflat;

namespace UnflatTester;

[ShortRunJob, MemoryDiagnoser, Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class ComplexTypesMapBenchmark
{
    static DataTable _dt;

    [Params(100, 1000, 10_000, 100_000)]
    public int _count;

    [GlobalSetup]
    public void Setup()
    {
        _dt = new();

        _dt.Columns.AddRange([
            new DataColumn(nameof(SomeComplexType.Id), typeof(int)),
            new DataColumn(nameof(SomeComplexType.Name), typeof(string)),
            new DataColumn(nameof(SomeComplexType.Age), typeof(int)),
            new DataColumn(nameof(SomeComplexType.Time), typeof(DateTime)),
        ]);

        _dt.Columns.Add(new DataColumn("0", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl1.NestedClassLvl1_1), typeof(string)),
            new DataColumn(nameof(NestedClassLvl1.NestedClassLvl1_2), typeof(int)),
        ]);

        _dt.Columns.Add(new DataColumn("1", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl1Lvl2.NestedClassLvl1Lvl2_1), typeof(long)),
            new DataColumn(nameof(NestedClassLvl1Lvl2.NestedClassLvl1Lvl2_2), typeof(DateTime)),
        ]);

        _dt.Columns.Add(new DataColumn("2", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl1Lvl22.NestedClassLvl1Lvl22_1), typeof(long)),
            new DataColumn(nameof(NestedClassLvl1Lvl22.NestedClassLvl1Lvl22_2), typeof(DateTime)),
        ]);

        _dt.Columns.Add(new DataColumn("3", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl11.NestedClassLvl11_1), typeof(string)),
            new DataColumn(nameof(NestedClassLvl11.NestedClassLvl11_2), typeof(int)),
        ]);

        _dt.Columns.Add(new DataColumn("4", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl11Lvl2.NestedClassLvl11Lvl2_1), typeof(long)),
            new DataColumn(nameof(NestedClassLvl11Lvl2.NestedClassLvl11Lvl2_2), typeof(DateTime)),
        ]);

        _dt.Columns.Add(new DataColumn("5", typeof(string)));

        _dt.Columns.AddRange([
            new DataColumn(nameof(NestedClassLvl11Lvl22.NestedClassLvl11Lvl22_1), typeof(long)),
            new DataColumn(nameof(NestedClassLvl11Lvl22.NestedClassLvl11Lvl22_2), typeof(DateTime)),
        ]);

        for(int i = 0; i < _count; i++)
        {
            _dt.Rows.Add([
                //SomeComplexType.Id
                i,

                //SomeComplexType.Name
                Random.Shared.NextInt64().ToString(),

                //SomeComplexType.Age
                i,

                 //SomeComplexType.Time
                new DateTime(Random.Shared.NextInt64()),

                // 0
                null,

                //NestedClassLvl1.NestedClassLvl1_1 typeof(string)
                Random.Shared.NextInt64().ToString(),

                //NestedClassLvl1.NestedClassLvl1_2, typeof(int)
                Random.Shared.Next(),

                // 1
                null,

                //NestedClassLvl1Lvl2.NestedClassLvl1Lvl2_1, typeof(long)
                Random.Shared.NextInt64(),

                //NestedClassLvl1Lvl2.NestedClassLvl1Lvl2_2, typeof(DateTime)
                new DateTime(Random.Shared.NextInt64()),

                // 2
                null,

                //NestedClassLvl1Lvl22.NestedClassLvl1Lvl22_1, typeof(long)
                Random.Shared.NextInt64(),

                //NestedClassLvl1Lvl22.NestedClassLvl1Lvl22_2, typeof(DateTime)
                new DateTime(Random.Shared.NextInt64()),

                // 3
                null,

                //NestedClassLvl11.NestedClassLvl11_1, typeof(string),
                Random.Shared.NextInt64().ToString(),

                //new DataColumn(nameof(NestedClassLvl11.NestedClassLvl11_2), typeof(int)),
                Random.Shared.Next(),

                // 4
                null,

                //NestedClassLvl11Lvl2.NestedClassLvl11Lvl2_1, typeof(long)
                Random.Shared.NextInt64(),

                //NestedClassLvl11Lvl2.NestedClassLvl11Lvl2_2, typeof(DateTime)
                new DateTime(Random.Shared.NextInt64()),

                // 5
                null,

                //NestedClassLvl11Lvl22.NestedClassLvl11Lvl22_1, typeof(long)
                Random.Shared.NextInt64(),

                //NestedClassLvl11Lvl22.NestedClassLvl11Lvl22_2, typeof(DateTime)
                new DateTime(Random.Shared.NextInt64()),
            ]);
        }
    }

    public void Dapper()
    {
        var reader = _dt.CreateDataReader();
        var list = reader.Parse<SomeComplexType>().AsList();
    }
}

[UnflatMarker]
public sealed class SomeComplexType
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public DateTime Time { get; set; }

    public NestedClassLvl1 Lvl1First { get; set; }
    public NestedClassLvl11 Lvl1Second { get; set; }
}

public sealed class NestedClassLvl1
{
    public string NestedClassLvl1_1 { get; set; }
    public int NestedClassLvl1_2 { get; set; }

    public NestedClassLvl1Lvl2 NestedFirst { get; set; }
    public NestedClassLvl1Lvl22 NestedSecond { get; set; }
}

public sealed class NestedClassLvl1Lvl2
{
    public long NestedClassLvl1Lvl2_1 { get; set; }
    public DateTime NestedClassLvl1Lvl2_2 { get; set; }
}

public sealed class NestedClassLvl1Lvl22
{
    public long NestedClassLvl1Lvl22_1 { get; set; }
    public DateTime NestedClassLvl1Lvl22_2 { get; set; }
}

public sealed class NestedClassLvl11
{
    public string NestedClassLvl11_1 { get; set; }
    public int NestedClassLvl11_2 { get; set; }

    public NestedClassLvl11Lvl2 NestedFirst { get; set; }
    public NestedClassLvl11Lvl22 NestedSecond { get; set; }
}

public sealed class NestedClassLvl11Lvl2
{
    public long NestedClassLvl11Lvl2_1 { get; set; }
    public DateTime NestedClassLvl11Lvl2_2 { get; set; }
}

public sealed class NestedClassLvl11Lvl22
{
    public long NestedClassLvl11Lvl22_1 { get; set; }
    public DateTime NestedClassLvl11Lvl22_2 { get; set; }
}

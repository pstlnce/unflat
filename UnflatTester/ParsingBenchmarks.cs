using Unflat;
using BenchmarkDotNet.Attributes;
using Dapper;
using MapDataReader;
using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace UnflatTester;

[ShortRunJob, MemoryDiagnoser, Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class ParsingBenchmarks
{
    static TestClass _o = new TestClass();
    static PropertyInfo _prop = _o.GetType().GetProperty("String1", BindingFlags.Public | BindingFlags.Instance);
    static PropertyInfo _nullableprop = _o.GetType().GetProperty("IntNullable", BindingFlags.Public | BindingFlags.Instance);

    [Benchmark]
    public void Dapper()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.Parse<TestClass2>().ToList();
    }

    [Benchmark]
    public void MapaDataReader()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.ToTestClass2();
    }

    [Benchmark]
    public void Unflat()
    {
        var dr = _dt.CreateDataReader();
        var list = TestClass2Parser.ReadList(dr).ToList();
    }

    static DataTable _dt;

    [GlobalSetup]
    public static void Setup()
    {
        //create datatable with test data
        _dt = new DataTable();
        _dt.Columns.AddRange(new[] {
                new DataColumn("String1", typeof(string)),
                new DataColumn("String2", typeof(string)),
                new DataColumn("String3", typeof(string)),
                new DataColumn("Int", typeof(int)),
                new DataColumn("Int2", typeof(int)),
                new DataColumn("IntNullable", typeof(int))
            });


        for (int i = 0; i < 1000; i++)
        {
            _dt.Rows.Add("xxx", "yyy", "zzz", 123, 321, 3211);
        }
    }
}

[GenerateDataReaderMapper, UnflatMarker(Case = MatchCase.MatchOriginal)]
public class TestClass2
{
    public string String1 { get; set; }
    public string String2 { get; set; }
    public string String3 { get; set; }
    public string Int { get; set; }
    public string Int2 { get; set; }
    public int IntNullable { get; set; }
}

[MemoryDiagnoser, Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[MediumRunJob(BenchmarkDotNet.Jobs.RuntimeMoniker.NativeAot90)]
public class Benchy
{
    static TestClass _o = new TestClass();
    static PropertyInfo _prop = _o.GetType().GetProperty("String1", BindingFlags.Public | BindingFlags.Instance);
    static PropertyInfo _nullableprop = _o.GetType().GetProperty("IntNullable", BindingFlags.Public | BindingFlags.Instance);

    [Params(100, 1000, 10_000, 50_000)]
    public static int count;

    [Benchmark]
    public void Dapper()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.Parse<TestClass>().AsList();
    }

    //[Benchmark]
    public void MapaDataReader()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.ToTestClass();
    }

    [Benchmark]
    public void Unflat()
    {
        var dr = _dt.CreateDataReader();
        var list = TestClassParser.ReadList(dr);
    }

    static DataTable _dt;

    [GlobalSetup]
    public static void Setup()
    {
        //create datatable with test data
        _dt = new DataTable();
        _dt.Columns.AddRange(new[] {
                new DataColumn("String1", typeof(string)),
                new DataColumn("String2", typeof(string)),
                new DataColumn("String3", typeof(string)),
                new DataColumn("Int", typeof(int)),
                new DataColumn("Int2", typeof(int)),
                new DataColumn("IntNullable", typeof(int)),

                new DataColumn("String1_1", typeof(string)),
                new DataColumn("String2_1", typeof(string)),
                new DataColumn("String3_1", typeof(string)),
                new DataColumn("Int_1", typeof(string)),
                new DataColumn("Int2_1", typeof(string)),
                new DataColumn("IntNullable_1", typeof(int)),

                new DataColumn("String1_2", typeof(string)),
                new DataColumn("String2_2", typeof(string)),
                new DataColumn("String3_2", typeof(string)),
                new DataColumn("Int_2", typeof(string)),
                new DataColumn("Int2_2", typeof(string)),
                new DataColumn("IntNullable_2", typeof(int)),

                //new DataColumn("String1_3", typeof(string)),
                //new DataColumn("String2_3", typeof(string)),
                //new DataColumn("String3_3", typeof(string)),
                //new DataColumn("Int_3", typeof(string)),
                //new DataColumn("Int2_3", typeof(string)),
                //new DataColumn("IntNullable_3", typeof(int))
            });


        for (int i = 0; i < count; i++)
        {
            _dt.Rows.Add(
                "xxx", "yyy", "zzz", 123, 321, 3211,
                "xxx", "yyy", "zzz", 123, 321, 3211,
                "xxx", "yyy", "zzz", 123, 321, 3211
                //"xxx", "yyy", "zzz", 123, 321, 3211
            );
        }
    }
}

[UnflatMarker]
[GenerateDataReaderMapper]
public class TestClass
{
    //[UnflatParser(IsDefault = true)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static int ConvertToInt(object value)
        => Convert.ToInt32(value);

    //[UnflatParser(IsDefault = true)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string ConvertToString(object value)
        => (value as string)!;

    public string String1 { get; set; }
    public string String2 { get; set; }
    public string String3 { get; set; }
    public string Int { get; set; }
    public string Int2 { get; set; }
    public int IntNullable { get; set; }

    public string String1_1 { get; set; }
    public string String2_1 { get; set; }
    public string String3_1 { get; set; }
    public string Int_1 { get; set; }
    public string Int2_1 { get; set; }
    public int IntNullable_1 { get; set; }

    public string String1_2 { get; set; }
    public string String2_2 { get; set; }
    public string String3_2 { get; set; }
    public string Int_2 { get; set; }
    public string Int2_2 { get; set; }
    public int IntNullable_2 { get; set; }

    //public string String1_3 { get; set; }
    //public string String2_3 { get; set; }
    //public string String3_3 { get; set; }
    //public string Int_3 { get; set; }
    //public string Int2_3 { get; set; }
    //public int IntNullable_3 { get; set; }

}

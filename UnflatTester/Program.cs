using Unflat;
using BenchmarkDotNet.Running;
using System.Data;
using System.Data.Common;
using System.Text.Json;

#if true
BenchmarkRunner.Run<UnflatTester.BigModelBenchmark>();
#elif false
BenchmarkRunner.Run<UnflatTester.Benchy>();
#elif true
BenchmarkRunner.Run<UnflatTester.ParsingBenchmarks>();
#elif false
#endif

return;

[
    UnflatMarker(Case = MatchCase.IgnoreCase),
    Some(Map = ["", 1, 3, null!]),
]
public sealed class Mapper
{
    public static string[] _p;

    [FieldSource([])]
    public int Num1 { get; set; }

    public string Name { get; set; }

    [SettableParser("Convert.ToString({0})")]
    public string Description { get; set; }

    [Unflat.SettableParser("Convert.ToDateTime({0})")]
    public DateTime Time { get; set; }

    [UnflatParser(IsDefault = false)]
    public static int Parse(object v)
    {
        return 0;
    }
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



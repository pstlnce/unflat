using Unflat;
using BenchmarkDotNet.Running;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text;
using Bogus;

#if false
BenchmarkRunner.Run<UnflatTester.BigModelBenchmark>();
#elif true
BenchmarkRunner.Run<UnflatTester.Benchy>();
#elif true
BenchmarkRunner.Run<UnflatTester.ParsingBenchmarks>();
#elif false
#endif

return;

[UnflatMarker(Case = MatchCase.IgnoreCase)]
public sealed class Mapper
{
    public static string[] _p;

    public required NestedClass NestedClass { get; set; }

    [UnflatSource(["Num33"])]
    public required int Num1 { get; set; }

    public required string Name { get; set; }

    [SettableParser("Convert.ToString({0})")]
    public string Description { get; set; }

    [Unflat.SettableParser("Convert.ToDateTime({0})")]
    public DateTime Time { get; set; }

    [UnflatParser(CallFormat = "Mapper.Parse({0}, {1})", IsDefault = false, NamespaceScope = "UnflatTester")]
    public static int Parse(object v, int index)
    {
        return Convert.ToInt32(v);
    }

    [UnflatRun(1)]
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



using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace UnflatTester;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
internal class SchemaReadingBenchmarks
{

}

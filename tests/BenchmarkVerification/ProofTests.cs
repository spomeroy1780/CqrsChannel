using System.Diagnostics;
using FluentAssertions;

namespace BenchmarkVerification;

public class ProofTests
{
    [Fact]
    public async Task Express_ShouldBeFaster_Than_Mediatr()
    {
        // Create instances of the benchmarks
        var benchmarks = new CqrsBenchmarks.Benchmarks();
        benchmarks.Setup();

        // Measure CompiledExpress performance (sync version for consistency)
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            await benchmarks.CqrsExpressDirect();
        }
        stopwatch.Stop();
        var compiledExpressTime = stopwatch.Elapsed.TotalNanoseconds / 10000;

        // Measure MediatR performance
        stopwatch.Restart();
        for (int i = 0; i < 10000; i++)
        {
            await benchmarks.Mediatr();
        }
        stopwatch.Stop();
        var mediatrTime = stopwatch.Elapsed.TotalNanoseconds / 10000;

        // CompiledExpress should be significantly faster than MediatR
        compiledExpressTime.Should().BeLessThan(mediatrTime, 
            $"CompiledExpress ({compiledExpressTime:F2}ns) should be faster than MediatR ({mediatrTime:F2}ns)");
        
        // CompiledExpress should be at least 10x faster than MediatR
        (mediatrTime / compiledExpressTime).Should().BeGreaterThan(10, 
            "CompiledExpress should be at least 10x faster than MediatR");
    }
}


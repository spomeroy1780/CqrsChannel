using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using CqrsBenchmarks;

// Create a custom config that doesn't require high priority
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default
        .WithToolchain(InProcessEmitToolchain.Instance)
        .WithId("LowPriority"));

// Parse command line arguments
if (args.Length > 0)
{
    switch (args[0].ToLower())
    {
        case "load":
        case "loadbenchmarks":
            BenchmarkRunner.Run<LoadBenchmarks>(config);
            break;
        case "standard":
        case "benchmarks":
            BenchmarkRunner.Run<Benchmarks>(config);
            break;
        case "all":
            BenchmarkRunner.Run(typeof(Program).Assembly, config);
            break;
        default:
            Console.WriteLine("Usage: dotnet run [load|standard|all]");
            Console.WriteLine("  load     - Run load testing benchmarks with parameterized operation counts");
            Console.WriteLine("  standard - Run standard single-operation benchmarks");
            Console.WriteLine("  all      - Run all benchmark classes");
            return;
    }
}
else
{
    // Default to standard benchmarks if no argument provided
    Console.WriteLine("No benchmark type specified. Running standard benchmarks.");
    Console.WriteLine("Use 'dotnet run load' for load testing or 'dotnet run all' for all benchmarks.");
    BenchmarkRunner.Run<Benchmarks>(config);
}

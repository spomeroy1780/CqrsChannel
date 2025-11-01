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
        case "pipeline":
        case "pipelines":
            BenchmarkRunner.Run<PipelineBenchmarks>(config);
            break;
        case "aot":
            BenchmarkRunner.Run<AotBenchmarks>(config);
            break;
        case "aot-pipeline":
        case "aotpipeline":
            BenchmarkRunner.Run<AotPipelineBenchmarks>(config);
            break;
        case "aot-vs-standard":
        case "aotvs":
            BenchmarkRunner.Run<AotVsStandardBenchmarks>(config);
            break;
        case "comprehensive":
        case "all":
            Console.WriteLine("Running comprehensive benchmarks comparing ALL implementations...");
            Console.WriteLine("This includes: MediatR, MessagePipe, ExpressMediator (all variants), and AotExpressMediator");
            BenchmarkRunner.Run<ComprehensiveBenchmarks>(config);
            break;
        case "all-classes":
            Console.WriteLine("Running ALL benchmark classes in the assembly...");
            BenchmarkRunner.Run(typeof(Program).Assembly, config);
            break;
        case "aot-scalability":
            Console.WriteLine("Running AOT scalability benchmarks (10-10,000 handlers)...");
            BenchmarkRunner.Run<AotScalabilityBenchmarks>(config);
            break;

        default:
            Console.WriteLine("Usage: dotnet run [load|standard|pipeline|aot|aot-pipeline|aot-vs-standard|aot-scalability|comprehensive|all|all-classes]");
            Console.WriteLine("  load            - Run load testing benchmarks with parameterized operation counts");
            Console.WriteLine("  standard        - Run standard single-operation benchmarks");
            Console.WriteLine("  pipeline        - Run pipeline performance benchmarks (MediatR vs ExpressMediator)");
            Console.WriteLine("  aot             - Run AOT vs Standard vs MediatR benchmarks");
            Console.WriteLine("  aot-pipeline    - Run AOT vs Standard vs MediatR with pipelines");
            Console.WriteLine("  aot-vs-standard - Direct comparison: AotExpressMediator vs ExpressMediator");
            Console.WriteLine("  aot-scalability - Run AOT scalability benchmarks (10-10,000 handlers)");
            Console.WriteLine("  comprehensive   - Run comprehensive benchmark comparing ALL implementations (RECOMMENDED)");
            Console.WriteLine("  all             - Same as comprehensive - compare MediatR, MessagePipe, ExpressMediator, AOT");
            Console.WriteLine("  all-classes     - Run every benchmark class in the assembly");
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

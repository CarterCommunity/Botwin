namespace Carter.Benchmarks
{
    using BenchmarkDotNet.Configs;
    using BenchmarkDotNet.Horology;
    using BenchmarkDotNet.Jobs;
    using BenchmarkDotNet.Running;

    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, CreateConfig());
        }
        
        private static IConfig CreateConfig()
        {
            var job = Job.Default
                .WithWarmupCount(1)
                .WithIterationTime(TimeInterval.FromMilliseconds(250))
                .WithMaxIterationCount(20);

            return DefaultConfig.Instance
                .With(job.AsDefault());
        }
    }
}

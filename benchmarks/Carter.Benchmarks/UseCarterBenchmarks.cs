namespace Carter.Benchmarks
{
    using System;
    using BenchmarkDotNet.Attributes;
    using Microsoft.AspNetCore.Builder.Internal;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public class UseCarterBenchmarks
    {
        private ApplicationBuilder app;
        
        [GlobalSetup]
        public void Setup()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.Configure((Action<RouteOptions>)(options => { }));

            serviceCollection.AddSingleton<ILoggerFactory, LoggerFactory>();
            serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            serviceCollection.AddCarter();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            this.app = new ApplicationBuilder(serviceProvider);
        }

        [Benchmark]
        public void UseCarter() => this.app.UseCarter();
    }
}

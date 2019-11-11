namespace Carter.Benchmarks
{
    using System.Threading.Tasks;

    public class TestModule : CarterModule
    {
        public TestModule()
        {
            Get("/", (request, response, routeData) => Task.CompletedTask);
        }
    }
}

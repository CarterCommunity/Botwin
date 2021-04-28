namespace Carter.Tests
{
    using System;
    using System.Globalization;
    using System.Linq;
    using Carter.Request;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;

    public class TestModule : CarterModule
    {
        private Guid instanceId;
        
        public TestModule()
        {
            this.instanceId = Guid.NewGuid();
            
            this.Before += async ctx =>
            {
                await ctx.Response.WriteAsync("Before");
                return true;
            };

            this.After = async ctx => { await ctx.Response.WriteAsync("After"); };
            
            this.Get("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });

            this.Get("/querystring", async ctx =>
            {
                var id = ctx.Request.Query.As<int>("id");
                await ctx.Response.WriteAsync($"Managed to parse an int {id}");
            });

            this.Get("/querystringarray", async ctx =>
            {
                var id = ctx.Request.Query.As<int[]>("id");
                var foo = string.Join(",", id);
                await ctx.Response.WriteAsync(foo);
            });

            this.Get("/nullablequerystring", async ctx =>
            {
                var id = ctx.Request.Query.As<int?>("id");
                await ctx.Response.WriteAsync($"Managed to parse a Nullable<int> {id}");
            });

            this.Get("/multiquerystring", async ctx =>
            {
                var id = ctx.Request.Query.AsMultiple<int>("id");
                await ctx.Response.WriteAsync($"Managed to parse multiple ints {id.Count()}");
            });

            this.Get("/nullablemultiquerystring", async ctx =>
            {
                var id = ctx.Request.Query.AsMultiple<int?>("id");
                await ctx.Response.WriteAsync($"Managed to parse multiple Nullable<int>s {id.Count()}");
            });

            this.Get("/querystringdefault", async ctx =>
            {
                var id = ctx.Request.Query.As("id", 69);
                await ctx.Response.WriteAsync($"Managed to parse default int {id}");
            });

            this.Post("/asstringasync", async ctx =>
            {
                var content = await ctx.Request.Body.AsStringAsync();
                await ctx.Response.WriteAsync(content);
            });

            this.Get("405test", context => context.Response.WriteAsync("hi"));
            this.Get("405testwithslash/", context => context.Response.WriteAsync("hi"));
            this.Get("405multiple", context => context.Response.WriteAsync("405multiple-get"));
            this.Post("405multiple", context => context.Response.WriteAsync("405multiple-post"));

            this.Get("/parameterized/{name:alpha}", ctx => ctx.Response.WriteAsync("echo " + ctx.GetRouteData().Values["name"]));
            this.Get("/parameterized/{id:int}", ctx => ctx.Response.WriteAsync("echo " + ctx.Request.RouteValues.As<int>("id")));
            this.Get("/parameterized/{id:guid}", ctx => ctx.Response.WriteAsync("echo " + ctx.Request.RouteValues.As<Guid>("id")));
            this.Get("/parameterized/{id:datetime}", ctx => ctx.Response.WriteAsync("echo " + ctx.Request.RouteValues.As<DateTime>("id").ToString("dd/MM/yyyy hh:mm:ss", CultureInfo.InvariantCulture)));

            this.Post("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });
            this.Put("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });
            this.Delete("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });
            this.Head("/head", async ctx => { await ctx.Response.WriteAsync("Hello"); });
            this.Patch("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });
            this.Options("/", async ctx => { await ctx.Response.WriteAsync("Hello"); });
        }
    }
}

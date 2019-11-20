namespace CarterSample
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Carter;
    using CarterSample.Features.Actors;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IActorProvider, ActorProvider>();

            services.AddCarter();
        }

        public void Configure(IApplicationBuilder app, IConfiguration config)
        {
            var appconfig = new AppConfiguration();
            config.Bind(appconfig);

            app.UseExceptionHandler("/errorhandler");
            app.UseRouting();
            app.UseSwaggerUI(opt =>
            {
                opt.RoutePrefix = "openapi/ui";
                opt.SwaggerEndpoint("/openapi", "Carter OpenAPI Sample");
            });

            app.UseEndpoints(builder=>builder.MapCarter(this.GetOptions(app.ServerFeatures.Get<IServerAddressesFeature>().Addresses)));
        }

        private CarterOptions GetOptions(ICollection<string> addresses)
        {
            return new CarterOptions(
                new OpenApiOptions("Carter <3 OpenApi", addresses,
                    new Dictionary<string, OpenApiSecurity>
                    {
                        { "BearerAuth", new OpenApiSecurity { BearerFormat = "JWT", Type = OpenApiSecurityType.http, Scheme = "bearer" } },
                        { "ApiKey", new OpenApiSecurity { Type = OpenApiSecurityType.apiKey, Name = "X-API-KEY", In = OpenApiIn.header } }
                    }, new[] { "BearerAuth" }));
        }

        private Task<bool> GetBeforeHook(HttpContext ctx)
        {
            ctx.Request.Headers["HOWDY"] = "FOLKS";
            return Task.FromResult(true);
        }

        private Task GetAfterHook(HttpContext ctx)
        {
            Console.WriteLine("We hit a route!");
            return Task.CompletedTask;
        }
    }
}

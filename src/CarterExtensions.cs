namespace Carter
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using FluentValidation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public static class CarterExtensions
    {
        /// <summary>
        /// Adds Carter to the specified <see cref="IApplicationBuilder"/>.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> to configure.</param>
        /// <param name="options">A <see cref="CarterOptions"/> instance.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IApplicationBuilder UseCarter(this IApplicationBuilder builder, CarterOptions options = null)
        {
            ApplyGlobalBeforeHook(builder, options);

            ApplyGlobalAfterHook(builder, options);

            var routeBuilder = new RouteBuilder(builder);

            //Create a "startup scope" to resolve modules from
            using (var scope = builder.ApplicationServices.CreateScope())
            {
                //Get all instances of CarterModule to fetch and register declared routes
                foreach (var module in scope.ServiceProvider.GetServices<CarterModule>())
                {
                    var moduleType = module.GetType();

                    var distinctPaths = module.Routes.Keys.Select(route => route.path).Distinct();

                    foreach (var path in distinctPaths)
                    {
                        routeBuilder.MapRoute(path, CreateRouteHandler(path, moduleType));
                    }
                }
            }

            return builder.UseRouter(routeBuilder.Build());
        }

        private static RequestDelegate CreateRouteHandler(string path, Type moduleType)
        {
            return async ctx =>
            {
                var module = ctx.RequestServices.GetRequiredService(moduleType) as CarterModule;
                var loggerFactory = ctx.RequestServices.GetService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger(moduleType);

                if (!module.Routes.TryGetValue((ctx.Request.Method, path), out var routeHandler))
                {
                    // if the path was registered but a handler matching the
                    // current method was not found, return MethodNotFound
                    ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }

                // begin handling the request
                if (HttpMethods.IsHead(ctx.Request.Method))
                {
                    //Cannot read the default stream once WriteAsync has been called on it
                    ctx.Response.Body = new MemoryStream();
                }

                // run the module handlers
                bool shouldContinue = true;

                if (module.Before != null)
                {
                    shouldContinue = await module.Before(ctx);
                }

                if (shouldContinue)
                {
                    // run the route handler
                    logger.LogTrace("Executing module route handler for {Method} /{Path}", ctx.Request.Method, path);
                    await routeHandler(ctx);

                    // run after handler
                    if (module.After != null)
                    {
                        await module.After(ctx);
                    }
                }

                // run status code handler
                var statusCodeHandlers = ctx.RequestServices.GetServices<IStatusCodeHandler>();
                var scHandler = statusCodeHandlers.FirstOrDefault(x => x.CanHandle(ctx.Response.StatusCode));

                if (scHandler != null)
                {
                    await scHandler.Handle(ctx);
                }

                if (HttpMethods.IsHead(ctx.Request.Method))
                {
                    var length = ctx.Response.Body.Length;
                    ctx.Response.Body.SetLength(0);
                    ctx.Response.ContentLength = length;
                }
            };
        }

        private static void ApplyGlobalAfterHook(IApplicationBuilder builder, CarterOptions options)
        {
            if (options?.After != null)
            {
                builder.Use(async (ctx, next) =>
                {
                    var loggerFactory = ctx.RequestServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("Carter.GlobalAfterHook");
                    await next();
                    logger.LogTrace("Executing global after hook");
                    await options.After(ctx);
                });
            }
        }

        private static void ApplyGlobalBeforeHook(IApplicationBuilder builder, CarterOptions options)
        {
            if (options?.Before != null)
            {
                builder.Use(async (ctx, next) =>
                {
                    var loggerFactory = ctx.RequestServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("Carter.GlobalBeforeHook");
                    logger.LogTrace("Executing global before hook");
                    
                    var carryOn = await options.Before(ctx); 
                    if (carryOn)
                    {
                        logger.LogTrace("Executing next handler after global before hook");
                        await next();
                    }
                });
            }
        }

        /// <summary>
        /// Adds Carter to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add Carter to.</param>
        /// <param name="assemblies">Optional array of <see cref="Assembly"/> to add to the services collection. If assemblies are not provided, Assembly.GetEntryAssembly is called.</param>
        /// <param name="loggerFactory">Optional <see cref="ILoggerFactory"/> to be passed for tracing of Carter setup</param>
        public static void AddCarter(this IServiceCollection services, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(CarterExtensions));
            AddCarter(services, logger);
        }

        /// <summary>
        /// Adds Carter to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add Carter to.</param>
        /// <param name="assemblies">Optional array of <see cref="Assembly"/> to add to the services collection. If assemblies are not provided, Assembly.GetEntryAssembly is called.</param>
        /// <param name="logger">Optional <see cref="ILogger"/> to be passed for tracing of Carter setup</param>
        public static void AddCarter(this IServiceCollection services, ILogger logger = null)
        {
            var assemblyCatalog = new DependencyContextAssemblyCatalog();

            var assemblies = assemblyCatalog.GetAssemblies();

            var validators = assemblies.SelectMany(ass => ass.GetTypes())
                .Where(typeof(IValidator).IsAssignableFrom)
                .Where(t => !t.GetTypeInfo().IsAbstract);

            foreach (var validator in validators)
            {
                logger?.LogTrace($"Found {validator.FullName}");
                services.AddSingleton(typeof(IValidator), validator);
            }

            services.AddSingleton<IValidatorLocator, DefaultValidatorLocator>();

            services.AddRouting();

            var modules = assemblies.SelectMany(x => x.GetTypes()
                .Where(t =>
                    !t.IsAbstract &&
                    typeof(CarterModule).IsAssignableFrom(t) &&
                    t != typeof(CarterModule) &&
                    t.IsPublic
                ));

            foreach (var module in modules)
            {
                logger?.LogTrace("Found {ModuleName}", module.FullName);
            
                services.AddScoped(module);
                services.AddScoped(typeof(CarterModule), module);
            }

            var schs = assemblies.SelectMany(x => x.GetTypes().Where(t => typeof(IStatusCodeHandler).IsAssignableFrom(t) && t != typeof(IStatusCodeHandler)));
            foreach (var sch in schs)
            {
                logger?.LogTrace("Found {StatusCodeHandlerName}", sch.FullName);
                services.AddScoped(typeof(IStatusCodeHandler), sch);
            }

            var responseNegotiators = assemblies.SelectMany(x => x.GetTypes().Where(t => typeof(IResponseNegotiator).IsAssignableFrom(t) && t != typeof(IResponseNegotiator)));
            foreach (var negotiatator in responseNegotiators)
            {
                logger?.LogTrace("Found {ResponseNegotiatorName}", negotiatator.FullName);
                services.AddSingleton(typeof(IResponseNegotiator), negotiatator);
            }

            services.AddSingleton<IResponseNegotiator, DefaultJsonResponseNegotiator>();
        }
    }
}

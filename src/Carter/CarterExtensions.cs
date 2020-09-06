namespace Carter
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Carter.ModelBinding;
    using Carter.OpenApi;
    using Carter.Response;
    using FluentValidation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static OpenApi.CarterOpenApi;

    public static class CarterExtensions
    {
        /// <summary>
        /// Adds Carter to the specified <see cref="IApplicationBuilder"/>.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> to configure.</param>
        /// <param name="options">A <see cref="CarterOptions"/> instance.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IEndpointConventionBuilder MapCarter(this IEndpointRouteBuilder builder)
        {
            var carterConfigurator = builder.ServiceProvider.GetService<CarterConfigurator>();

            var loggerFactory = builder.ServiceProvider.GetService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(typeof(CarterConfigurator));

            carterConfigurator.LogDiscoveredCarterTypes(logger);

            var builders = new List<IEndpointConventionBuilder>();

            var routeMetaData = new Dictionary<(string verb, string path), IRouteMetaData>();

            //Create a "startup scope" to resolve modules from
            using (var scope = builder.ServiceProvider.CreateScope())
            {
                var statusCodeHandlers = scope.ServiceProvider.GetServices<IStatusCodeHandler>().ToList();

                var carterLogger = loggerFactory.CreateLogger("Carter");

                //Get all instances of CarterModule to fetch and register declared routes
                foreach(var module in scope.ServiceProvider.GetServices<CarterModule>()) {
                    routeMetaData = routeMetaData.Concat(module.RouteMetaData).ToDictionary(x => x.Key, x => x.Value);

                    foreach (var route in module.Routes)
                    {
                        var conventionBuilder = builder.MapMethods(route.Key.path, new[] { route.Key.verb },
                            CreateRouteHandler(route.Key.path, module.GetType(), statusCodeHandlers, carterLogger));

                        if (module.AuthPolicies.Any())
                        {
                            conventionBuilder.RequireAuthorization(module.AuthPolicies);
                        }
                        else if (module.RequiresAuth)
                        {
                            conventionBuilder.RequireAuthorization();
                        }

                        route.Value.conventions.Apply(conventionBuilder);
                        builders.Add(conventionBuilder);
                    }
                }
            }

            var options = builder.ServiceProvider.GetRequiredService<IOptions<CarterOptions>>().Value;
            if (options.OpenApi.Enabled)
            {
                builders.Add(builder.MapGet("openapi", BuildOpenApiResponse(options, routeMetaData)));
            }

            return new CompositeConventionBuilder(builders);
        }

        private static RequestDelegate CreateRouteHandler(
            string path, Type moduleType, IEnumerable<IStatusCodeHandler> statusCodeHandlers, ILogger logger)
        {
            return async ctx =>
            {
                // Now in per-request scope
                var module = ctx.RequestServices.GetRequiredService(moduleType) as CarterModule;

                if (!module.Routes.TryGetValue((ctx.Request.Method, path), out var routeHandler))
                {
                    // if the path was registered but a handler matching the
                    // current method was not found, return MethodNotFound
                    // ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    throw new Exception();
                }

                // run the module handlers
                bool shouldContinue = true;

                if (module.Before != null)
                {
                    foreach (var beforeDelegate in module.Before.GetInvocationList())
                    {
                        var beforeTask = (Task<bool>)beforeDelegate.DynamicInvoke(ctx);
                        shouldContinue = await beforeTask;
                        if (!shouldContinue)
                        {
                            break;
                        }
                    }
                }

                if (shouldContinue)
                {
                    // run the route handler
                    logger.LogDebug("Executing {ModuleType} route handler for {Method} {Path}", 
                        moduleType.Namespace, ctx.Request.Method, path);
                    await routeHandler.handler(ctx);

                    // run after handler
                    if (module.After != null)
                    {
                        await module.After(ctx);
                    }
                }

                // run status code handler
                var scHandler = statusCodeHandlers.FirstOrDefault(x => x.CanHandle(ctx.Response.StatusCode));
                if (scHandler != null)
                {
                    await scHandler.Handle(ctx);
                }
            };
        }

        /// <summary>
        /// Adds Carter to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add Carter to.</param>
        /// <param name="assemblyCatalog">Optional <see cref="DependencyContextAssemblyCatalog"/> containing assemblies to add to the services collection. If not provided, the default catalog of assemblies is added, which includes Assembly.GetEntryAssembly.</param>
        /// <param name="configurator">Optional <see cref="CarterConfigurator"/> to enable registration of specific types within Carter</param>
        public static void AddCarter(this IServiceCollection services, Action<CarterOptions> options = null, DependencyContextAssemblyCatalog assemblyCatalog = null,
            Action<CarterConfigurator> configurator = null)
        {
            assemblyCatalog ??= new DependencyContextAssemblyCatalog();

            var config = new CarterConfigurator();
            configurator?.Invoke(config);

            options ??= carterOptions => { };

            WireupCarter(services, assemblyCatalog, config, options);
        }

        private static void WireupCarter(this IServiceCollection services, DependencyContextAssemblyCatalog assemblyCatalog, CarterConfigurator carterConfigurator, Action<CarterOptions> options)
        {
            var assemblies = assemblyCatalog.GetAssemblies();

            var validators = GetValidators(carterConfigurator, assemblies);

            var modules = GetModules(carterConfigurator, assemblies);

            var statusCodeHandlers = GetStatusCodeHandlers(carterConfigurator, assemblies);

            var responseNegotiators = GetResponseNegotiators(carterConfigurator, assemblies);

            var modelBinder = GetModelBinder(carterConfigurator, assemblies);

            services.Configure(options);
            
            services.AddSingleton(carterConfigurator);

            foreach (var validator in validators)
            {
                services.AddSingleton(typeof(IValidator), validator);
            }

            services.AddSingleton<IValidatorLocator, DefaultValidatorLocator>();

            services.AddRouting();

            foreach (var module in modules)
            {
                services.AddScoped(module);
                services.AddScoped(typeof(CarterModule), module);
            }

            foreach (var sch in statusCodeHandlers)
            {
                services.AddScoped(typeof(IStatusCodeHandler), sch);
            }

            foreach (var negotiator in responseNegotiators)
            {
                services.AddSingleton(typeof(IResponseNegotiator), negotiator);
            }

            if (modelBinder != null)
            {
                services.AddSingleton(typeof(IModelBinder), modelBinder);
            }
            else
            {
                services.AddSingleton<IModelBinder, DefaultJsonModelBinder>();
            }

            services.AddSingleton<IResponseNegotiator, DefaultJsonResponseNegotiator>();
        }

        private static Type GetModelBinder(CarterConfigurator carterConfigurator, IReadOnlyCollection<Assembly> assemblies)
        {
            Type modelBinder;
            if (carterConfigurator.ModelBinder == null)
            {
                modelBinder = assemblies.SelectMany(x => x.GetTypes()
                    .Where(t =>
                        !t.IsAbstract &&
                        typeof(IModelBinder).IsAssignableFrom(t) &&
                        t != typeof(IModelBinder) &&
                        t != typeof(NewtonsoftJsonModelBinder) &&
                        t != typeof(DefaultJsonModelBinder)
                    )).FirstOrDefault();

                carterConfigurator.ModelBinder = modelBinder;
            }
            else
            {
                modelBinder = carterConfigurator.ModelBinder;
            }

            return modelBinder;
        }

        private static IEnumerable<Type> GetResponseNegotiators(CarterConfigurator carterConfigurator, IReadOnlyCollection<Assembly> assemblies)
        {
            IEnumerable<Type> responseNegotiators;
            if (!carterConfigurator.ResponseNegotiatorTypes.Any())
            {
                responseNegotiators = assemblies.SelectMany(x => x.GetTypes()
                    .Where(t =>
                        !t.IsAbstract &&
                        typeof(IResponseNegotiator).IsAssignableFrom(t) &&
                        t != typeof(IResponseNegotiator) &&
                        t != typeof(DefaultJsonResponseNegotiator) &&
                        t != typeof(NewtonsoftJsonResponseNegotiator)
                    ));

                carterConfigurator.ResponseNegotiatorTypes.AddRange(responseNegotiators);
            }
            else
            {
                responseNegotiators = carterConfigurator.ResponseNegotiatorTypes;
            }

            return responseNegotiators;
        }

        private static IEnumerable<Type> GetStatusCodeHandlers(CarterConfigurator carterConfigurator, IReadOnlyCollection<Assembly> assemblies)
        {
            IEnumerable<Type> statusCodeHandlers;
            if (!carterConfigurator.StatusCodeHandlerTypes.Any())
            {
                statusCodeHandlers = assemblies.SelectMany(x =>
                    x.GetTypes().Where(t =>
                        typeof(IStatusCodeHandler).IsAssignableFrom(t) &&
                        t != typeof(IStatusCodeHandler)));

                carterConfigurator.StatusCodeHandlerTypes.AddRange(statusCodeHandlers);
            }
            else
            {
                statusCodeHandlers = carterConfigurator.StatusCodeHandlerTypes;
            }

            return statusCodeHandlers;
        }

        private static IEnumerable<Type> GetModules(CarterConfigurator carterConfigurator, IReadOnlyCollection<Assembly> assemblies)
        {
            IEnumerable<Type> modules;
            if (!carterConfigurator.ModuleTypes.Any())
            {
                modules = assemblies.SelectMany(x => x.GetTypes()
                    .Where(t =>
                        !t.IsAbstract &&
                        typeof(CarterModule).IsAssignableFrom(t) &&
                        t != typeof(CarterModule) &&
                        t.IsPublic
                    ));

                carterConfigurator.ModuleTypes.AddRange(modules);
            }
            else
            {
                modules = carterConfigurator.ModuleTypes;
            }

            return modules;
        }

        private static IEnumerable<Type> GetValidators(CarterConfigurator carterConfigurator, IReadOnlyCollection<Assembly> assemblies)
        {
            IEnumerable<Type> validators;
            if (!carterConfigurator.ValidatorTypes.Any())
            {
                validators = assemblies.SelectMany(ass => ass.GetTypes())
                    .Where(typeof(IValidator).IsAssignableFrom)
                    .Where(t => !t.GetTypeInfo().IsAbstract);

                carterConfigurator.ValidatorTypes.AddRange(validators);
            }
            else
            {
                validators = carterConfigurator.ValidatorTypes;
            }

            return validators;
        }

        private class CompositeConventionBuilder : IEndpointConventionBuilder
        {
            private readonly List<IEndpointConventionBuilder> _builders;

            public CompositeConventionBuilder(List<IEndpointConventionBuilder> builders)
            {
                _builders = builders;
            }

            public void Add(Action<EndpointBuilder> convention)
            {
                foreach (var builder in _builders)
                {
                    builder.Add(convention);
                }
            }
        }
    }
}

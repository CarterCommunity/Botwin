namespace Carter
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Carter.OpenApi;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;

    /// <summary>
    /// A class for defining routes in your Carter application
    /// </summary>
    public class CarterModule
    {
        internal readonly Dictionary<(string verb, string path), (RequestDelegate handler, RouteConventions conventions)> Routes;

        internal readonly Dictionary<(string verb, string path), RouteMetaData> RouteMetaData;

        private readonly string basePath;

        internal bool RequiresAuth { get; set; }

        internal string[] AuthPolicies { get; set; } = Array.Empty<string>();

        /// <summary>
        /// A handler that can be invoked before the defined route
        /// </summary>
        public Func<HttpContext, Task<bool>> Before { get; set; }

        /// <summary>
        /// A handler that can be invoked after the defined route
        /// </summary>
        public RequestDelegate After { get; protected set; }

        /// <summary>
        /// Initializes a new instance of <see cref="CarterModule"/>
        /// </summary>
        protected CarterModule() : this(string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CarterModule"/>
        /// </summary>
        /// <param name="basePath">A base path to group routes in your <see cref="CarterModule"/></param>
        protected CarterModule(string basePath)
        {
            this.Routes = new Dictionary<(string verb, string path), (RequestDelegate handler, RouteConventions conventions)>(RouteComparer.Comparer);
            this.RouteMetaData = new Dictionary<(string verb, string path), RouteMetaData>(RouteComparer.Comparer);

            this.basePath = basePath;
        }

        /// <summary>
        /// Declares a route for GET requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Get(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Get(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for GET requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Get(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();
            this.Routes.Add((HttpMethods.Get, path), (handler, conventions));
            this.Routes.Add((HttpMethods.Head, path), (handler, conventions));
            return conventions;
        }

        /// <summary>
        /// Declares a route for GET requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Get<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Get<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for GET requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Get<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();
            this.Routes.Add((HttpMethods.Get, path), (handler, conventions));
            this.Routes.Add((HttpMethods.Head, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Get, path), Activator.CreateInstance<T>());
            return conventions;
        }

        /// <summary>
        /// Declares a route for POST requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Post(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Post(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for POST requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Post(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Post, path), (handler, conventions));
            return conventions;
        }

        /// <summary>
        /// Declares a route for POST requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Post<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Post<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for POST requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Post<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Post, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Post, path), Activator.CreateInstance<T>());

            return conventions;
        }

        /// <summary>
        /// Declares a route for DELETE requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Delete(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Delete(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for DELETE requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Delete(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Delete, path), (handler, conventions));

            return conventions;
        }

        /// <summary>
        /// Declares a route for DELETE requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Delete<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Delete<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for DELETE requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Delete<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Delete, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Delete, path), Activator.CreateInstance<T>());

            return conventions;
        }

        /// <summary>
        /// Declares a route for PUT requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Put(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Put(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for PUT requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Put(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Put, path), (handler, conventions));

            return conventions;
        }

        /// <summary>
        /// Declares a route for PUT requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Put<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Put<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for PUT requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Put<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Put, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Put, path), Activator.CreateInstance<T>());

            return conventions;
        }

        /// <summary>
        /// Declares a route for HEAD requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Head(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Head(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for HEAD requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Head(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Head, path), (handler, conventions));
            return conventions;
        }

        /// <summary>
        /// Declares a route for HEAD requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Head<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Head<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for HEAD requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Head<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Head, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Head, path), Activator.CreateInstance<T>());
            return conventions;
        }

        /// <summary>
        /// Declares a route for PATCH requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Patch(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);

            return this.Patch(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for PATCH requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Patch(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Patch, path), (handler, conventions));

            return conventions;
        }

        /// <summary>
        /// Declares a route for PATCH requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Patch<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Patch<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for PATCH requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Patch<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Patch, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Patch, path), Activator.CreateInstance<T>());
            return conventions;
        }

        /// <summary>
        /// Declares a route for OPTIONS requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Options(string path, Func<HttpRequest, HttpResponse, Task> handler)
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Options(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for OPTIONS requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        protected IEndpointConventionBuilder Options(string path, RequestDelegate handler)
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Options, path), (handler, conventions));
            return conventions;
        }

        /// <summary>
        /// Declares a route for OPTIONS requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Options<T>(string path, Func<HttpRequest, HttpResponse, Task> handler) where T : RouteMetaData
        {
            Task RequestDelegate(HttpContext httpContext) => handler(httpContext.Request, httpContext.Response);
            return this.Options<T>(path, RequestDelegate);
        }

        /// <summary>
        /// Declares a route for OPTIONS requests
        /// </summary>
        /// <param name="path">The path for your route</param>
        /// <param name="handler">The handler that is invoked when the route is hit</param>
        /// <typeparam name="T">The <see cref="OpenApi.RouteMetaData"/> implementation for OpenApi use</typeparam>
        protected IEndpointConventionBuilder Options<T>(string path, RequestDelegate handler) where T : RouteMetaData
        {
            path = this.PrependBasePath(path);
            var conventions = new RouteConventions();

            this.Routes.Add((HttpMethods.Options, path), (handler, conventions));

            this.RouteMetaData.Add((HttpMethods.Options, path), Activator.CreateInstance<T>());
            return conventions;
        }

        private string PrependBasePath(string path)
        {
            if (string.IsNullOrEmpty(this.basePath))
            {
                return path;
            }

            return $"{this.basePath}{path}";
        }

        /// <summary>
        /// Case-insensitive comparer for routes.
        /// </summary>
        private class RouteComparer : IEqualityComparer<(string verb, string path)>
        {
            /// <summary>
            /// Shared comparer instance.
            /// </summary>
            public static RouteComparer Comparer = new RouteComparer();

            public bool Equals((string verb, string path) x, (string verb, string path) y)
                => StringComparer.OrdinalIgnoreCase.Equals(x.verb, y.verb) && StringComparer.OrdinalIgnoreCase.Equals(x.path, y.path);

            public int GetHashCode((string verb, string path) obj)
                => (StringComparer.OrdinalIgnoreCase.GetHashCode(obj.verb), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.path)).GetHashCode();
        }
    }
}

namespace Carter.OpenApi
{
    using System;
    using System.Net.Http;

    /// <summary>
    /// A class to supply OpenApi data
    /// </summary>
    public interface IRouteMetaData
    {
        /// <summary>
        /// A description about the route
        /// </summary>
        string Description { get; }

        /// <summary>
        /// A tag to group routes by
        /// </summary>
        string Tag { get; }

        /// <summary>
        /// An array of <see cref="RouteMetaDataRequest"/>s that must be sent.
        /// </summary>
        RouteMetaDataRequest[] Requests { get; }

        /// <summary>
        /// An array of possible <see cref="RouteMetaDataResponse"/>s that can be returned from the route
        /// </summary>
        RouteMetaDataResponse[] Responses { get; }
        
        /// <summary>
        /// A unique identifier for the route
        /// </summary>
        string OperationId { get; }

        /// <summary>
        /// The name of a security schema used in the API
        /// </summary>
       string SecuritySchema { get; set; }
        
        /// <summary>
        /// An array of possible <see cref="QueryStringParameter"/>s that a route may use
        /// </summary>
        QueryStringParameter[] QueryStringParameter { get; }

        /// <summary>
        /// The <see cref="HttpContent"/> type associated with a request.
        /// </summary>
        Type Content { get; }
    }
}
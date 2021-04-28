namespace Carter.Request
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.AspNetCore.Http;

    public static class QueryStringExtensions
    {
        /// <summary>
        /// Retrieve strongly typed query parameter value for given key
        /// </summary>
        /// <typeparam name="T">Query param type</typeparam>
        /// <param name="query"><see cref="IQueryCollection"/></param>
        /// <param name="key">Query param key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>Query parameter value</returns>
        public static T As<T>(this IQueryCollection query, string key, T defaultValue = default)
        {
            var vals = query[key];
            var propertyType = typeof(T);

            if (propertyType.IsArray() || propertyType.IsCollection() || propertyType.IsEnumerable())
            {
                var colType = propertyType.GetElementType();
                if (colType == null)
                {
                    colType = propertyType.GetGenericArguments().First();
                }

                var convertedvalues = new List<object>();
                foreach (var stringValue in vals)
                {
                    var f = stringValue.ConvertTo(colType, defaultValue);
                    convertedvalues.Add(f);
                }

                var ll = Array.CreateInstance(colType, convertedvalues.Count);
                Array.Copy(convertedvalues.ToArray(), ll, convertedvalues.Count);

                return (T)(object)ll;
            }

            var value = vals.FirstOrDefault();

            if (value == null)
            {
                return defaultValue;
            }

            return value.ConvertTo<T>();
        }

        /// <summary>
        /// Retrieve strongly typed query parameter values for given key
        /// </summary>
        /// <typeparam name="T">Query param type</typeparam>
        /// <param name="query"><see cref="IQueryCollection"/></param>
        /// <param name="key">Query param key</param>
        /// <returns><see cref="IEnumerable{T}"/></returns>
        public static IEnumerable<T> AsMultiple<T>(this IQueryCollection query, string key)
        {
            var values = query[key];

            var splitValues = values.SelectMany(x => x.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

            return splitValues.ConvertMultipleTo<T>();
        }
    }
}

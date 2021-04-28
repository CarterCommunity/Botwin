namespace Carter.Request
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;

    internal static class ConvertExtensions
    {
        internal static object ConvertTo(this object value, Type type, object defaultValue = default)
        {
            if (value != null)
            {
                try
                {
                    var currentType = value.GetType();
                    var newType = type;

                    if (currentType.IsAssignableFrom(newType))
                    {
                        return value;
                    }

                    var stringValue = value as string;

                    if (newType == typeof(DateTime))
                    {
                        if (DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dateResult))
                        {
                            return (object)dateResult;
                        }

                        return defaultValue;
                    }

                    if (stringValue != null)
                    {
                        var converter = TypeDescriptor.GetConverter(newType);

                        if (converter.CanConvertFrom(typeof(string)))
                        {
                            return converter.ConvertFromInvariantString(stringValue);
                        }

                        return defaultValue;
                    }

                    var underlyingType = Nullable.GetUnderlyingType(newType) ?? newType;

                    return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }
        
        internal static T ConvertTo<T>(this object value, T defaultValue = default)
        {
            var newType = typeof(T);
            var result = ConvertTo(value, newType, defaultValue);
            return (T)result;
        }

        internal static IEnumerable<T> ConvertMultipleTo<T>(this IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                yield return ConvertTo<T>(value);
            }
        }

        internal static bool IsArray(this Type source)
        {
            return source.BaseType == typeof(Array);
        }

        internal static bool IsCollection(this Type source)
        {
            var collectionType = typeof(ICollection<>);

            return source.IsGenericType && source
                .GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == collectionType);
        }

        internal static bool IsEnumerable(this Type source)
        {
            var enumerableType = typeof(IEnumerable<>);

            return source.IsGenericType && source.GetGenericTypeDefinition() == enumerableType;
        }
    }
}

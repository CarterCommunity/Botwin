namespace Carter.OpenApi
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Threading.Tasks;
    using Carter.Request;
    using FluentValidation.Validators;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.AspNetCore.Routing.Template;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.OpenApi;
    using Microsoft.OpenApi.Any;
    using Microsoft.OpenApi.Extensions;
    using Microsoft.OpenApi.Models;

    internal static class CarterOpenApi
    {
        internal static RequestDelegate BuildOpenApiResponse(CarterOptions options, Dictionary<(string verb, string path), IRouteMetaData> metaDatas)
        {
            return context =>
            {
                var schemaNavigation = ReadSchemaInformation(metaDatas);
                if (!UniqueShortNames(schemaNavigation))
                {
                    // Cannot generate unique names.  Therefore, exit with an error.   
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Task.CompletedTask;
                }
                var document = CreateDocument(options);
                AddSchema(schemaNavigation, document, context);
                AddSecurityInformation(options, document);
                AddPaths(schemaNavigation, metaDatas, document, context);
                
                var syncIOFeature = context.Features.Get<IHttpBodyControlFeature>();

                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }
                
                context.Response.ContentType = "application/json; charset=utf-8";
                document.SerializeAsJson(context.Response.Body, OpenApiSpecVersion.OpenApi3_0);
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// A function to read schema information from the provided RouteMetaData into a schema navigation map.
        /// </summary>
        /// <param name="metaDatas">Input information read from definitions and meta data classes.</param>
        /// <returns>A map that contains unique SchemaElements.  These SchemaElements may reference each other.  The keys of the map are the unique long names.</returns>
        private static Dictionary<string, SchemaElement> ReadSchemaInformation(Dictionary<(string verb, string path), IRouteMetaData> metaDatas)
        {
            var navigation = new Dictionary<string, SchemaElement>();
            foreach (var routeMetaData in metaDatas)
            {
                if (routeMetaData.Value.Requests != null)
                {
                    foreach (var request in routeMetaData.Value.Requests)
                    {
                        ReadTypeInformation(request.Request, navigation);
                    }
                }
                if (routeMetaData.Value.Responses != null)
                {
                    foreach (var response in routeMetaData.Value.Responses)
                    {
                        ReadTypeInformation(response.Response, navigation);
                    }
                }
            }
            return navigation;
        }

        /// <summary>
        /// A function to read information from types and populate a schema navigation map.
        /// </summary>
        /// <param name="navigation">A map of SchemaElements that are in a hierarchical structure.  The keys of the map are the unique long names.</param>
        /// <param name="type">The input class type.</param>
        private static void ReadTypeInformation(Type type, Dictionary<string, SchemaElement> navigation)
        {
            if (type == null)
            {
                return;
            }

            // Build the unique long name and the short name.
            var fullName = type.FullName;
            var shortName = type.Name;
            var pos = shortName.IndexOf("`", System.StringComparison.Ordinal);
            if (pos != -1)
            {
                shortName = shortName.Substring(0, pos);
            }

            // If this is a known type.
            if (navigation.ContainsKey(fullName))
            {
                return;
            }

            var schemaElement = new SchemaElement
            {
                FullName = fullName,
                ShortName = shortName,
                ElementType = type
            };

            // Prevent the navigation from including the properties of a string.
            if (fullName == "System.String")
            {
                navigation.Add(fullName, schemaElement);
                return;
            }

            // Prevent the navigation from including the properties of a simple nullable type.
            if (schemaElement.IsSimpleNullable())
            {
                schemaElement.ShortName = "NullableOf" + Nullable.GetUnderlyingType(schemaElement.ElementType).Name;
                navigation.Add(fullName, schemaElement);
                return;
            }

            // Add this first to avoid infinite recursion.
            navigation.Add(fullName, schemaElement);

            foreach (var genericType in type.GenericTypeArguments)
            {
                ReadTypeInformation(genericType, navigation);
                var genericTypeElement = navigation[genericType.FullName];
                navigation[fullName].GenericTypes.Add(genericTypeElement);
            }

            // Arrays do not have GenericTypeArguments.
            if (navigation[fullName].IsArray() && navigation[fullName].GenericTypes.Count() == 0)
            {
                var elementType = type.GetElementType();
                ReadTypeInformation(elementType, navigation);
                var elementTypeElement = navigation[elementType.FullName];
                navigation[fullName].GenericTypes.Add(elementTypeElement);
            }

            foreach (var propertyInfo in type.GetProperties())
            {
                ReadTypeInformation(propertyInfo.PropertyType, navigation);
                var propertyTypeElement = navigation[propertyInfo.PropertyType.FullName];
                navigation[fullName].DataMembers.Add(CamelCase(propertyInfo.Name), propertyTypeElement);
            }
        }

        /// <summary>
        /// A function to check the short names for the classes and preappend sections of the namespace if needed.
        /// </summary>
        /// <param name="navigation">A map of SchemaElements that are in a hierarchical structure.  The keys of the map are the unique long names.</param>
        /// <returns>True if the short names are or can be made unique and false if this operation false.</returns>
        private static bool UniqueShortNames(Dictionary<string, SchemaElement> navigation)
        {
            // Attempt to remove duplicate short names by preappending minimal namepace sections.
            // The generic classes are not included, since they need to be considered after being flatterned.
            var (foundDuplicates, longNamesAffected, newShortNames) = DetectDuplicateNames(navigation, checkGeneric: false);
            if (!foundDuplicates)
            {
                if (!CorrectShortNames(navigation, longNamesAffected, newShortNames))
                {
                    return false;
                }
            }
            FlatternGenericNames(navigation);
            (foundDuplicates, longNamesAffected, newShortNames) = DetectDuplicateNames(navigation, checkGeneric: true);
            if (!foundDuplicates)
            {
                if (!CorrectShortNames(navigation, longNamesAffected, newShortNames))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A function to detect duplicate short names.
        /// </summary>
        /// <param name="navigation">The input map of long names and SchemaElements.</param>
        /// <param name="checkGeneric">A bool to include generic classes within this test.</param>
        /// <returns>A bool to indicate if duplicate names were deletected, together with affected names and initial short names.</returns>
        private static (bool,
            Dictionary<string, List<string>>,
            Dictionary<string, List<string>>) DetectDuplicateNames(Dictionary<string, SchemaElement> navigation, bool checkGeneric = false)
        {
            // Find names that are not unique
            var shortNames = navigation.Values.Select(o => o.ShortName).ToList();
            var longNamesAffected = new Dictionary<string, List<string>>();
            var newShortNames = new Dictionary<string, List<string>>();
            foreach (var keyValuePair in navigation)
            {
                // Skip generic classes if requested.
                if (!checkGeneric && keyValuePair.Value.IsGeneric())
                {
                    continue;
                }

                // Exclude short names that are unique.
                if (shortNames.Where(o => o.Equals(keyValuePair.Value.ShortName)).Count() == 1)
                {
                    continue;
                }

                // Record the namespace fragments of the affected long names.
                var namespaceString = keyValuePair.Value.ElementType.Namespace;
                var fragments = new List<string>();
                if (!string.IsNullOrWhiteSpace(namespaceString))
                {
                    if (!namespaceString.Contains("."))
                    {
                        fragments.Add(namespaceString);
                    }
                    else
                    {
                        fragments.AddRange(namespaceString.Split('.').ToList());
                    }
                }
                longNamesAffected.Add(keyValuePair.Key, fragments);

                // Store this ready to append namespace fragments.
                newShortNames.Add(keyValuePair.Key, new List<string>
                {
                    keyValuePair.Value.ShortName
                });
            }
            var foundDuplicates = false;
            if (longNamesAffected.Count() == 0)
            {
                foundDuplicates = true;
            }
            return (foundDuplicates, longNamesAffected, newShortNames);
        }

        /// <summary>
        /// A function to attempt to rename the short names, such that they are unique.  Name space sections
        /// are prepended to try to create unique simple names.
        /// </summary>
        /// <param name="navigation">The input map of long names and SchemaElements.</param>
        /// <param name="longNamesAffected">The long names that are associated with duplicate short names and their namespace fragments.</param>
        /// <param name="newShortNames">The new short names without prepended namespace information.</param>
        /// <returns>False if the attempt to correct the short names was not successful.</returns>
        private static bool CorrectShortNames(Dictionary<string, SchemaElement> navigation,
            Dictionary<string, List<string>> longNamesAffected,
            Dictionary<string, List<string>> newShortNames)
        {
            // Attempt to correct the short names
            var success = false;
            for (int i = 0; i < 5; i++)
            {
                foreach (var keyValuePair in newShortNames)
                {
                    var nAvailable = longNamesAffected[keyValuePair.Key].Count(); // Contains namespace.
                    var nUsed = keyValuePair.Value.Count() - 1; // Contains short name and then namespace.

                    // If there are no namespace fragments, then this short name cannot be corrected.
                    if (nAvailable == 0)
                    {
                        continue;
                    }

                    // If all of the namespace elements have been preappended, then no more changes are available.
                    if (nAvailable == nUsed)
                    {
                        continue;
                    }

                    // Preappend the next namespace element.
                    keyValuePair.Value.Insert(0, longNamesAffected[keyValuePair.Key][nAvailable - nUsed - 1]);
                }

                // Test if the names are now unique
                var updatedShortNames = new List<string>();
                foreach (var nameFragments in newShortNames)
                {
                    updatedShortNames.Add(string.Join("_", nameFragments.Value));
                }
                if (updatedShortNames.Distinct().Count() == updatedShortNames.Count())
                {
                    // Update the short names.
                    foreach (var keyValuePair in newShortNames)
                    {
                        navigation[keyValuePair.Key].ShortName = string.Join("_", keyValuePair.Value);
                    }
                    success = true;
                    break;
                }
            }
            return success;
        }

        /// <summary>
        /// Correct any generic class names to include the type names following the generic name.
        /// The form is Generic<T,M> becomes GenericOfTAndM.
        /// </summary>
        /// <param name="navigation">The input map of long names and SchemaElements.</param>
        private static void FlatternGenericNames(Dictionary<string, SchemaElement> navigation)
        {
            foreach (var keyValuePair in navigation)
            {
                if (!keyValuePair.Value.IsGeneric())
                {
                    continue;
                }
                var genericSuffix = string.Empty;
                foreach (var shortName in keyValuePair.Value.GenericTypes.Select(o => o.ShortName))
                {
                    if (genericSuffix.Length == 0)
                    {
                        genericSuffix = "Of";
                    }
                    else
                    {
                        genericSuffix = "And";
                    }
                    genericSuffix += shortName;
                }
                keyValuePair.Value.ShortName += genericSuffix;
            }
        }

        /// <summary>
        /// Create a basic OpenApiDocument object.
        /// </summary>
        /// <param name="options">The input Carter options.</param>
        /// <returns>A basic OpenApiDocument.</returns>
        private static OpenApiDocument CreateDocument(CarterOptions options)
        {
            return new OpenApiDocument
            {
                Info = new OpenApiInfo
                {
                    Version = options.OpenApi.Version,
                    Title = options.OpenApi.DocumentTitle
                },
                Servers = new List<OpenApiServer>(options.OpenApi.ServerUrls.Select(x => new OpenApiServer { Url = x })),
                Paths = new OpenApiPaths(),
                Components = new OpenApiComponents()
            };
        }

        /// <summary>
        /// Fill the OpenApiDocument document with the schema definitions for objects.
        /// </summary>
        /// <param name="navigation">The input SchemaElements that have been read from the input classes.</param>
        /// <param name="document">The OpenApiDocument that is to be filled with the schema information.</param>
        private static void AddSchema(Dictionary<string, SchemaElement> navigation, OpenApiDocument document, HttpContext context)
        {
            foreach (var keyValuePair in navigation.OrderBy(o => o.Value.ShortName))
            {
                // Skip simple types and array types, since they are not objects.
                if (keyValuePair.Value.IsSimple() || keyValuePair.Value.IsSimpleNullable() || keyValuePair.Value.IsArray())
                {
                    continue;
                }

                var schema = new OpenApiSchema
                {
                    Type = "object"
                };
                
                foreach (var memberKeyValue in keyValuePair.Value.DataMembers)
                {
                    var propertySchema = SchemaFromElement(memberKeyValue.Value);
                    var propertyInfo = keyValuePair.Value.ElementType.GetProperties().Where(o => CamelCase(o.Name) == memberKeyValue.Key).SingleOrDefault();
                    object[] attribute = propertyInfo.GetCustomAttributes(typeof(ApiSchemaAttributes), true);
                    if (attribute.Length > 0)
                    {
                        var myAttribute = (ApiSchemaAttributes)attribute[0];
                        propertySchema.Format = myAttribute.Format;
                    }

                    schema.Properties.Add(memberKeyValue.Key, propertySchema);
                    AddValidationInformation(schema, context, keyValuePair.Value.ElementType);
                }

                document.Components.Schemas.Add(keyValuePair.Value.ShortName, schema);
            }
        }

        /// <summary>
        /// A function to create an OpenApiSchema object from a SchemaElement.
        /// </summary>
        /// <param name="schemaElement">The input information.</param>
        /// <returns>OpenApiSchema filled from input information.</returns>
        private static OpenApiSchema SchemaFromElement(SchemaElement schemaElement)
        {
            if (schemaElement == null)
            {
                return null;
            }

            var schema = new OpenApiSchema();
            if (schemaElement.IsSimple())
            {
                schema.Type = GetOpenApiTypeMapping(schemaElement.ElementType.Name);
            }
            else if (schemaElement.IsSimpleNullable())
            {
                schema.Type = GetOpenApiTypeMapping(Nullable.GetUnderlyingType(schemaElement.ElementType).Name);
                schema.Nullable = true;
            }
            else if (schemaElement.IsArray())
            {
                schema.Type = "array";
                var typeSchema = schemaElement.GenericTypes.FirstOrDefault();
                schema.Items = SchemaFromElement(typeSchema);
            }
            else
            {
                schema.Reference = new OpenApiReference
                {
                    Id = schemaElement.ShortName,
                    Type = ReferenceType.Schema
                };
            }
            return schema;
        }

        private static void AddSecurityInformation(CarterOptions options, OpenApiDocument document)
        {
            foreach (var globalSecurity in options.OpenApi.GlobalSecurityDefinitions)
            {
                var req = new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Id = globalSecurity,
                                Type = ReferenceType.SecurityScheme
                            },
                            UnresolvedReference = true
                        },
                            new List<string>()
                        }
                    };
                document.SecurityRequirements.Add(req);
            }

            foreach (var apiSecurity in options.OpenApi.Securities)
            {
                var scheme = new OpenApiSecurityScheme
                {
                    Name = apiSecurity.Value.Name,
                    Type = (SecuritySchemeType)Enum.Parse(typeof(SecuritySchemeType), apiSecurity.Value.Type.ToString(), ignoreCase: true),
                    Scheme = apiSecurity.Value.Scheme,
                    BearerFormat = apiSecurity.Value.BearerFormat,
                    In = (ParameterLocation)Enum.Parse(typeof(ParameterLocation), apiSecurity.Value.In.ToString(), ignoreCase: true)
                };

                document.Components.SecuritySchemes.Add(apiSecurity.Key, scheme);
            }
        }

        /// <summary>
        /// A function to write the path information into the OpenApiDocument.
        /// </summary>
        /// <param name="navigation">The input schema information.</param>
        /// <param name="metaDatas"></param>
        /// <param name="document">The OpenApiDocument that the path information will be written into.</param>
        /// <param name="context">The HttpContext.</param>
        private static void AddPaths(Dictionary<string, SchemaElement> navigation, Dictionary<(string verb, string path), IRouteMetaData> metaDatas, OpenApiDocument document, HttpContext context)
        {
            foreach (var routeMetaData in metaDatas.GroupBy(pair => pair.Key.path))
            {
                var pathItem = new OpenApiPathItem();

                //Get /foo/{id:int}
                var template = TemplateParser.Parse(routeMetaData.Key);

                //Turn it into /foo/{id}
                var templateName = string.Join("/",
                    template.Segments.Select(x => string.Join(string.Empty,
                        x.Parts.Select(z => !z.IsParameter ? z.Text : "{" + (z.IsCatchAll ? "*" : string.Empty) + z.Name + (z.IsOptional ? "?" : string.Empty) + "}"))));

                var methodRoutes = routeMetaData.Select(x => x);

                foreach (var methodRoute in methodRoutes)
                {
                    var operation = new OpenApiOperation
                    {
                        Description = methodRoute.Value.Description,
                        OperationId = methodRoute.Value.OperationId,
                    };

                    if (!string.IsNullOrWhiteSpace(methodRoute.Value.Tag))
                    {
                        operation.Tags = new List<OpenApiTag>
                        {
                            new OpenApiTag
                            {
                                Name = methodRoute.Value.Tag
                            }
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(methodRoute.Value.SecuritySchema))
                    {
                        operation.Security = new List<OpenApiSecurityRequirement>(new[]
                        {
                                new OpenApiSecurityRequirement
                                {
                                    {
                                        new OpenApiSecurityScheme
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Id = methodRoute.Value.SecuritySchema,
                                            Type = ReferenceType.SecurityScheme
                                        },
                                        UnresolvedReference = true
                                    },
                                        new List<string>()
                                    }
                                }
                            });
                    }

                    CreateOpenApiRequestBody(navigation, document, methodRoute, operation, context);

                    CreateOpenApiResponseBody(navigation, document, methodRoute, operation);

                    CreateOpenApiRouteConstraints(template, operation);

                    CreateOpenApiQueryStringParameters(operation, methodRoute.Value.QueryStringParameter);

                    var verb = CreateOpenApiOperationVerb(methodRoute);

                    pathItem.Operations.Add(verb, operation);
                }

                document.Paths.Add("/" + templateName, pathItem);
            }
        }



        private static void CreateOpenApiQueryStringParameters(OpenApiOperation operation, QueryStringParameter[] queryStringParameters)
        {
            if (queryStringParameters == null || !queryStringParameters.Any())
            {
                return;
            }

            foreach (var queryStringParameter in queryStringParameters)
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    In = ParameterLocation.Query,
                    Required = queryStringParameter.Required,
                    Name = queryStringParameter.Name,
                    Description = queryStringParameter.Description,
                    Schema = new OpenApiSchema
                    {
                        Type = GetOpenApiTypeMapping(queryStringParameter.Type.Name)
                    }
                });
            }
        }

        private static void CreateOpenApiRouteConstraints(RouteTemplate template, OpenApiOperation operation)
        {
            if (template.Parameters.Any())
            {
                operation.Parameters = template.Parameters.Select(x => new OpenApiParameter
                {
                    Name = x.Name,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = GetOpenApiTypeMapping(x.InlineConstraints.FirstOrDefault()?.Constraint)
                    }
                }).ToList();
            }
        }

        private static void CreateOpenApiResponseBody(Dictionary<string, SchemaElement> navigation, OpenApiDocument document, KeyValuePair<(string verb, string path), IRouteMetaData> methodRoute, OpenApiOperation operation)
        {
            if (methodRoute.Value.Responses == null)
            {
                operation.Responses.Add("200", new OpenApiResponse
                {
                    Description = string.Empty
                });
                return;
            }
            foreach (var routeMetaDataResponse in methodRoute.Value.Responses)
            {
                var openApiResponse = CreateOpenApiResponse(navigation, document, routeMetaDataResponse);
                operation.Responses.Add(routeMetaDataResponse.Code.ToString(), openApiResponse);
            }
        }

        private static void CreateOpenApiRequestBody(Dictionary<string, SchemaElement> navigation, OpenApiDocument document, KeyValuePair<(string verb, string path), IRouteMetaData> keyValuePair, OpenApiOperation operation, HttpContext context)
        {
            if (keyValuePair.Key.verb == "GET")
            {
                return;
            }
            if (keyValuePair.Value.Requests == null)
            {
                return;
            }
            if (keyValuePair.Value.Requests.Count() == 0)
            {
                return;
            }

            var requestBody = new OpenApiRequestBody();
            foreach (var requestMetaData in keyValuePair.Value.Requests)
            {
                var openApiMediaType = new OpenApiMediaType();
                if (requestMetaData.Request == null)
                {
                    continue;
                }
                var fullName = requestMetaData.Request.FullName;
            var schemaElement = navigation.Values.Where(o => o.FullName == fullName).FirstOrDefault();
            if (schemaElement == null)
            {
                    continue;
            }
            var schema = SchemaFromElement(schemaElement);

            AddValidationInformation(schema, context, schemaElement.ElementType);
                openApiMediaType.Schema = schema;

                var mediaType = requestMetaData.MediaType;
                if (string.IsNullOrWhiteSpace(mediaType))
            {
                    mediaType = "application/json";
                }

                var format = requestMetaData.Format;
                if (string.IsNullOrWhiteSpace(format))
                {
                    format = "string";
                }

                openApiMediaType.Schema.Format = format;

                if (!requestBody.Content.ContainsKey(mediaType))
                {
                    requestBody.Content.Add(mediaType, openApiMediaType);
                }
            }
            operation.RequestBody = requestBody;
        }

        private static void AddValidationInformation(OpenApiSchema schema, HttpContext context, Type requestType)
        {
            var validatorLocator = context.RequestServices.GetRequiredService<IValidatorLocator>();

            var validator = validatorLocator.GetValidator(requestType);

            if (validator != null)
            {
                var validatorDescriptor = validator.CreateDescriptor();

                //Thanks for the pointers https://github.com/micro-elements/MicroElements.Swashbuckle.FluentValidation
                //TODO Also need to look at request parameters that might be required from the header or querystring for example. MVC does binding on GETs from these sources
                foreach (var key in schema.Properties.Keys)
                {
                    foreach (var propertyValidator in validatorDescriptor
                        .GetValidatorsForMember(ToPascalCase(key)))
                    {
                        if (propertyValidator is INotNullValidator
                            || propertyValidator is INotEmptyValidator)
                        {
                            if (!schema.Required.Contains(key))
                            {
                                schema.Required.Add(key);
                            }

                            if (propertyValidator is INotEmptyValidator)
                            {
                                schema.Properties[key].MinLength = 1;
                            }
                        }

                        if (propertyValidator is ILengthValidator lengthValidator)
                        {
                            if (lengthValidator.Max > 0)
                                schema.Properties[key].MaxLength = lengthValidator.Max;

                            schema.Properties[key].MinLength = lengthValidator.Min;
                        }

                        if (propertyValidator is IComparisonValidator comparisonValidator)
                        {
                            //var comparisonValidator = (IComparisonValidator)context.PropertyValidator;
                            if (comparisonValidator.ValueToCompare.IsNumeric())
                            {
                                var valueToCompare = comparisonValidator.ValueToCompare.NumericToDecimal();
                                var schemaProperty = schema.Properties[key];

                                if (comparisonValidator.Comparison == Comparison.GreaterThanOrEqual)
                                {
                                    schemaProperty.Minimum = valueToCompare;
                                }
                                else if (comparisonValidator.Comparison == Comparison.GreaterThan)
                                {
                                    schemaProperty.Minimum = valueToCompare;
                                    schemaProperty.ExclusiveMinimum = true;
                                }
                                else if (comparisonValidator.Comparison == Comparison.LessThanOrEqual)
                                {
                                    schemaProperty.Maximum = valueToCompare;
                                }
                                else if (comparisonValidator.Comparison == Comparison.LessThan)
                                {
                                    schemaProperty.Maximum = valueToCompare;
                                    schemaProperty.ExclusiveMaximum = true;
                                }
                            }
                        }

                        if (propertyValidator is RegularExpressionValidator expressionValidator)
                        {
                            schema.Properties[key].Pattern = expressionValidator.Expression;
                        }

                        if (propertyValidator is IBetweenValidator betweenValidator)
                        {
                            var schemaProperty = schema.Properties[key];

                            if (betweenValidator.From.IsNumeric())
                            {
                                schemaProperty.Minimum = betweenValidator.From.NumericToDecimal();

                                if (betweenValidator is ExclusiveBetweenValidator)
                                {
                                    schemaProperty.ExclusiveMinimum = true;
                                }
                            }

                            if (betweenValidator.To.IsNumeric())
                            {
                                schemaProperty.Maximum = betweenValidator.To.NumericToDecimal();

                                if (betweenValidator is ExclusiveBetweenValidator)
                                {
                                    schemaProperty.ExclusiveMaximum = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static OpenApiResponse CreateOpenApiResponse(Dictionary<string, SchemaElement> navigation, OpenApiDocument document, RouteMetaDataResponse valueStatusCode)
        {
            var openApiResponse = new OpenApiResponse
            {
                Description = valueStatusCode.Description
            };

            if (valueStatusCode.Response == null)
            {
                return openApiResponse;
            }

            var fullName = valueStatusCode.Response.FullName;
            var schemaElement = navigation.Values.Where(o => o.FullName == fullName).FirstOrDefault();
            if (schemaElement == null)
            {
                return openApiResponse;
            }

            openApiResponse.Content = new Dictionary<string, OpenApiMediaType>
            {
                {
                    "application/json",
                    new OpenApiMediaType
                    {
                        Schema = SchemaFromElement(schemaElement)
                    }
                }
            };
            return openApiResponse;
        }

        /// <summary>
        /// A function to set the first letter in the input string to lower case.
        /// </summary>
        /// <param name="inputString"></param>
        /// <returns>A copy of the inputString with the first letter set to lower case.</returns>
        public static string CamelCase(string inputString)
        {
            if (inputString == null)
            {
                return null;
            }
            if (inputString.Length == 0)
            {
                return inputString;
            }
            if (inputString.Length == 1)
            {
                return inputString.ToLower();
            }
            return Char.ToLowerInvariant(inputString[0]) + inputString.Substring(1);
        }

        public static bool IsNumeric(this object value) =>
            value is sbyte ||
            value is byte ||
            value is short ||
            value is ushort ||
            value is int ||
            value is uint ||
            value is long ||
            value is ulong ||
            value is Int32 ||
            value is UInt32 ||
            value is Int64 ||
            value is UInt64 ||
            value is Int16 ||
            value is UInt16 ||
            value is Single ||
            value is decimal ||
            value is double ||
            value is float;

        /// <summary>
        /// Convert numeric to double.
        /// </summary>
        public static decimal NumericToDecimal(this object value) => Convert.ToDecimal(value);

        private static string ToPascalCase(string inputString)
        {
            // If there are 0 or 1 characters, just return the string.
            if (inputString == null)
            {
                return null;
            }
            if (inputString.Length < 2)
            {
                return inputString.ToUpper();
            }
            return inputString.Substring(0, 1).ToUpper() + inputString.Substring(1);
        }

        private static OperationType CreateOpenApiOperationVerb(KeyValuePair<(string verb, string path), IRouteMetaData> methodRoute)
        {
            switch (methodRoute.Key.verb)
            {
                case "GET":
                    return OperationType.Get;
                case "POST":
                    return OperationType.Post;
                case "PUT":
                    return OperationType.Put;
                case "DELETE":
                    return OperationType.Delete;
                case "HEAD":
                    return OperationType.Head;
                case "OPTIONS":
                    return OperationType.Options;
                case "PATCH":
                    return OperationType.Patch;
                default:
                    return OperationType.Get;
            }
        }

        private static string GetOpenApiTypeMapping(string constraint)
        {
            if (constraint == null)
            {
                return null;
            }

            constraint = constraint.ToLower();
            switch (constraint)
            {
                case "sbyte":
                case "byte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                    return "integer";

                case "single":
                case "float":
                case "double":
                case "decimal":
                    return "number";

                case "boolean":
                case "bool":
                    return "boolean";

                case "datetime":
                    return "string";

                case "string":
                    return "string";

                default:
                    return null;
            }
        }
    }
}

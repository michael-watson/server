using GraphQL.Http;
using GraphQL.Server.Transports.AspNetCore.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace GraphQL.Server.Transports.AspNetCore
{
    public static class GraphQLHttpMiddlewareExtensions
    {
        public static Task WriteBadRequestResponseAsync(this HttpContext context, IDocumentWriter writer, string errorMessage)
        {
            var result = new ExecutionResult()
            {
                Errors = new ExecutionErrors()
                {
                    new ExecutionError(errorMessage)
                }
            };

            var json = writer.Write(result);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 400; // Bad Request

            return context.Response.WriteAsync(json);
        }

        public static Task WriteResponseAsync(this HttpContext context, IDocumentWriter writer, ExecutionResult result)
        {
            var json = writer.Write(result);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200; // OK

            return context.Response.WriteAsync(json);
        }

        public static T Deserialize<T>(this Stream s)
        {
            using (var reader = new StreamReader(s))
            using (var jsonReader = new JsonTextReader(reader))
            {
                return new JsonSerializer().Deserialize<T>(jsonReader);
            }
        }

        public static async Task<string> ReadAsStringAsync(this Stream s)
        {
            using (var reader = new StreamReader(s))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public static void ExtractGraphQLRequestFromQueryString(this IQueryCollection qs, GraphQLRequest gqlRequest)
        {
            gqlRequest.Query = qs.TryGetValue(GraphQLRequest.QueryKey, out var queryValues) ? queryValues[0] : null;
            gqlRequest.Variables = qs.TryGetValue(GraphQLRequest.VariablesKey, out var variablesValues) ? JObject.Parse(variablesValues[0]) : null;
            gqlRequest.OperationName = qs.TryGetValue(GraphQLRequest.OperationNameKey, out var operationNameValues) ? operationNameValues[0] : null;
        }
    }
}

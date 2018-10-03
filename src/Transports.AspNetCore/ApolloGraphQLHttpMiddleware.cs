using GraphQL.Http;
using GraphQL.Instrumentation;
using GraphQL.Server.Internal;
using GraphQL.Server.Transports.AspNetCore.Common;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace GraphQL.Server.Transports.AspNetCore
{
    public class ApolloGraphQLHttpMiddleware<TSchema>
        where TSchema : ISchema
    {
        private const string JsonContentType = "application/json";
        private const string GraphQLContentType = "application/graphql";

        private readonly ILogger _logger;
        private readonly RequestDelegate _next;
        private readonly PathString _path;

        public ApolloGraphQLHttpMiddleware(ILogger<GraphQLHttpMiddleware<TSchema>> logger, RequestDelegate next, PathString path)
        {
            _logger = logger;
            _next = next;
            _path = path;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest || !context.Request.Path.StartsWithSegments(_path))
            {
                await _next(context);
                return;
            }

            // Handle requests as per recommendation at http://graphql.org/learn/serving-over-http/
            var httpRequest = context.Request;
            var gqlRequest = new GraphQLRequest();

            var writer = context.RequestServices.GetRequiredService<IDocumentWriter>();

            if (HttpMethods.IsGet(httpRequest.Method) || (HttpMethods.IsPost(httpRequest.Method) && httpRequest.Query.ContainsKey(GraphQLRequest.QueryKey)))
            {
                httpRequest.Query.ExtractGraphQLRequestFromQueryString(gqlRequest);
            }
            else if (HttpMethods.IsPost(httpRequest.Method))
            {
                if (!MediaTypeHeaderValue.TryParse(httpRequest.ContentType, out var mediaTypeHeader))
                {
                    await context.WriteBadRequestResponseAsync(writer, $"Invalid 'Content-Type' header: value '{httpRequest.ContentType}' could not be parsed.");
                    return;
                }

                switch (mediaTypeHeader.MediaType)
                {
                    case JsonContentType:
                        gqlRequest = httpRequest.Body.Deserialize<GraphQLRequest>();
                        break;
                    case GraphQLContentType:
                        gqlRequest.Query = await httpRequest.Body.ReadAsStringAsync();
                        break;
                    default:
                        await context.WriteBadRequestResponseAsync(writer, $"Invalid 'Content-Type' header: non-supported media type. Must be of '{JsonContentType}' or '{GraphQLContentType}'. See: http://graphql.org/learn/serving-over-http/.");
                        return;
                }
            }

            object userContext = null;
            var userContextBuilder = context.RequestServices.GetService<IUserContextBuilder>();

            if (userContextBuilder != null)
            {
                userContext = await userContextBuilder.BuildUserContext(context);
            }

            var executer = context.RequestServices.GetRequiredService<IGraphQLExecuter<TSchema>>() as DefaultGraphQLExecuter<TSchema>;

            var start = DateTime.Now;
            var result = await executer.ExecuteAsync(
                gqlRequest.OperationName,
                gqlRequest.Query,
                gqlRequest.GetInputs(),
                userContext,
                context.RequestAborted);

            result.EnrichWithApolloTracing(start);

            if (result.Errors != null)
            {
                _logger.LogError("GraphQL execution error(s): {Errors}", result.Errors);
            }

            await context.WriteResponseAsync(writer, result);
        }
    }
}

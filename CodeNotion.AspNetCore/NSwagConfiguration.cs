using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Writers;
using NSwag;
using NSwag.Generation;
using Swashbuckle.AspNetCore.Swagger;

namespace CodeNotion.AspNetCore;

public static class NSwagConfiguration
{
    public static readonly bool IsOpenApiSchemeGenerationRuntime = Environment.StackTrace.Contains(typeof(NSwag.AspNetCore.SwaggerSettings).Namespace!);

    public static IServiceCollection AddNSwag(this IServiceCollection source)
    {
        source.AddTransient<IOpenApiDocumentGenerator, OpenApiDocumentGenerator>();
        return source;
    }

    public class OpenApiDocumentGenerator : IOpenApiDocumentGenerator
    {
        private readonly ISwaggerProvider _provider;

        public OpenApiDocumentGenerator(ISwaggerProvider provider)
        {
            _provider = provider;
        }

        public Task<OpenApiDocument?> GenerateAsync(string documentName)
        {
	        if (SwaggerConfiguration.InternalConfig is null)
	        {
		        throw new InvalidOperationException($"Attempting to set up NSwag Generation without correct service registration. Please register required services by calling services.{nameof(SwaggerConfiguration.AddSwagger)}()");
	        }
	        
            _provider.GetSwagger(SwaggerConfiguration.InternalConfig.ApiVersion, null, "/"); // first execution is faulty
            var doc = _provider.GetSwagger(SwaggerConfiguration.InternalConfig.ApiVersion, null, "/");
            using var streamWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(streamWriter);
            doc.SerializeAsV3(writer);
            var json = streamWriter.ToString();
            return OpenApiDocument.FromJsonAsync(json);
        }
    }
}
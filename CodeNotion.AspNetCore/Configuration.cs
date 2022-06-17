using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace CodeNotion.AspNetCore;

public record SwaggerConfig
{
	public string ApiTitle { get; set; } = "Application Web API";
	public string ApiVersion { get; set; } = "1.0.0";
	public string ApiName { get; set; } = "api";
	public string SecurityScheme { get; set; } = "oauth2";

	public Uri? TokenUrl { get; set; }
	public string[] IgnoredParameterNames { get; set; } = Array.Empty<string>();
}

public static class SwaggerConfiguration
{
	internal static SwaggerConfig? InternalConfig;
	
	public static IServiceCollection AddSwagger(this IServiceCollection source, Action<SwaggerConfig> action)
	{
		var config = new SwaggerConfig();
		action.Invoke(config);
		if (config.TokenUrl is null)
		{
			throw new ArgumentNullException(nameof(SwaggerConfig.TokenUrl));
		}

		InternalConfig = config;
		var scheme = new OpenApiSecurityScheme
		{
			Reference = new OpenApiReference {Id = config.SecurityScheme, Type = ReferenceType.SecurityScheme},
			Flows = new OpenApiOAuthFlows
			{
				Password = new OpenApiOAuthFlow
				{
					Scopes = new Dictionary<string, string>
					{
						{config.ApiName, config.ApiTitle},
					},
					TokenUrl = config.TokenUrl,
					RefreshUrl = config.TokenUrl
				}
			},
			In = ParameterLocation.Header,
			Type = SecuritySchemeType.OAuth2,
			Scheme = "Bearer",
			Name = "Authentication",
			BearerFormat = "Bearer {token}"
		};

		return source.AddSwaggerGen(options =>
		{
			options.SwaggerDoc(config.ApiVersion, new OpenApiInfo {Title = config.ApiTitle, Version = config.ApiVersion});
			options.DocumentFilter<SwaggerExcludeParametersFilter>(new object[] {config.IgnoredParameterNames});
			options.OperationFilter<AuthorizeCheckOperationFilter>(scheme, config);
			options.OperationFilter<SwaggerExcludeFilter>();
			options.OperationFilter<AddOdataParametersTypeFilter>();
			options.SchemaFilter<XEnumNamesSchemaFilter>();
			options.AddSecurityDefinition(config.SecurityScheme, scheme);
			options.CustomOperationIds(description => description.TryGetMethodInfo(out MethodInfo methodInfo) ? $"{methodInfo.DeclaringType!.Name.Replace("Controller", string.Empty)}_{methodInfo.Name}" : null);
		});
	}

	// ReSharper disable once ClassNeverInstantiated.Local
	private sealed class AuthorizeCheckOperationFilter : IOperationFilter
	{
		private readonly OpenApiSecurityScheme _scheme;
		private readonly SwaggerConfig _swaggerConfig;

		public AuthorizeCheckOperationFilter(OpenApiSecurityScheme scheme, SwaggerConfig swaggerConfig)
		{
			_scheme = scheme;
			_swaggerConfig = swaggerConfig;
		}

		public void Apply(OpenApiOperation operation, OperationFilterContext context)
		{
			operation.Responses.Add("401", new OpenApiResponse {Description = "Unauthorized"});
			operation.Responses.Add("403", new OpenApiResponse {Description = "Forbidden"});
			operation.Responses.Add("400", new OpenApiResponse {Description = "BadRequest"});

			operation.Security.Add(new OpenApiSecurityRequirement
			{
				{_scheme, new List<string> {_swaggerConfig.ApiName}}
			});
		}
	}

	public static IApplicationBuilder UseApplicationSwagger(this IApplicationBuilder source)
	{
		if (InternalConfig is null)
		{
			throw new InvalidOperationException($"Attempting to set up Swagger Generation without correct service registration. Please register required services by calling services.{nameof(AddSwagger)}()");
		}
		
		source.UseSwagger(new Action<SwaggerOptions>(_ => { }));
		source.UseSwaggerUI(c =>
		{
			c.SwaggerEndpoint($"/swagger/{InternalConfig.ApiVersion}/swagger.json", $"{InternalConfig.ApiTitle} {InternalConfig.ApiVersion}");
			c.DocExpansion(DocExpansion.None);
			c.ConfigObject.DisplayRequestDuration = true;
			c.DefaultModelExpandDepth(0);
			c.DefaultModelsExpandDepth(-1);
			c.DisplayOperationId();
		});

		// Activates filters before first server request
		source.ApplicationServices
			.GetRequiredService<ISwaggerProvider>()
			.GetSwagger(InternalConfig.ApiVersion, null, "/");
		return source;
	}

	public class XEnumNamesSchemaFilter : ISchemaFilter
	{
		private const string Name = "x-enumNames";

		public void Apply(OpenApiSchema model, SchemaFilterContext context)
		{
			var typeInfo = context.Type;
			if (!typeInfo.IsEnum || model.Extensions.ContainsKey(Name))
			{
				return;
			}

			var names = Enum.GetNames(context.Type);
			var arr = new OpenApiArray();
			arr.AddRange(names.Select(name => new OpenApiString(name)));
			model.Extensions.Add(Name, arr);
		}
	}

	private class SwaggerExcludeFilter : IOperationFilter
	{
		public void Apply(OpenApiOperation operation, OperationFilterContext context)
		{
			var ignoredParameters = context
				.ApiDescription
				.ParameterDescriptions
				.Where(pd =>
				{
					var info = pd.ParameterInfo();
					if (info == null)
					{
						return false;
					}

					return info
						.CustomAttributes
						.Any(x => x.AttributeType == typeof(SwaggerExcludeAttribute));
				});

			foreach (var ignoredParameter in ignoredParameters)
			{
				var paramToRemove = operation.Parameters.SingleOrDefault(x => x.Name == ignoredParameter.Name);
				if (paramToRemove != null)
				{
					operation.Parameters.Remove(paramToRemove);
				}
			}
		}
	}

	private class AddOdataParametersTypeFilter : IOperationFilter
	{
		public void Apply(OpenApiOperation operation, OperationFilterContext context)
		{
			if (context.ApiDescription.HttpMethod != "GET" || !context.ApiDescription.RelativePath!.EndsWith("/odata"))
			{
				return;
			}

			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$count",
				In = ParameterLocation.Query,
				Description = "Defines if the total element count should be computed. ref: https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview#count",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "boolean",
					Nullable = true,
					Default = new OpenApiBoolean(false)
				}
			});
			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$skip",
				In = ParameterLocation.Query,
				Description = "Defines how many elements to skip. ref: https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview#top-and-skip",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "integer",
					Nullable = true,
					Default = new OpenApiInteger(0)
				}
			});
			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$top",
				In = ParameterLocation.Query,
				Description = "Defines how many elements to return. ref: https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview#top-and-skip",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "integer",
					Nullable = true,
					Default = new OpenApiInteger(30)
				}
			});
			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$filter",
				In = ParameterLocation.Query,
				Description = "Defines the filtering expression. ref: https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview#filter",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "string",
					Nullable = true
				}
			});
			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$orderBy",
				In = ParameterLocation.Query,
				Description = "Defines the ordering expression. ref: https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview#orderby",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "string",
					Nullable = true
				}
			});
			operation.Parameters.Add(new OpenApiParameter
			{
				Name = "$apply",
				In = ParameterLocation.Query,
				Description = "Defines the Aggregation behavior. ref: http://docs.oasis-open.org/odata/odata-data-aggregation-ext/v4.0/cs01/odata-data-aggregation-ext-v4.0-cs01.html#_Toc378326289",
				Required = false,
				Schema = new OpenApiSchema
				{
					Type = "string",
					Nullable = true
				}
			});
		}
	}

	public class SwaggerExcludeParametersFilter : IDocumentFilter
	{
		private readonly string[] _ignoredParameterNames;

		public SwaggerExcludeParametersFilter(string[] ignoredParameterNames)
		{
			_ignoredParameterNames = ignoredParameterNames;
		}

		public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
		{
			// swaggerDoc.Paths contiene tutte le api dei vari controller sottoforma di dizionario path - oggetto
			var operations = swaggerDoc.Paths
				.Where(x => x.Value != null)
				.SelectMany(x => x.Value.Operations.Where(y => y.Value != null));

			foreach (var operation in operations)
			{
				// route.Operations contiene le API all'interno di un determinato controller sottoforma di tipo operazione - oggetto
				FilterParametersFromMethod(operation.Value);
			}
		}

		private void FilterParametersFromMethod(OpenApiOperation operation)
		{
			// operation.Parameters contiene tutti i parametri di una determinata API
			for (var i = operation.Parameters.Count - 1; i >= 0; i--)
			{
				var parameter = operation.Parameters[i];
				if (parameter is null || !_ignoredParameterNames.Contains(parameter.Name))
				{
					continue;
				}

				operation.Parameters.RemoveAt(i);
			}
		}
	}
}
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CodeNotion.AspNetCore;

public class SwaggerExcludeParameterTypeFilter<T> : IOperationFilter
{
	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		var ignoredParameters = context
			.ApiDescription
			.ParameterDescriptions
			.Where(x => typeof(T).IsAssignableFrom(x.Type))
			.ToArray();

		foreach (var ignoredParameter in ignoredParameters)
		{
			context.ApiDescription.ParameterDescriptions.Remove(ignoredParameter);
			var paramToRemove = operation.Parameters.SingleOrDefault(x => x.Name == ignoredParameter.Name);
			if (paramToRemove != null)
			{
				operation.Parameters.Remove(paramToRemove);
			}
		}
	}
}
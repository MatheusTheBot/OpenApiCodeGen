using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Scriban;
using Scriban.Runtime;

namespace OpenApiCodeGen;

public class OpenApiGenerator(
    string? specFilePath,
    string? outputDirectory,
    string? templatesDirectory,
    string? outputNamespace)
{
    private OpenApiDocument _document;

    public async Task GenerateAsync()
    {
        await LoadSpecificationAsync();
        await GenerateModelsAsync();
        await GenerateApisAsync();
    }

    private async Task LoadSpecificationAsync()
    {
        await using var stream = File.OpenRead(specFilePath);
        var reader = new OpenApiStreamReader();
        var result = await reader.ReadAsync(stream);
        _document = result.OpenApiDocument;
    }

    private async Task GenerateModelsAsync()
    {
        var modelTemplatePath = Path.Combine(templatesDirectory, "model.scriban");
        var modelTemplate = Template.Parse(await File.ReadAllTextAsync(modelTemplatePath));
        var modelsDirectory = Path.Combine(outputDirectory, "Models");

        Directory.CreateDirectory(modelsDirectory);

        foreach (var schema in _document.Components.Schemas)
        {
            var model = new
            {
                Name = schema.Key,
                Properties = schema.Value.Properties.Select(p => new
                {
                    Name = p.Key,
                    Type = GetCSharpType(p.Value),
                    IsRequired = schema.Value.Required.Contains(p.Key)
                })
            };

            var scriptObject = new ScriptObject
            {
                { "model", model },
                { "namespace", outputNamespace } // Add namespace here
            };

            var result = await modelTemplate.RenderAsync(scriptObject);
            await File.WriteAllTextAsync(
                Path.Combine(modelsDirectory, $"{schema.Key}.cs"),
                result
            );
        }
    }

    private async Task GenerateApisAsync()
    {
        var apiTemplatePath = Path.Combine(templatesDirectory, "api.scriban");
        var apiTemplate = Template.Parse(await File.ReadAllTextAsync(apiTemplatePath));
        var apisDirectory = Path.Combine(outputDirectory, "Interfaces");

        Directory.CreateDirectory(apisDirectory);

        // Group paths by their first segment to determine controller grouping
        {
            var controllerGroups = _document.Paths
                .GroupBy(p => GetControllerName(p.Key));

            foreach (var group in controllerGroups)
            {
                var operations = new List<object>();

                foreach (var pathItem in group)
                {
                    foreach (var operation in pathItem.Value.Operations)
                    {
                        var responseType = GetOperationResponseType(operation.Value);

                        operations.Add(new
                        {
                            Path = pathItem.Key,
                            Method = operation.Key.ToString().ToUpper(),
                            operation.Value.OperationId,
                            Parameters = operation.Value.Parameters?.Select(p => new
                            {
                                p.Name,
                                Type = GetCSharpType(p.Schema),
                                Location = p.In.ToString(),
                                IsRequired = p.Required
                            }) ?? Enumerable.Empty<object>(),
                            ResponseType = responseType,
                            HasRequestBody = operation.Value.RequestBody != null,
                            RequestBodyType = operation.Value.RequestBody != null
                                ? GetRequestBodyType(operation.Value.RequestBody)
                                : null
                        });
                    }
                }

                var controller = new
                {
                    Name = group.Key,
                    Operations = operations
                };

                var scriptObject = new ScriptObject
                {
                    { "interface", controller },
                    { "namespace", outputNamespace } // Add namespace here
                };

                var result = await apiTemplate.RenderAsync(scriptObject);
                await File.WriteAllTextAsync(
                    Path.Combine(apisDirectory, $"I{controller.Name}Api.cs"),
                    result
                );
            }
        }
    }

    private string GetOperationResponseType(OpenApiOperation operation)
    {
        // Look for 200 or 201 response first
        var successResponse = operation.Responses.FirstOrDefault(r =>
            r.Key == "200" || r.Key == "201").Value;

        if (successResponse?.Content?.Any() == true)
        {
            var content = successResponse.Content.First().Value;
            return GetResponseType(content.Schema);
        }

        return "void";
    }

    private string GetRequestBodyType(OpenApiRequestBody requestBody)
    {
        if (requestBody.Content.TryGetValue("application/json", out var content))
        {
            return GetResponseType(content.Schema);
        }

        return "object";
    }

    private string GetCSharpType(OpenApiSchema schema)
    {
        if (schema.Reference != null)
        {
            return schema.Reference.Id;
        }

        return schema.Type switch
        {
            "integer" when schema.Format == "int64" => "long",
            "integer" => "int",
            "number" when schema.Format == "float" => "float",
            "number" => "decimal",
            "boolean" => "bool",
            "string" when schema.Format == "date-time" => "DateTime",
            "string" when schema.Format == "uuid" => "Guid",
            "string" => "string",
            "array" => $"List<{GetCSharpType(schema.Items)}>",
            _ => "object"
        };
    }

    private string GetResponseType(OpenApiSchema schema)
    {
        if (schema.Reference != null)
        {
            return schema.Reference.Id;
        }

        if (schema.Type == "array")
        {
            return $"List<{GetCSharpType(schema.Items)}>";
        }

        return GetCSharpType(schema);
    }

    private string GetControllerName(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return char.ToUpper(segments[0][0]) + segments[0][1..];
    }
}
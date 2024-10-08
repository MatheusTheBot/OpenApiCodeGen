using CommandLine;
using OpenApiCodeGen;


Parser.Default.ParseArguments<Options>(args)
    .WithParsed(async o =>
    {
        try
        {
            var generator = new OpenApiGenerator(o.InputOpenApiFilePath, o.OutputDirectoryPath,
                o.TemplatesDirectoryPath, o.GeneratedNamespace);
            await generator.GenerateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }).WithNotParsed(o =>
    {
        Console.WriteLine(
            "Usage: openapi-gen <spec-file-path> <output-directory> <templates-directory> [--namespace <namespace>]");
    });


public class Options
{
    [Option('i', "input", Required = true, HelpText = "The relative path to the OpenApi Yaml file.")]
    public string? InputOpenApiFilePath { get; set; }

    [Option('o', "output", Required = true, HelpText = "The desired output directory path.")]
    public string? OutputDirectoryPath { get; set; }

    [Option('t', "template", Required = true, HelpText = "The relative path to template directory (Scriban files).")]
    public string? TemplatesDirectoryPath { get; set; }

    [Option('n', "namespace", Required = true,
        HelpText = "The desired namespace name for the generated open API code.")]
    public string? GeneratedNamespace { get; set; }
}
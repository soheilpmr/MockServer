using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MockServer.Models;
using MockServer.Utilities;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MockServer.Services
{
    public class RuntimeApiCompilerService
    {
        private readonly ApplicationPartManager _partManager;
        private readonly IWebHostEnvironment _env;

        public RuntimeApiCompilerService(ApplicationPartManager partManager, IWebHostEnvironment env)
        {
            _partManager = partManager;
            _env = env;
        }

        public (bool Success, string GeneratedCode, IEnumerable<string> Errors) GenerateAndRegisterApiFromExcel(CreateDynamicApiRequest request)
        {
            string safeName = SanitizeIdentifier(request.ApiName);
            string uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
            string className = $"{safeName}_{uniqueSuffix}";

            var responseProps = JsonSerializer.Deserialize<Dictionary<string, string>>(request.ResponsePropertiesJson) ?? new();
            var requestParams = ParseRequestParameters(request.RequestParametersJson);

            // 1. Parse Excel into a List of Dictionaries & Save to JSON file
            var parsedRecords = ExcelParserUtility.ParseExcelRows(request.ExcelFile, responseProps);
            string outputDir = Path.Combine(_env.ContentRootPath, "GeneratedApis");
            Directory.CreateDirectory(outputDir);

            string dataFilePath = Path.Combine(outputDir, $"data_{className}.json");
            File.WriteAllText(dataFilePath, JsonSerializer.Serialize(parsedRecords, new JsonSerializerOptions { WriteIndented = true }));

            // 2. Generate C# Source Code
            string sourceCode = BuildCSharpSource(
                className,
                request.Route,
                request.HttpMethod,
                request.ResponseFormat,
                responseProps,
                requestParams);

            File.WriteAllText(Path.Combine(outputDir, $"{className}Controller.cs"), sourceCode);

            // 3. Compile in-memory using Roslyn
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName: $"DynamicApi_{className}",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage());
                return (false, sourceCode, errors);
            }

            // 4. Inject assembly into ASP.NET Core MVC Runtime & Swagger
            ms.Seek(0, SeekOrigin.Begin);
            Assembly assembly = Assembly.Load(ms.ToArray());
            _partManager.ApplicationParts.Add(new AssemblyPart(assembly));
            DynamicActionDescriptorChangeProvider.Instance.NotifyChanges();

            return (true, sourceCode, Array.Empty<string>());
        }

        public void LoadExistingApis()
        {
            string outputDir = Path.Combine(_env.ContentRootPath, "GeneratedApis");
            if (!Directory.Exists(outputDir)) return;

            var controllerFiles = Directory.GetFiles(outputDir, "*Controller.cs");
            bool anyLoaded = false;

            foreach (var file in controllerFiles)
            {
                try
                {
                    string sourceCode = File.ReadAllText(file);
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

                    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
                    var references = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                        .Select(a => MetadataReference.CreateFromFile(a.Location))
                        .Cast<MetadataReference>()
                        .ToList();

                    var compilation = CSharpCompilation.Create(
                        assemblyName: $"DynamicApi_{fileNameWithoutExt}",
                        syntaxTrees: new[] { syntaxTree },
                        references: references,
                        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                    using var ms = new MemoryStream();
                    var emitResult = compilation.Emit(ms);
                    if (emitResult.Success)
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        var assembly = Assembly.Load(ms.ToArray());
                        _partManager.ApplicationParts.Add(new AssemblyPart(assembly));
                        anyLoaded = true;
                    }
                }
                catch
                {
                    // Ignore individual compile failures for legacy or incompatible files
                }
            }

            if (anyLoaded)
            {
                DynamicActionDescriptorChangeProvider.Instance.NotifyChanges();
            }
        }

        private static List<RequestParameterDefinition> ParseRequestParameters(string? json)
        {
            var list = new List<RequestParameterDefinition>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    list = JsonSerializer.Deserialize<List<RequestParameterDefinition>>(json, options) ?? new();
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        list.Add(new RequestParameterDefinition
                        {
                            Name = prop.Name,
                            Type = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "string" : "string",
                            TargetProperty = prop.Name,
                            MatchMode = "Equals"
                        });
                    }
                }
            }
            catch
            {
                // Fallback to empty list on parsing failure
            }

            return list;
        }

        private static string BuildCSharpSource(
            string className,
            string route,
            string httpMethod,
            string responseFormat,
            Dictionary<string, string> responseProps,
            List<RequestParameterDefinition> requestParams)
        {
            var sb = new StringBuilder();
            string responseClassName = $"{className}ResponseDto";
            string requestClassName = $"{className}RequestDto";

            string method = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod.Trim().ToUpperInvariant();
            string httpAttribute = method switch
            {
                "POST" => "[HttpPost]",
                "PUT" => "[HttpPut]",
                "DELETE" => "[HttpDelete]",
                _ => "[HttpGet]"
            };

            string bindingAttribute = (method == "POST" || method == "PUT")
                ? "[FromBody]"
                : "[FromQuery]";

            bool hasRequestParams = requestParams != null && requestParams.Count > 0;

            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("using Microsoft.AspNetCore.Hosting;");
            sb.AppendLine();
            sb.AppendLine("namespace DynamicApiGenerator.Generated");
            sb.AppendLine("{");

            // Response DTO
            sb.AppendLine($"    public class {responseClassName}");
            sb.AppendLine("    {");
            foreach (var prop in responseProps)
            {
                string pName = SanitizeIdentifier(prop.Key);
                string pType = MapType(prop.Value);
                string escapedKey = prop.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine($"        [JsonPropertyName(\"{escapedKey}\")]");
                sb.AppendLine($"        public {pType} {pName} {{ get; set; }} = default!;");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // Request DTO (if input parameters are defined)
            if (hasRequestParams)
            {
                sb.AppendLine($"    public class {requestClassName}");
                sb.AppendLine("    {");
                foreach (var param in requestParams!)
                {
                    string pName = SanitizeIdentifier(param.Name);
                    string pType = MapNullableType(param.Type);
                    string escapedParam = param.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"        [JsonPropertyName(\"{escapedParam}\")]");
                    sb.AppendLine($"        public {pType} {pName} {{ get; set; }}");
                }
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // Controller
            sb.AppendLine("    [ApiController]");
            sb.AppendLine($"    [Route(\"{route.TrimStart('/')}\")]");
            sb.AppendLine($"    public class {className}Controller : ControllerBase");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static List<{responseClassName}>? _cachedData;");
            sb.AppendLine("        private readonly string _dataPath;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}Controller(IWebHostEnvironment env)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _dataPath = Path.Combine(env.ContentRootPath, \"GeneratedApis\", \"data_{className}.json\");");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        private List<{responseClassName}> LoadData()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (_cachedData != null) return _cachedData;");
            sb.AppendLine($"            if (!System.IO.File.Exists(_dataPath)) return new List<{responseClassName}>();");
            sb.AppendLine("            string json = System.IO.File.ReadAllText(_dataPath);");
            sb.AppendLine($"            _cachedData = JsonSerializer.Deserialize<List<{responseClassName}>>(json, new JsonSerializerOptions {{ PropertyNameCaseInsensitive = true }}) ?? new();");
            sb.AppendLine("            return _cachedData;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Endpoint Handler
            sb.AppendLine($"        {httpAttribute}");
            if (hasRequestParams)
            {
                sb.AppendLine($"        public IActionResult Handle({bindingAttribute} {requestClassName}? filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)");
            }
            else
            {
                sb.AppendLine("        public IActionResult Handle([FromQuery] int page = 1, [FromQuery] int pageSize = 50)");
            }
            sb.AppendLine("        {");
            sb.AppendLine("            var data = LoadData();");
            sb.AppendLine($"            IEnumerable<{responseClassName}> query = data;");
            sb.AppendLine();

            if (hasRequestParams)
            {
                // Required parameters validation
                var requiredParams = requestParams!.Where(p => p.IsRequired).ToList();
                if (requiredParams.Count > 0)
                {
                    sb.AppendLine("            if (filter == null)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                return BadRequest(new { Message = \"Request filter parameters are required.\" });");
                    sb.AppendLine("            }");
                    foreach (var req in requiredParams)
                    {
                        string rName = SanitizeIdentifier(req.Name);
                        string rType = (req.Type ?? "string").ToLowerInvariant();
                        if (rType is "string")
                        {
                            sb.AppendLine($"            if (string.IsNullOrWhiteSpace(filter.{rName}))");
                            sb.AppendLine($"                return BadRequest(new {{ Message = \"Parameter '{req.Name}' is required.\" }});");
                        }
                        else
                        {
                            sb.AppendLine($"            if (!filter.{rName}.HasValue)");
                            sb.AppendLine($"                return BadRequest(new {{ Message = \"Parameter '{req.Name}' is required.\" }});");
                        }
                    }
                    sb.AppendLine();
                }

                // Filtering conditions
                sb.AppendLine("            if (filter != null)");
                sb.AppendLine("            {");
                foreach (var param in requestParams!)
                {
                    string paramProp = SanitizeIdentifier(param.Name);
                    string normTarget = NormalizeIdentifier(param.TargetProperty ?? param.Name);
                    string normName = NormalizeIdentifier(param.Name);

                    string? matchedKey = responseProps.Keys.FirstOrDefault(k => k.Equals(param.TargetProperty ?? param.Name, StringComparison.OrdinalIgnoreCase))
                        ?? responseProps.Keys.FirstOrDefault(k => k.Equals(param.Name, StringComparison.OrdinalIgnoreCase))
                        ?? responseProps.Keys.FirstOrDefault(k => NormalizeIdentifier(k).Equals(normTarget, StringComparison.OrdinalIgnoreCase))
                        ?? responseProps.Keys.FirstOrDefault(k => NormalizeIdentifier(k).Equals(normName, StringComparison.OrdinalIgnoreCase));

                    if (matchedKey == null) continue;

                    string targetProp = SanitizeIdentifier(matchedKey);
                    string targetType = responseProps[matchedKey].ToLowerInvariant();
                    string mode = param.MatchMode?.Trim() ?? "Equals";

                    switch (targetType)
                    {
                        case "string":
                            sb.AppendLine($"                var {paramProp}_val = filter.{paramProp} ?? Request.Query[\"{paramProp}\"].FirstOrDefault() ?? Request.Query[\"{param.Name}\"].FirstOrDefault();");
                            if (mode.Equals("Contains", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (!string.IsNullOrWhiteSpace({paramProp}_val))");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} != null && x.{targetProp}.Contains({paramProp}_val.Trim(), StringComparison.OrdinalIgnoreCase));");
                            }
                            else if (mode.Equals("StartsWith", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (!string.IsNullOrWhiteSpace({paramProp}_val))");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} != null && x.{targetProp}.StartsWith({paramProp}_val.Trim(), StringComparison.OrdinalIgnoreCase));");
                            }
                            else
                            {
                                sb.AppendLine($"                if (!string.IsNullOrWhiteSpace({paramProp}_val))");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} != null && string.Equals(x.{targetProp}.Trim(), {paramProp}_val.Trim(), StringComparison.OrdinalIgnoreCase));");
                            }
                            break;

                        case "datetime" or "date":
                            if (mode.Equals("GreaterThan", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} > filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("LessThan", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} < filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("GreaterThanOrEqual", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} >= filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("LessThanOrEqual", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} <= filter.{paramProp}.Value);");
                            }
                            else
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp}.Date == filter.{paramProp}.Value.Date);");
                            }
                            break;

                        default: // int, long, decimal, double, bool
                            if (mode.Equals("GreaterThan", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} > filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("LessThan", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} < filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("GreaterThanOrEqual", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} >= filter.{paramProp}.Value);");
                            }
                            else if (mode.Equals("LessThanOrEqual", StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} <= filter.{paramProp}.Value);");
                            }
                            else
                            {
                                sb.AppendLine($"                if (filter.{paramProp}.HasValue)");
                                sb.AppendLine($"                    query = query.Where(x => x.{targetProp} == filter.{paramProp}.Value);");
                            }
                            break;
                    }
                }
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            var filteredItems = query.ToList();");
            sb.AppendLine();

            string format = responseFormat?.Trim() ?? "PaginatedList";
            if (format.Equals("SingleObject", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("            var match = filteredItems.FirstOrDefault();");
                sb.AppendLine("            if (match == null)");
                sb.AppendLine("            {");
                sb.AppendLine("                return NotFound(new { Message = \"No matching record found.\" });");
                sb.AppendLine("            }");
                sb.AppendLine("            return Ok(match);");
            }
            else if (format.Equals("DirectArray", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("            return Ok(filteredItems);");
            }
            else
            {
                sb.AppendLine("            var pagedItems = filteredItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();");
                sb.AppendLine("            return Ok(new {");
                sb.AppendLine("                TotalRecords = filteredItems.Count,");
                sb.AppendLine("                CurrentPage = page,");
                sb.AppendLine("                PageSize = pageSize,");
                sb.AppendLine("                Data = pagedItems");
                sb.AppendLine("            });");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string MapType(string typeName) => typeName.ToLowerInvariant() switch
        {
            "int" or "integer" => "int",
            "long" => "long",
            "decimal" or "money" => "decimal",
            "double" or "float" => "double",
            "bool" or "boolean" => "bool",
            "datetime" or "date" => "DateTime",
            _ => "string"
        };

        private static string MapNullableType(string typeName) => typeName.ToLowerInvariant() switch
        {
            "int" or "integer" => "int?",
            "long" => "long?",
            "decimal" or "money" => "decimal?",
            "double" or "float" => "double?",
            "bool" or "boolean" => "bool?",
            "datetime" or "date" => "DateTime?",
            _ => "string?"
        };

        private static string SanitizeIdentifier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "_item";
            var clean = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(clean)) return "_item";
            if (char.IsDigit(clean[0])) clean = "_" + clean;
            return clean;
        }

        private static string NormalizeIdentifier(string raw) =>
            new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}

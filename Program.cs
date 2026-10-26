using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using MockServer.Models;
using MockServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAntiforgery();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register the real-time change provider and Roslyn compiler
builder.Services.AddSingleton<IActionDescriptorChangeProvider>(DynamicActionDescriptorChangeProvider.Instance);
builder.Services.AddSingleton(DynamicActionDescriptorChangeProvider.Instance);
builder.Services.AddSingleton<RuntimeApiCompilerService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// REQUIRED: Enables serving files from wwwroot
app.UseStaticFiles();

app.UseAntiforgery();

app.MapControllers();

app.MapPost("/api/generator/addApi", async (
    [FromForm] CreateDynamicApiRequest request,
    RuntimeApiCompilerService compiler) =>
{
    var result = compiler.GenerateAndRegisterApiFromExcel(request);
    if (!result.Success)
    {
        return Results.BadRequest(new { result.Errors, result.GeneratedCode });
    }

    return Results.Ok(new
    {
        Message = $"API '{request.Route}' successfully generated with Excel mock data and live in Swagger!",
        result.GeneratedCode
    });
}).DisableAntiforgery();

// Automatically load previously generated APIs on startup
using (var scope = app.Services.CreateScope())
{
    var compiler = scope.ServiceProvider.GetRequiredService<RuntimeApiCompilerService>();
    compiler.LoadExistingApis();
}

app.Run();
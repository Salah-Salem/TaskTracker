using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using TaskTracker.Application.Common;
using TaskTracker.Domain.Enums;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Api.Swagger;

/// <summary>
/// <see cref="Optional{T}"/> serialises as its inner value; tell Swagger so the PATCH bodies
/// document plain fields (e.g. <c>"title": "string"</c>) instead of <c>{ isSet, value }</c> objects.
/// </summary>
public static class OptionalSchemas
{
    public static void Register(SwaggerGenOptions c)
    {
        // Optional<string> and Optional<string?> are the same runtime type — one mapping covers both.
        c.MapType<Optional<string>>(() => new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null });
        c.MapType<Optional<int>>(() => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" });
        c.MapType<Optional<Guid?>>(() => new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "uuid" });
        c.MapType<Optional<DateOnly>>(() => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" });
        c.MapType<Optional<DateOnly?>>(() => new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "date" });
        c.MapType<Optional<TaskStatus>>(() => EnumSchema<TaskStatus>());
        c.MapType<Optional<TaskPriority>>(() => EnumSchema<TaskPriority>());
    }

    private static OpenApiSchema EnumSchema<TEnum>() where TEnum : struct, Enum => new()
    {
        Type = JsonSchemaType.String,
        Enum = Enum.GetNames<TEnum>().Select(n => (JsonNode)JsonValue.Create(n)!).ToList(),
    };
}

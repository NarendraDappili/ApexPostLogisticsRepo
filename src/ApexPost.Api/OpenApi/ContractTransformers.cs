using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

// The built-in generator describes the CLR types; these transformers make the document describe the HTTP contract
// instead, where the two differ. A client generated from the document (C6) is built from exactly these schemas.

/// <summary>
/// Enums are documented in the form they travel in. The generator gives an enum one schema, named after the type and
/// shared by every use, so a request field that takes names ("PickedUp") pointed at the response's integer schema.
/// <list type="bullet">
/// <item>Written or read as a name (a request field with a string converter, a query parameter, or a response field
/// with a string converter): the shared component, named after the enum, listing the names.</item>
/// <item>Written as a number (v1 response fields): an inline integer schema listing the values. The field's own
/// description says which number is which status.</item>
/// </list>
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    // The generator's metadata: the component name, and the field's description for the $ref that points to it.
    private const string SchemaIdKey = "x-schema-id";
    private const string RefDescriptionKey = "x-ref-description";

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        if (!enumType.IsEnum) return Task.CompletedTask;

        // The generator lists names only when a string converter applies; model binding reads a query value by name.
        var byName = schema.Enum is { Count: > 0 } || context.ParameterDescription?.Source == BindingSource.Query;
        schema.Metadata ??= new Dictionary<string, object>();
        if (byName)
        {
            schema.Type = JsonSchemaType.String;
            schema.Enum = [.. Enum.GetNames(enumType).Select(name => (JsonNode)JsonValue.Create(name))];
            schema.Metadata[SchemaIdKey] = enumType.Name;
        }
        else
        {
            var values = Enum.GetValues(enumType).Cast<object>().Select(value => (Name: value.ToString()!, Number: Convert.ToInt32(value, CultureInfo.InvariantCulture))).ToList();
            schema.Type = JsonSchemaType.Integer;
            schema.Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value.Number))];
            schema.Metadata.Remove(SchemaIdKey);
            // The field's XML comment is kept for the $ref the generator no longer writes; an inline schema carries it.
            schema.Description = schema.Metadata.Remove(RefDescriptionKey, out var description) && description is string text
                ? text
                : string.Join(", ", values.Select(value => $"{value.Number} {value.Name}"));
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Numbers are documented as numbers. The web JSON defaults also read a number written as a string ("2.5"), so the
/// generator types every number as "number or string" with a digits pattern. That leniency stays, but a client
/// should send numbers, and a response never writes a number as a string.
/// </summary>
internal sealed class NumberSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is { } type && (type.HasFlag(JsonSchemaType.Number) || type.HasFlag(JsonSchemaType.Integer)) && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// A [Required] field is never null. Request DTO fields are nullable in C# only so that [Required] can report a
/// missing field; a null value is answered with a 400, so the document must not offer null as a valid value. A
/// response field that can be null (a scan's notes) keeps null: there the generator's "required" only means the field
/// is always present.
/// </summary>
internal sealed class RequiredNotNullSchemaTransformer : IOpenApiSchemaTransformer
{
    // Set by the generator on a nullable field that points to a shared schema; it writes oneOf: [null, $ref] for it.
    private const string NullablePropertyKey = "x-is-nullable-property";

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonPropertyInfo?.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: true) != true) return Task.CompletedTask;

        if (schema.Type is { } type && type.HasFlag(JsonSchemaType.Null))
        {
            schema.Type = type & ~JsonSchemaType.Null;
        }
        schema.Metadata?.Remove(NullablePropertyKey);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A 201 response documents its Location header, the URL of what was created. The generator cannot know that
/// CreatedAtAction sets it.
/// </summary>
internal sealed class CreatedLocationHeaderTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Responses?.TryGetValue("201", out var created) == true && created is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["Location"] = new OpenApiHeader
            {
                Description = "The URL of the created shipment.",
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
            };
        }
        return Task.CompletedTask;
    }
}

using System.Text.Json;
using ApexPost.Application.Auditing;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>The one JSON shape of DataChanges.ChangesJson: <c>[{"column":"Name","old":"A","new":"B"}]</c>.</summary>
public static class DataChangeJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<DataChangeColumn> columns) => JsonSerializer.Serialize(columns, Options);

    public static IReadOnlyList<DataChangeColumn> Deserialize(string json) =>
        JsonSerializer.Deserialize<DataChangeColumn[]>(json, Options) ?? [];
}

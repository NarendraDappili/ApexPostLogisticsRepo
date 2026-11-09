using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Courier.Api;

/// <summary>
/// The app's JSON, generated at compile time with the same conventions the v2 API uses: camelCase names, names read
/// in any case, enums by name, and numbers accepted when sent as strings. Every body the app sends or reads goes
/// through this context. Fields the app does not know are skipped, and missing ones are left null or zero, so a
/// newer API does not break an older app.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ScanUploadRequest))]
[JsonSerializable(typeof(ScanUploadResult))]
[JsonSerializable(typeof(ApiProblem))]
[JsonSerializable(typeof(DevelopmentTokenRequest))]
[JsonSerializable(typeof(DevelopmentToken))]
internal sealed partial class CourierJsonContext : JsonSerializerContext
{
    /// <summary>The shared options behind the context, for any code that needs JsonSerializerOptions itself.</summary>
    public static JsonSerializerOptions SharedOptions => Default.Options;
}

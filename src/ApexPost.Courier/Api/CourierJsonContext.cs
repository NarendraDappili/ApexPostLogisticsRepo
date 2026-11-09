using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Courier.Api;

/// <summary>
/// The app's JSON, generated at compile time: camelCase names, case-insensitive reading and enums by name, as the v2
/// API writes them. Every body the app sends or reads goes through this context.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ScanUploadRequest))]
[JsonSerializable(typeof(ScanUploadResult))]
[JsonSerializable(typeof(DevelopmentTokenRequest))]
[JsonSerializable(typeof(DevelopmentToken))]
internal sealed partial class CourierJsonContext : JsonSerializerContext;

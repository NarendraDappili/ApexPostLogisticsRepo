using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ApexPost.Api.Validation;

/// <summary>
/// Names a property in validation messages the way the client writes it: "weightKg must be between 0.1 and 70 kg.",
/// not "WeightKg ...". The errors are already keyed by JSON name (SystemTextJsonValidationMetadataProvider); this
/// makes the message text match its key. An explicit [Display(Name = ...)] still wins.
/// </summary>
internal sealed class JsonDisplayNameProvider : IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        if (context.Key.Name is { } name && context.DisplayMetadata.DisplayName is null)
        {
            var jsonName = JsonNamingPolicy.CamelCase.ConvertName(name);
            context.DisplayMetadata.DisplayName = () => jsonName;
        }
    }
}

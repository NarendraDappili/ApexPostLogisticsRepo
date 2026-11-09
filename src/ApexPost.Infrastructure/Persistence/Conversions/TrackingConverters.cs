using ApexPost.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApexPost.Infrastructure.Persistence.Conversions;

// The tracking API's identifiers (Week 4), stored like the other value objects: as their text, read back through
// the constructor so a malformed row fails loudly.

internal sealed class TrackingNumberConverter() : ValueConverter<TrackingNumber, string>(value => value.Value, value => new TrackingNumber(value));

internal sealed class TrackingScanIdConverter() : ValueConverter<TrackingScanId, string>(value => value.Value, value => new TrackingScanId(value));

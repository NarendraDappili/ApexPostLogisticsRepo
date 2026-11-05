using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// Converts between <see cref="ShipmentScan"/> and its JSON document. Only this class knows the document layout, so
/// the domain stays free of Couchbase types.
/// </summary>
/// <remarks>
/// Enums are stored as their names (readable JSON, stable if members are reordered). The scan time is stored as a
/// fixed-width UTC ISO 8601 string, so sorting the text in SQL++ gives the same order as sorting the times.
/// </remarks>
internal static class ScanDocumentMapper
{
    internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static MutableDocument ToNewDocument(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var document = new MutableDocument(scan.Id.Value);
        Write(scan, document);
        return document;
    }

    public static void Write(ShipmentScan scan, MutableDocument document)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(document);
        document.SetString(ScanFields.TrackingNumber, scan.TrackingNumber);
        document.SetString(ScanFields.ScanType, scan.ScanType.ToString());
        document.SetString(ScanFields.CourierId, scan.CourierId);
        document.SetString(ScanFields.Location, scan.Location);
        document.SetString(ScanFields.ScannedAtUtc, FormatTimestamp(scan.ScannedAtUtc));
        document.SetString(ScanFields.Notes, scan.Notes);
        document.SetString(ScanFields.SyncStatus, scan.SyncStatus.ToString());
    }

    /// <summary>Reads a stored document; a document that is not a valid scan raises <see cref="InvalidDataException"/>.</summary>
    public static ShipmentScan ToScan(string documentId, IDictionaryObject properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!ScanId.TryParse(documentId, out var id)) throw Invalid(documentId, "document ID");

        try
        {
            return new ShipmentScan(
                id,
                RequiredString(documentId, properties, ScanFields.TrackingNumber),
                ParseEnum<ScanType>(documentId, properties, ScanFields.ScanType),
                RequiredString(documentId, properties, ScanFields.CourierId),
                RequiredString(documentId, properties, ScanFields.Location),
                ParseTimestamp(documentId, properties),
                properties.GetString(ScanFields.Notes),
                ParseEnum<SyncStatus>(documentId, properties, ScanFields.SyncStatus));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Document {documentId} is not a valid shipment scan.", exception);
        }
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string documentId, IDictionaryObject properties)
    {
        var text = RequiredString(documentId, properties, ScanFields.ScannedAtUtc);
        return DateTimeOffset.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : throw Invalid(documentId, ScanFields.ScannedAtUtc);
    }

    private static TEnum ParseEnum<TEnum>(string documentId, IDictionaryObject properties, string field)
        where TEnum : struct, Enum
    {
        var text = RequiredString(documentId, properties, field);
        // Names only: Enum.TryParse would also accept "7" or "Pickup, Delivered".
        return Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal)
            ? Enum.Parse<TEnum>(text)
            : throw Invalid(documentId, field);
    }

    private static string RequiredString(string documentId, IDictionaryObject properties, string field) =>
        properties.GetString(field) ?? throw Invalid(documentId, field);

    private static InvalidDataException Invalid(string documentId, string field) =>
        new($"Document {documentId} is not a valid shipment scan: {field} is missing or invalid.");
}

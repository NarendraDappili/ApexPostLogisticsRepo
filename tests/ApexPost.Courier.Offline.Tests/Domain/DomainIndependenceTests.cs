using System.Reflection;
using System.Runtime.CompilerServices;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Tests.Domain;

/// <summary>The brief: domain classes must not reference any Couchbase types.</summary>
public class DomainIndependenceTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string DomainNamespace = typeof(ShipmentScan).Namespace!;

    [Fact]
    public void No_domain_type_exposes_a_couchbase_type_in_its_signatures()
    {
        var domainTypes = typeof(ShipmentScan).Assembly.GetTypes()
            .Where(type => type.Namespace == DomainNamespace)
            .ToList();
        Assert.Contains(typeof(IScanRepository), domainTypes);

        var offenders = domainTypes
            .SelectMany(type => ReferencedTypes(type).Select(referenced => (type, referenced)))
            .Where(pair => IsCouchbase(pair.referenced))
            .Select(pair => $"{pair.type.FullName} -> {pair.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_check_does_find_couchbase_types_in_the_storage_layer()
    {
        var storageMapper = typeof(ShipmentScan).Assembly.GetType("ApexPost.Courier.Offline.Storage.ScanDocumentMapper", throwOnError: true)!;

        Assert.Contains(ReferencedTypes(storageMapper), IsCouchbase);
    }

    [Fact]
    public void No_domain_source_file_uses_couchbase()
    {
        var domainFolder = Path.Combine(SourceRoot(), "src", "ApexPost.Courier.Offline", "Domain");
        var files = Directory.GetFiles(domainFolder, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var offenders = files.Where(file => File.ReadAllText(file).Contains("Couchbase", StringComparison.Ordinal)).ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null) yield return type.BaseType;
        foreach (var contract in type.GetInterfaces()) yield return contract;
        foreach (var field in type.GetFields(AllMembers)) yield return field.FieldType;
        foreach (var property in type.GetProperties(AllMembers)) yield return property.PropertyType;
        foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
        {
            if (method is MethodInfo info) yield return info.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
            foreach (var local in method.GetMethodBody()?.LocalVariables ?? []) yield return local.LocalType;
        }
    }

    private static bool IsCouchbase(Type type)
    {
        if (type.HasElementType) return IsCouchbase(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericArguments().Any(IsCouchbase)) return true;
        return type.Assembly.GetName().Name!.StartsWith("Couchbase", StringComparison.Ordinal);
    }

    private static string SourceRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("ApexPost.slnx was not found above the test source.");
    }
}

using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

var path = args.Length > 0
    ? args[0]
    : @"C:\Program Files\Autodesk\AutoCAD 2026\ACA\AecPropDataMgd.dll";
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
// Usage: metadata-probe <assembly.dll> [FullTypeName ...]
// With no type names, falls back to the Phase 2F AEC property-data set.
var targets = args.Length > 1
    ? new HashSet<string>(args.Skip(1), StringComparer.Ordinal)
    : new HashSet<string>(StringComparer.Ordinal)
{
    "Autodesk.Aec.DatabaseServices.Dictionary",
    "Autodesk.Aec.DatabaseServices.UnitType",
    "Autodesk.Aec.PropertyData.DataType",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertySetDefinition",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertyDefinitionCollection",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertyDefinition",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertySet",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertySetData",
    "Autodesk.Aec.PropertyData.DatabaseServices.PropertyDataServices",
    "Autodesk.Aec.PropertyData.DatabaseServices.DictionaryPropertySetDefinitions"
};

foreach (var handle in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(handle);
    var ns = md.GetString(td.Namespace);
    var name = md.GetString(td.Name);
    var full = ns + "." + name;
    if (!targets.Contains(full))
    {
        continue;
    }

    Console.WriteLine("==== " + full);
    Console.WriteLine("Methods:");
    var provider = new SignatureProvider(md);
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var sig = m.DecodeSignature(provider, genericContext: null);
        Console.WriteLine("  " + sig.ReturnType + " " + md.GetString(m.Name) + "(" + string.Join(", ", sig.ParameterTypes) + ")");
    }

    Console.WriteLine("Properties:");
    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        Console.WriteLine("  " + md.GetString(p.Name));
    }

    Console.WriteLine("Fields:");
    foreach (var fh in td.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        Console.WriteLine("  " + md.GetString(f.Name));
    }
}

internal sealed class SignatureProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;

    public SignatureProvider(MetadataReader reader)
    {
        _reader = reader;
    }

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(td.Namespace);
        var name = reader.GetString(td.Name);
        return string.IsNullOrWhiteSpace(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var ns = reader.GetString(tr.Namespace);
        var name = reader.GetString(tr.Name);
        return string.IsNullOrWhiteSpace(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var spec = reader.GetTypeSpecification(handle);
        return spec.DecodeSignature(this, genericContext);
    }
}

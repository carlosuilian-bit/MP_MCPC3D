using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.AutoCAD;

namespace Civil3D.HybridMcp.Tools.AutoCAD.Tests;

/// <summary>
/// Phase 2G: generic entity reads by handle.
///
/// These tools exist to investigate an object that has no typed tool yet, so the
/// rules under test are about honesty rather than geometry: an unresolvable
/// handle must fail structurally, a property the host refused to evaluate must
/// be reported rather than invented, and a truncated read must say so.
/// </summary>
public sealed class ObjectToolTests
{
    [Fact]
    public void ObjectGetResponseSerializesWithStructuredIdentity()
    {
        var response = new ObjectGetResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Object = SampleObject()
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ObjectGetResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"document_id\"", json);
        Assert.Contains("\"object_class\"", json);
        Assert.Contains("\"dxf_name\"", json);
        Assert.Contains("\"class_hierarchy\"", json);
        Assert.Contains("\"owner_handle\"", json);
        Assert.Contains("\"is_entity\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("16FD0D", roundTrip!.Object.Handle);
        Assert.Equal("AeccDbAlignment", roundTrip.Object.ObjectClass);
        Assert.Equal("C-ROAD-CNTR", roundTrip.Object.Entity?.Layer);
        Assert.Equal(2, roundTrip.Object.ClassHierarchy.Count);
    }

    [Fact]
    public void ObjectPropertiesResponseSerializesWithInvariantValues()
    {
        var response = new ObjectPropertiesResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "16FD0D",
            ObjectClass = "AeccDbAlignment",
            Count = 1,
            Truncated = false,
            Properties =
            [
                new ObjectPropertyInfo
                {
                    Name = "Length",
                    DeclaringType = "Alignment",
                    DataType = "Double",
                    Value = "1234.5678",
                    NumericValue = 1234.5678,
                    IsReadOnly = true
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ObjectPropertiesResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"numeric_value\"", json);
        Assert.Contains("\"declaring_type\"", json);
        Assert.Contains("\"is_read_only\"", json);
        Assert.Contains("\"object_class\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(1234.5678, roundTrip!.Properties[0].NumericValue);
        Assert.Equal("1234.5678", roundTrip.Properties[0].Value);
    }

    [Fact]
    public void ObjectGetRequiresAHandleLocator()
    {
        var service = new ObjectService();
        var request = new ObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        };

        var error = Assert.Throws<ObjectToolException>(
            () => service.GetObject(new FakeObjectAdapter(), SampleDocument(), request));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void ObjectGetAcceptsHandleFromObjectReference()
    {
        var service = new ObjectService();
        var request = new ObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            ObjectReference = SampleReference("16FD0D")
        };

        var response = service.GetObject(new FakeObjectAdapter(SampleObject()), SampleDocument(), request);

        Assert.Equal("16FD0D", response.Object.Handle);
        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal("doc-1", response.DocumentId);
    }

    [Fact]
    public void ObjectGetReturnsObjectNotFoundWhenHandleDoesNotResolve()
    {
        var service = new ObjectService();
        var request = new ObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "DEADBEEF"
        };

        var error = Assert.Throws<ObjectToolException>(
            () => service.GetObject(new FakeObjectAdapter(), SampleDocument(), request));

        Assert.Equal(ErrorCode.ObjectNotFound, error.Code);
    }

    [Fact]
    public void ObjectGetRejectsAForeignDocumentTarget()
    {
        var service = new ObjectService();
        var request = new ObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-9",
            Handle = "16FD0D"
        };

        var error = Assert.Throws<ObjectToolException>(
            () => service.GetObject(new FakeObjectAdapter(SampleObject()), SampleDocument(), request));

        Assert.Equal(ErrorCode.DocumentNotFound, error.Code);
    }

    [Fact]
    public void ObjectPropertiesFiltersByNameContainsCaseInsensitively()
    {
        var service = new ObjectService();
        var adapter = new FakeObjectAdapter(SampleObject(), SampleProperties());
        var request = new ObjectPropertiesRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "16FD0D",
            NameContains = "sta"
        };

        var response = service.GetObjectProperties(adapter, SampleDocument(), request);

        Assert.Equal(2, response.Count);
        Assert.Equal(2, response.Properties.Count);
        Assert.All(response.Properties, property => Assert.Contains("sta", property.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ObjectPropertiesOmitsUnreadableValuesUnlessRequested()
    {
        var service = new ObjectService();
        var adapter = new FakeObjectAdapter(SampleObject(), SampleProperties());
        var baseRequest = new ObjectPropertiesRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "16FD0D"
        };

        var filtered = service.GetObjectProperties(adapter, SampleDocument(), baseRequest);
        var complete = service.GetObjectProperties(adapter, SampleDocument(), baseRequest with { IncludeUnreadable = true });

        Assert.DoesNotContain(filtered.Properties, property => property.Value is null);
        Assert.Contains(complete.Properties, property => property.Value is null);
        Assert.True(complete.Count > filtered.Count);
    }

    [Fact]
    public void ObjectPropertiesKeepsTruncationReportedByTheAdapter()
    {
        var service = new ObjectService();
        var adapter = new FakeObjectAdapter(SampleObject(), SampleProperties(), truncated: true);
        var request = new ObjectPropertiesRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "16FD0D",
            NameContains = "sta"
        };

        var response = service.GetObjectProperties(adapter, SampleDocument(), request);

        Assert.True(response.Truncated);
        Assert.Equal("AeccDbAlignment", response.ObjectClass);
        Assert.Equal("16FD0D", response.Handle);
    }

    [Fact]
    public void ObjectPropertiesReturnsObjectNotFoundWhenHandleDoesNotResolve()
    {
        var service = new ObjectService();
        var request = new ObjectPropertiesRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "DEADBEEF"
        };

        var error = Assert.Throws<ObjectToolException>(
            () => service.GetObjectProperties(new FakeObjectAdapter(), SampleDocument(), request));

        Assert.Equal(ErrorCode.ObjectNotFound, error.Code);
    }

    [Fact]
    public void ObjectPropertiesRequiresAHandleLocator()
    {
        var service = new ObjectService();
        var request = new ObjectPropertiesRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        };

        var error = Assert.Throws<ObjectToolException>(
            () => service.GetObjectProperties(new FakeObjectAdapter(), SampleDocument(), request));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void AutoCadCatalogPublishesObjectReadDescriptors()
    {
        var tools = AutoCadToolCatalog.ListTools();
        var get = Assert.Single(tools, tool => tool.Name == AutoCadToolNames.ObjectGet);
        var properties = Assert.Single(tools, tool => tool.Name == AutoCadToolNames.ObjectProperties);

        // Counts the object tools rather than the whole catalog, so adding an
        // unrelated AutoCAD tool does not fail this test. It hardcoded the full
        // catalog size and broke when the block reads were added.
        Assert.Equal(2, tools.Count(tool => tool.Name.StartsWith("object_", StringComparison.Ordinal)));
        foreach (var tool in new[] { get, properties })
        {
            Assert.Equal(OperationClass.Read, tool.OperationClass);
            Assert.Equal(ToolRisk.Low, tool.Risk);
            Assert.Equal(RollbackSemantics.NoneRequired, tool.RollbackSemantics);
            Assert.False(tool.RequiresConfirmation);
            Assert.Equal("AutoCAD", tool.Module);
        }
    }

    [Fact]
    public void ObjectSchemasRequireDocumentTarget()
    {
        var schema = ObjectSchemas.ObjectGetInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.True(properties.ContainsKey("handle"));
        Assert.True(properties.ContainsKey("object_reference"));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    private static DocumentDescriptor SampleDocument() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "Drawing1.dwg",
            FullPath = null,
            IsActive = true,
            IsSaved = false,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = "fingerprint-1",
            SessionIdentifier = "session-1"
        };

    private static CadObjectInfo SampleObject() =>
        new()
        {
            Handle = "16FD0D",
            ObjectId = "(2130843560256)",
            ObjectClass = "AeccDbAlignment",
            DxfName = "AECC_ALIGNMENT",
            AppName = "AeccDbMgd",
            ClassHierarchy = ["AeccDbEntity", "AcDbEntity"],
            IsEntity = true,
            IsErased = false,
            IsProxy = false,
            IsPersistent = true,
            OwnerHandle = "1F",
            OwnerClass = "AcDbBlockTableRecord",
            ObjectReference = SampleReference("16FD0D"),
            Entity = new CadEntityInfo
            {
                Layer = "C-ROAD-CNTR",
                Linetype = "Continuous",
                LinetypeScale = 1.0,
                Lineweight = "ByLayer",
                ColorIndex = 256,
                Color = "ByLayer",
                IsVisible = true,
                BlockName = "*Model_Space",
                Extents = new CadExtentsInfo
                {
                    MinX = 100.0,
                    MinY = 200.0,
                    MinZ = 0.0,
                    MaxX = 900.0,
                    MaxY = 800.0,
                    MaxZ = 0.0
                }
            }
        };

    private static IReadOnlyList<ObjectPropertyInfo> SampleProperties() =>
    [
        new()
        {
            Name = "StartingStation",
            DeclaringType = "Alignment",
            DataType = "Double",
            Value = "0",
            NumericValue = 0,
            IsReadOnly = true
        },
        new()
        {
            Name = "EndingStation",
            DeclaringType = "Alignment",
            DataType = "Double",
            Value = "2178.913",
            NumericValue = 2178.913,
            IsReadOnly = true
        },
        new()
        {
            Name = "Length",
            DeclaringType = "Alignment",
            DataType = "Double",
            Value = "2178.913",
            NumericValue = 2178.913,
            IsReadOnly = true
        },
        new()
        {
            // A property the host refused to evaluate: reported, never invented.
            Name = "ReferencePointStation",
            DeclaringType = "Alignment",
            DataType = "Double",
            Value = null,
            NumericValue = null,
            IsReadOnly = true,
            Error = "eNotApplicable"
        }
    ];

    private static CadObjectReference SampleReference(string handle) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            DatabaseFingerprint = "fingerprint-1",
            Handle = handle,
            ObjectId = "(2130843560256)",
            Type = "AeccDbAlignment",
            Name = null
        };

    private sealed class FakeObjectAdapter : IObjectReadAdapter
    {
        private readonly CadObjectInfo? _object;
        private readonly IReadOnlyList<ObjectPropertyInfo> _properties;
        private readonly bool _truncated;

        public FakeObjectAdapter(
            CadObjectInfo? knownObject = null,
            IReadOnlyList<ObjectPropertyInfo>? properties = null,
            bool truncated = false)
        {
            _object = knownObject;
            _properties = properties ?? Array.Empty<ObjectPropertyInfo>();
            _truncated = truncated;
        }

        public CadObjectInfo? GetObject(string handle) =>
            _object is not null && string.Equals(_object.Handle, handle, StringComparison.OrdinalIgnoreCase)
                ? _object
                : null;

        public ObjectPropertyReadResult? ReadProperties(string handle) =>
            _object is not null && string.Equals(_object.Handle, handle, StringComparison.OrdinalIgnoreCase)
                ? new ObjectPropertyReadResult(_object.ObjectClass, _properties, _truncated)
                : null;
    }
}

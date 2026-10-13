using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Civil;

public static class CivilToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> ListTools() =>
    [
        ReadTool(CivilToolNames.AlignmentList, "List Civil 3D alignments in the specified drawing.", CivilSchemas.NamedListInput(), nameof(AlignmentListResponse)),
        ReadTool(CivilToolNames.AlignmentGet, "Get one Civil 3D alignment by name, handle, or object reference.", CivilSchemas.NamedGetInput(), nameof(AlignmentGetResponse)),
        ReadTool(CivilToolNames.AlignmentStationOffset, "Convert XY coordinates to station and offset on a Civil 3D alignment.", CivilSchemas.AlignmentStationOffsetInput(), nameof(AlignmentStationOffsetResponse)),
        ReadTool(CivilToolNames.AlignmentPointLocation, "Convert station and offset to XY coordinates on a Civil 3D alignment.", CivilSchemas.AlignmentPointLocationInput(), nameof(AlignmentPointLocationResponse)),
        ReadTool(CivilToolNames.SurfaceList, "List Civil 3D surfaces in the specified drawing.", CivilSchemas.NamedListInput(), nameof(SurfaceListResponse)),
        ReadTool(CivilToolNames.SurfaceGet, "Get one Civil 3D surface by name, handle, or object reference.", CivilSchemas.NamedGetInput(), nameof(SurfaceGetResponse)),
        ReadTool(CivilToolNames.SurfaceElevationAtXy, "Read a Civil 3D surface elevation at XY.", CivilSchemas.SurfaceElevationAtXyInput(), nameof(SurfaceElevationAtXyResponse)),
        ReadTool(CivilToolNames.CogoList, "List COGO points in the specified drawing.", CivilSchemas.CogoListInput(), nameof(CogoListResponse)),
        ReadTool(CivilToolNames.CogoGet, "Get one COGO point by point number, handle, or object reference.", CivilSchemas.CogoGetInput(), nameof(CogoGetResponse)),
        ReadTool(CivilToolNames.ProfileList, "List Civil 3D profiles in the specified drawing, optionally filtered by parent alignment.", CivilSchemas.ProfileListInput(), nameof(ProfileListResponse)),
        ReadTool(CivilToolNames.ProfileGet, "Get one Civil 3D profile by name, handle, or object reference, optionally disambiguated by parent alignment.", CivilSchemas.ProfileGetInput(), nameof(ProfileGetResponse)),
        ReadTool(CivilToolNames.ProfileElevationAtStation, "Read a Civil 3D profile elevation and grade at a station.", CivilSchemas.ProfileElevationAtStationInput(), nameof(ProfileElevationAtStationResponse)),
        ReadTool(CivilToolNames.CorridorList, "List Civil 3D corridors in the specified drawing with baseline and surface counts.", CivilSchemas.NamedListInput(), nameof(CorridorListResponse)),
        ReadTool(CivilToolNames.CorridorGet, "Get one Civil 3D corridor with its baselines, regions, and applied assemblies.", CivilSchemas.NamedGetInput(), nameof(CorridorGetResponse)),
        ReadTool(CivilToolNames.PropertySetDefinitionList, "List AEC property set definitions available in the specified drawing, with their property definitions.", CivilSchemas.PropertySetDefinitionListInput(), nameof(PropertySetDefinitionListResponse)),
        ReadTool(CivilToolNames.PropertySetList, "List AEC property sets attached to one object, including current property values.", CivilSchemas.PropertySetListInput(), nameof(PropertySetListResponse)),
        ReadTool(CivilToolNames.PropertySetGet, "Get one AEC property set attached to an object by property set name.", CivilSchemas.PropertySetGetInput(), nameof(PropertySetGetResponse)),
        WriteTool(CivilToolNames.PropertySetNumericTestCreate, "Create a temporary DBPoint with a test AEC property set containing one Real value, then read it back.", CivilSchemas.PropertySetNumericTestCreateInput(), nameof(PropertySetNumericTestCreateResponse)),
        ReadTool(CivilToolNames.PropertySetFind, "Find objects carrying a named AEC property set, optionally narrowed by property values. This is how a caller obtains the handles the other property-set tools need.", CivilSchemas.PropertySetFindInput(), nameof(PropertySetFindResponse)),
        ConfirmedWriteTool(CivilToolNames.PropertySetSet, "Update values in an AEC property set already attached to one object. Returns a preview and requires confirmation before writing.", CivilSchemas.PropertySetSetInput(), nameof(PropertySetUpdateResponse))
    ];

    /// <summary>
    /// A write on data an engineer already put in the drawing. Medium risk and
    /// confirmation-required, unlike the bounded writes that only touch objects
    /// this platform created itself.
    /// </summary>
    private static ToolDescriptor ConfirmedWriteTool(string name, string description, JsonObject inputSchema, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Medium,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(30),
            RequiresConfirmation = true,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "Civil3D",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor ReadTool(string name, string description, JsonObject inputSchema, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Civil3D",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor WriteTool(string name, string description, JsonObject inputSchema, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "Civil3D",
            Version = ProtocolVersion.Current
        };
}

public static class CivilToolNames
{
    public const string AlignmentList = "alignment_list";
    public const string AlignmentGet = "alignment_get";
    public const string AlignmentStationOffset = "alignment_station_offset";
    public const string AlignmentPointLocation = "alignment_point_location";
    public const string SurfaceList = "surface_list";
    public const string SurfaceGet = "surface_get";
    public const string SurfaceElevationAtXy = "surface_elevation_at_xy";
    public const string CogoList = "cogo_list";
    public const string CogoGet = "cogo_get";
    public const string ProfileList = "profile_list";
    public const string ProfileGet = "profile_get";
    public const string ProfileElevationAtStation = "profile_elevation_at_station";
    public const string CorridorList = "corridor_list";
    public const string CorridorGet = "corridor_get";
    public const string PropertySetDefinitionList = "propertyset_definition_list";
    public const string PropertySetList = "propertyset_list";
    public const string PropertySetGet = "propertyset_get";
    public const string PropertySetNumericTestCreate = "propertyset_numeric_test_create";
    public const string PropertySetSet = "propertyset_set";
    public const string PropertySetFind = "propertyset_find";
}

public static class CivilSchemas
{
    public static JsonObject PropertySetFindInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["set_name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name of the property set to look for."
            },
            ["limit"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 0,
                ["description"] = "Maximum objects to return. Results beyond it set truncated."
            },
            ["max_scan"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["description"] = "How many objects the sweep may examine before stopping. Raise it when truncated is true on an unfiltered find."
            },
            ["match"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Value filters, combined with AND. Each needs equals or contains.",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("name"),
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["equals"] = new JsonObject { ["type"] = "string" },
                        ["contains"] = new JsonObject { ["type"] = "string" }
                    }
                }
            }
        }, requiredExtraProperties: ["set_name"]);

    public static JsonObject PropertySetSetInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Handle of the object carrying the property set."
            },
            ["set_name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name of the attached property set to update."
            },
            ["properties"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["description"] = "Properties to update. Each must already exist on the set.",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("name", "value"),
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["value"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "New value as invariant text, matching how the read tools report values."
                        }
                    }
                }
            }
        }, requiredExtraProperties: ["handle", "set_name", "properties"]);

    public static JsonObject NamedListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = StringProperty("Case-insensitive substring filter for object names.")
        });

    public static JsonObject NamedGetInput() =>
        DocumentTargetSchema(LocatorProperties("Object name."));

    public static JsonObject AlignmentStationOffsetInput() =>
        DocumentTargetSchema(
            Merge(
                LocatorProperties("Alignment name."),
                new JsonObject
                {
                    ["x"] = NumberProperty("Drawing X coordinate."),
                    ["y"] = NumberProperty("Drawing Y coordinate.")
                }),
            requiredExtraProperties: ["x", "y"]);

    public static JsonObject AlignmentPointLocationInput() =>
        DocumentTargetSchema(
            Merge(
                LocatorProperties("Alignment name."),
                new JsonObject
                {
                    ["station"] = NumberProperty("Alignment station."),
                    ["offset"] = NumberProperty("Offset from the alignment. Defaults to 0 when omitted.")
                }),
            requiredExtraProperties: ["station"]);

    public static JsonObject SurfaceElevationAtXyInput() =>
        DocumentTargetSchema(
            Merge(
                LocatorProperties("Surface name."),
                new JsonObject
                {
                    ["x"] = NumberProperty("Drawing X coordinate."),
                    ["y"] = NumberProperty("Drawing Y coordinate.")
                }),
            requiredExtraProperties: ["x", "y"]);

    public static JsonObject CogoListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["raw_description_contains"] = StringProperty("Case-insensitive substring filter for raw descriptions."),
            ["full_description_contains"] = StringProperty("Case-insensitive substring filter for full descriptions."),
            ["limit"] = IntegerProperty("Optional positive maximum number of COGO points to return.")
        });

    public static JsonObject ProfileListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["alignment_name"] = StringProperty("Optional parent alignment name. When omitted, profiles from every alignment are listed."),
            ["name_contains"] = StringProperty("Case-insensitive substring filter for profile names.")
        });

    public static JsonObject ProfileGetInput() =>
        DocumentTargetSchema(
            Merge(
                LocatorProperties("Profile name."),
                new JsonObject
                {
                    ["alignment_name"] = StringProperty("Optional parent alignment name used to disambiguate profile names repeated across alignments.")
                }));

    public static JsonObject ProfileElevationAtStationInput() =>
        DocumentTargetSchema(
            Merge(
                LocatorProperties("Profile name."),
                new JsonObject
                {
                    ["alignment_name"] = StringProperty("Optional parent alignment name used to disambiguate profile names repeated across alignments."),
                    ["station"] = NumberProperty("Station along the parent alignment.")
                }),
            requiredExtraProperties: ["station"]);

    public static JsonObject PropertySetDefinitionListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = StringProperty("Case-insensitive substring filter for property set definition names.")
        });

    public static JsonObject PropertySetListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = StringProperty("AutoCAD object handle of the object whose property sets should be read."),
            ["object_reference"] = ObjectReferenceSchema(),
            ["name_contains"] = StringProperty("Case-insensitive substring filter for property set names.")
        });

    public static JsonObject PropertySetGetInput() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["handle"] = StringProperty("AutoCAD object handle of the object whose property set should be read."),
                ["object_reference"] = ObjectReferenceSchema(),
                ["name"] = StringProperty("Property set name.")
            },
            requiredExtraProperties: ["name"]);

    public static JsonObject PropertySetNumericTestCreateInput() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["definition_name"] = StringProperty("Test property set definition name. Must start with MCP_NUMERIC_TEST."),
                ["property_name"] = StringProperty("Name of the Real property to create and populate."),
                ["numeric_value"] = NumberProperty("Finite numeric value to store in the Real property."),
                ["target_kind"] = StringProperty("Optional target object kind. Only db_point is supported; omitted defaults to db_point.")
            },
            requiredExtraProperties: ["definition_name", "property_name", "numeric_value"]);

    public static JsonObject CogoGetInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["point_number"] = IntegerProperty("COGO point number."),
            ["handle"] = StringProperty("AutoCAD object handle."),
            ["object_reference"] = ObjectReferenceSchema()
        });

    private static JsonObject DocumentTargetSchema(JsonObject extraProperties, IReadOnlyList<string>? requiredExtraProperties = null)
    {
        var properties = new JsonObject
        {
            ["instance_id"] = StringProperty("Civil 3D bridge instance identifier."),
            ["document_id"] = StringProperty("Target drawing document identifier.")
        };

        foreach (var property in extraProperties)
        {
            properties[property.Key] = property.Value?.DeepClone();
        }

        var required = new JsonArray("instance_id", "document_id");
        foreach (var property in requiredExtraProperties ?? Array.Empty<string>())
        {
            required.Add(property);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties
        };
    }

    private static JsonObject LocatorProperties(string nameDescription) =>
        new()
        {
            ["name"] = StringProperty(nameDescription),
            ["handle"] = StringProperty("AutoCAD object handle."),
            ["object_reference"] = ObjectReferenceSchema()
        };

    private static JsonObject ObjectReferenceSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["instance_id"] = StringProperty("Civil 3D bridge instance identifier."),
                ["document_id"] = StringProperty("Target drawing document identifier."),
                ["database_fingerprint"] = StringProperty("Optional database fingerprint."),
                ["handle"] = StringProperty("AutoCAD object handle."),
                ["object_id"] = StringProperty("Session-scoped ObjectId string."),
                ["type"] = StringProperty("Object type."),
                ["name"] = StringProperty("Object name.")
            }
        };

    private static JsonObject Merge(params JsonObject[] objects)
    {
        var merged = new JsonObject();
        foreach (var obj in objects)
        {
            foreach (var property in obj)
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        return merged;
    }

    private static JsonObject StringProperty(string description) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description
        };

    private static JsonObject NumberProperty(string description) =>
        new()
        {
            ["type"] = "number",
            ["description"] = description
        };

    private static JsonObject IntegerProperty(string description) =>
        new()
        {
            ["type"] = "integer",
            ["description"] = description
        };
}

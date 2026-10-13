using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

public static class AutoCadToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> ListTools() =>
    [
        new()
        {
            Name = AutoCadToolNames.LayerList,
            Description = "List layers in the specified drawing using the AutoCAD LayerTable.",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = LayerSchemas.LayerListInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(LayerListResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.LayerGet,
            Description = "Get one layer by name, handle, or layer object reference in the specified drawing.",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = LayerSchemas.LayerGetInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(LayerGetResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.LayerCreate,
            Description = "Create one layer in the specified drawing using a document lock and AutoCAD transaction.",
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Low,
            InputSchema = LayerSchemas.LayerCreateInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(LayerCreateResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.LayerUpdate,
            Description = "Update one existing layer in the specified drawing using a document lock and AutoCAD transaction.",
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Low,
            InputSchema = LayerSchemas.LayerUpdateInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(LayerUpdateResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.LayerSetCurrent,
            Description = "Set the current layer in the specified drawing using a document lock and AutoCAD transaction.",
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Low,
            InputSchema = LayerSchemas.LayerSetCurrentInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(LayerSetCurrentResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.ObjectGet,
            Description = "Get identity, class hierarchy, and entity properties for any drawing object addressed by handle.",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = ObjectSchemas.ObjectGetInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(ObjectGetResponse)),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = AutoCadToolNames.ObjectProperties,
            Description = "List readable properties of any drawing object addressed by handle, including Civil 3D and AEC types.",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = ObjectSchemas.ObjectPropertiesInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(ObjectPropertiesResponse)),
            Timeout = TimeSpan.FromSeconds(30),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        },
        BlockRead(
            AutoCadToolNames.BlockList,
            "List block definitions with their attribute definitions and insert counts. A dynamic block's count includes inserts made through its anonymous representations.",
            BlockSchemas.BlockListInput(),
            nameof(BlockListResponse),
            TimeSpan.FromSeconds(30)),
        BlockRead(
            AutoCadToolNames.BlockReferenceList,
            "List block inserts in model and paper space, filtered by block name or layer. Finds dynamic inserts by their real name, and reports truncated when a scan or result limit cut the list.",
            BlockSchemas.BlockReferenceListInput(),
            nameof(BlockReferenceListResponse),
            TimeSpan.FromSeconds(60)),
        BlockRead(
            AutoCadToolNames.BlockGetAttributes,
            "Read the attributes of one block insert by handle, including constant attributes that exist only on the block definition.",
            BlockSchemas.BlockGetAttributesInput(),
            nameof(BlockGetAttributesResponse),
            TimeSpan.FromSeconds(15))
    ];

    /// <summary>
    /// Block reads work on plain AutoCAD entities, so unlike the Civil 3D reads
    /// they keep answering after a project has been exported.
    /// </summary>
    private static ToolDescriptor BlockRead(string name, string description, JsonObject inputSchema, string outputTitle, TimeSpan timeout) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        };
}

public static class AutoCadToolNames
{
    public const string LayerList = "layer_list";
    public const string LayerGet = "layer_get";
    public const string LayerCreate = "layer_create";
    public const string LayerUpdate = "layer_update";
    public const string LayerSetCurrent = "layer_set_current";
    public const string ObjectGet = "object_get";
    public const string ObjectProperties = "object_properties";
    public const string BlockList = "block_list";
    public const string BlockReferenceList = "block_reference_list";
    public const string BlockGetAttributes = "block_get_attributes";
}

public static class LayerSchemas
{
    public static JsonObject LayerListInput() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["name_contains"] = StringProperty("Case-insensitive substring filter for layer names."),
                ["is_on"] = BoolProperty("When set, returns only layers whose on/off state matches."),
                ["is_frozen"] = BoolProperty("When set, returns only layers whose frozen state matches."),
                ["is_locked"] = BoolProperty("When set, returns only layers whose locked state matches."),
                ["is_plottable"] = BoolProperty("When set, returns only layers whose plottable state matches.")
            });

    public static JsonObject LayerGetInput() => LayerLocatorInput("Layer name.", "AutoCAD layer handle.");

    public static JsonObject LayerSetCurrentInput() => LayerLocatorInput("Layer name to make current.", "AutoCAD layer handle to make current.");

    public static JsonObject LayerUpdateInput() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["name"] = StringProperty("Layer name to update."),
                ["handle"] = StringProperty("AutoCAD layer handle to update."),
                ["object_reference"] = ObjectReferenceSchema("Layer name to update."),
                ["new_name"] = StringProperty("Optional replacement layer name."),
                ["color_index"] = NumberProperty("Optional AutoCAD ACI color index, 1 through 255."),
                ["is_on"] = BoolProperty("Optional on/off state for the layer."),
                ["is_frozen"] = BoolProperty("Optional frozen state for the layer."),
                ["is_locked"] = BoolProperty("Optional locked state for the layer."),
                ["is_plottable"] = BoolProperty("Optional plottable state for the layer."),
                ["description"] = StringProperty("Optional layer description.")
            });

    private static JsonObject LayerLocatorInput(string nameDescription, string handleDescription)
    {
        return DocumentTargetSchema(
            new JsonObject
            {
                ["name"] = StringProperty(nameDescription),
                ["handle"] = StringProperty(handleDescription),
                ["object_reference"] = ObjectReferenceSchema(nameDescription)
            });
    }

    public static JsonObject LayerCreateInput() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["name"] = StringProperty("Layer name to create."),
                ["color_index"] = NumberProperty("Optional AutoCAD ACI color index, 1 through 255."),
                ["is_plottable"] = BoolProperty("Optional plottable flag for the new layer."),
                ["description"] = StringProperty("Optional layer description.")
            },
            requiredExtraProperties: ["name"]);

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

    private static JsonObject StringProperty(string description) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description
        };

    private static JsonObject BoolProperty(string description) =>
        new()
        {
            ["type"] = "boolean",
            ["description"] = description
        };

    private static JsonObject NumberProperty(string description) =>
        new()
        {
            ["type"] = "integer",
            ["minimum"] = 1,
            ["maximum"] = 255,
            ["description"] = description
        };

    private static JsonObject ObjectReferenceSchema(string nameDescription) =>
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
                ["name"] = StringProperty(nameDescription)
            }
        };
}

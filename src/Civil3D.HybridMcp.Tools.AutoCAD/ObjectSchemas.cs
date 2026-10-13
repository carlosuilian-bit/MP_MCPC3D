using System.Text.Json.Nodes;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>
/// Input schemas for the generic object read tools.
///
/// Both tools address an object by handle only. Name lookup is deliberately
/// absent: a handle is the one locator that is unambiguous across every object
/// class in the drawing, and these tools exist precisely for classes the
/// platform has no typed lookup for.
/// </summary>
public static class ObjectSchemas
{
    public static JsonObject ObjectGetInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = StringProperty("AutoCAD object handle, as hexadecimal text."),
            ["object_reference"] = ObjectReferenceSchema()
        });

    public static JsonObject ObjectPropertiesInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = StringProperty("AutoCAD object handle, as hexadecimal text."),
            ["object_reference"] = ObjectReferenceSchema(),
            ["name_contains"] = StringProperty("Case-insensitive substring filter for property names."),
            ["include_unreadable"] = BoolProperty("Include properties whose value could not be read. Defaults to false.")
        });

    private static JsonObject DocumentTargetSchema(JsonObject extraProperties)
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

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("instance_id", "document_id"),
            ["properties"] = properties
        };
    }

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
                ["name"] = StringProperty("Optional object name.")
            }
        };

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
}

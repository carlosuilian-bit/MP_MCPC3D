using System.Text.Json.Nodes;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>Input schemas for the block read tools.</summary>
public static class BlockSchemas
{
    public static JsonObject BlockListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = StringProperty("Case-insensitive substring filter on the block definition name."),
            ["include_anonymous"] = BoolProperty("Include anonymous definitions (*U, *D, *X...). Defaults to false."),
            ["include_layouts"] = BoolProperty("Include model space and paper space layout records. Defaults to false.")
        });

    public static JsonObject BlockReferenceListInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["block_name"] = StringProperty(
                "Exact block name, case-insensitive. Matches a dynamic block by its own name even when the insert points at an anonymous definition."),
            ["layer"] = StringProperty("Exact layer name, case-insensitive."),
            ["include_anonymous"] = BoolProperty("Include inserts of anonymous blocks. Defaults to false; an anonymous block named in block_name is always included."),
            ["include_attributes"] = BoolProperty("Return each insert's attributes, including the definition's constant attributes. Defaults to false."),
            ["max_scan"] = IntegerProperty("Entities to inspect before stopping and reporting truncated. Defaults to 250000.", minimum: 1),
            ["max_results"] = IntegerProperty($"Inserts to return. Defaults to {BlockService.DefaultMaxResults}.", minimum: 1, maximum: BlockService.MaxResultsCap)
        });

    public static JsonObject BlockGetAttributesInput() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = StringProperty("Handle of the block insert, as hexadecimal text."),
            ["object_reference"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Object reference whose handle identifies the block insert.",
                ["properties"] = new JsonObject
                {
                    ["handle"] = StringProperty("Handle of the block insert.")
                }
            }
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

    private static JsonObject IntegerProperty(string description, int minimum, int? maximum = null)
    {
        var property = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description,
            ["minimum"] = minimum
        };

        if (maximum is { } max)
        {
            property["maximum"] = max;
        }

        return property;
    }
}

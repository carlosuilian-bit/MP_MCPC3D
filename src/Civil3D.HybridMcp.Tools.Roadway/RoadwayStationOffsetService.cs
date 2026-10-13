using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Roadway;

/// <param name="Resolved">False when the alignment could not answer for this point.</param>
/// <param name="Error">Why, when it could not.</param>
public sealed record RoadwayStationOffsetResult(bool Resolved, double? Station, double? Offset, string? Error);

/// <summary>
/// The narrow port this module needs from the Civil 3D side.
///
/// Deliberately small: the roadway layer owns the batch, and the geometry stays
/// where it already works. Keeping the port this thin is what lets this module
/// carry no Autodesk reference at all.
/// </summary>
public interface IRoadwayAlignmentAdapter
{
    bool AlignmentFound(string alignment);

    RoadwayStationOffsetResult StationOffset(string alignment, double x, double y);
}

public sealed class RoadwayToolException : Exception
{
    public RoadwayToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public RoadwayToolException(ErrorCode code, string message, JsonObject? details)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public ErrorCode Code { get; }

    /// <summary>
    /// Structured payload travelling with the error. A confirmation-required
    /// failure uses it to hand back the token, without which the caller is told
    /// to confirm and given nothing to confirm with.
    /// </summary>
    public JsonObject? Details { get; }
}

/// <summary>
/// Station and offset for a whole list of points in one call.
///
/// This replaces typing points into Civil 3D one at a time, so the behaviour
/// that matters is what happens to the list when one point misbehaves: a point
/// that falls outside the alignment is reported on its own row and the rest of
/// the batch still comes back. Failing all forty points because of one is how a
/// batch tool becomes useless in the field.
///
/// A missing alignment is different. That is one caller mistake, not forty bad
/// points, so it fails once before any point is touched.
/// </summary>
public sealed class RoadwayStationOffsetService
{
    /// <summary>
    /// Upper bound on one batch. This work runs on the Autodesk idle loop, so an
    /// unbounded list would freeze the application the operator is using.
    /// </summary>
    public const int MaxPoints = 2000;

    public RoadwayStationOffsetBatchResponse StationOffsetBatch(
        IRoadwayAlignmentAdapter adapter,
        DocumentDescriptor descriptor,
        RoadwayStationOffsetBatchRequest request)
    {
        ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);

        if (string.IsNullOrWhiteSpace(request.Alignment))
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "roadway_station_offset_batch requires alignment.");
        }

        if (request.Points.Count == 0)
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "roadway_station_offset_batch requires at least one point.");
        }

        if (request.Points.Count > MaxPoints)
        {
            throw new RoadwayToolException(
                ErrorCode.InvalidRequest,
                $"A batch is limited to {MaxPoints} points; {request.Points.Count} were supplied. Split the list.");
        }

        var alignment = request.Alignment.Trim();
        if (!adapter.AlignmentFound(alignment))
        {
            throw new RoadwayToolException(
                ErrorCode.AlignmentNotFound,
                $"Alignment '{alignment}' was not found in the target drawing.");
        }

        var points = new List<RoadwayStationOffsetPoint>(request.Points.Count);
        for (var index = 0; index < request.Points.Count; index++)
        {
            var input = request.Points[index];
            var result = Evaluate(adapter, alignment, input);

            points.Add(new RoadwayStationOffsetPoint
            {
                Index = index,
                X = input.X,
                Y = input.Y,
                Label = input.Label,
                Resolved = result.Resolved,
                Station = result.Station,
                Offset = result.Offset,
                Error = result.Error
            });
        }

        return new RoadwayStationOffsetBatchResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Alignment = alignment,
            Count = points.Count,
            ResolvedCount = points.Count(point => point.Resolved),
            FailedCount = points.Count(point => !point.Resolved),
            Points = points
        };
    }

    /// <summary>
    /// One point. An adapter that throws is turned into a failed row rather than
    /// being allowed to end the batch, because one bad point is not a reason to
    /// discard the work already done on the others.
    /// </summary>
    private static RoadwayStationOffsetResult Evaluate(
        IRoadwayAlignmentAdapter adapter,
        string alignment,
        RoadwayPointInput input)
    {
        try
        {
            return adapter.StationOffset(alignment, input.X, input.Y);
        }
        catch (Exception ex)
        {
            return new RoadwayStationOffsetResult(false, null, null, ex.Message);
        }
    }

    private static void ValidateDocumentTarget(string instanceId, string documentId, DocumentDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new RoadwayToolException(ErrorCode.DocumentAmbiguous, "document_id is required for roadway tools.");
        }

        if (!string.Equals(instanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(documentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new RoadwayToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }
    }
}

public static class RoadwayToolNames
{
    public const string StationOffsetBatch = "roadway_station_offset_batch";
    public const string PropertySetWrite = "roadway_propertyset_write";
}

public static class RoadwayToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> ListTools() =>
    [
        new()
        {
            Name = RoadwayToolNames.StationOffsetBatch,
            Description = "Station and offset for a list of points against one alignment. Reports each point on its own row, so one point outside the alignment does not fail the batch.",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = RoadwaySchemas.StationOffsetBatchInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(RoadwayStationOffsetBatchResponse)),
            Timeout = TimeSpan.FromSeconds(60),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Roadway",
            Version = ProtocolVersion.Current
        },
        new()
        {
            Name = RoadwayToolNames.PropertySetWrite,
            Description = "Update a property set across many objects in one atomic write. Takes explicit handles from propertyset_find, returns a summarised preview, and requires confirmation.",
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Medium,
            InputSchema = RoadwaySchemas.PropertySetWriteInput(),
            OutputSchema = ToolSchema.ObjectOutput(nameof(RoadwayPropertySetWriteResponse)),
            Timeout = TimeSpan.FromSeconds(120),
            RequiresConfirmation = true,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "Roadway",
            Version = ProtocolVersion.Current
        }
    ];
}

public static class RoadwaySchemas
{
    public static JsonObject StationOffsetBatchInput() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("instance_id", "document_id", "alignment", "points"),
            ["properties"] = new JsonObject
            {
                ["instance_id"] = StringProperty("Civil 3D bridge instance identifier."),
                ["document_id"] = StringProperty("Target drawing document identifier."),
                ["alignment"] = StringProperty("Alignment name, as reported by alignment_list."),
                ["points"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = RoadwayStationOffsetService.MaxPoints,
                    ["description"] = "Points to resolve, in drawing coordinates.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("x", "y"),
                        ["properties"] = new JsonObject
                        {
                            ["x"] = new JsonObject { ["type"] = "number" },
                            ["y"] = new JsonObject { ["type"] = "number" },
                            ["label"] = StringProperty("Optional caller label, echoed back untouched.")
                        }
                    }
                }
            }
        };

    public static JsonObject PropertySetWriteInput() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("instance_id", "document_id", "set_name", "objects"),
            ["properties"] = new JsonObject
            {
                ["instance_id"] = StringProperty("Civil 3D bridge instance identifier."),
                ["document_id"] = StringProperty("Target drawing document identifier."),
                ["set_name"] = StringProperty("Property set to update on every listed object."),
                ["objects"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = RoadwayPropertySetWriteService.MaxObjects,
                    ["description"] = "Explicit handles, normally taken from propertyset_find. A filter is deliberately not accepted.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("handle", "properties"),
                        ["properties"] = new JsonObject
                        {
                            ["handle"] = StringProperty("Handle of the object carrying the set."),
                            ["properties"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["minItems"] = 1,
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["additionalProperties"] = false,
                                    ["required"] = new JsonArray("name", "value"),
                                    ["properties"] = new JsonObject
                                    {
                                        ["name"] = new JsonObject { ["type"] = "string" },
                                        ["value"] = StringProperty("New value as invariant text.")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

    private static JsonObject StringProperty(string description) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description
        };
}

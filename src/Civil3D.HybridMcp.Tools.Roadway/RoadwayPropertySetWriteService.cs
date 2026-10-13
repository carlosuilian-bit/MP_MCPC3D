using System.Globalization;
using System.Text.Json.Nodes;
using System.Text;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Roadway;

/// <param name="ObjectFound">False when the handle does not resolve.</param>
/// <param name="PropertySet">Null when the object exists but does not carry the set.</param>
public sealed record RoadwayPropertySetState(bool ObjectFound, PropertySetInfo? PropertySet);

/// <param name="CorrelationId">Ties the preview a person approved to the write that follows.</param>
public sealed record RoadwayWriteContext(string CorrelationId, ConfirmationToken? Token);

/// <summary>
/// The narrow port the roadway module needs to read and write property sets,
/// defined in terms of Protocol types so this module keeps no Autodesk
/// reference.
/// </summary>
public interface IRoadwayPropertySetAdapter
{
    RoadwayPropertySetState Read(string handle, string setName);

    IReadOnlyList<PropertyValueInfo> Apply(
        string handle,
        string setName,
        IReadOnlyList<PropertySetValueUpdate> updates);
}

/// <summary>
/// Updates one property set across many objects, atomically.
///
/// Two decisions separate this from the batch read next to it.
///
/// It is **all or nothing**. A batch read may return partial results, because
/// half a list of stations is still useful. A batch write may not: 526
/// half-updated kilometre markers is a worse state than 526 untouched ones, and
/// nobody can tell by looking which half went through. So every object is
/// resolved and validated before anything is written, and one failure aborts the
/// lot.
///
/// It takes **explicit handles, never a filter**. The caller runs
/// `propertyset_find`, looks at what came back, and passes that list. The
/// platform therefore cannot expand a query into a write the operator never saw,
/// which is the failure mode that makes batch tools dangerous.
/// </summary>
public sealed class RoadwayPropertySetWriteService
{
    /// <summary>Upper bound on one batch, since this runs on the Autodesk idle loop.</summary>
    public const int MaxObjects = 5000;

    /// <summary>How many changes the preview spells out before summarising the rest.</summary>
    private const int PreviewSamples = 3;

    private const string ToolName = "roadway_propertyset_write";

    private readonly IConfirmationGate _confirmation;

    public RoadwayPropertySetWriteService(IConfirmationGate confirmation)
    {
        _confirmation = confirmation;
    }

    public RoadwayPropertySetWriteResponse Write(
        IRoadwayPropertySetAdapter adapter,
        DocumentDescriptor descriptor,
        RoadwayPropertySetWriteRequest request,
        RoadwayWriteContext context)
    {
        Validate(descriptor, request);

        var setName = request.SetName.Trim();
        var plans = new List<ObjectPlan>(request.Objects.Count);

        // Everything is resolved before anything is written, so a bad object in
        // the middle of the list cannot leave the first half committed.
        foreach (var target in request.Objects)
        {
            var handle = target.Handle.Trim();
            var state = adapter.Read(handle, setName);

            if (!state.ObjectFound)
            {
                throw new RoadwayToolException(
                    ErrorCode.ObjectNotFound,
                    $"Handle '{handle}' does not resolve in the target drawing; nothing was written.");
            }

            if (state.PropertySet is null)
            {
                throw new RoadwayToolException(
                    ErrorCode.PropertySetNotFound,
                    $"Property set '{setName}' is not attached to '{handle}'; nothing was written.");
            }

            plans.Add(new ObjectPlan(handle, BuildChanges(handle, setName, state.PropertySet, target.Properties)));
        }

        var changing = plans.Where(plan => plan.Changes.Any(change => change.Changed)).ToArray();
        if (changing.Length == 0)
        {
            throw new RoadwayToolException(
                ErrorCode.InvalidRequest,
                $"No value would change across {plans.Count} object(s); nothing to confirm or write.");
        }

        var confirmation = new ConfirmationRequest
        {
            ToolName = ToolName,
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            CorrelationId = context.CorrelationId,
            ArgumentsHash = string.Empty,
            AffectedCount = changing.Length,
            OperationClass = OperationClass.Write,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            PreviewSummary = BuildPreview(descriptor, setName, changing)
        };

        var evaluation = _confirmation.Evaluate(confirmation, context.Token);
        switch (evaluation.Decision)
        {
            case ConfirmationDecision.Required:
                throw new RoadwayToolException(
                    ErrorCode.ConfirmationRequired,
                    confirmation.PreviewSummary!,
                    new JsonObject
                    {
                        ["preview"] = confirmation.PreviewSummary,
                        ["affected_count"] = confirmation.AffectedCount,
                        ["confirmation_token"] = JsonContractOptions.ToJsonNode(evaluation.IssuedToken)
                    });
            case ConfirmationDecision.Invalid:
                throw new RoadwayToolException(
                    ErrorCode.ConfirmationInvalid,
                    "The confirmation does not match this batch. Confirm the preview returned for these exact objects and values.");
            case ConfirmationDecision.Expired:
                throw new RoadwayToolException(
                    ErrorCode.ConfirmationExpired,
                    "The confirmation has expired. Request the preview again and confirm the current values.");
        }

        var results = new List<RoadwayPropertySetResult>(plans.Count);
        foreach (var plan in plans)
        {
            var changed = plan.Changes.Where(change => change.Changed).ToArray();
            if (changed.Length == 0)
            {
                // Already holds the requested values. Reported, not written.
                results.Add(new RoadwayPropertySetResult
                {
                    Handle = plan.Handle,
                    Changed = false,
                    Properties = plan.Changes
                });
                continue;
            }

            var applied = adapter.Apply(
                plan.Handle,
                setName,
                changed.Select(change => new PropertySetValueUpdate { Name = change.Name, Value = change.NewValue! }).ToArray());

            results.Add(new RoadwayPropertySetResult
            {
                Handle = plan.Handle,
                Changed = true,
                Properties = Merge(plan.Changes, applied)
            });
        }

        return new RoadwayPropertySetWriteResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            SetName = setName,
            Applied = true,
            ObjectCount = results.Count,
            ChangedCount = results.Count(result => result.Changed),
            SkippedCount = results.Count(result => !result.Changed),
            Objects = results
        };
    }

    private static void Validate(DocumentDescriptor descriptor, RoadwayPropertySetWriteRequest request)
    {
        if (!string.Equals(request.InstanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(request.DocumentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new RoadwayToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }

        if (string.IsNullOrWhiteSpace(request.SetName))
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "roadway_propertyset_write requires set_name.");
        }

        if (request.Objects.Count == 0)
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "roadway_propertyset_write requires at least one object.");
        }

        if (request.Objects.Count > MaxObjects)
        {
            throw new RoadwayToolException(
                ErrorCode.InvalidRequest,
                $"A batch is limited to {MaxObjects} objects; {request.Objects.Count} were supplied.");
        }

        var duplicate = request.Objects
            .GroupBy(target => target.Handle?.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            // Two updates to one object make the result depend on ordering.
            throw new RoadwayToolException(
                ErrorCode.InvalidRequest,
                $"Handle '{duplicate.Key}' appears more than once; each object must be named once per batch.");
        }

        foreach (var target in request.Objects)
        {
            if (string.IsNullOrWhiteSpace(target.Handle))
            {
                throw new RoadwayToolException(ErrorCode.InvalidRequest, "Every object requires a handle.");
            }

            if (target.Properties.Count == 0)
            {
                throw new RoadwayToolException(
                    ErrorCode.InvalidRequest,
                    $"Object '{target.Handle}' has no properties to write.");
            }
        }
    }

    private static IReadOnlyList<PropertySetValueChange> BuildChanges(
        string handle,
        string setName,
        PropertySetInfo propertySet,
        IReadOnlyList<PropertySetValueUpdate> updates)
    {
        var changes = new List<PropertySetValueChange>(updates.Count);

        foreach (var update in updates)
        {
            var current = propertySet.Properties
                .FirstOrDefault(property => string.Equals(property.Name, update.Name, StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                throw new RoadwayToolException(
                    ErrorCode.PropertyNotFound,
                    $"Property '{update.Name}' is not defined on '{setName}' for object '{handle}'; nothing was written.");
            }

            if (current.IsReadOnly == true)
            {
                throw new RoadwayToolException(
                    ErrorCode.InvalidRequest,
                    $"Property '{current.Name}' is read-only on '{setName}' for object '{handle}'.");
            }

            changes.Add(new PropertySetValueChange
            {
                Name = current.Name,
                DataType = current.DataType,
                PreviousValue = current.Value,
                PreviousNumericValue = current.NumericValue,
                NewValue = update.Value,
                NewNumericValue = ParseNumeric(update.Value),
                Changed = !string.Equals(current.Value, update.Value, StringComparison.Ordinal)
            });
        }

        return changes;
    }

    /// <summary>
    /// The text a person reads before approving hundreds of changes.
    ///
    /// Spelling out every line would produce a preview nobody reads, which is
    /// worse than none: it transfers responsibility without transferring
    /// understanding. So it leads with the scale, names the set and the drawing,
    /// and shows a few real before/after pairs so the change is recognisable.
    /// </summary>
    private static string BuildPreview(
        DocumentDescriptor descriptor,
        string setName,
        IReadOnlyList<ObjectPlan> changing)
    {
        var totalValues = changing.Sum(plan => plan.Changes.Count(change => change.Changed));
        var preview = new StringBuilder();
        preview.Append(CultureInfo.InvariantCulture,
            $"Update {totalValues} value(s) across {changing.Count} object(s) in property set '{setName}' in '{descriptor.Name}'.");

        foreach (var plan in changing.Take(PreviewSamples))
        {
            foreach (var change in plan.Changes.Where(change => change.Changed))
            {
                preview.Append(CultureInfo.InvariantCulture,
                    $" {plan.Handle} {change.Name}: '{change.PreviousValue}' -> '{change.NewValue}';");
            }
        }

        if (changing.Count > PreviewSamples)
        {
            preview.Append(CultureInfo.InvariantCulture, $" and {changing.Count - PreviewSamples} more object(s).");
        }

        return preview.ToString().TrimEnd(';');
    }

    private static IReadOnlyList<PropertySetValueChange> Merge(
        IReadOnlyList<PropertySetValueChange> planned,
        IReadOnlyList<PropertyValueInfo> applied) =>
        planned
            .Select(change =>
            {
                var result = applied.FirstOrDefault(value =>
                    string.Equals(value.Name, change.Name, StringComparison.OrdinalIgnoreCase));

                return result is null
                    ? change
                    : change with { NewValue = result.Value, NewNumericValue = result.NumericValue };
            })
            .ToArray();

    private static double? ParseNumeric(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private sealed record ObjectPlan(string Handle, IReadOnlyList<PropertySetValueChange> Changes);
}

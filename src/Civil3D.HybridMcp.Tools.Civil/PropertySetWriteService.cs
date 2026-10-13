using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Civil;

/// <param name="CorrelationId">Ties the preview a person approved to the write that follows.</param>
/// <param name="Token">The confirmation the caller is presenting, if any.</param>
public sealed record PropertySetWriteContext(string CorrelationId, ConfirmationToken? Token);

/// <param name="ObjectFound">False when the handle does not resolve at all.</param>
/// <param name="PropertySet">Null when the object exists but does not carry the set.</param>
public sealed record PropertySetReadState(bool ObjectFound, PropertySetInfo? PropertySet);

public interface IPropertySetWriteAdapter
{
    PropertySetReadState Read(string handle, string setName);

    IReadOnlyList<PropertyValueInfo> Apply(
        string handle,
        string setName,
        IReadOnlyList<PropertySetValueUpdate> updates);
}

/// <summary>
/// Updates values in an AEC property set on an object an engineer already
/// populated.
///
/// Every earlier write in this platform was bounded to something the platform
/// had just created: a new layer, a temporary DBPoint. This one edits production
/// data, so it is the first tool that refuses to act until a person has seen the
/// exact before and after and approved that exact change.
///
/// The order of the checks is the design. The object, the set and every property
/// are resolved first, and the preview is built from real current values, so the
/// confirmation a person sees describes what will actually happen rather than
/// what was requested.
/// </summary>
public sealed class PropertySetWriteService
{
    private const string ToolName = "propertyset_set";

    private readonly IConfirmationGate _confirmation;

    public PropertySetWriteService(IConfirmationGate confirmation)
    {
        _confirmation = confirmation;
    }

    public PropertySetUpdateResponse SetPropertySet(
        IPropertySetWriteAdapter adapter,
        DocumentDescriptor descriptor,
        PropertySetUpdateRequest request,
        PropertySetWriteContext context)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        Validate(request);

        var state = adapter.Read(request.Handle.Trim(), request.SetName.Trim());
        if (!state.ObjectFound)
        {
            throw new CivilToolException(
                ErrorCode.ObjectNotFound,
                $"Handle '{request.Handle}' does not resolve to an object in the target drawing.");
        }

        if (state.PropertySet is null)
        {
            throw new CivilToolException(
                ErrorCode.PropertySetNotFound,
                $"Property set '{request.SetName}' is not attached to handle '{request.Handle}'. This tool updates attached sets; it does not attach them.");
        }

        var changes = BuildChanges(state.PropertySet, request.Properties);
        var changed = changes.Where(change => change.Changed).ToArray();

        if (changed.Length == 0)
        {
            // Asking a person to approve a write that changes nothing teaches
            // them to approve without reading.
            throw new CivilToolException(
                ErrorCode.InvalidRequest,
                $"No value would change on property set '{request.SetName}'; nothing to confirm or write.");
        }

        var confirmation = new ConfirmationRequest
        {
            ToolName = ToolName,
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            CorrelationId = context.CorrelationId,
            ArgumentsHash = string.Empty,
            AffectedCount = changed.Length,
            OperationClass = OperationClass.Write,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            PreviewSummary = BuildPreview(descriptor, request, changed)
        };

        var evaluation = _confirmation.Evaluate(confirmation, context.Token);
        switch (evaluation.Decision)
        {
            case ConfirmationDecision.Required:
                // The token travels back with the refusal. Telling a caller to
                // confirm without handing them the thing to confirm with would
                // make the tool unusable end to end.
                throw new CivilToolException(
                    ErrorCode.ConfirmationRequired,
                    confirmation.PreviewSummary!,
                    new JsonObject
                    {
                        ["preview"] = confirmation.PreviewSummary,
                        ["affected_count"] = confirmation.AffectedCount,
                        ["confirmation_token"] = JsonContractOptions.ToJsonNode(evaluation.IssuedToken)
                    });
            case ConfirmationDecision.Invalid:
                throw new CivilToolException(
                    ErrorCode.ConfirmationInvalid,
                    "The confirmation does not match this write. Confirm the preview that was returned for these exact values.");
            case ConfirmationDecision.Expired:
                throw new CivilToolException(
                    ErrorCode.ConfirmationExpired,
                    "The confirmation has expired. Request the preview again and confirm the current values.");
        }

        // Only the values that actually change are written.
        var applied = adapter.Apply(
            request.Handle.Trim(),
            request.SetName.Trim(),
            changed.Select(change => new PropertySetValueUpdate { Name = change.Name, Value = change.NewValue! }).ToArray());

        return new PropertySetUpdateResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Handle = request.Handle.Trim(),
            SetName = request.SetName.Trim(),
            Applied = true,
            ChangedCount = changed.Length,
            Properties = Merge(changed, applied)
        };
    }

    private static void Validate(PropertySetUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Handle))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "propertyset_set requires handle.");
        }

        if (string.IsNullOrWhiteSpace(request.SetName))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "propertyset_set requires set_name.");
        }

        if (request.Properties.Count == 0)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "propertyset_set requires at least one property.");
        }

        var duplicate = request.Properties
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new CivilToolException(
                ErrorCode.InvalidRequest,
                $"Property '{duplicate.Key}' appears more than once; a write must name each property once.");
        }
    }

    private static IReadOnlyList<PropertySetValueChange> BuildChanges(
        PropertySetInfo propertySet,
        IReadOnlyList<PropertySetValueUpdate> updates)
    {
        var changes = new List<PropertySetValueChange>();

        foreach (var update in updates)
        {
            var current = propertySet.Properties
                .FirstOrDefault(property => string.Equals(property.Name, update.Name, StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                throw new CivilToolException(
                    ErrorCode.PropertyNotFound,
                    $"Property '{update.Name}' is not defined on property set '{propertySet.Name}'. This tool updates existing properties; it does not add them.");
            }

            if (current.IsReadOnly == true)
            {
                throw new CivilToolException(
                    ErrorCode.InvalidRequest,
                    $"Property '{current.Name}' is read-only on property set '{propertySet.Name}'.");
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
    /// The text a person reads before approving. It names the drawing, the
    /// object, the set and every before/after pair, because that is what they are
    /// being asked to take responsibility for.
    /// </summary>
    private static string BuildPreview(
        DocumentDescriptor descriptor,
        PropertySetUpdateRequest request,
        IReadOnlyList<PropertySetValueChange> changes)
    {
        var preview = new StringBuilder();
        preview.Append(CultureInfo.InvariantCulture, $"Update {changes.Count} value(s) in property set '{request.SetName}' ");
        preview.Append(CultureInfo.InvariantCulture, $"on object {request.Handle} in '{descriptor.Name}':");

        foreach (var change in changes)
        {
            preview.Append(CultureInfo.InvariantCulture, $" {change.Name}: '{change.PreviousValue}' -> '{change.NewValue}';");
        }

        return preview.ToString().TrimEnd(';');
    }

    private static IReadOnlyList<PropertySetValueChange> Merge(
        IReadOnlyList<PropertySetValueChange> planned,
        IReadOnlyList<PropertyValueInfo> applied)
    {
        return planned
            .Select(change =>
            {
                var result = applied.FirstOrDefault(value =>
                    string.Equals(value.Name, change.Name, StringComparison.OrdinalIgnoreCase));

                // The adapter reports what the drawing now holds, which is the
                // honest "after" rather than what was requested.
                return result is null
                    ? change
                    : change with { NewValue = result.Value, NewNumericValue = result.NumericValue };
            })
            .ToArray();
    }

    private static double? ParseNumeric(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}

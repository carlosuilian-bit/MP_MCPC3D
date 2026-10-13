using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public interface IArgumentsHasher
{
    string ComputeHash(JsonNode? arguments);
}

public sealed class CanonicalJsonArgumentsHasher : IArgumentsHasher
{
    public string ComputeHash(JsonNode? arguments)
    {
        var json = arguments?.ToJsonString(JsonContractOptions.Default) ?? "{}";
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }
}

public interface IConfirmationTokenService
{
    ConfirmationToken CreateToken(ConfirmationRequest request, DateTimeOffset utcNow);
    ConfirmationValidationResult ValidateToken(
        ConfirmationToken? token,
        ConfirmationRequest request,
        DateTimeOffset utcNow);
}

public sealed class HmacConfirmationTokenService : IConfirmationTokenService
{
    private readonly byte[] _secret;

    public HmacConfirmationTokenService(byte[]? secret = null)
    {
        _secret = secret is { Length: > 0 } ? secret : RandomNumberGenerator.GetBytes(32);
    }

    public ConfirmationToken CreateToken(ConfirmationRequest request, DateTimeOffset utcNow)
    {
        var token = new ConfirmationToken
        {
            TokenId = Guid.NewGuid().ToString("N"),
            ToolName = request.ToolName,
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            CorrelationId = request.CorrelationId,
            ArgumentsHash = request.ArgumentsHash,
            AffectedCount = request.AffectedCount,
            OperationClass = request.OperationClass,
            IssuedUtc = utcNow,
            ExpiresUtc = request.ExpiresUtc,
            Signature = ""
        };

        return token with { Signature = Sign(token) };
    }

    public ConfirmationValidationResult ValidateToken(
        ConfirmationToken? token,
        ConfirmationRequest request,
        DateTimeOffset utcNow)
    {
        if (token is null)
        {
            return ConfirmationValidationResult.Invalid(ErrorCode.ConfirmationRequired, "Confirmation token is required.");
        }

        if (utcNow > token.ExpiresUtc || utcNow > request.ExpiresUtc)
        {
            return ConfirmationValidationResult.Invalid(ErrorCode.ConfirmationExpired, "Confirmation token expired.");
        }

        if (!Matches(token, request))
        {
            return ConfirmationValidationResult.Invalid(ErrorCode.ConfirmationInvalid, "Confirmation token does not match the requested operation.");
        }

        var expected = Sign(token with { Signature = "" });
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(token.Signature);
        if (expectedBytes.Length != actualBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return ConfirmationValidationResult.Invalid(ErrorCode.ConfirmationInvalid, "Confirmation token signature is invalid.");
        }

        return ConfirmationValidationResult.Valid();
    }

    private string Sign(ConfirmationToken token)
    {
        using var hmac = new HMACSHA256(_secret);
        var payload = string.Join(
            "|",
            token.TokenId,
            token.ToolName,
            token.InstanceId,
            token.DocumentId,
            token.CorrelationId,
            token.ArgumentsHash,
            token.AffectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            token.OperationClass,
            token.IssuedUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            token.ExpiresUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static bool Matches(ConfirmationToken token, ConfirmationRequest request) =>
        string.Equals(token.ToolName, request.ToolName, StringComparison.Ordinal) &&
        string.Equals(token.InstanceId, request.InstanceId, StringComparison.Ordinal) &&
        string.Equals(token.DocumentId, request.DocumentId, StringComparison.Ordinal) &&
        string.Equals(token.CorrelationId, request.CorrelationId, StringComparison.Ordinal) &&
        string.Equals(token.ArgumentsHash, request.ArgumentsHash, StringComparison.Ordinal) &&
        token.AffectedCount == request.AffectedCount &&
        token.OperationClass == request.OperationClass;
}


/// <summary>What the confirmation gate decided about one write.</summary>
public enum ConfirmationDecision
{
    /// <summary>A valid token covers this exact write; it may proceed.</summary>
    Approved,

    /// <summary>No token was supplied. The caller has to confirm first.</summary>
    Required,

    /// <summary>A token was supplied but does not match this write.</summary>
    Invalid,

    /// <summary>A token was supplied and matched, but its validity has passed.</summary>
    Expired
}

/// <param name="Decision">Whether the write may proceed.</param>
/// <param name="IssuedToken">
/// The token the caller presents back to approve. Present only when a
/// confirmation is required: without returning it, the caller would be told to
/// confirm and given no way to do so.
/// </param>
public sealed record ConfirmationEvaluation(ConfirmationDecision Decision, ConfirmationToken? IssuedToken);

/// <summary>
/// Decides whether a write may proceed. Hides token signing and argument hashing
/// so the write rules can be tested without a crypto dependency.
/// </summary>
public interface IConfirmationGate
{
    ConfirmationEvaluation Evaluate(ConfirmationRequest request, ConfirmationToken? token);
}


/// <summary>
/// The production gate. Binds a confirmation to one tool, one instance, one
/// document and one exact set of arguments, using the HMAC token service that
/// has existed in Core since Phase 1 and had no consumer until now.
///
/// The arguments hash covers the request body only. The token travels in the
/// request envelope, not in the arguments, so a token can never be part of the
/// thing it is confirming.
/// </summary>
public sealed class HmacConfirmationGate : IConfirmationGate
{
    private readonly IConfirmationTokenService _tokens;
    private readonly IArgumentsHasher _hasher;
    private readonly JsonNode? _arguments;
    private readonly Func<DateTimeOffset> _clock;

    public HmacConfirmationGate(
        IConfirmationTokenService tokens,
        IArgumentsHasher hasher,
        JsonNode? arguments,
        Func<DateTimeOffset>? clock = null)
    {
        _tokens = tokens;
        _hasher = hasher;
        _arguments = arguments;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ConfirmationEvaluation Evaluate(ConfirmationRequest request, ConfirmationToken? token)
    {
        var bound = request with { ArgumentsHash = _hasher.ComputeHash(_arguments) };
        if (token is null)
        {
            LastRequest = bound;
            LastToken = _tokens.CreateToken(bound, _clock());
            return new ConfirmationEvaluation(ConfirmationDecision.Required, LastToken);
        }

        var validation = _tokens.ValidateToken(token, bound, _clock());
        if (validation.Success)
        {
            return new ConfirmationEvaluation(ConfirmationDecision.Approved, null);
        }

        return new ConfirmationEvaluation(
            validation.Error?.Code == ErrorCode.ConfirmationExpired
                ? ConfirmationDecision.Expired
                : ConfirmationDecision.Invalid,
            null);
    }

    /// <summary>The confirmation the caller must approve, populated when one is required.</summary>
    public ConfirmationRequest? LastRequest { get; private set; }

    /// <summary>The token the caller presents back to approve it.</summary>
    public ConfirmationToken? LastToken { get; private set; }
}

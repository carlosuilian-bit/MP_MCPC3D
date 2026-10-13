using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public sealed record VersionNegotiationResult
{
    public required CompatibilityStatus Status { get; init; }
    public required ProtocolVersion ServerProtocol { get; init; }
    public required ProtocolVersion BridgeProtocol { get; init; }
    public required string Reason { get; init; }
}

public static class VersionNegotiator
{
    public static VersionNegotiationResult Evaluate(
        ProtocolVersion serverProtocol,
        ProtocolVersion bridgeProtocol)
    {
        if (serverProtocol.Major != bridgeProtocol.Major)
        {
            return new VersionNegotiationResult
            {
                Status = CompatibilityStatus.Incompatible,
                ServerProtocol = serverProtocol,
                BridgeProtocol = bridgeProtocol,
                Reason = "Protocol major versions differ."
            };
        }

        if (bridgeProtocol.Minor < serverProtocol.Minor)
        {
            return new VersionNegotiationResult
            {
                Status = CompatibilityStatus.PartiallyCompatible,
                ServerProtocol = serverProtocol,
                BridgeProtocol = bridgeProtocol,
                Reason = "Bridge protocol minor version is lower than the server requires."
            };
        }

        return new VersionNegotiationResult
        {
            Status = CompatibilityStatus.Compatible,
            ServerProtocol = serverProtocol,
            BridgeProtocol = bridgeProtocol,
            Reason = bridgeProtocol.Minor == serverProtocol.Minor
                ? "Protocol versions are compatible."
                : "Bridge protocol minor version is newer and same-major compatible."
        };
    }

    public static bool IsToolContractCompatible(ProtocolVersion supportedVersion, ToolDescriptor descriptor) =>
        supportedVersion.Major == descriptor.Version.Major &&
        supportedVersion.Minor >= descriptor.Version.Minor;
}


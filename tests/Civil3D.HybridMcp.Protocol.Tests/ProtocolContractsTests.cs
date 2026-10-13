using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Protocol.Tests;

public sealed class ProtocolContractsTests
{
    [Fact]
    public void ProtocolVersionParsesAndCompares()
    {
        var parsed = ProtocolVersion.Parse("1.2.3");

        Assert.Equal(1, parsed.Major);
        Assert.True(parsed.CompareTo(new ProtocolVersion(1, 2, 2)) > 0);
        Assert.Equal("1.2.3", parsed.ToString());
    }

    [Fact]
    public void EndpointDescriptorSerializesWithStructuredCapabilities()
    {
        var descriptor = TestContracts.EndpointDescriptor("instance-a");

        var json = JsonSerializer.Serialize(descriptor, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<EndpointDescriptor>(json, JsonContractOptions.Default);

        Assert.NotNull(roundTrip);
        Assert.Equal(descriptor.InstanceId, roundTrip!.InstanceId);
        Assert.True(roundTrip.Capabilities.Capabilities.Core);
        Assert.Equal("Civil3DHybridMcp-123-instance-a", roundTrip.PipeName);
    }

    [Fact]
    public void ToolResponseCarriesStructuredErrors()
    {
        var response = new ToolResponse
        {
            Success = false,
            Tool = "drawing_info",
            CorrelationId = "corr-1",
            Error = new ErrorEnvelope
            {
                Code = ErrorCode.DocumentNotFound,
                Message = "No active drawing.",
                CorrelationId = "corr-1"
            }
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ToolResponse>(json, JsonContractOptions.Default);

        Assert.NotNull(roundTrip?.Error);
        Assert.Equal(ErrorCode.DocumentNotFound, roundTrip!.Error!.Code);
        Assert.Equal("corr-1", roundTrip.Error.CorrelationId);
    }

    [Fact]
    public void ProtocolAssemblyDoesNotReferenceAutodeskAssemblies()
    {
        var references = typeof(ProtocolVersion).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, name => name.Name?.Contains("Autodesk", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, name => name.Name?.StartsWith("Ac", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, name => name.Name?.StartsWith("Aecc", StringComparison.OrdinalIgnoreCase) == true);
    }
}

internal static class TestContracts
{
    public static EndpointDescriptor EndpointDescriptor(string instanceId) =>
        new()
        {
            InstanceId = instanceId,
            ProcessId = 123,
            ProcessStartTimeUtc = DateTimeOffset.UtcNow,
            Product = "Autodesk Civil 3D",
            ProductVersion = "2026",
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            PipeName = $"Civil3DHybridMcp-123-{instanceId}",
            StartedUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            Capabilities = new CapabilityManifest
            {
                InstanceId = instanceId,
                Product = "Autodesk Civil 3D",
                ProductVersion = "2026",
                Capabilities = new CapabilityFlags
                {
                    Core = true,
                    AutoCad = true,
                    Civil3D = true,
                    Diagnostics = true,
                    DialogMonitor = "partial"
                }
            }
        };
}


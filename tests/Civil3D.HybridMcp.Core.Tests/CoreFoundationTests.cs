using System.Diagnostics;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core.Tests;

public sealed class CoreFoundationTests
{
    [Fact]
    public void VersionNegotiationClassifiesCompatiblePartialAndIncompatible()
    {
        Assert.Equal(
            CompatibilityStatus.Compatible,
            VersionNegotiator.Evaluate(new ProtocolVersion(1, 0, 0), new ProtocolVersion(1, 1, 0)).Status);

        Assert.Equal(
            CompatibilityStatus.PartiallyCompatible,
            VersionNegotiator.Evaluate(new ProtocolVersion(1, 2, 0), new ProtocolVersion(1, 1, 0)).Status);

        Assert.Equal(
            CompatibilityStatus.Incompatible,
            VersionNegotiator.Evaluate(new ProtocolVersion(1, 0, 0), new ProtocolVersion(2, 0, 0)).Status);
    }

    [Fact]
    public void EndpointRegistryRejectsStaleHeartbeat()
    {
        using var temp = TempDirectory.Create();
        var options = new EndpointRegistryOptions
        {
            DirectoryPath = temp.Path,
            StaleAfter = TimeSpan.FromSeconds(5)
        };
        var descriptor = EndpointDescriptorFactory.Create("stale-1") with
        {
            LastHeartbeatUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10)
        };

        new EndpointRegistryWriter(options).Write(descriptor);

        var reader = new EndpointRegistryReader(options);
        Assert.Empty(reader.ListValidEndpoints(cleanupStale: true));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.json"));
    }

    [Fact]
    public void EndpointRegistryValidatesPidAndProcessStartTime()
    {
        using var temp = TempDirectory.Create();
        var options = new EndpointRegistryOptions { DirectoryPath = temp.Path };
        var descriptor = EndpointDescriptorFactory.Create("valid-1");

        new EndpointRegistryWriter(options).Write(descriptor);

        var endpoints = new EndpointRegistryReader(options).ListValidEndpoints();
        Assert.Single(endpoints);
        Assert.Equal("valid-1", endpoints[0].InstanceId);
    }

    [Fact]
    public void EndpointSelectorRejectsAmbiguousInstance()
    {
        var endpoints = new[]
        {
            EndpointDescriptorFactory.Create("a"),
            EndpointDescriptorFactory.Create("b")
        };

        var selection = EndpointSelector.Select(endpoints);

        Assert.False(selection.Success);
        Assert.Equal(ErrorCode.InstanceAmbiguous, selection.Error?.Code);
    }

    [Fact]
    public void DocumentIdentityUsesInstancePathAndSession()
    {
        var first = DocumentIdentity.ComputeDocumentId("i1", "a.dwg", "C:\\Temp\\a.dwg", "doc-session-1");
        var second = DocumentIdentity.ComputeDocumentId("i1", "a.dwg", "C:\\Temp\\a.dwg", "doc-session-1");
        var differentSession = DocumentIdentity.ComputeDocumentId("i1", "a.dwg", "C:\\Temp\\a.dwg", "doc-session-2");

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentSession);
    }

    [Fact]
    public void ArgumentsHashIsCanonicalForObjectPropertyOrder()
    {
        var hasher = new CanonicalJsonArgumentsHasher();
        var a = JsonNode.Parse("""{"b":2,"a":1}""");
        var b = JsonNode.Parse("""{"a":1,"b":2}""");

        Assert.Equal(hasher.ComputeHash(a), hasher.ComputeHash(b));
    }

    [Fact]
    public void ConfirmationTokenValidatesBoundFieldsAndExpiry()
    {
        var service = new HmacConfirmationTokenService("0123456789abcdef0123456789abcdef"u8.ToArray());
        var request = new ConfirmationRequest
        {
            ToolName = "object_delete",
            InstanceId = "instance-1",
            DocumentId = "document-1",
            CorrelationId = "corr-1",
            ArgumentsHash = "abc123",
            AffectedCount = 2,
            OperationClass = OperationClass.Destructive,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        var token = service.CreateToken(request, DateTimeOffset.UtcNow);
        var valid = service.ValidateToken(token, request, DateTimeOffset.UtcNow);
        var wrongDocument = service.ValidateToken(token, request with { DocumentId = "other" }, DateTimeOffset.UtcNow);
        var expired = service.ValidateToken(token, request, DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.True(valid.Success);
        Assert.False(wrongDocument.Success);
        Assert.Equal(ErrorCode.ConfirmationInvalid, wrongDocument.Error?.Code);
        Assert.False(expired.Success);
        Assert.Equal(ErrorCode.ConfirmationExpired, expired.Error?.Code);
    }

    [Fact]
    public async Task JsonLinesRequestLoggerWritesAuditLine()
    {
        using var temp = TempDirectory.Create();
        var logger = new JsonLinesRequestLogger(temp.Path);

        await logger.LogAsync(new RequestLogEntry
        {
            CorrelationId = "corr-log",
            SessionId = "session",
            InstanceId = "instance",
            DocumentId = "doc",
            Tool = "civil3d_ping",
            Status = "success",
            Duration = TimeSpan.FromMilliseconds(1)
        });

        var file = Assert.Single(Directory.EnumerateFiles(temp.Path, "*.jsonl"));
        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("civil3d_ping", text);
        Assert.Contains("corr-log", text);
    }
}

internal static class EndpointDescriptorFactory
{
    public static EndpointDescriptor Create(string instanceId)
    {
        var process = Process.GetCurrentProcess();
        return new EndpointDescriptor
        {
            InstanceId = instanceId,
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Product = "Autodesk Civil 3D",
            ProductVersion = "2026",
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            PipeName = $"{ProtocolConstants.PipePrefix}-{process.Id}-{instanceId}",
            StartedUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            ActiveDocument = new DocumentDescriptor
            {
                InstanceId = instanceId,
                DocumentId = $"doc-{instanceId}",
                Name = "Test.dwg",
                FullPath = "C:\\Temp\\Test.dwg",
                IsActive = true,
                IsSaved = true,
                IsReadOnly = false,
                Units = "Meters",
                DatabaseFingerprint = $"fingerprint-{instanceId}",
                SessionIdentifier = $"session-{instanceId}"
            },
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
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Civil3DHybridMcpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}


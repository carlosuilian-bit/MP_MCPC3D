using System.Text.Json;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public sealed record RequestLogEntry
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string CorrelationId { get; init; }
    public string? SessionId { get; init; }
    public string? InstanceId { get; init; }
    public string? DocumentId { get; init; }
    public required string Tool { get; init; }
    public TimeSpan Duration { get; init; }
    public required string Status { get; init; }
    public ErrorCode? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IRequestLogger
{
    Task LogAsync(RequestLogEntry entry, CancellationToken cancellationToken = default);
}

public sealed class JsonLinesRequestLogger : IRequestLogger
{
    private readonly string _path;

    public JsonLinesRequestLogger(string? directoryPath = null)
    {
        var root = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProtocolConstants.EndpointFolderName,
            ProtocolConstants.LogSubfolderName);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, $"requests-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
    }

    public async Task LogAsync(RequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(entry, JsonContractOptions.Default);
        await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken);
    }
}


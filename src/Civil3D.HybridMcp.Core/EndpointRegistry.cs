using System.Diagnostics;
using System.Text.Json;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public sealed record EndpointRegistryOptions
{
    public required string DirectoryPath { get; init; }
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ProcessStartTolerance { get; init; } = TimeSpan.FromSeconds(2);

    public static EndpointRegistryOptions Default()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new EndpointRegistryOptions
        {
            DirectoryPath = Path.Combine(
                root,
                ProtocolConstants.EndpointFolderName,
                ProtocolConstants.EndpointSubfolderName)
        };
    }
}

public sealed record EndpointValidationResult
{
    public required EndpointHealth Health { get; init; }
    public required bool IsValid { get; init; }
    public required string Reason { get; init; }
}

public sealed class EndpointRegistryWriter
{
    private readonly EndpointRegistryOptions _options;

    public EndpointRegistryWriter(EndpointRegistryOptions options)
    {
        _options = options;
    }

    public string Write(EndpointDescriptor descriptor)
    {
        Directory.CreateDirectory(_options.DirectoryPath);
        var path = GetPath(descriptor);
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(descriptor, JsonContractOptions.Default);
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public string Heartbeat(EndpointDescriptor descriptor, DateTimeOffset utcNow)
    {
        var refreshed = descriptor with { LastHeartbeatUtc = utcNow };
        return Write(refreshed);
    }

    public void Remove(EndpointDescriptor descriptor)
    {
        var path = GetPath(descriptor);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public string GetPath(EndpointDescriptor descriptor) =>
        Path.Combine(_options.DirectoryPath, GetFileName(descriptor.ProcessId, descriptor.InstanceId));

    public static string GetFileName(int processId, string instanceId) =>
        $"civil3d-hybrid-mcp-{processId}-{Sanitize(instanceId)}.json";

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }
}

public sealed class EndpointRegistryReader
{
    private readonly EndpointRegistryOptions _options;

    public EndpointRegistryReader(EndpointRegistryOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<EndpointDescriptor> ListValidEndpoints(bool cleanupStale = false)
    {
        if (!Directory.Exists(_options.DirectoryPath))
        {
            return Array.Empty<EndpointDescriptor>();
        }

        var valid = new List<EndpointDescriptor>();
        foreach (var path in Directory.EnumerateFiles(_options.DirectoryPath, "*.json"))
        {
            EndpointDescriptor? descriptor = null;
            try
            {
                descriptor = JsonSerializer.Deserialize<EndpointDescriptor>(
                    File.ReadAllText(path),
                    JsonContractOptions.Default);
            }
            catch
            {
                if (cleanupStale)
                {
                    TryDelete(path);
                }

                continue;
            }

            if (descriptor is null)
            {
                if (cleanupStale)
                {
                    TryDelete(path);
                }

                continue;
            }

            var validation = Validate(descriptor, DateTimeOffset.UtcNow);
            if (validation.IsValid)
            {
                valid.Add(descriptor);
            }
            else if (cleanupStale)
            {
                TryDelete(path);
            }
        }

        return valid
            .OrderByDescending(endpoint => endpoint.LastHeartbeatUtc)
            .ToArray();
    }

    public EndpointValidationResult Validate(EndpointDescriptor descriptor, DateTimeOffset utcNow)
    {
        if (utcNow - descriptor.LastHeartbeatUtc > _options.StaleAfter)
        {
            return new EndpointValidationResult
            {
                Health = EndpointHealth.StaleHeartbeat,
                IsValid = false,
                Reason = "Endpoint heartbeat is stale."
            };
        }

        try
        {
            using var process = Process.GetProcessById(descriptor.ProcessId);
            if (process.HasExited)
            {
                return new EndpointValidationResult
                {
                    Health = EndpointHealth.ProcessExited,
                    IsValid = false,
                    Reason = "Process has exited."
                };
            }

            var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            var delta = (actualStart - descriptor.ProcessStartTimeUtc).Duration();
            if (delta > _options.ProcessStartTolerance)
            {
                return new EndpointValidationResult
                {
                    Health = EndpointHealth.ProcessStartMismatch,
                    IsValid = false,
                    Reason = "PID is alive but process start time does not match descriptor."
                };
            }
        }
        catch
        {
            return new EndpointValidationResult
            {
                Health = EndpointHealth.ProcessExited,
                IsValid = false,
                Reason = "Process ID is not running."
            };
        }

        if (string.IsNullOrWhiteSpace(descriptor.PipeName))
        {
            return new EndpointValidationResult
            {
                Health = EndpointHealth.InvalidDescriptor,
                IsValid = false,
                Reason = "Endpoint descriptor has no pipe name."
            };
        }

        return new EndpointValidationResult
        {
            Health = EndpointHealth.Healthy,
            IsValid = true,
            Reason = "Endpoint descriptor is valid."
        };
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}

public sealed record EndpointSelectorCriteria
{
    public string? InstanceId { get; init; }
}

public sealed record EndpointSelection
{
    public bool Success { get; init; }
    public EndpointDescriptor? Endpoint { get; init; }
    public ErrorEnvelope? Error { get; init; }
}

public static class EndpointSelector
{
    public static EndpointSelection Select(
        IReadOnlyList<EndpointDescriptor> endpoints,
        EndpointSelectorCriteria? criteria = null)
    {
        var candidates = endpoints;
        if (!string.IsNullOrWhiteSpace(criteria?.InstanceId))
        {
            candidates = endpoints
                .Where(endpoint => string.Equals(endpoint.InstanceId, criteria.InstanceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (candidates.Count == 0)
        {
            return Failure(ErrorCode.InstanceNotFound, "No matching Civil 3D bridge endpoint was found.");
        }

        if (candidates.Count > 1 && string.IsNullOrWhiteSpace(criteria?.InstanceId))
        {
            return Failure(ErrorCode.InstanceAmbiguous, "Multiple Civil 3D bridge endpoints are available. Specify instance_id.");
        }

        return new EndpointSelection
        {
            Success = true,
            Endpoint = candidates[0]
        };
    }

    private static EndpointSelection Failure(ErrorCode code, string message) =>
        new()
        {
            Success = false,
            Error = new ErrorEnvelope
            {
                Code = code,
                Message = message
            }
        };
}


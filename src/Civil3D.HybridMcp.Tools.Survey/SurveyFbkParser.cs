using System.Globalization;
using System.Text.RegularExpressions;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Survey;

internal static partial class SurveyFbkParser
{
    private static readonly IReadOnlyList<string> SupportedKeywords =
    [
        "JOB", "UNIT", "EDM", "SCALE", "HORIZ", "VERT", "NEZ", "STN", "AZ", "BS", "PRISM", "F1", "F2"
    ];

    public static SurveyFbkDryRunResponse Parse(string instanceId, SurveyFbkDryRunRequest request)
    {
        var records = new List<SurveyFbkRecordInfo>();
        var controlPoints = new List<SurveyFbkControlPointInfo>();
        var setupBuilders = new List<FbkSetupBuilder>();
        var warnings = new List<string>();
        var currentPrismHeight = 0d;
        FbkSetupBuilder? currentSetup = null;

        var lines = SplitLines(request.Content);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var record = ParseLine(lines[i], lineNumber);
            if (record is null)
            {
                continue;
            }

            records.Add(record);
            if (!string.IsNullOrWhiteSpace(record.Warning))
            {
                warnings.Add($"Line {record.LineNumber}: {record.Warning}");
            }

            switch (record.RecordType)
            {
                case "NEZ":
                    if (record.PointNumber is { } pointNumber &&
                        record.Northing is { } northing &&
                        record.Easting is { } easting &&
                        record.Elevation is { } elevation)
                    {
                        controlPoints.Add(new SurveyFbkControlPointInfo
                        {
                            PointNumber = pointNumber,
                            Northing = northing,
                            Easting = easting,
                            Elevation = elevation,
                            Description = record.Description
                        });
                    }
                    break;

                case "STN":
                    currentSetup = new FbkSetupBuilder(
                        record.PointNumber ?? 0,
                        record.InstrumentHeight ?? 0,
                        record.Description);
                    setupBuilders.Add(currentSetup);
                    break;

                case "BS":
                    if (currentSetup is null)
                    {
                        warnings.Add($"Line {record.LineNumber}: BS before STN, ignored");
                    }
                    else
                    {
                        currentSetup.BacksightPoint = record.PointNumber;
                        currentSetup.BacksightCircleDms = record.Circle;
                    }
                    break;

                case "AZ":
                    if (currentSetup is null)
                    {
                        warnings.Add($"Line {record.LineNumber}: AZ before STN, ignored");
                    }
                    else
                    {
                        currentSetup.AzimuthDms = record.AzimuthDms;
                        currentSetup.AzFrom = record.AzFrom;
                        currentSetup.AzTo = record.AzTo;
                    }
                    break;

                case "PRISM":
                    currentPrismHeight = record.PrismHeight ?? 0;
                    break;

                case "F1":
                case "F2":
                    if (currentSetup is null)
                    {
                        warnings.Add($"Line {record.LineNumber}: observation before STN, ignored");
                    }
                    else
                    {
                        var observation = record.PrismHeight is null
                            ? record with { PrismHeight = currentPrismHeight }
                            : record;
                        records[^1] = observation;
                        currentSetup.ObservationLines.Add(observation.LineNumber);
                    }
                    break;
            }
        }

        var setups = setupBuilders
            .Select(setup => new SurveyFbkSetupInfo
            {
                StationPoint = setup.StationPoint,
                InstrumentHeight = setup.InstrumentHeight,
                Description = setup.Description,
                BacksightPoint = setup.BacksightPoint,
                BacksightCircleDms = setup.BacksightCircleDms,
                AzimuthDms = setup.AzimuthDms,
                AzFrom = setup.AzFrom,
                AzTo = setup.AzTo,
                ObservationCount = setup.ObservationLines.Count,
                ObservationLines = setup.ObservationLines.ToArray()
            })
            .ToArray();

        return new SurveyFbkDryRunResponse
        {
            InstanceId = instanceId,
            SourceName = string.IsNullOrWhiteSpace(request.SourceName) ? null : request.SourceName,
            TotalLines = lines.Count,
            RecordCount = records.Count,
            ControlPointCount = controlPoints.Count,
            SetupCount = setups.Length,
            ObservationCount = setups.Sum(setup => setup.ObservationCount),
            WarningCount = warnings.Count,
            Records = records,
            ControlPoints = controlPoints,
            Setups = setups,
            Warnings = warnings,
            SupportedKeywords = SupportedKeywords
        };
    }

    private static SurveyFbkRecordInfo? ParseLine(string rawLine, int lineNumber)
    {
        var trimmed = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.StartsWith('!') ||
            trimmed.StartsWith(';') ||
            trimmed.StartsWith('#'))
        {
            return null;
        }

        var description = QuotedTextRegex().Match(trimmed) is { Success: true } match
            ? match.Groups["text"].Value
            : null;
        var body = QuotedTextRegex().Replace(trimmed, " ").Trim();
        var tokens = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var keyword = tokens[0].ToUpperInvariant();
        try
        {
            return keyword switch
            {
                "JOB" or "UNIT" or "EDM" or "SCALE" or "HORIZ" or "VERT" => BaseRecord(lineNumber, keyword, keyword, description),
                "NEZ" => BaseRecord(lineNumber, keyword, "NEZ", description) with
                {
                    PointNumber = RequiredInt(tokens, 1, keyword),
                    Northing = RequiredDouble(tokens, 2, keyword),
                    Easting = RequiredDouble(tokens, 3, keyword),
                    Elevation = RequiredDouble(tokens, 4, keyword)
                },
                "STN" => BaseRecord(lineNumber, keyword, "STN", description) with
                {
                    PointNumber = RequiredInt(tokens, 1, keyword),
                    InstrumentHeight = OptionalDouble(tokens, 2) ?? 0
                },
                "BS" => BaseRecord(lineNumber, keyword, "BS", description) with
                {
                    PointNumber = RequiredInt(tokens, 1, keyword),
                    Circle = OptionalDouble(tokens, 2) ?? 0
                },
                "AZ" => BaseRecord(lineNumber, keyword, "AZ", description) with
                {
                    AzFrom = RequiredInt(tokens, 1, keyword),
                    AzTo = RequiredInt(tokens, 2, keyword),
                    AzimuthDms = RequiredDouble(tokens, 3, keyword)
                },
                "PRISM" => BaseRecord(lineNumber, keyword, "PRISM", description) with
                {
                    PrismHeight = RequiredDouble(tokens, 1, keyword)
                },
                "F1" or "F2" => ParseObservation(lineNumber, keyword, tokens, description),
                _ => BaseRecord(lineNumber, keyword, "UNKNOWN", description) with
                {
                    Warning = $"Unrecognized keyword: {keyword}"
                }
            };
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or SurveyToolException)
        {
            return BaseRecord(lineNumber, keyword, keyword, description) with
            {
                Warning = $"Parse error ({keyword}): {ex.Message}"
            };
        }
    }

    private static SurveyFbkRecordInfo ParseObservation(
        int lineNumber,
        string keyword,
        IReadOnlyList<string> tokens,
        string? description)
    {
        var index = 1;
        string? angleFlavor = null;
        if (tokens.Count > 1 &&
            (tokens[1].Equals("VA", StringComparison.OrdinalIgnoreCase) ||
             tokens[1].Equals("ZA", StringComparison.OrdinalIgnoreCase) ||
             tokens[1].Equals("ZE", StringComparison.OrdinalIgnoreCase)))
        {
            angleFlavor = tokens[1].ToUpperInvariant();
            index = 2;
        }

        return BaseRecord(lineNumber, keyword, keyword, description) with
        {
            PointNumber = RequiredInt(tokens, index++, keyword),
            AngleFlavor = angleFlavor,
            HorizontalAngle = RequiredDouble(tokens, index++, keyword),
            SlopeDistance = RequiredDouble(tokens, index++, keyword),
            VerticalAngle = RequiredDouble(tokens, index, keyword)
        };
    }

    private static SurveyFbkRecordInfo BaseRecord(int lineNumber, string keyword, string type, string? description) =>
        new()
        {
            LineNumber = lineNumber,
            Keyword = keyword,
            RecordType = type,
            Description = description
        };

    private static IReadOnlyList<string> SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static int RequiredInt(IReadOnlyList<string> tokens, int index, string keyword)
    {
        if (index >= tokens.Count)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, $"{keyword} requires token {index + 1}.");
        }

        return int.Parse(tokens[index], CultureInfo.InvariantCulture);
    }

    private static double RequiredDouble(IReadOnlyList<string> tokens, int index, string keyword)
    {
        if (index >= tokens.Count)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, $"{keyword} requires token {index + 1}.");
        }

        return double.Parse(tokens[index], CultureInfo.InvariantCulture);
    }

    private static double? OptionalDouble(IReadOnlyList<string> tokens, int index) =>
        index < tokens.Count ? double.Parse(tokens[index], CultureInfo.InvariantCulture) : null;

    [GeneratedRegex("\"(?<text>[^\"]*)\"", RegexOptions.Compiled)]
    private static partial Regex QuotedTextRegex();

    private sealed class FbkSetupBuilder
    {
        public FbkSetupBuilder(int stationPoint, double instrumentHeight, string? description)
        {
            StationPoint = stationPoint;
            InstrumentHeight = instrumentHeight;
            Description = description;
        }

        public int StationPoint { get; }
        public double InstrumentHeight { get; }
        public string? Description { get; }
        public int? BacksightPoint { get; set; }
        public double? BacksightCircleDms { get; set; }
        public double? AzimuthDms { get; set; }
        public int? AzFrom { get; set; }
        public int? AzTo { get; set; }
        public List<int> ObservationLines { get; } = [];
    }
}

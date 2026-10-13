using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D.HybridMcp.Protocol;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3D.HybridMcp.Tools.Civil;

public sealed partial class AutodeskCivilReadAdapter : ICivilReadAdapter
{
    private readonly Document _document;
    private readonly DocumentDescriptor _descriptor;

    public AutodeskCivilReadAdapter(Document document, DocumentDescriptor descriptor)
    {
        _document = document;
        _descriptor = descriptor;
    }

    public IReadOnlyList<AlignmentInfo> ListAlignments()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var civilDocument = CivilDocument.GetCivilDocument(database);
        var ids = civilDocument.GetAlignmentIds();
        var alignments = new List<AlignmentInfo>(ids.Count);

        foreach (ObjectId id in ids)
        {
            var alignment = (Alignment)transaction.GetObject(id, OpenMode.ForRead);
            alignments.Add(ToAlignmentInfo(alignment));
        }

        transaction.Commit();
        return alignments;
    }

    public AlignmentInfo? GetAlignment(CivilObjectLocator locator)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var alignment = FindAlignment(database, transaction, locator);
        var result = alignment is null ? null : ToAlignmentInfo(alignment);
        transaction.Commit();
        return result;
    }

    public AlignmentStationOffsetResult GetStationOffset(CivilObjectLocator locator, double x, double y)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var alignment = FindAlignment(database, transaction, locator)
            ?? throw new CivilToolException(ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.");

        double station = 0;
        double offset = 0;
        try
        {
            alignment.StationOffset(x, y, ref station, ref offset);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not compute station/offset: {ex.Message}");
        }

        var result = new AlignmentStationOffsetResult(ToAlignmentInfo(alignment), station, offset);
        transaction.Commit();
        return result;
    }

    public AlignmentPointLocationResult GetPointLocation(CivilObjectLocator locator, double station, double offset)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var alignment = FindAlignment(database, transaction, locator)
            ?? throw new CivilToolException(ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.");

        double x = 0;
        double y = 0;
        try
        {
            alignment.PointLocation(station, offset, ref x, ref y);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not compute point location: {ex.Message}");
        }

        var result = new AlignmentPointLocationResult(ToAlignmentInfo(alignment), x, y);
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<SurfaceInfo> ListSurfaces()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var civilDocument = CivilDocument.GetCivilDocument(database);
        var ids = civilDocument.GetSurfaceIds();
        var surfaces = new List<SurfaceInfo>(ids.Count);

        foreach (ObjectId id in ids)
        {
            var surface = (CivilSurface)transaction.GetObject(id, OpenMode.ForRead);
            surfaces.Add(ToSurfaceInfo(surface));
        }

        transaction.Commit();
        return surfaces;
    }

    public SurfaceInfo? GetSurface(CivilObjectLocator locator)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var surface = FindSurface(database, transaction, locator);
        var result = surface is null ? null : ToSurfaceInfo(surface);
        transaction.Commit();
        return result;
    }

    public SurfaceElevationAtXyResult GetSurfaceElevationAtXy(CivilObjectLocator locator, double x, double y)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var surface = FindSurface(database, transaction, locator)
            ?? throw new CivilToolException(ErrorCode.SurfaceNotFound, "Surface was not found in the target drawing.");

        double elevation;
        try
        {
            elevation = surface.FindElevationAtXY(x, y);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not read surface elevation at XY: {ex.Message}");
        }

        var result = new SurfaceElevationAtXyResult(ToSurfaceInfo(surface), elevation);
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<CogoPointInfo> ListCogoPoints()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var civilDocument = CivilDocument.GetCivilDocument(database);
        var ids = civilDocument.GetAllPointIds();
        var points = new List<CogoPointInfo>(ids.Count);

        foreach (ObjectId id in ids)
        {
            var point = (CogoPoint)transaction.GetObject(id, OpenMode.ForRead);
            points.Add(ToCogoPointInfo(point));
        }

        transaction.Commit();
        return points;
    }

    public CogoPointInfo? GetCogoPoint(CogoPointLocator locator)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var point = FindCogoPoint(database, transaction, locator);
        var result = point is null ? null : ToCogoPointInfo(point);
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<CorridorInfo> ListCorridors()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var civilDocument = CivilDocument.GetCivilDocument(database);
        var corridors = new List<CorridorInfo>();

        foreach (ObjectId corridorId in civilDocument.CorridorCollection)
        {
            if (transaction.GetObject(corridorId, OpenMode.ForRead, openErased: false) is Corridor corridor)
            {
                corridors.Add(ToCorridorInfo(corridor, transaction, includeBaselines: false));
            }
        }

        transaction.Commit();
        return corridors;
    }

    public CorridorInfo? GetCorridor(CivilObjectLocator locator)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        CorridorInfo? result = null;

        if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased &&
            transaction.GetObject(objectId, OpenMode.ForRead, openErased: false) is Corridor byHandle)
        {
            result = ToCorridorInfo(byHandle, transaction, includeBaselines: true);
        }
        else if (!string.IsNullOrWhiteSpace(locator.Name))
        {
            var civilDocument = CivilDocument.GetCivilDocument(database);
            foreach (ObjectId corridorId in civilDocument.CorridorCollection)
            {
                if (transaction.GetObject(corridorId, OpenMode.ForRead, openErased: false) is Corridor corridor &&
                    string.Equals(Safe(() => corridor.Name), locator.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result = ToCorridorInfo(corridor, transaction, includeBaselines: true);
                    break;
                }
            }
        }

        transaction.Commit();
        return result;
    }

    private CorridorInfo ToCorridorInfo(Corridor corridor, Transaction transaction, bool includeBaselines)
    {
        var handle = Safe(() => corridor.Handle.ToString());
        var name = Safe(() => corridor.Name) ?? string.Empty;
        var baselines = Safe(() => corridor.Baselines);

        return new CorridorInfo
        {
            Name = name,
            Handle = handle,
            ObjectReference = ToReference(corridor, handle, nameof(Corridor), name),
            Description = Safe(() => string.IsNullOrWhiteSpace(corridor.Description) ? null : corridor.Description),
            IsOutOfDate = Safe(() => (bool?)corridor.IsOutOfDate),
            RebuildAutomatic = Safe(() => (bool?)corridor.RebuildAutomatic),
            CodeSetStyleName = Safe(() => string.IsNullOrWhiteSpace(corridor.CodeSetStyleName) ? null : corridor.CodeSetStyleName),
            MaximumTriangleSideLength = Safe(() => (double?)corridor.MaximumTriangleSideLength),
            RegionLockMode = Safe(() => corridor.RegionLockMode.ToString()),
            BaselineCount = Safe(() => (int?)baselines!.Count),
            CorridorSurfaceCount = Safe(() => (int?)corridor.CorridorSurfaces.Count),
            Baselines = includeBaselines ? ToBaselineInfos(baselines, transaction) : []
        };
    }

    private IReadOnlyList<CorridorBaselineInfo> ToBaselineInfos(BaselineCollection? baselines, Transaction transaction)
    {
        if (baselines is null)
        {
            return [];
        }

        var results = new List<CorridorBaselineInfo>();
        int count;
        try
        {
            count = baselines.Count;
        }
        catch
        {
            return results;
        }

        for (var i = 0; i < count; i++)
        {
            Baseline baseline;
            try
            {
                baseline = baselines[i];
            }
            catch
            {
                continue;
            }

            var regions = ToRegionInfos(Safe(() => baseline.BaselineRegions), transaction);
            results.Add(new CorridorBaselineInfo
            {
                Name = Safe(() => baseline.Name) ?? string.Empty,
                BaselineType = Safe(() => baseline.BaselineType.ToString()),
                AlignmentName = ReadEntityName(transaction, Safe(() => baseline.AlignmentId)),
                AlignmentHandle = ReadHandle(Safe(() => baseline.AlignmentId)),
                ProfileName = ReadEntityName(transaction, Safe(() => baseline.ProfileId)),
                ProfileHandle = ReadHandle(Safe(() => baseline.ProfileId)),
                StartStation = regions.Count == 0 ? null : regions.Min(region => region.StartStation),
                EndStation = regions.Count == 0 ? null : regions.Max(region => region.EndStation),
                RegionCount = regions.Count,
                Regions = regions
            });
        }

        return results;
    }

    private IReadOnlyList<CorridorRegionInfo> ToRegionInfos(BaselineRegionCollection? regions, Transaction transaction)
    {
        if (regions is null)
        {
            return [];
        }

        var results = new List<CorridorRegionInfo>();
        int count;
        try
        {
            count = regions.Count;
        }
        catch
        {
            return results;
        }

        for (var i = 0; i < count; i++)
        {
            BaselineRegion region;
            try
            {
                region = regions[i];
            }
            catch
            {
                continue;
            }

            results.Add(new CorridorRegionInfo
            {
                Name = Safe(() => region.Name) ?? string.Empty,
                StartStation = Safe(() => (double?)region.StartStation),
                EndStation = Safe(() => (double?)region.EndStation),
                AssemblyName = ReadEntityName(transaction, Safe(() => region.AssemblyId)),
                AssemblyHandle = ReadHandle(Safe(() => region.AssemblyId))
            });
        }

        return results;
    }

    private static string? ReadEntityName(Transaction transaction, ObjectId id)
    {
        if (id.IsNull || id.IsErased)
        {
            return null;
        }

        try
        {
            return transaction.GetObject(id, OpenMode.ForRead, openErased: false) is Autodesk.Civil.DatabaseServices.Entity entity
                ? entity.Name
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadHandle(ObjectId id)
    {
        if (id.IsNull)
        {
            return null;
        }

        try
        {
            return id.Handle.ToString();
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ProfileInfo> ListProfiles()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var civilDocument = CivilDocument.GetCivilDocument(database);
        var profiles = new List<ProfileInfo>();

        foreach (ObjectId alignmentId in civilDocument.GetAlignmentIds())
        {
            var alignment = (Alignment)transaction.GetObject(alignmentId, OpenMode.ForRead);
            var alignmentHandle = Safe(() => alignment.Handle.ToString());
            var alignmentName = Safe(() => alignment.Name);

            ObjectIdCollection profileIds;
            try
            {
                profileIds = alignment.GetProfileIds();
            }
            catch
            {
                continue;
            }

            foreach (ObjectId profileId in profileIds)
            {
                if (transaction.GetObject(profileId, OpenMode.ForRead, openErased: false) is Profile profile)
                {
                    profiles.Add(ToProfileInfo(profile, alignmentName, alignmentHandle));
                }
            }
        }

        transaction.Commit();
        return profiles;
    }

    public ProfileStationResult GetProfileStationValues(string profileHandle, double station)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        if (!TryGetObjectId(database, profileHandle, out var objectId) ||
            objectId.IsNull ||
            objectId.IsErased ||
            transaction.GetObject(objectId, OpenMode.ForRead, openErased: false) is not Profile profile)
        {
            transaction.Commit();
            throw new CivilToolException(ErrorCode.ProfileNotFound, "Profile was not found in the target drawing.");
        }

        double? elevation;
        try
        {
            elevation = profile.ElevationAt(station);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not read profile elevation at station: {ex.Message}");
        }

        var grade = Safe(() => (double?)profile.GradeAt(station));

        transaction.Commit();
        return new ProfileStationResult(elevation, grade);
    }

    private ProfileInfo ToProfileInfo(Profile profile, string? alignmentName, string? alignmentHandle)
    {
        var handle = Safe(() => profile.Handle.ToString());
        var name = Safe(() => profile.Name) ?? string.Empty;
        return new ProfileInfo
        {
            Name = name,
            Handle = handle,
            ObjectReference = ToReference(profile, handle, nameof(Profile), name),
            Description = Safe(() => string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description),
            AlignmentName = alignmentName,
            AlignmentHandle = alignmentHandle,
            ProfileType = Safe(() => profile.ProfileType.ToString()),
            StartingStation = Safe(() => (double?)profile.StartingStation),
            EndingStation = Safe(() => (double?)profile.EndingStation),
            Length = Safe(() => (double?)profile.Length),
            Offset = Safe(() => (double?)profile.Offset),
            ElevationMin = Safe(() => (double?)profile.ElevationMin),
            ElevationMax = Safe(() => (double?)profile.ElevationMax),
            PviCount = Safe(() => (int?)profile.PVIs.Count),
            StyleName = Safe(() => string.IsNullOrWhiteSpace(profile.StyleName) ? null : profile.StyleName)
        };
    }

    private Alignment? FindAlignment(Database database, Transaction transaction, CivilObjectLocator locator)
    {
        if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased &&
            transaction.GetObject(objectId, OpenMode.ForRead, openErased: false) is Alignment byHandle)
        {
            return byHandle;
        }

        if (!string.IsNullOrWhiteSpace(locator.Name))
        {
            var civilDocument = CivilDocument.GetCivilDocument(database);
            foreach (ObjectId id in civilDocument.GetAlignmentIds())
            {
                var alignment = (Alignment)transaction.GetObject(id, OpenMode.ForRead);
                if (string.Equals(alignment.Name, locator.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return alignment;
                }
            }
        }

        return null;
    }

    private CivilSurface? FindSurface(Database database, Transaction transaction, CivilObjectLocator locator)
    {
        if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased &&
            transaction.GetObject(objectId, OpenMode.ForRead, openErased: false) is CivilSurface byHandle)
        {
            return byHandle;
        }

        if (!string.IsNullOrWhiteSpace(locator.Name))
        {
            var civilDocument = CivilDocument.GetCivilDocument(database);
            foreach (ObjectId id in civilDocument.GetSurfaceIds())
            {
                var surface = (CivilSurface)transaction.GetObject(id, OpenMode.ForRead);
                if (string.Equals(surface.Name, locator.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return surface;
                }
            }
        }

        return null;
    }

    private CogoPoint? FindCogoPoint(Database database, Transaction transaction, CogoPointLocator locator)
    {
        if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased &&
            transaction.GetObject(objectId, OpenMode.ForRead, openErased: false) is CogoPoint byHandle)
        {
            return byHandle;
        }

        if (locator.PointNumber is { } pointNumber)
        {
            var civilDocument = CivilDocument.GetCivilDocument(database);
            foreach (ObjectId id in civilDocument.GetAllPointIds())
            {
                var point = (CogoPoint)transaction.GetObject(id, OpenMode.ForRead);
                if (Safe(() => Convert.ToInt64(point.PointNumber)) == pointNumber)
                {
                    return point;
                }
            }
        }

        return null;
    }

    private AlignmentInfo ToAlignmentInfo(Alignment alignment)
    {
        var handle = Safe(() => alignment.Handle.ToString());
        return new AlignmentInfo
        {
            Name = alignment.Name,
            Handle = handle,
            ObjectReference = ToReference(alignment, handle, nameof(Alignment), alignment.Name),
            Description = Safe(() => string.IsNullOrWhiteSpace(alignment.Description) ? null : alignment.Description),
            Type = Safe(() => alignment.AlignmentType.ToString()),
            Length = Safe(() => alignment.Length),
            StartingStation = Safe(() => alignment.StartingStation),
            EndingStation = Safe(() => alignment.EndingStation),
            SiteHandle = Safe(() => alignment.SiteId.IsNull ? null : alignment.SiteId.Handle.ToString()),
            StyleHandle = Safe(() => alignment.StyleId.IsNull ? null : alignment.StyleId.Handle.ToString())
        };
    }

    private SurfaceInfo ToSurfaceInfo(CivilSurface surface)
    {
        var handle = Safe(() => surface.Handle.ToString());
        var properties = Safe(() => surface.GetGeneralProperties());
        return new SurfaceInfo
        {
            Name = surface.Name,
            Handle = handle,
            ObjectReference = ToReference(surface, handle, surface.GetType().Name, surface.Name),
            Description = Safe(() => string.IsNullOrWhiteSpace(surface.Description) ? null : surface.Description),
            Type = surface.GetType().Name,
            PointCount = Safe(() => properties?.NumberOfPoints),
            MinimumElevation = Safe(() => properties?.MinimumElevation),
            MaximumElevation = Safe(() => properties?.MaximumElevation),
            MeanElevation = Safe(() => properties?.MeanElevation)
        };
    }

    private CogoPointInfo ToCogoPointInfo(CogoPoint point)
    {
        var handle = Safe(() => point.Handle.ToString());
        var pointNumber = Safe(() => Convert.ToInt64(point.PointNumber));
        return new CogoPointInfo
        {
            PointNumber = pointNumber,
            Handle = handle,
            ObjectReference = ToReference(point, handle, nameof(CogoPoint), pointNumber.ToString()),
            Easting = Safe(() => point.Easting),
            Northing = Safe(() => point.Northing),
            Elevation = Safe(() => point.Elevation),
            RawDescription = Safe(() => string.IsNullOrWhiteSpace(point.RawDescription) ? null : point.RawDescription),
            FullDescription = Safe(() => string.IsNullOrWhiteSpace(point.FullDescription) ? null : point.FullDescription),
            IsLocked = Safe(() => point.IsLocked)
        };
    }

    private CadObjectReference ToReference(Autodesk.AutoCAD.DatabaseServices.DBObject obj, string? handle, string type, string? name) =>
        new()
        {
            InstanceId = _descriptor.InstanceId,
            DocumentId = _descriptor.DocumentId,
            DatabaseFingerprint = _descriptor.DatabaseFingerprint,
            Handle = handle,
            ObjectId = Safe(() => obj.ObjectId.ToString()),
            Type = type,
            Name = name
        };

    private static bool TryGetObjectId(Database database, string handleText, out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        try
        {
            var value = Convert.ToInt64(handleText, 16);
            objectId = database.GetObjectId(createIfNotFound: false, new Handle(value), identifier: 0);
            return !objectId.IsNull;
        }
        catch
        {
            return false;
        }
    }

    private static T? Safe<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}

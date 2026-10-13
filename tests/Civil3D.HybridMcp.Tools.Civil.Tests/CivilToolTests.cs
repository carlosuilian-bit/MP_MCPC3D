using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Civil;

namespace Civil3D.HybridMcp.Tools.Civil.Tests;

public sealed class CivilToolTests
{
    [Fact]
    public void CivilResultSerializesWithSnakeCaseDocumentTarget()
    {
        var response = new SurfaceElevationAtXyResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Surface = SampleSurface("EG"),
            X = 10,
            Y = 20,
            Elevation = 101.5
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<SurfaceElevationAtXyResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"document_id\"", json);
        Assert.Contains("\"object_reference\"", json);
        Assert.Contains("\"minimum_elevation\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(101.5, roundTrip!.Elevation);
        Assert.Equal("EG", roundTrip.Surface.Name);
    }

    [Fact]
    public void ManifestPublishesCivilToolsWithExpectedSafetyMetadata()
    {
        var tools = CivilToolCatalog.ListTools();

        Assert.Equal(20, tools.Count);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetSet);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetFind);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetDefinitionList);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetList);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetGet);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.PropertySetNumericTestCreate);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.CorridorList);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.CorridorGet);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.AlignmentStationOffset);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.SurfaceElevationAtXy);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.CogoGet);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.ProfileList);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.ProfileGet);
        Assert.Contains(tools, tool => tool.Name == CivilToolNames.ProfileElevationAtStation);
        Assert.Equal(18, tools.Count(tool => tool.OperationClass == OperationClass.Read));
        Assert.Equal(2, tools.Count(tool => tool.OperationClass == OperationClass.Write));
        Assert.Equal(18, tools.Count(tool => tool.RollbackSemantics == RollbackSemantics.NoneRequired));
        Assert.Equal(2, tools.Count(tool => tool.RollbackSemantics == RollbackSemantics.TransactionRollbackBeforeCommit));
        Assert.All(tools, tool => Assert.Equal("Civil3D", tool.Module));

        // Every read is Low and unconfirmed. Risk and confirmation are carried
        // only by the write that touches data an engineer already put there.
        Assert.All(
            tools.Where(tool => tool.OperationClass == OperationClass.Read),
            tool =>
            {
                Assert.Equal(ToolRisk.Low, tool.Risk);
                Assert.False(tool.RequiresConfirmation);
            });
        Assert.Single(tools, tool => tool.RequiresConfirmation);
        Assert.Single(tools, tool => tool.Risk == ToolRisk.Medium);
    }

    [Fact]
    public void ManifestPublishesNumericPropertySetTestAsBoundedWrite()
    {
        var tools = CivilToolCatalog.ListTools();

        var tool = Assert.Single(tools, tool => tool.Name == CivilToolNames.PropertySetNumericTestCreate);
        Assert.Equal(OperationClass.Write, tool.OperationClass);
        Assert.Equal(ToolRisk.Low, tool.Risk);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, tool.RollbackSemantics);
        Assert.Equal("Civil3D", tool.Module);
        Assert.False(tool.RequiresConfirmation);
    }

    [Fact]
    public void AlignmentStationOffsetSchemaRequiresCoordinatesAndDocumentTarget()
    {
        var schema = CivilSchemas.AlignmentStationOffsetInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.Contains("x", required);
        Assert.Contains("y", required);
        Assert.Contains("object_reference", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void CogoLimitMustBePositive()
    {
        var ex = Assert.Throws<CivilToolException>(() => CivilRequestValidator.ValidateCogoList(new CogoListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Limit = 0
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void DocumentTargetMustMatchDescriptor()
    {
        var service = new CivilReadService();
        var ex = Assert.Throws<CivilToolException>(() => service.ListAlignments(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "other-doc"
        }));

        Assert.Equal(ErrorCode.DocumentNotFound, ex.Code);
    }

    [Fact]
    public void CivilServiceFiltersAlignmentsByName()
    {
        var service = new CivilReadService();
        var response = service.ListAlignments(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "main"
        });

        var alignment = Assert.Single(response.Alignments);
        Assert.Equal("MAIN_ROAD", alignment.Name);
    }

    [Fact]
    public void CivilServiceComputesAlignmentStationOffsetShape()
    {
        var service = new CivilReadService();
        var response = service.AlignmentStationOffset(new FakeCivilReadAdapter(), SampleDocument(), new AlignmentStationOffsetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "MAIN_ROAD",
            X = 1000,
            Y = 2000
        });

        Assert.Equal("MAIN_ROAD", response.Alignment.Name);
        Assert.Equal(100, response.Station);
        Assert.Equal(12.5, response.Offset);
    }

    [Fact]
    public void MissingAlignmentRaisesStructuredErrorCode()
    {
        var service = new CivilReadService();
        var ex = Assert.Throws<CivilToolException>(() => service.GetAlignment(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "MISSING"
        }));

        Assert.Equal(ErrorCode.AlignmentNotFound, ex.Code);
    }

    private static DocumentDescriptor SampleDocument() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "Drawing1.dwg",
            FullPath = null,
            IsActive = true,
            IsSaved = false,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = "fingerprint-1",
            SessionIdentifier = "session-1"
        };

    private static CadObjectReference SampleReference(string name, string handle, string type) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            DatabaseFingerprint = "fingerprint-1",
            Handle = handle,
            ObjectId = $"({handle})",
            Type = type,
            Name = name
        };

    private static AlignmentInfo SampleAlignment(string name, string handle = "A1") =>
        new()
        {
            Name = name,
            Handle = handle,
            ObjectReference = SampleReference(name, handle, "Alignment"),
            Type = "Centerline",
            Length = 500,
            StartingStation = 0,
            EndingStation = 500
        };

    private static SurfaceInfo SampleSurface(string name, string handle = "B1") =>
        new()
        {
            Name = name,
            Handle = handle,
            ObjectReference = SampleReference(name, handle, "TinSurface"),
            Type = "TinSurface",
            PointCount = 10,
            MinimumElevation = 100,
            MaximumElevation = 120,
            MeanElevation = 110
        };

    private static CogoPointInfo SamplePoint(long pointNumber, string handle = "C1") =>
        new()
        {
            PointNumber = pointNumber,
            Handle = handle,
            ObjectReference = SampleReference(pointNumber.ToString(), handle, "CogoPoint"),
            Easting = 1000,
            Northing = 2000,
            Elevation = 50,
            RawDescription = "TREE",
            FullDescription = "Tree"
        };

    [Fact]
    public void ProfileListReturnsProfilesOrderedByAlignmentAndName()
    {
        var service = new CivilReadService();

        var response = service.ListProfiles(new FakeCivilReadAdapter(), SampleDocument(), new ProfileListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        });

        Assert.Equal(5, response.Count);
        Assert.Equal(5, response.Profiles.Count);
        Assert.Equal("MAIN_ROAD", response.Profiles[0].AlignmentName);
        Assert.Equal("EG-MAIN", response.Profiles[0].Name);
        Assert.Equal("FG-MAIN", response.Profiles[1].Name);
        Assert.Equal("SHARED", response.Profiles[2].Name);
        Assert.Equal("SIDE_ROAD", response.Profiles[3].AlignmentName);
        Assert.Equal("EG-SIDE", response.Profiles[3].Name);
        Assert.Equal("SHARED", response.Profiles[4].Name);
    }

    [Fact]
    public void ProfileListFiltersByAlignmentAndNameSubstring()
    {
        var service = new CivilReadService();

        var byAlignment = service.ListProfiles(new FakeCivilReadAdapter(), SampleDocument(), new ProfileListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            AlignmentName = "side_road"
        });

        Assert.Equal(2, byAlignment.Count);
        Assert.All(byAlignment.Profiles, profile => Assert.Equal("SIDE_ROAD", profile.AlignmentName));

        var byName = service.ListProfiles(new FakeCivilReadAdapter(), SampleDocument(), new ProfileListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "fg"
        });

        Assert.Equal(1, byName.Count);
        Assert.Equal("FG-MAIN", byName.Profiles[0].Name);
    }

    [Fact]
    public void ProfileGetResolvesByNameAndByHandle()
    {
        var service = new CivilReadService();

        var byName = service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "fg-main"
        });

        Assert.Equal("FG-MAIN", byName.Profile.Name);
        Assert.Equal("MAIN_ROAD", byName.Profile.AlignmentName);
        Assert.Equal(150.25, byName.Profile.ElevationMin);

        var byHandle = service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "P3"
        });

        Assert.Equal("EG-SIDE", byHandle.Profile.Name);
    }

    [Fact]
    public void ProfileGetThrowsProfileNotFound()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "NO_SUCH_PROFILE"
        }));

        Assert.Equal(ErrorCode.ProfileNotFound, ex.Code);
    }

    [Fact]
    public void ProfileGetThrowsProfileAmbiguousWhenNameRepeatsAcrossAlignments()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "SHARED"
        }));

        Assert.Equal(ErrorCode.ProfileAmbiguous, ex.Code);
    }

    [Fact]
    public void ProfileGetDisambiguatesRepeatedNameWithAlignmentName()
    {
        var service = new CivilReadService();

        var response = service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "SHARED",
            AlignmentName = "SIDE_ROAD"
        });

        Assert.Equal("SHARED", response.Profile.Name);
        Assert.Equal("SIDE_ROAD", response.Profile.AlignmentName);
    }

    [Fact]
    public void ProfileGetRequiresLocator()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetProfile(new FakeCivilReadAdapter(), SampleDocument(), new ProfileGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void ProfileElevationAtStationReturnsElevationAndGrade()
    {
        var service = new CivilReadService();

        var response = service.ProfileElevationAtStation(new FakeCivilReadAdapter(), SampleDocument(), new ProfileElevationAtStationRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "FG-MAIN",
            Station = 1000
        });

        Assert.Equal("FG-MAIN", response.Profile.Name);
        Assert.Equal(1000, response.Station);
        Assert.Equal(742.5, response.Elevation);
        Assert.Equal(0.025, response.Grade);
    }

    [Fact]
    public void ProfileElevationAtStationRejectsNonFiniteStation()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.ProfileElevationAtStation(new FakeCivilReadAdapter(), SampleDocument(), new ProfileElevationAtStationRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "FG-MAIN",
            Station = double.NaN
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void ProfileListSchemaSupportsAlignmentAndNameFilters()
    {
        var schema = CivilSchemas.ProfileListInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.DoesNotContain("alignment_name", required);
        Assert.Contains("alignment_name", properties.Select(property => property.Key));
        Assert.Contains("name_contains", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void ProfileElevationAtStationSchemaRequiresStation()
    {
        var schema = CivilSchemas.ProfileElevationAtStationInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.Contains("station", required);
        Assert.Contains("alignment_name", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void ProfileResponseSerializesWithSnakeCaseFields()
    {
        var response = new ProfileElevationAtStationResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Profile = SampleProfile("FG-MAIN", "P2", "MAIN_ROAD"),
            Station = 1000,
            Elevation = 742.5,
            Grade = 0.025
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ProfileElevationAtStationResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"document_id\"", json);
        Assert.Contains("\"alignment_name\"", json);
        Assert.Contains("\"profile_type\"", json);
        Assert.Contains("\"starting_station\"", json);
        Assert.Contains("\"elevation_min\"", json);
        Assert.Contains("\"pvi_count\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(742.5, roundTrip!.Elevation);
        Assert.Equal("MAIN_ROAD", roundTrip.Profile.AlignmentName);
    }

    [Fact]
    public void CorridorListReturnsSummariesOrderedByName()
    {
        var service = new CivilReadService();

        var response = service.ListCorridors(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        });

        Assert.Equal(2, response.Count);
        Assert.Equal("CORR-BD-100", response.Corridors[0].Name);
        Assert.Equal("CORR-BD-NORTE", response.Corridors[1].Name);
        Assert.Equal(2, response.Corridors[0].BaselineCount);
        Assert.False(response.Corridors[0].IsOutOfDate);
        Assert.Empty(response.Corridors[0].Baselines);
    }

    [Fact]
    public void CorridorListFiltersByNameSubstring()
    {
        var service = new CivilReadService();

        var response = service.ListCorridors(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "norte"
        });

        var corridor = Assert.Single(response.Corridors);
        Assert.Equal("CORR-BD-NORTE", corridor.Name);
    }

    [Fact]
    public void CorridorGetReturnsBaselinesAndRegions()
    {
        var service = new CivilReadService();

        var response = service.GetCorridor(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "corr-bd-100"
        });

        Assert.Equal("CORR-BD-100", response.Corridor.Name);
        Assert.Equal(2, response.Corridor.BaselineCount);
        Assert.Equal(2, response.Corridor.Baselines.Count);

        var baseline = response.Corridor.Baselines[0];
        Assert.Equal("BL - BD - 100", baseline.Name);
        Assert.Equal("BD - 100", baseline.AlignmentName);
        Assert.Equal("V - BD - 100", baseline.ProfileName);
        Assert.Equal(2000, baseline.StartStation);
        Assert.Equal(2357.5, baseline.EndStation);
        Assert.Equal(2, baseline.RegionCount);

        var region = baseline.Regions[0];
        Assert.Equal("RG - BD - 100 - 1", region.Name);
        Assert.Equal("ASSEMBLY-PISTA", region.AssemblyName);
        Assert.Equal(2000, region.StartStation);
    }

    [Fact]
    public void CorridorGetResolvesByHandle()
    {
        var service = new CivilReadService();

        var response = service.GetCorridor(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "K2"
        });

        Assert.Equal("CORR-BD-NORTE", response.Corridor.Name);
    }

    [Fact]
    public void CorridorGetThrowsCorridorNotFound()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetCorridor(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "NO_SUCH_CORRIDOR"
        }));

        Assert.Equal(ErrorCode.CorridorNotFound, ex.Code);
    }

    [Fact]
    public void CorridorGetRequiresLocator()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetCorridor(new FakeCivilReadAdapter(), SampleDocument(), new CivilObjectGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void CorridorResponseSerializesWithSnakeCaseFields()
    {
        var response = new CorridorGetResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Corridor = SampleCorridor("CORR-BD-100", "K1", withBaselines: true)
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<CorridorGetResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"document_id\"", json);
        Assert.Contains("\"is_out_of_date\"", json);
        Assert.Contains("\"baseline_count\"", json);
        Assert.Contains("\"alignment_name\"", json);
        Assert.Contains("\"start_station\"", json);
        Assert.Contains("\"assembly_name\"", json);
        Assert.Contains("\"region_count\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("CORR-BD-100", roundTrip!.Corridor.Name);
        Assert.Equal("BD - 100", roundTrip.Corridor.Baselines[0].AlignmentName);
    }

    [Fact]
    public void PropertySetDefinitionListReturnsDefinitionsOrderedByName()
    {
        var service = new CivilReadService();

        var response = service.ListPropertySetDefinitions(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetDefinitionListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        });

        Assert.Equal(2, response.Count);
        Assert.Equal("Faixa de Dominio", response.Definitions[0].Name);
        Assert.Equal("Rodovia", response.Definitions[1].Name);
        Assert.Equal(2, response.Definitions[1].PropertyCount);
        Assert.Equal("Codigo", response.Definitions[1].Properties[0].Name);
        Assert.Equal("Text", response.Definitions[1].Properties[0].DataType);
    }

    [Fact]
    public void PropertySetDefinitionListFiltersByName()
    {
        var service = new CivilReadService();

        var response = service.ListPropertySetDefinitions(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetDefinitionListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "rodo"
        });

        var definition = Assert.Single(response.Definitions);
        Assert.Equal("Rodovia", definition.Name);
    }

    [Fact]
    public void PropertySetListReturnsAttachedSetsWithValues()
    {
        var service = new CivilReadService();

        var response = service.ListPropertySets(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "E1"
        });

        Assert.Equal("E1", response.ObjectHandle);
        Assert.Equal(1, response.Count);

        var set = Assert.Single(response.PropertySets);
        Assert.Equal("Rodovia", set.Name);
        Assert.Equal(2, set.PropertyCount);
        Assert.Equal("Codigo", set.Properties[0].Name);
        Assert.Equal("BR-040", set.Properties[0].Value);
        Assert.Null(set.Properties[0].NumericValue);
        Assert.Equal("Extensao", set.Properties[1].Name);
        Assert.Equal(12.5, set.Properties[1].NumericValue);
    }

    [Fact]
    public void PropertySetListRequiresObjectLocator()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.ListPropertySets(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void PropertySetListReportsObjectNotFound()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.ListPropertySets(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "NO_SUCH_HANDLE"
        }));

        Assert.Equal(ErrorCode.ObjectNotFound, ex.Code);
    }

    [Fact]
    public void PropertySetGetReturnsOneSetByName()
    {
        var service = new CivilReadService();

        var response = service.GetPropertySet(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "E1",
            Name = "rodovia"
        });

        Assert.Equal("Rodovia", response.PropertySet.Name);
        Assert.Equal("E1", response.ObjectHandle);
    }

    [Fact]
    public void PropertySetGetReportsPropertySetNotFound()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.GetPropertySet(new FakeCivilReadAdapter(), SampleDocument(), new PropertySetGetRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = "E1",
            Name = "NO_SUCH_SET"
        }));

        Assert.Equal(ErrorCode.PropertySetNotFound, ex.Code);
    }

    [Fact]
    public void PropertySetSchemasDeclareExpectedShape()
    {
        var definitionSchema = CivilSchemas.PropertySetDefinitionListInput();
        Assert.Contains("name_contains", definitionSchema["properties"]!.AsObject().Select(property => property.Key));
        Assert.False(definitionSchema["additionalProperties"]!.GetValue<bool>());

        var listSchema = CivilSchemas.PropertySetListInput();
        var listRequired = listSchema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Contains("instance_id", listRequired);
        Assert.Contains("document_id", listRequired);
        Assert.DoesNotContain("handle", listRequired);
        Assert.Contains("handle", listSchema["properties"]!.AsObject().Select(property => property.Key));

        var getSchema = CivilSchemas.PropertySetGetInput();
        var getRequired = getSchema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Contains("name", getRequired);
    }

    [Fact]
    public void PropertySetResponseSerializesWithSnakeCaseFields()
    {
        var response = new PropertySetListResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            ObjectHandle = "E1",
            Count = 1,
            PropertySets = [SamplePropertySet()]
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<PropertySetListResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"object_handle\"", json);
        Assert.Contains("\"property_sets\"", json);
        Assert.Contains("\"property_count\"", json);
        Assert.Contains("\"data_type\"", json);
        Assert.Contains("\"numeric_value\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("Rodovia", roundTrip!.PropertySets[0].Name);
        Assert.Equal(12.5, roundTrip.PropertySets[0].Properties[1].NumericValue);
    }

    [Fact]
    public void PropertySetNumericTestCreateReturnsNumericRoundTrip()
    {
        var service = new CivilReadService();

        var response = service.CreatePropertySetNumericTest(new FakeCivilWriteAdapter(), SampleDocument(), new PropertySetNumericTestCreateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            DefinitionName = "MCP_NUMERIC_TEST",
            PropertyName = "Comprimento",
            NumericValue = 123.456
        });

        Assert.Equal("MCP_NUMERIC_TEST", response.DefinitionName);
        Assert.Equal("TMP1", response.ObjectHandle);
        Assert.Equal("MCP_NUMERIC_TEST", response.PropertySet.Name);

        var value = Assert.Single(response.PropertySet.Properties);
        Assert.Equal("Comprimento", value.Name);
        Assert.Equal("Real", value.DataType);
        Assert.Equal("123.456", value.Value);
        Assert.Equal(123.456, value.NumericValue);
    }

    [Fact]
    public void PropertySetNumericTestCreateRejectsNonMcpDefinitionNames()
    {
        var service = new CivilReadService();

        var ex = Assert.Throws<CivilToolException>(() => service.CreatePropertySetNumericTest(new FakeCivilWriteAdapter(), SampleDocument(), new PropertySetNumericTestCreateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            DefinitionName = "Dados_Rodovia",
            PropertyName = "Comprimento",
            NumericValue = 123.456
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void PropertySetNumericTestCreateSchemaRequiresDefinitionPropertyAndValue()
    {
        var schema = CivilSchemas.PropertySetNumericTestCreateInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.Contains("definition_name", required);
        Assert.Contains("property_name", required);
        Assert.Contains("numeric_value", required);
        Assert.Contains("target_kind", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    private static PropertySetInfo SamplePropertySet() =>
        new()
        {
            Name = "Rodovia",
            Handle = "PS1",
            DefinitionHandle = "PSD1",
            PropertyCount = 2,
            Properties =
            [
                new PropertyValueInfo { Name = "Codigo", DataType = "Text", Value = "BR-040" },
                new PropertyValueInfo { Name = "Extensao", DataType = "Real", Value = "12.5", NumericValue = 12.5 }
            ]
        };

    private static CorridorInfo SampleCorridor(string name, string handle, bool withBaselines) =>
        new()
        {
            Name = name,
            Handle = handle,
            Description = null,
            IsOutOfDate = false,
            RebuildAutomatic = true,
            CodeSetStyleName = "_MP Codes",
            MaximumTriangleSideLength = 30.5,
            RegionLockMode = "Unlocked",
            BaselineCount = 2,
            CorridorSurfaceCount = 1,
            Baselines = withBaselines
                ?
                [
                    new CorridorBaselineInfo
                    {
                        Name = "BL - BD - 100",
                        BaselineType = "AlignmentProfile",
                        AlignmentName = "BD - 100",
                        AlignmentHandle = "A1",
                        ProfileName = "V - BD - 100",
                        ProfileHandle = "1124BE",
                        StartStation = 2000,
                        EndStation = 2357.5,
                        RegionCount = 2,
                        Regions =
                        [
                            new CorridorRegionInfo
                            {
                                Name = "RG - BD - 100 - 1",
                                StartStation = 2000,
                                EndStation = 2180,
                                AssemblyName = "ASSEMBLY-PISTA",
                                AssemblyHandle = "S1"
                            },
                            new CorridorRegionInfo
                            {
                                Name = "RG - BD - 100 - 2",
                                StartStation = 2180,
                                EndStation = 2357.5,
                                AssemblyName = "ASSEMBLY-PONTE",
                                AssemblyHandle = "S2"
                            }
                        ]
                    },
                    new CorridorBaselineInfo
                    {
                        Name = "BL - BD - 100 - ACESSO",
                        BaselineType = "AlignmentProfile",
                        AlignmentName = "BD - Acesso",
                        StartStation = 0,
                        EndStation = 120,
                        RegionCount = 0
                    }
                ]
                : []
        };

    private static ProfileInfo SampleProfile(string name, string handle, string alignmentName) =>
        new()
        {
            Name = name,
            Handle = handle,
            AlignmentName = alignmentName,
            AlignmentHandle = "A1",
            Description = null,
            ProfileType = "FG",
            StartingStation = 0,
            EndingStation = 2000,
            Length = 2000,
            Offset = 0,
            ElevationMin = 150.25,
            ElevationMax = 800.5,
            PviCount = 12,
            StyleName = "Design Profile"
        };

    private sealed class FakeCivilReadAdapter : ICivilReadAdapter
    {
        private readonly IReadOnlyList<AlignmentInfo> _alignments =
        [
            SampleAlignment("MAIN_ROAD", "A1"),
            SampleAlignment("SIDE_ROAD", "A2")
        ];

        private readonly IReadOnlyList<SurfaceInfo> _surfaces = [SampleSurface("EG")];
        private readonly IReadOnlyList<CogoPointInfo> _points = [SamplePoint(1)];

        public IReadOnlyList<AlignmentInfo> ListAlignments() => _alignments;

        public AlignmentInfo? GetAlignment(CivilObjectLocator locator) =>
            _alignments.FirstOrDefault(item =>
                string.Equals(item.Name, locator.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase));

        public AlignmentStationOffsetResult GetStationOffset(CivilObjectLocator locator, double x, double y) =>
            new(GetAlignment(locator) ?? throw new CivilToolException(ErrorCode.AlignmentNotFound, "Alignment missing."), 100, 12.5);

        public AlignmentPointLocationResult GetPointLocation(CivilObjectLocator locator, double station, double offset) =>
            new(GetAlignment(locator) ?? throw new CivilToolException(ErrorCode.AlignmentNotFound, "Alignment missing."), 1000, 2000);

        public IReadOnlyList<SurfaceInfo> ListSurfaces() => _surfaces;

        public SurfaceInfo? GetSurface(CivilObjectLocator locator) =>
            _surfaces.FirstOrDefault(item =>
                string.Equals(item.Name, locator.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase));

        public SurfaceElevationAtXyResult GetSurfaceElevationAtXy(CivilObjectLocator locator, double x, double y) =>
            new(GetSurface(locator) ?? throw new CivilToolException(ErrorCode.SurfaceNotFound, "Surface missing."), 101.5);

        public IReadOnlyList<CogoPointInfo> ListCogoPoints() => _points;

        public CogoPointInfo? GetCogoPoint(CogoPointLocator locator) =>
            _points.FirstOrDefault(item =>
                item.PointNumber == locator.PointNumber ||
                string.Equals(item.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase));

        private readonly IReadOnlyList<ProfileInfo> _profiles =
        [
            SampleProfile("FG-MAIN", "P2", "MAIN_ROAD"),
            SampleProfile("EG-MAIN", "P1", "MAIN_ROAD"),
            SampleProfile("EG-SIDE", "P3", "SIDE_ROAD"),
            SampleProfile("SHARED", "P4", "MAIN_ROAD"),
            SampleProfile("SHARED", "P5", "SIDE_ROAD")
        ];

        public IReadOnlyList<ProfileInfo> ListProfiles() => _profiles;

        public ProfileStationResult GetProfileStationValues(string profileHandle, double station) =>
            new(742.5, 0.025);

        private readonly IReadOnlyList<CorridorInfo> _corridors =
        [
            SampleCorridor("CORR-BD-NORTE", "K2", withBaselines: true),
            SampleCorridor("CORR-BD-100", "K1", withBaselines: true)
        ];

        public IReadOnlyList<CorridorInfo> ListCorridors() =>
            _corridors.Select(item => item with { Baselines = [] }).ToArray();

        public IReadOnlyList<PropertySetDefinitionInfo> ListPropertySetDefinitions() =>
        [
            new()
            {
                Name = "Rodovia",
                Handle = "PSD1",
                IsStyleBased = false,
                IsVisible = true,
                AppliesToAll = false,
                AppliesTo = ["AeccDbAlignment"],
                PropertyCount = 2,
                Properties =
                [
                    new PropertyDefinitionInfo { Name = "Codigo", DataType = "Text" },
                    new PropertyDefinitionInfo { Name = "Extensao", DataType = "Real", Units = "m" }
                ]
            },
            new()
            {
                Name = "Faixa de Dominio",
                Handle = "PSD2",
                IsStyleBased = false,
                IsVisible = true,
                AppliesToAll = true,
                PropertyCount = 1,
                Properties = [new PropertyDefinitionInfo { Name = "Largura", DataType = "Real", Units = "m" }]
            }
        ];

        public IReadOnlyList<PropertySetInfo>? ListPropertySets(string objectHandle) =>
            string.Equals(objectHandle, "E1", StringComparison.OrdinalIgnoreCase)
                ?
                [
                    new PropertySetInfo
                    {
                        Name = "Rodovia",
                        Handle = "PS1",
                        DefinitionHandle = "PSD1",
                        PropertyCount = 2,
                        Properties =
                        [
                            new PropertyValueInfo { Name = "Codigo", DataType = "Text", Value = "BR-040" },
                            new PropertyValueInfo { Name = "Extensao", DataType = "Real", Value = "12.5", NumericValue = 12.5, Units = "m" }
                        ]
                    }
                ]
                : null;

        public CorridorInfo? GetCorridor(CivilObjectLocator locator) =>
            _corridors.FirstOrDefault(item =>
                string.Equals(item.Name, locator.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeCivilWriteAdapter : ICivilWriteAdapter
    {
        public PropertySetNumericTestResult CreatePropertySetNumericTest(PropertySetNumericTestCreateRequest request)
        {
            var value = request.NumericValue.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
            var propertySet = new PropertySetInfo
            {
                Name = request.DefinitionName,
                Handle = "PS-TMP1",
                DefinitionHandle = "PSD-TMP1",
                PropertyCount = 1,
                Properties =
                [
                    new PropertyValueInfo
                    {
                        Name = request.PropertyName,
                        Id = 1,
                        DataType = "Real",
                        Value = value,
                        NumericValue = request.NumericValue
                    }
                ]
            };

            return new PropertySetNumericTestResult(
                request.DefinitionName,
                "PSD-TMP1",
                "TMP1",
                request.TargetKind ?? "db_point",
                propertySet);
        }
    }
}

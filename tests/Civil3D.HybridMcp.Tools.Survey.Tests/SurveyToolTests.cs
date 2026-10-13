using System.Text.Json;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Survey;

namespace Civil3D.HybridMcp.Tools.Survey.Tests;

public sealed class SurveyToolTests
{
    [Fact]
    public void SurveyBearingToAzimuthSerializesWithSnakeCaseFields()
    {
        var response = new SurveyBearingToAzimuthResponse
        {
            InstanceId = "instance-1",
            InputBearing = "S 45 0 0 E",
            Bearing = new SurveyBearingInfo
            {
                NorthSouth = "S",
                AngleDegrees = 45,
                EastWest = "E",
                Text = "S 45 E"
            },
            AzimuthDegrees = 135
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<SurveyBearingToAzimuthResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"instance_id\"", json);
        Assert.Contains("\"input_bearing\"", json);
        Assert.Contains("\"azimuth_degrees\"", json);
        Assert.Contains("\"north_south\"", json);
        Assert.Contains("\"east_west\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(135, roundTrip!.AzimuthDegrees);
        Assert.Equal("S", roundTrip.Bearing.NorthSouth);
    }

    [Theory]
    [InlineData("N 30 0 0 E", 30)]
    [InlineData("S 45 0 0 E", 135)]
    [InlineData("S 12 30 0 W", 192.5)]
    [InlineData("N 45 30 0 W", 314.5)]
    public void BearingToAzimuthParsesQuadrantBearings(string bearing, double expectedAzimuth)
    {
        var service = new SurveyGeometryService();

        var response = service.BearingToAzimuth("instance-1", new SurveyBearingToAzimuthRequest
        {
            Bearing = bearing
        });

        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal(expectedAzimuth, response.AzimuthDegrees, precision: 8);
    }

    [Theory]
    [InlineData(45, "N", 45, "E")]
    [InlineData(135, "S", 45, "E")]
    [InlineData(225, "S", 45, "W")]
    [InlineData(315, "N", 45, "W")]
    [InlineData(450, "N", 90, "E")]
    public void AzimuthToBearingNormalizesAngles(double azimuth, string northSouth, double angle, string eastWest)
    {
        var service = new SurveyGeometryService();

        var response = service.AzimuthToBearing("instance-1", new SurveyAzimuthToBearingRequest
        {
            AzimuthDegrees = azimuth
        });

        Assert.Equal(NormalizeAzimuth(azimuth), response.AzimuthDegrees, precision: 8);
        Assert.Equal(northSouth, response.Bearing.NorthSouth);
        Assert.Equal(angle, response.Bearing.AngleDegrees, precision: 8);
        Assert.Equal(eastWest, response.Bearing.EastWest);
    }

    [Fact]
    public void DistanceUsesSurveyAzimuthFromEastingNorthingDeltas()
    {
        var service = new SurveyGeometryService();

        var response = service.Distance("instance-1", new SurveyDistanceRequest
        {
            StartX = 1000,
            StartY = 2000,
            EndX = 1010,
            EndY = 2010
        });

        Assert.Equal(10, response.DeltaX);
        Assert.Equal(10, response.DeltaY);
        Assert.Equal(Math.Sqrt(200), response.Distance, precision: 8);
        Assert.Equal(45, response.AzimuthDegrees, precision: 8);
    }

    [Fact]
    public void PointByAzimuthUsesSurveyEastingNorthingConvention()
    {
        var service = new SurveyGeometryService();

        var response = service.PointByAzimuth("instance-1", new SurveyPointByAzimuthRequest
        {
            StartX = 1000,
            StartY = 2000,
            AzimuthDegrees = 90,
            Distance = 25
        });

        Assert.Equal(1025, response.X, precision: 8);
        Assert.Equal(2000, response.Y, precision: 8);
    }

    [Fact]
    public void FbkDryRunParsesControlPointsSetupsAndObservationsWithoutCadAccess()
    {
        var service = new SurveyGeometryService();
        const string fbk = """
            JOB "MCP SAMPLE"
            UNIT METER
            NEZ 1 2000.000 1000.000 50.000 "CP-1"
            NEZ 2 2010.000 1000.000 51.000 "BS-2"
            STN 1 1.500 "Setup 1"
            BS 2 0.0000
            PRISM 1.800
            F1 VA 10 45.0000 25.000 88.5000 "TREE"
            F2 ZA 11 225.0000 30.000 91.2500 "POLE"
            """;

        var response = service.FbkDryRun("instance-1", new SurveyFbkDryRunRequest
        {
            SourceName = "sample.fbk",
            Content = fbk
        });

        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal("sample.fbk", response.SourceName);
        Assert.True(response.DryRun);
        Assert.Equal(9, response.TotalLines);
        Assert.Equal(9, response.RecordCount);
        Assert.Equal(2, response.ControlPointCount);
        Assert.Equal(1, response.SetupCount);
        Assert.Equal(2, response.ObservationCount);
        Assert.Equal(0, response.WarningCount);
        Assert.Equal(2, response.ControlPoints.Count);
        var setup = Assert.Single(response.Setups);
        Assert.Equal(1, response.ControlPoints[0].PointNumber);
        Assert.Equal(2000, response.ControlPoints[0].Northing);
        Assert.Equal(1000, response.ControlPoints[0].Easting);
        Assert.Equal("CP-1", response.ControlPoints[0].Description);
        Assert.Equal(1, setup.StationPoint);
        Assert.Equal(1.5, setup.InstrumentHeight);
        Assert.Equal(2, setup.BacksightPoint);
        Assert.Equal(2, setup.ObservationCount);
        Assert.Equal([8, 9], setup.ObservationLines);
        Assert.Equal(1.8, response.Records.Single(record => record.LineNumber == 8).PrismHeight);
        Assert.Contains("NEZ", response.SupportedKeywords);
        Assert.Contains("F1", response.SupportedKeywords);
    }

    [Fact]
    public void FbkDryRunReportsWarningsForUnknownKeywordsAndObservationsBeforeSetup()
    {
        var service = new SurveyGeometryService();
        const string fbk = """
            F1 VA 10 45.0000 25.000 88.5000 "TREE"
            BOGUS 1 2 3
            STN 1 0.000
            """;

        var response = service.FbkDryRun("instance-1", new SurveyFbkDryRunRequest
        {
            Content = fbk
        });

        Assert.Equal(3, response.RecordCount);
        Assert.Equal(1, response.SetupCount);
        Assert.Equal(0, response.ObservationCount);
        Assert.Equal(2, response.WarningCount);
        Assert.Contains(response.Warnings, warning => warning.Contains("observation before STN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Warnings, warning => warning.Contains("Unrecognized keyword: BOGUS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FbkDryRunSkipsBangComments()
    {
        var service = new SurveyGeometryService();
        const string fbk = """
            ! Field crew note
            NEZ 1 2000 1000 50 "CP-1"
            """;

        var response = service.FbkDryRun("instance-1", new SurveyFbkDryRunRequest
        {
            Content = fbk
        });

        Assert.Equal(2, response.TotalLines);
        Assert.Equal(1, response.RecordCount);
        Assert.Equal(1, response.ControlPointCount);
        Assert.Equal(0, response.WarningCount);
    }

    [Fact]
    public void DatabaseStatusReportsUnavailableGatewayWithoutComAccess()
    {
        var service = new SurveyGeometryService();

        var response = service.DatabaseStatus("instance-1", new SurveyDatabaseStatusRequest());

        Assert.Equal("instance-1", response.InstanceId);
        Assert.False(response.SurveyComAvailable);
        Assert.False(response.SurveyDatabaseAvailable);
        Assert.True(response.RequiresActiveDocument);
        Assert.Equal("none", response.Adapter);
        Assert.Equal("not_configured", response.Status);
        Assert.Contains(response.Limitations, limitation => limitation.Contains("adapter is not configured", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DatabaseStatusUsesInjectedGateway()
    {
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(new SurveyDatabaseStatusResponse
        {
            InstanceId = "gateway-instance",
            SurveyComAvailable = true,
            SurveyDatabaseAvailable = false,
            RequiresActiveDocument = true,
            Adapter = "fake",
            Status = "requires_active_document",
            InteropAssemblies = ["Autodesk.AECC.Interop.Survey.dll"],
            Capabilities = ["working_folder_read"],
            Limitations = ["No active drawing is open."]
        }));

        var response = service.DatabaseStatus("instance-1", new SurveyDatabaseStatusRequest());

        Assert.Equal("instance-1", response.InstanceId);
        Assert.True(response.SurveyComAvailable);
        Assert.False(response.SurveyDatabaseAvailable);
        Assert.Equal("fake", response.Adapter);
        Assert.Contains("working_folder_read", response.Capabilities);
    }

    [Fact]
    public void DatabaseStatusDetectsSurveyInteropFilesWithoutInitializingCom()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "Civil3D.HybridMcp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "Autodesk.AECC.Interop.Survey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(tempPath, "Autodesk.AECC.Interop.UiSurvey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(tempPath, "AeccXSurvey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(tempPath, "AeccSurvey.dbx"), string.Empty);
            var service = new SurveyGeometryService(new SurveyDatabaseEnvironmentGateway(tempPath));

            var response = service.DatabaseStatus("instance-1", new SurveyDatabaseStatusRequest());

            Assert.Equal("instance-1", response.InstanceId);
            Assert.True(response.SurveyComAvailable);
            Assert.False(response.SurveyDatabaseAvailable);
            Assert.True(response.RequiresActiveDocument);
            Assert.Equal("environment_probe", response.Adapter);
            Assert.Equal("interop_available", response.Status);
            Assert.Contains("Autodesk.AECC.Interop.Survey.dll", response.InteropAssemblies);
            Assert.Contains("Autodesk.AECC.Interop.UiSurvey.dll", response.InteropAssemblies);
            Assert.Contains("survey_com_interop_available", response.Capabilities);
            Assert.Contains(response.Limitations, limitation => limitation.Contains("does not initialize COM", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void DatabaseStatusCanProbeC3DSubdirectoryFromAutoCadRoot()
    {
        var autoCadRoot = Path.Combine(Path.GetTempPath(), "Civil3D.HybridMcp.Tests", Guid.NewGuid().ToString("N"));
        var c3dPath = Path.Combine(autoCadRoot, "C3D");
        Directory.CreateDirectory(c3dPath);
        try
        {
            File.WriteAllText(Path.Combine(c3dPath, "Autodesk.AECC.Interop.Survey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(c3dPath, "Autodesk.AECC.Interop.UiSurvey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(c3dPath, "AeccXSurvey.dll"), string.Empty);
            File.WriteAllText(Path.Combine(c3dPath, "AeccSurvey.dbx"), string.Empty);
            var service = new SurveyGeometryService(new SurveyDatabaseEnvironmentGateway(autoCadRoot));

            var response = service.DatabaseStatus("instance-1", new SurveyDatabaseStatusRequest());

            Assert.True(response.SurveyComAvailable);
            Assert.Equal("environment_probe", response.Adapter);
            Assert.Equal("interop_available", response.Status);
        }
        finally
        {
            Directory.Delete(autoCadRoot, recursive: true);
        }
    }

    [Fact]
    public void WorkingFolderGetUsesInjectedGateway()
    {
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            new SurveyDatabaseStatusResponse
            {
                InstanceId = "gateway-instance",
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = false,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "interop_available",
                InteropAssemblies = ["Autodesk.AECC.Interop.Survey.dll"],
                Capabilities = ["working_folder_read"],
                Limitations = []
            },
            new SurveyWorkingFolderGetResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "working_folder_available",
                WorkingFolder = @"C:\SurveyData",
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read"],
                Limitations = []
            }));

        var response = service.WorkingFolderGet("instance-1", new SurveyWorkingFolderGetRequest());

        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal("fake", response.Adapter);
        Assert.Equal("working_folder_available", response.Status);
        Assert.Equal(@"C:\SurveyData", response.WorkingFolder);
        Assert.True(response.RequiresActiveDocument);
        Assert.Contains("working_folder_read", response.Capabilities);
    }

    [Fact]
    public void ProjectListUsesInjectedGateway()
    {
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            new SurveyDatabaseStatusResponse
            {
                InstanceId = "gateway-instance",
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = true,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "projects_available",
                InteropAssemblies = ["Autodesk.AECC.Interop.Survey.dll"],
                Capabilities = ["working_folder_read", "project_list_read"],
                Limitations = []
            },
            projectListResponse: new SurveyProjectListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "projects_available",
                WorkingFolder = @"C:\SurveyData",
                Count = 2,
                Projects =
                [
                    new SurveyProjectInfo
                    {
                        Name = "Topo-2026",
                        Path = @"C:\SurveyData\Topo-2026",
                        ProjectGuid = "{11111111-1111-1111-1111-111111111111}"
                    },
                    new SurveyProjectInfo
                    {
                        Name = "Control",
                        Path = @"C:\SurveyData\Control",
                        ProjectGuid = "{22222222-2222-2222-2222-222222222222}"
                    }
                ],
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read", "project_list_read"],
                Limitations = ["Read-only project metadata."]
            }));

        var response = service.ProjectList("instance-1", new SurveyProjectListRequest());

        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal("fake", response.Adapter);
        Assert.Equal("projects_available", response.Status);
        Assert.Equal(@"C:\SurveyData", response.WorkingFolder);
        Assert.Equal(2, response.Count);
        Assert.Equal(2, response.Projects.Count);
        Assert.Equal("Topo-2026", response.Projects[0].Name);
        Assert.Equal(@"{11111111-1111-1111-1111-111111111111}", response.Projects[0].ProjectGuid);
        Assert.True(response.RequiresActiveDocument);
        Assert.Contains("project_list_read", response.Capabilities);
    }

    [Fact]
    public void NetworkListUsesInjectedGatewayAndForwardsProjectFilter()
    {
        var gateway = new FakeSurveyDatabaseGateway(
            SampleStatus(),
            networkListResponse: new SurveyNetworkListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "networks_available",
                ProjectName = "Topo-2026",
                WorkingFolder = @"C:\SurveyData",
                Count = 2,
                Networks =
                [
                    new SurveyNetworkInfo
                    {
                        Name = "NET-CONTROLE",
                        ProjectName = "Topo-2026",
                        Description = "Rede de controle",
                        Path = @"C:\SurveyData\Topo-2026\Survey\NET-CONTROLE",
                        Id = 1
                    },
                    new SurveyNetworkInfo
                    {
                        Name = "NET-APOIO",
                        ProjectName = "Topo-2026"
                    }
                ],
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read", "project_list_read", "network_list_read"],
                Limitations = ["Read-only network metadata."]
            });
        var service = new SurveyGeometryService(gateway);

        var response = service.NetworkList("instance-1", new SurveyNetworkListRequest
        {
            ProjectName = "Topo-2026"
        });

        Assert.Equal("Topo-2026", gateway.LastProjectNameFilter);
        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal("fake", response.Adapter);
        Assert.Equal("networks_available", response.Status);
        Assert.Equal("Topo-2026", response.ProjectName);
        Assert.Equal(@"C:\SurveyData", response.WorkingFolder);
        Assert.Equal(2, response.Count);
        Assert.Equal("NET-CONTROLE", response.Networks[0].Name);
        Assert.Equal("Rede de controle", response.Networks[0].Description);
        Assert.Equal(1, response.Networks[0].Id);
        Assert.Null(response.Networks[1].Description);
        Assert.True(response.RequiresActiveDocument);
        Assert.Contains("network_list_read", response.Capabilities);
    }

    [Fact]
    public void NetworkListWithoutProjectFilterPassesNullToGateway()
    {
        var gateway = new FakeSurveyDatabaseGateway(
            SampleStatus(),
            networkListResponse: new SurveyNetworkListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "no_projects",
                Count = 0,
                RequiresActiveDocument = true
            });
        var service = new SurveyGeometryService(gateway);

        var response = service.NetworkList("instance-1", new SurveyNetworkListRequest());

        Assert.Null(gateway.LastProjectNameFilter);
        Assert.Equal("no_projects", response.Status);
        Assert.Equal(0, response.Count);
        Assert.Empty(response.Networks);
    }

    [Fact]
    public void NetworkListSerializesWithSnakeCaseFields()
    {
        var response = new SurveyNetworkListResponse
        {
            InstanceId = "instance-1",
            Adapter = "survey_com",
            Status = "networks_partial",
            ProjectName = "Topo-2026",
            WorkingFolder = @"C:\SurveyData",
            Count = 1,
            Networks =
            [
                new SurveyNetworkInfo
                {
                    Name = "NET-CONTROLE",
                    ProjectName = "Topo-2026",
                    Description = "Rede de controle",
                    Path = @"C:\SurveyData\Topo-2026",
                    Id = 7
                }
            ],
            RequiresActiveDocument = true,
            Warnings = ["Project 'Control' networks could not be read."]
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<SurveyNetworkListResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"instance_id\"", json);
        Assert.Contains("\"project_name\"", json);
        Assert.Contains("\"working_folder\"", json);
        Assert.Contains("\"requires_active_document\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("networks_partial", roundTrip!.Status);
        Assert.Equal("NET-CONTROLE", roundTrip.Networks[0].Name);
        Assert.Equal(7, roundTrip.Networks[0].Id);
        Assert.Single(roundTrip.Warnings);
    }

    [Fact]
    public async Task NetworkListToolExecutesThroughHostDispatcherAndIncludesActiveDocument()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.NetworkList);
        var document = SampleDocument();
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            SampleStatus(),
            networkListResponse: new SurveyNetworkListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "networks_available",
                WorkingFolder = @"C:\SurveyData",
                Count = 1,
                Networks =
                [
                    new SurveyNetworkInfo
                    {
                        Name = "NET-CONTROLE",
                        ProjectName = "Topo-2026"
                    }
                ],
                RequiresActiveDocument = true,
                Capabilities = ["network_list_read"]
            }));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher, () => document);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.NetworkList
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal("doc-1", response.Document!.DocumentId);
        Assert.Equal(1, response.Data!["count"]!.GetValue<int>());
        Assert.Equal("NET-CONTROLE", response.Data["networks"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task NetworkListToolPropagatesStructuredDocumentErrorDetails()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.NetworkList);
        var service = new SurveyGeometryService(new ThrowingSurveyDatabaseGateway(
            new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey network listing requires an active Civil 3D document.",
                new()
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = 0,
                    ["requires_active_document"] = true
                })));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.NetworkList
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.DocumentNotFound, response.Error!.Code);
        Assert.Equal("survey_com", response.Error.Details!["adapter"]!.GetValue<string>());
        Assert.Equal(0, response.Error.Details["document_count"]!.GetValue<int>());
        Assert.True(response.Error.Details["requires_active_document"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NetworkListToolReadsOptionalProjectNameArgument()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.NetworkList);
        var gateway = new FakeSurveyDatabaseGateway(
            SampleStatus(),
            networkListResponse: new SurveyNetworkListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "networks_available",
                ProjectName = "Control",
                Count = 0,
                RequiresActiveDocument = true
            });
        var handler = new SurveyToolHandler(descriptor, "instance-1", new SurveyGeometryService(gateway), dispatcher);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.NetworkList,
            Arguments = new JsonObject
            {
                ["project_name"] = "Control"
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Control", gateway.LastProjectNameFilter);
    }

    [Fact]
    public void UnavailableGatewayRejectsNetworkListAsNotSupported()
    {
        var service = new SurveyGeometryService();

        var ex = Assert.Throws<SurveyToolException>(() =>
            service.NetworkList("instance-1", new SurveyNetworkListRequest()));

        Assert.Equal(ErrorCode.NotSupported, ex.Code);
    }

    [Fact]
    public void EnvironmentProbeGatewayRejectsNetworkListAsNotSupported()
    {
        var gateway = new SurveyDatabaseEnvironmentGateway(civil3DDirectory: null);

        var ex = Assert.Throws<SurveyToolException>(() => gateway.ListNetworks("instance-1", projectName: null));

        Assert.Equal(ErrorCode.NotSupported, ex.Code);
    }

    [Fact]
    public async Task WorkingFolderToolExecutesThroughHostDispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.WorkingFolderGet);
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            new SurveyDatabaseStatusResponse
            {
                InstanceId = "gateway-instance",
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = false,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "interop_available"
            },
            new SurveyWorkingFolderGetResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "working_folder_available",
                WorkingFolder = @"C:\SurveyData",
                RequiresActiveDocument = true
            }));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.WorkingFolderGet
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal(@"C:\SurveyData", response.Data!["working_folder"]!.GetValue<string>());
    }

    [Fact]
    public async Task WorkingFolderToolIncludesActiveDocumentWhenAccessorIsConfigured()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.WorkingFolderGet);
        var document = SampleDocument();
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            new SurveyDatabaseStatusResponse
            {
                InstanceId = "gateway-instance",
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = false,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "interop_available"
            },
            new SurveyWorkingFolderGetResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "working_folder_available",
                WorkingFolder = @"C:\SurveyData",
                RequiresActiveDocument = true
            }));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher, () => document);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.WorkingFolderGet
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("doc-1", response.Document!.DocumentId);
        Assert.Equal("SurveyTest.dwg", response.Document.Name);
    }

    [Fact]
    public async Task WorkingFolderToolPropagatesStructuredDocumentErrorDetails()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.WorkingFolderGet);
        var service = new SurveyGeometryService(new ThrowingSurveyDatabaseGateway(
            new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey working folder read requires an active Civil 3D document.",
                new()
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = 0,
                    ["requires_active_document"] = true
                })));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.WorkingFolderGet
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.DocumentNotFound, response.Error!.Code);
        Assert.Equal("survey_com", response.Error.Details!["adapter"]!.GetValue<string>());
        Assert.Equal(0, response.Error.Details["document_count"]!.GetValue<int>());
        Assert.True(response.Error.Details["requires_active_document"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ProjectListToolExecutesThroughHostDispatcherAndIncludesActiveDocument()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.ProjectList);
        var document = SampleDocument();
        var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway(
            new SurveyDatabaseStatusResponse
            {
                InstanceId = "gateway-instance",
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = true,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "projects_available"
            },
            projectListResponse: new SurveyProjectListResponse
            {
                InstanceId = "gateway-instance",
                Adapter = "fake",
                Status = "projects_available",
                WorkingFolder = @"C:\SurveyData",
                Count = 1,
                Projects =
                [
                    new SurveyProjectInfo
                    {
                        Name = "Topo-2026",
                        Path = @"C:\SurveyData\Topo-2026",
                        ProjectGuid = "{11111111-1111-1111-1111-111111111111}"
                    }
                ],
                RequiresActiveDocument = true,
                Capabilities = ["project_list_read"],
                Limitations = []
            }));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher, () => document);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.ProjectList
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal("doc-1", response.Document!.DocumentId);
        Assert.Equal(1, response.Data!["count"]!.GetValue<int>());
        Assert.Equal("Topo-2026", response.Data["projects"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProjectListToolPropagatesStructuredDocumentErrorDetails()
    {
        var dispatcher = new RecordingDispatcher();
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.ProjectList);
        var service = new SurveyGeometryService(new ThrowingSurveyDatabaseGateway(
            new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey project listing requires an active Civil 3D document.",
                new()
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = 0,
                    ["requires_active_document"] = true
                })));
        var handler = new SurveyToolHandler(descriptor, "instance-1", service, dispatcher);

        var response = await handler.ExecuteAsync(new ToolRequest
        {
            CorrelationId = "correlation-1",
            SessionId = "session-1",
            InstanceId = "instance-1",
            ToolName = SurveyToolNames.ProjectList
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.DocumentNotFound, response.Error!.Code);
        Assert.Equal("survey_com", response.Error.Details!["adapter"]!.GetValue<string>());
        Assert.Equal(0, response.Error.Details["document_count"]!.GetValue<int>());
        Assert.True(response.Error.Details["requires_active_document"]!.GetValue<bool>());
    }

    [Fact]
    public void InvalidBearingRaisesStructuredErrorCode()
    {
        var service = new SurveyGeometryService();

        var ex = Assert.Throws<SurveyToolException>(() => service.BearingToAzimuth("instance-1", new SurveyBearingToAzimuthRequest
        {
            Bearing = "E 45 N"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void NegativeDistanceIsRejected()
    {
        var service = new SurveyGeometryService();

        var ex = Assert.Throws<SurveyToolException>(() => service.PointByAzimuth("instance-1", new SurveyPointByAzimuthRequest
        {
            StartX = 1000,
            StartY = 2000,
            AzimuthDegrees = 10,
            Distance = -1
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void ManifestPublishesReadOnlySurveyGeometryTools()
    {
        var tools = SurveyToolCatalog.ListTools();

        Assert.Equal(9, tools.Count);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.BearingToAzimuth);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.AzimuthToBearing);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.Distance);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.PointByAzimuth);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.FbkDryRun);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.DatabaseStatus);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.WorkingFolderGet);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.ProjectList);
        Assert.Contains(tools, tool => tool.Name == SurveyToolNames.NetworkList);
        Assert.All(tools, tool => Assert.Equal(OperationClass.Read, tool.OperationClass));
        Assert.All(tools, tool => Assert.Equal(ToolRisk.Low, tool.Risk));
        Assert.All(tools, tool => Assert.Equal(RollbackSemantics.NoneRequired, tool.RollbackSemantics));
        Assert.All(tools, tool => Assert.Equal("Survey", tool.Module));
        Assert.All(tools, tool => Assert.False(tool.RequiresConfirmation));
    }

    [Fact]
    public void BearingToAzimuthSchemaRequiresBearingOnly()
    {
        var schema = SurveySchemas.BearingToAzimuthInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("bearing", required);
        Assert.Contains("bearing", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void DistanceSchemaRequiresCoordinatePairs()
    {
        var schema = SurveySchemas.DistanceInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Contains("start_x", required);
        Assert.Contains("start_y", required);
        Assert.Contains("end_x", required);
        Assert.Contains("end_y", required);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void FbkDryRunSchemaRequiresContentOnly()
    {
        var schema = SurveySchemas.FbkDryRunInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Contains("content", required);
        Assert.DoesNotContain("source_name", required);
        Assert.Contains("content", properties.Select(property => property.Key));
        Assert.Contains("source_name", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void DatabaseStatusSchemaHasNoRequiredArguments()
    {
        var schema = SurveySchemas.DatabaseStatusInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Empty(required);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void ProjectListSchemaHasNoRequiredArguments()
    {
        var schema = SurveySchemas.ProjectListInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Empty(required);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void NetworkListSchemaAcceptsOptionalProjectName()
    {
        var schema = SurveySchemas.NetworkListInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var properties = schema["properties"]!.AsObject();

        Assert.Empty(required);
        Assert.Contains("project_name", properties.Select(property => property.Key));
        Assert.Equal("string", properties["project_name"]!["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void NetworkListToolIsReadOnlyWithTenSecondTimeout()
    {
        var descriptor = SurveyToolCatalog.ListTools().Single(tool => tool.Name == SurveyToolNames.NetworkList);

        Assert.Equal(OperationClass.Read, descriptor.OperationClass);
        Assert.Equal(ToolRisk.Low, descriptor.Risk);
        Assert.Equal(RollbackSemantics.NoneRequired, descriptor.RollbackSemantics);
        Assert.False(descriptor.RequiresConfirmation);
        Assert.Equal(TimeSpan.FromSeconds(10), descriptor.Timeout);
        Assert.Equal("Survey", descriptor.Module);
    }

    [Fact]
    public void WorkingFolderGetSchemaHasNoRequiredArguments()
    {
        var schema = SurveySchemas.WorkingFolderGetInput();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Empty(required);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    private static double NormalizeAzimuth(double azimuth)
    {
        var normalized = azimuth % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static DocumentDescriptor SampleDocument() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "SurveyTest.dwg",
            IsActive = true,
            IsSaved = true,
            IsReadOnly = false,
            SessionIdentifier = "session-doc-1"
        };

    private static SurveyDatabaseStatusResponse SampleStatus() =>
        new()
        {
            InstanceId = "gateway-instance",
            SurveyComAvailable = true,
            SurveyDatabaseAvailable = true,
            RequiresActiveDocument = true,
            Adapter = "fake",
            Status = "adapter_available"
        };

    private sealed class FakeSurveyDatabaseGateway : ISurveyDatabaseGateway
    {
        private readonly SurveyDatabaseStatusResponse _statusResponse;
        private readonly SurveyWorkingFolderGetResponse? _workingFolderResponse;
        private readonly SurveyProjectListResponse? _projectListResponse;
        private readonly SurveyNetworkListResponse? _networkListResponse;

        public FakeSurveyDatabaseGateway(
            SurveyDatabaseStatusResponse statusResponse,
            SurveyWorkingFolderGetResponse? workingFolderResponse = null,
            SurveyProjectListResponse? projectListResponse = null,
            SurveyNetworkListResponse? networkListResponse = null)
        {
            _statusResponse = statusResponse;
            _workingFolderResponse = workingFolderResponse;
            _projectListResponse = projectListResponse;
            _networkListResponse = networkListResponse;
        }

        public string? LastProjectNameFilter { get; private set; }

        public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
            _statusResponse with { InstanceId = instanceId };

        public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
            (_workingFolderResponse ?? throw new SurveyToolException(ErrorCode.NotSupported, "Working folder read is not configured."))
            with { InstanceId = instanceId };

        public SurveyProjectListResponse ListProjects(string instanceId) =>
            (_projectListResponse ?? throw new SurveyToolException(ErrorCode.NotSupported, "Survey project listing is not configured."))
            with { InstanceId = instanceId };

        public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName)
        {
            LastProjectNameFilter = projectName;
            return (_networkListResponse ?? throw new SurveyToolException(ErrorCode.NotSupported, "Survey network listing is not configured."))
                with { InstanceId = instanceId };
        }
    }

    private sealed class ThrowingSurveyDatabaseGateway : ISurveyDatabaseGateway
    {
        private readonly SurveyToolException _exception;

        public ThrowingSurveyDatabaseGateway(SurveyToolException exception)
        {
            _exception = exception;
        }

        public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
            throw _exception;

        public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
            throw _exception;

        public SurveyProjectListResponse ListProjects(string instanceId) =>
            throw _exception;

        public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName) =>
            throw _exception;
    }

    private sealed class RecordingDispatcher : IHostApplicationDispatcher
    {
        public int CallCount { get; private set; }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(action(cancellationToken));
        }
    }
}

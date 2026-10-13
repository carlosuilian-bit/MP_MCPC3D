// =============================================================================
// Civil3dMcpBridge.cs
// .NET Plugin that runs INSIDE Civil 3D's process, exposing a local HTTP API
// for the MCP server to call into.
//
// BUILD REQUIREMENTS:
//   - .NET 8.0 (AutoCAD/Civil 3D 2026 target framework)
//   - References:
//       AcMgd.dll, AcDbMgd.dll, AcCoreMgd.dll    (AutoCAD managed)
//       AeccDbMgd.dll, AeccCogoMgd.dll, AeccUiMgd.dll (Civil 3D managed)
//   - These DLLs are in: C:\Program Files\Autodesk\AutoCAD 2026\
//   - Set "Copy Local" = false for all Autodesk references
//
// LOADING:
//   In Civil 3D command line: NETLOAD -> browse to Civil3dMcpBridge.dll
//   Then: MCPSTART to start the HTTP listener
//         MCPSTOP to stop it
//
// ARCHITECTURE:
//   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  stdio   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  HTTP    â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
//   â”‚  Claude   â”‚â—„â”€â”€â”€â”€â”€â”€â”€â”€â–ºâ”‚ MCP Server â”‚â—„â”€â”€â”€â”€â”€â”€â”€â”€â–ºâ”‚ THIS PLUGIN â”‚
//   â”‚          â”‚  (MCP)   â”‚ (Python)   â”‚ :8270   â”‚ (in C3D)    â”‚
//   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜          â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜          â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;          // Include Newtonsoft.Json NuGet or bundle it
using Newtonsoft.Json.Linq;

// AutoCAD base
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

// Civil 3D specific
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.Settings;

// Civil 3D 2026 Survey API is COM-only; interop wraps AeccXSurveyLib v13.8
using Autodesk.AECC.Interop.Survey;
using Autodesk.AECC.Interop.UiLand;

// Register commands with AutoCAD
[assembly: CommandClass(typeof(Civil3dMcpBridge.BridgeCommands))]

namespace Civil3dMcpBridge
{
    // =========================================================================
    // COMMAND REGISTRATION
    // =========================================================================

    public class BridgeCommands
    {
        private static HttpBridge _bridge;

        [CommandMethod("MCPSTART")]
        public void StartBridge()
        {
            if (_bridge != null && _bridge.IsRunning)
            {
                Ed.WriteMessage("\nMCP Bridge is already running on port 8270.");
                return;
            }

            _bridge = new HttpBridge(8270);
            _bridge.Start();
            Ed.WriteMessage("\nMCP Bridge started on http://localhost:8270/");
            Ed.WriteMessage("\nReady to receive commands from Claude.");
        }

        [CommandMethod("MCPSTOP")]
        public void StopBridge()
        {
            if (_bridge != null)
            {
                _bridge.Stop();
                _bridge = null;
                Ed.WriteMessage("\nMCP Bridge stopped.");
            }
        }

        [CommandMethod("MCPSTATUS")]
        public void BridgeStatus()
        {
            if (_bridge != null && _bridge.IsRunning)
                Ed.WriteMessage("\nMCP Bridge: RUNNING on port 8270");
            else
                Ed.WriteMessage("\nMCP Bridge: STOPPED");
        }

        private Editor Ed =>
            Application.DocumentManager.MdiActiveDocument.Editor;
    }

    // =========================================================================
    // WORKFLOW TRACE LOG
    // Appends every bridge call (action, params, result summary, status,
    // duration) to a per-session JSONL file. Purpose: capture real survey/CAD
    // workflow sequences while the licensed software is available, to spec and
    // validate an open-source clone later. Observation-only: every failure is
    // swallowed so tracing can never break a CAD operation.
    //   Disable with env CAD_MCP_TRACE=0 ; redirect with CAD_MCP_TRACE_DIR.
    // =========================================================================

    internal static class TraceLog
    {
        private static readonly object _lock = new object();
        // No-BOM UTF-8: File.AppendAllText writes the encoding preamble on
        // file creation, and a BOM on line 1 breaks strict json.loads readers.
        private static readonly Encoding _enc = new UTF8Encoding(false);
        private const int MaxResultStr = 4000;
        private static bool _disabled;
        private static string _path;
        private static string _session;
        private static int _seq;

        private static string ResolveDir()
        {
            var env = Environment.GetEnvironmentVariable("CAD_MCP_TRACE_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            try
            {
                var asmDir = Path.GetDirectoryName(typeof(TraceLog).Assembly.Location);
                // Climb to the repo root (marked by the canonical csproj / .git)
                // and write to <repo>\traces, so sidecar and canonical builds
                // share one trace corpus.
                var dir = new DirectoryInfo(asmDir);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Civil3dMcpBridge.csproj"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return Path.Combine(dir.FullName, "traces");
                    dir = dir.Parent;
                }
                return Path.Combine(asmDir, "traces");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "civil3d-mcp-traces");
            }
        }

        private static void EnsureInit()
        {
            if (_path != null || _disabled) return;
            var flag = Environment.GetEnvironmentVariable("CAD_MCP_TRACE");
            if (!string.IsNullOrEmpty(flag) &&
                (flag == "0" || flag.Equals("off", StringComparison.OrdinalIgnoreCase)
                             || flag.Equals("false", StringComparison.OrdinalIgnoreCase)))
            {
                _disabled = true;
                return;
            }
            try
            {
                var dir = ResolveDir();
                Directory.CreateDirectory(dir);
                _session = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-"
                         + Guid.NewGuid().ToString("N").Substring(0, 6);
                _path = Path.Combine(dir, $"Civil3D-{_session}.jsonl");

                var start = new JObject
                {
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["session"] = _session,
                    ["app"] = "Civil3D",
                    ["seq"] = 0,
                    ["kind"] = "session_start",
                    ["layer"] = "bridge",
                    ["pid"] = Process.GetCurrentProcess().Id,
                };
                File.AppendAllText(_path, start.ToString(Formatting.None) + "\n", _enc);
            }
            catch { _disabled = true; }
        }

        // Cap long strings and oversized arrays so the trace stays lean. The
        // authoritative golden output is the saved drawing / export, not this.
        private static JToken Summarize(JToken tok)
        {
            if (tok == null) return JValue.CreateNull();
            switch (tok.Type)
            {
                case JTokenType.String:
                    var s = tok.Value<string>() ?? "";
                    if (s.Length > MaxResultStr)
                        return s.Substring(0, MaxResultStr) + $"...<+{s.Length - MaxResultStr} chars>";
                    return s;
                case JTokenType.Object:
                    var o = new JObject();
                    foreach (var p in (JObject)tok)
                        o[p.Key] = Summarize(p.Value);
                    return o;
                case JTokenType.Array:
                    var arr = (JArray)tok;
                    var outArr = new JArray();
                    int n = 0;
                    foreach (var item in arr)
                    {
                        if (n++ >= 200) { outArr.Add($"...<+{arr.Count - 200} items>"); break; }
                        outArr.Add(Summarize(item));
                    }
                    return outArr;
                default:
                    return tok;
            }
        }

        public static void Log(string action, JObject parameters, object result,
                               string error, long elapsedMs, bool hasDialog)
        {
            try
            {
                lock (_lock)
                {
                    EnsureInit();
                    if (_disabled) return;
                    _seq++;

                    JToken resultTok = JValue.CreateNull();
                    if (error == null && result != null)
                    {
                        try { resultTok = Summarize(JToken.FromObject(result)); }
                        catch { resultTok = "<unserialisable result>"; }
                    }

                    var rec = new JObject
                    {
                        ["ts"] = DateTime.UtcNow.ToString("o"),
                        ["session"] = _session,
                        ["app"] = "Civil3D",
                        ["seq"] = _seq,
                        ["kind"] = "call",
                        ["layer"] = "bridge",
                        ["tool"] = action,
                        ["params"] = parameters != null ? (JToken)parameters : JValue.CreateNull(),
                        ["result"] = resultTok,
                        ["status"] = error != null ? "error"
                                     : (hasDialog ? "ok_with_dialog" : "ok"),
                        ["duration_ms"] = elapsedMs,
                        ["error"] = error != null ? (JToken)error : JValue.CreateNull(),
                    };
                    File.AppendAllText(_path, rec.ToString(Formatting.None) + "\n", _enc);
                }
            }
            catch { /* tracing must never break dispatch */ }
        }
    }

    // =========================================================================
    // HTTP LISTENER â€” receives JSON requests from the MCP Python server
    // =========================================================================

    public class HttpBridge
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly int _port;

        public bool IsRunning { get; private set; }

        public HttpBridge(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            IsRunning = true;

            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            IsRunning = false;
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
            }
        }

        // Safety-valve actions that MUST work while a modal dialog is open.
        // They bypass BOTH the dialog pre-check AND ExecuteOnMainThread,
        // because ExecuteOnMainThread queues work via Civil 3D's command
        // context, which is exactly what's blocked by a modal dialog. These
        // actions are safe to run directly on the HTTP handler thread:
        //
        //   ping            — pure return value, no Civil 3D access.
        //   list_dialogs    — pure Win32 (EnumWindows on this PID).
        //   dismiss_dialog  — pure Win32 (BM_CLICK to a button hwnd).
        //   cancel_command  — Doc.SendStringToExecute, which is documented
        //                     thread-safe and just queues a script for the
        //                     command pump (modal dialogs do consume ESC).
        //
        // None of these require a document lock or a managed-side Civil 3D
        // transaction. If you add a new bypass action, ensure it has the
        // same property — or the whole dialog-recovery path will deadlock.
        private static readonly HashSet<string> _dialogBypassActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ping",
                "list_dialogs",
                "dismiss_dialog",
                "cancel_command",
            };

        private void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;

            try
            {
                // Read request body
                string body;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = reader.ReadToEnd();

                var request = JObject.Parse(body);
                string action = request["action"]?.ToString() ?? "";
                var parameters = request["params"] as JObject ?? new JObject();

                var _traceSw = Stopwatch.StartNew();

                bool isBypass = _dialogBypassActions.Contains(action);

                // === DIALOG PRE-CHECK (skip for bypass actions) ===
                // If a modal dialog is open in Civil 3D, the command thread is
                // blocked. ExecuteOnMainThread would just spin until its
                // 5-minute timeout. Detect it now and return a structured
                // "blocked_by_dialog" so the agent can call list_dialogs /
                // dismiss_dialog and retry.
                if (!isBypass)
                {
                    List<DialogProbe.DialogInfo> preDialogs = null;
                    try { preDialogs = DialogProbe.List(Handlers.GetCadMainHwnd()); }
                    catch { /* probe failure must not break dispatch */ }

                    if (preDialogs != null && preDialogs.Count > 0)
                    {
                        var blocked = new JObject
                        {
                            ["success"] = false,
                            ["error"] = "blocked_by_dialog",
                            ["dialogs"] = JToken.FromObject(preDialogs),
                            ["hint"] = "A modal dialog is open in Civil 3D. "
                                     + "Inspect via list_dialogs and clear via "
                                     + "dismiss_dialog (or cancel_command for ESC), "
                                     + "then retry.",
                        };
                        byte[] bb = Encoding.UTF8.GetBytes(blocked.ToString());
                        resp.ContentType = "application/json";
                        resp.ContentLength64 = bb.Length;
                        resp.OutputStream.Write(bb, 0, bb.Length);
                        _traceSw.Stop();
                        TraceLog.Log(action, parameters, null, "blocked_by_dialog",
                                     _traceSw.ElapsedMilliseconds, true);
                        return;
                    }
                }

                // Route to handler.
                object result = null;
                string error = null;

                if (isBypass)
                {
                    // Run directly on the HTTP handler thread — these actions
                    // exist precisely to work when ExecuteOnMainThread is
                    // wedged behind a modal dialog.
                    try
                    {
                        result = RouteAction(action, parameters);
                    }
                    catch (System.Exception ex)
                    {
                        error = ex.Message;
                    }
                }
                else
                {
                    // ALL non-bypass Civil 3D API calls must happen on the
                    // document's thread, with the doc lock held.
                    ExecuteOnMainThread(() =>
                    {
                        try
                        {
                            result = RouteAction(action, parameters);
                        }
                        catch (System.Exception ex)
                        {
                            error = ex.Message;
                        }
                    });
                }

                // === DIALOG POST-CHECK ===
                // The action may have *triggered* a dialog (e.g. send_command
                // run an op that prompts). Surface it on the same response so
                // the agent learns about it now, not on the next call's hang.
                List<DialogProbe.DialogInfo> postDialogs = null;
                if (error == null && !isBypass)
                {
                    try { postDialogs = DialogProbe.List(Handlers.GetCadMainHwnd()); }
                    catch { }
                }

                // Send response
                var responseObj = new JObject();
                if (error != null)
                {
                    responseObj["success"] = false;
                    responseObj["error"] = error;
                }
                else
                {
                    responseObj["success"] = true;
                    responseObj["result"] = result != null
                        ? JToken.FromObject(result)
                        : JValue.CreateNull();
                    if (postDialogs != null && postDialogs.Count > 0)
                    {
                        responseObj["dialogs"] = JToken.FromObject(postDialogs);
                    }
                }

                _traceSw.Stop();
                TraceLog.Log(action, parameters, result, error,
                             _traceSw.ElapsedMilliseconds,
                             postDialogs != null && postDialogs.Count > 0);

                byte[] buffer = Encoding.UTF8.GetBytes(responseObj.ToString());
                resp.ContentType = "application/json";
                resp.ContentLength64 = buffer.Length;
                resp.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (System.Exception ex)
            {
                byte[] buffer = Encoding.UTF8.GetBytes(
                    $"{{\"success\":false,\"error\":\"{ex.Message}\"}}");
                resp.StatusCode = 500;
                resp.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                resp.Close();
            }
        }

        /// <summary>
        /// Execute a delegate on AutoCAD's main/document thread. This is the
        /// STA thread that owns the AcadApplication COM object. Managed
        /// wrappers work from any thread via marshaling, but raw COM calls
        /// (IAcadApplication.GetInterfaceObject, dispatched property/method
        /// calls on System.__ComObject) require us to be ON that thread.
        ///
        /// ExecuteInCommandContextAsync queues the delegate to run on the
        /// main thread during AutoCAD's next command-context window. We
        /// block the HTTP handler thread until it completes; that's fine
        /// because the handler is itself running on a Task.Run background.
        /// </summary>
        private void ExecuteOnMainThread(Action action)
        {
            var docs = Application.DocumentManager;
            var doc = docs.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active document");

            System.Exception caught = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            docs.ExecuteInCommandContextAsync((obj) =>
            {
                try
                {
                    using (var docLock = doc.LockDocument())
                    {
                        action();
                    }
                }
                catch (System.Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    done.Set();
                }
                return Task.CompletedTask;
            }, null);

            // Wait for the main thread to pick up and run our delegate.
            // Use a long timeout so long-running Civil 3D operations
            // (FBK import, rebuild, etc.) don't time out spuriously.
            if (!done.Wait(TimeSpan.FromMinutes(5)))
                throw new TimeoutException(
                    "ExecuteOnMainThread: main thread did not dispatch the "
                    + "delegate within 5 minutes. Is AutoCAD stuck in a modal "
                    + "prompt?");

            if (caught != null)
            {
                // Re-throw with original stack preserved
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(caught).Throw();
            }
        }

        // =====================================================================
        // ACTION ROUTER â€” maps action strings to handler methods
        // =====================================================================

        private object RouteAction(string action, JObject p)
        {
            switch (action)
            {
                // --- Connection / Info ---
                case "ping":
                    return new { status = "ok", app = "Civil3D", version = "2026" };

                case "get_drawing_info":
                    return Handlers.GetDrawingInfo();

                // --- Document Management ---
                case "open_drawing":
                    return Handlers.OpenDrawing(p["path"]?.ToString());
                case "switch_document":
                    return Handlers.SwitchDocument(p["filename"]?.ToString());

                // --- Drawing Settings ---
                case "get_drawing_settings":
                    return Handlers.GetDrawingSettings();
                case "set_drawing_settings":
                    return Handlers.SetDrawingSettings(p);

                // --- Layer Management ---
                case "list_layers":
                    return Handlers.ListLayers();
                case "create_layer":
                    return Handlers.CreateLayer(
                        p["name"]?.ToString(),
                        p["color"]?.Value<int>() ?? 7);
                case "set_current_layer":
                    return Handlers.SetCurrentLayer(p["name"]?.ToString());

                // --- Geometry Creation ---
                case "draw_line":
                    return Handlers.DrawLine(p);
                case "draw_arc":
                    return Handlers.DrawArc(p);
                case "draw_polyline":
                    return Handlers.DrawPolyline(p);

                // --- COGO Points ---
                case "list_cogo_points":
                    return Handlers.ListCogoPoints();
                case "get_cogo_point":
                    return Handlers.GetCogoPoint(p["number"]?.Value<uint>() ?? 0);
                case "edit_cogo_point":
                    return Handlers.EditCogoPoint(p);
                case "create_cogo_point":
                    return Handlers.CreateCogoPoint(p);
                case "import_points_file":
                    return Handlers.ImportPointsFile(p);

                // --- Surfaces ---
                case "create_surface_from_points":
                    return Handlers.CreateSurfaceFromPoints(p);
                case "generate_contours":
                    return Handlers.GenerateContours(p);
                case "surface_volume":
                    return Handlers.SurfaceVolume(p);

                // --- Point Styles ---
                case "list_point_styles":
                    return Handlers.ListPointStyles();
                case "create_point_style":
                    return Handlers.CreatePointStyle(p);

                // --- Point Label Styles ---
                case "list_point_label_styles":
                    return Handlers.ListPointLabelStyles();
                case "create_point_label_style":
                    return Handlers.CreatePointLabelStyle(p);

                // --- Line Label Styles ---
                case "list_line_label_styles":
                    return Handlers.ListLineLabelStyles();
                case "create_line_label_style":
                    return Handlers.CreateLineLabelStyle(p);

                // --- Labels ---
                case "add_line_label":
                    return Handlers.AddLineLabel(p);

                // --- Entity Query ---
                case "get_entity_handles":
                    return Handlers.GetEntityHandles(p);

                // --- Parcels ---
                case "list_parcels":
                    return Handlers.ListParcels(p);
                case "get_parcel_layout_settings":
                    return Handlers.GetParcelLayoutSettings(p);

                // --- Text Style ---
                case "set_text_style_font":
                    return Handlers.SetTextStyleFont(p);

                // --- Viewport ---
                case "zoom_to_point":
                    return Handlers.ZoomToPoint(p);

                // --- Geometry Operations ---
                case "offset_entity":
                    return Handlers.OffsetEntity(p);

                // --- Raster Images ---
                case "attach_raster_image":
                    return Handlers.AttachRasterImage(p);

                // --- Command Execution (fallback) ---
                case "send_command":
                    return Handlers.SendCommand(p["command"]?.ToString());

                // --- Survey (COM Interop) ---
                case "get_survey_working_folder":
                    return Handlers.GetSurveyWorkingFolder();
                case "set_survey_working_folder":
                    return Handlers.SetSurveyWorkingFolder(p["path"]?.ToString());
                case "create_survey_project":
                    return Handlers.CreateSurveyProject(
                        p["name"]?.ToString(),
                        p["description"]?.ToString());
                case "list_survey_projects":
                    return Handlers.ListSurveyProjects();
                case "create_survey_network":
                    return Handlers.CreateSurveyNetwork(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["description"]?.ToString());
                case "list_survey_networks":
                    return Handlers.ListSurveyNetworks(p["projectName"]?.ToString());
                case "import_fbk":
                    return Handlers.ImportFbk(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["fbkPath"]?.ToString(),
                        p["insertIntoDrawing"]?.Value<bool?>(),
                        p["showErrors"]?.Value<bool?>(),
                        p["showWarnings"]?.Value<bool?>(),
                        p["timeoutSeconds"]?.Value<int?>());
                case "cancel_command":
                    return Handlers.CancelCommand(
                        p["escCount"]?.Value<int?>());
                case "parse_fbk":
                    return Handlers.ParseFbk(
                        p["fbkPath"]?.ToString());
                case "import_fbk_parsed":
                    return Handlers.ImportFbkParsed(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["fbkPath"]?.ToString(),
                        p["dryRun"]?.Value<bool?>(),
                        p["stopOnError"]?.Value<bool?>());
                case "list_survey_control_points":
                    return Handlers.ListSurveyControlPoints(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString());
                case "list_survey_non_control_points":
                    return Handlers.ListSurveyNonControlPoints(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString());
                case "list_survey_setups":
                    return Handlers.ListSurveySetups(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString());
                case "list_survey_observations":
                    return Handlers.ListSurveyObservations(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["setupIndex"]?.Value<int?>(),
                        p["stationPoint"]?.Value<int?>());
                case "survey_network_summary":
                    return Handlers.SurveyNetworkSummary(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString());
                case "add_survey_points_to_drawing":
                    return Handlers.AddSurveyPointsToDrawing(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["includeControl"]?.Value<bool?>(),
                        p["includeNonControl"]?.Value<bool?>());
                case "debug_setup_param_set":
                    return Handlers.DebugSetupParamSet(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["setupIndex"]?.Value<int?>() ?? 0,
                        p["testHi"]?.Value<double?>() ?? 1.651,
                        p["testBsOrient"]?.Value<double?>(),
                        p["testBsFace1"]?.Value<double?>());
                case "fix_survey_setups":
                    return Handlers.FixSurveySetups(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["fbkPath"]?.ToString(),
                        p["updateNetwork"]?.Value<bool?>());
                case "update_survey_network":
                    return Handlers.UpdateSurveyNetwork(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString());

                // --- v19 NEW endpoints (added 2026-05-12) ---
                case "get_survey_user_settings":
                    return Handlers.GetSurveyUserSettingsInfo();
                case "set_survey_user_settings":
                    return Handlers.SetSurveyUserSettingsInfo(
                        p["figurePrefixDatabasePath"]?.ToString(),
                        p["equipmentDatabasePath"]?.ToString(),
                        p["currentFigurePrefixDatabase"]?.ToString(),
                        p["currentEquipmentDatabase"]?.ToString(),
                        p["defaultFigureLayer"]?.ToString(),
                        p["defaultFigureStyle"]?.ToString(),
                        p["insertFigureObjects"]?.Value<bool?>(),
                        p["insertCivil3DPoints"]?.Value<bool?>(),
                        p["insertNetworkObject"]?.Value<bool?>());

                case "list_figure_prefix_dbs":
                    return Handlers.ListFigurePrefixDbsTyped();
                case "create_figure_prefix_db":
                    return Handlers.CreateFigurePrefixDbTyped(p["name"]?.ToString());
                case "list_figure_prefix_db_entries":
                    return Handlers.ListFigurePrefixDbEntriesTyped(p["dbName"]?.ToString());
                case "add_figure_prefix_db_entry":
                    return Handlers.AddFigurePrefixDbEntryTyped(
                        p["dbName"]?.ToString(),
                        p["prefix"]?.ToString(),
                        p["isBreakline"]?.Value<bool?>(),
                        p["isLotLine"]?.Value<bool?>(),
                        p["layer"]?.ToString(),
                        p["style"]?.ToString(),
                        p["site"]?.ToString());
                case "com_invoke":
                    return Handlers.ComInvoke(
                        p["chain"] as JArray,
                        p["methodName"]?.ToString(),
                        p["args"] as JArray);

                case "list_figures":
                    return Handlers.ListFigures(p["projectName"]?.ToString());
                case "get_figure_properties":
                    return Handlers.GetFigureProperties(
                        p["projectName"]?.ToString(),
                        p["figureName"]?.ToString());
                case "get_figure_elevations":
                    return Handlers.GetFigureElevations(
                        p["projectName"]?.ToString(),
                        p["figureName"]?.ToString());
                case "set_figure_elevations":
                    return Handlers.SetFigureElevations(
                        p["projectName"]?.ToString(),
                        p["figureName"]?.ToString(),
                        p["uniform"]?.Value<double?>(),
                        p["perVertex"] as JArray);

                case "import_pnezd_to_sdb":
                    return Handlers.ImportPnezdToSdb(
                        p["projectName"]?.ToString(),
                        p["networkName"]?.ToString(),
                        p["pnezdPath"]?.ToString());

                // --- v20 NEW endpoints (added 2026-05-12) ---
                case "load_assembly":
                    return Handlers.LoadAssembly(p["path"]?.ToString());
                case "com_discover":
                    return Handlers.ComDiscover(
                        p["chain"] as JArray,
                        p["probeNames"] as JArray);
                case "getvar":
                    return Handlers.GetSysVar(p["name"]?.ToString());
                case "setvar":
                    return Handlers.SetSysVar(p["name"]?.ToString(), p["value"]);

                // --- v22 NEW endpoints (added 2026-05-12) ---
                case "get_survey_user_settings_v2":
                    return Handlers.GetSurveyUserSettingsV2();
                case "set_current_figure_prefix_db":
                    return Handlers.SetCurrentFigurePrefixDb(p["name"]?.ToString());
                case "set_survey_user_settings_v2":
                    return Handlers.SetSurveyUserSettingsV2(p);

                // --- v23 NEW endpoints (added 2026-05-14) ---
                case "set_figure_elevations_persistent":
                    return Handlers.SetFigureElevationsPersistent(
                        p["projectName"]?.ToString(),
                        p["figureName"]?.ToString(),
                        p["uniform"]?.Value<double?>(),
                        p["perVertex"] as JArray);

                case "inspect_figure_entity":
                    return Handlers.InspectFigureEntity(p["projectName"]?.ToString(), p["figureName"]?.ToString());

                // --- Dialog Inspection / Dismissal ---
                case "list_dialogs":
                    return Handlers.ListDialogs();
                case "dismiss_dialog":
                    return Handlers.DismissDialog(p);

                default:
                    throw new ArgumentException($"Unknown action: {action}");
            }
        }
    }

    // =========================================================================
    // DIALOG PROBE — pure Win32 enumeration of modal dialogs in this process
    // =========================================================================
    //
    // Civil 3D's HandleRequest pipeline can hang silently when a modal dialog
    // (Project Manager, Save-changes prompt, FBK error, etc.) blocks the main
    // command thread. ExecuteOnMainThread waits up to 5 minutes before
    // throwing TimeoutException — useless for an MCP agent that needs to
    // recover and try something else.
    //
    // DialogProbe enumerates top-level windows in the current process and
    // returns those that look like modals. The primary signal is the
    // *enable state* of the main window: when a true modal dialog is shown,
    // Windows disables its parent. Palettes (Toolspace, Properties, etc.)
    // do NOT disable the parent, so they're naturally excluded. We also
    // filter strictly by ProcessId, so unrelated popups from other apps
    // (e.g. GlobalProtect reauth prompts) never appear here.
    //
    // All Win32 calls used here are thread-safe; this class can be invoked
    // from the HTTP handler thread without going through ExecuteOnMainThread,
    // which is critical because that's exactly what's blocked when a
    // dialog is open.
    // =========================================================================

    public static class DialogProbe
    {
        // --- P/Invoke ---
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        private const uint BM_CLICK = 0x00F5;
        private const uint WM_CLOSE = 0x0010;
        private const uint GW_OWNER = 4;
        private const int IDOK = 1;
        private const int IDCANCEL = 2;

        // --- Public types (POCOs are used directly in JSON responses) ---

        public class DialogInfo
        {
            public long hwnd;
            public string title;
            public string className;
            public string message;
            public bool ownedByMain;
            public List<ButtonInfo> buttons;
        }

        public class ButtonInfo
        {
            public long hwnd;
            public int id;
            public string text;
        }

        /// <summary>
        /// Enumerate modal/popup dialogs in the current Civil 3D process.
        /// Returns empty list when the main window is enabled — palettes
        /// don't disable the parent, only true modals do.
        /// </summary>
        /// <param name="mainHwnd">Civil 3D's main window handle. Pass 0 to
        /// auto-detect via Process.GetCurrentProcess().MainWindowHandle.</param>
        public static List<DialogInfo> List(IntPtr mainHwnd)
        {
            var result = new List<DialogInfo>();
            uint myPid;
            try { myPid = (uint)Process.GetCurrentProcess().Id; }
            catch { return result; }

            if (mainHwnd == IntPtr.Zero)
            {
                try { mainHwnd = Process.GetCurrentProcess().MainWindowHandle; }
                catch { mainHwnd = IntPtr.Zero; }
            }

            // Primary modal signal: main window is disabled.
            // If main is still enabled, no true modal is up — skip enumeration.
            // (We still allow enumeration if mainHwnd is unknown, as a fallback.)
            bool mainDisabled = mainHwnd != IntPtr.Zero && !IsWindowEnabled(mainHwnd);
            if (mainHwnd != IntPtr.Zero && !mainDisabled)
                return result;

            var candidates = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != myPid) return true;             // strict PID filter
                if (!IsWindowVisible(hWnd)) return true;
                if (!IsWindowEnabled(hWnd)) return true;   // skip disabled (main MDI when modal up)
                if (hWnd == mainHwnd) return true;
                candidates.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            foreach (var hWnd in candidates)
            {
                var info = Inspect(hWnd, mainHwnd);
                if (info != null) result.Add(info);
            }
            return result;
        }

        private static DialogInfo Inspect(IntPtr hWnd, IntPtr mainHwnd)
        {
            var info = new DialogInfo
            {
                hwnd = hWnd.ToInt64(),
                title = GetText(hWnd),
                className = GetClass(hWnd),
                message = "",
                buttons = new List<ButtonInfo>(),
            };

            // ownedByMain helps the agent distinguish dialogs that belong to
            // Civil 3D vs detached top-level windows that happen to share PID.
            try
            {
                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                info.ownedByMain = (owner == mainHwnd) && mainHwnd != IntPtr.Zero;
            }
            catch { info.ownedByMain = false; }

            var statics = new List<string>();
            EnumChildWindows(hWnd, (child, _) =>
            {
                string cls = GetClass(child);
                string text = GetText(child);

                // Win32 native class is "Button"; WinForms exposes ".Button"
                // suffix; WPF buttons go through HwndWrapper but are usually
                // *not* leaf hwnds, so we won't collect them here. Most
                // AutoCAD dialogs we care about (the standard ones) are
                // MFC/Win32 and use the native "Button" class.
                bool isButton = cls.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isStatic = cls.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isButton)
                {
                    // Emit caption-less buttons too — many dialogs have
                    // glyph-only buttons (icon close, etc.) that the agent
                    // can target by control id.
                    info.buttons.Add(new ButtonInfo
                    {
                        hwnd = child.ToInt64(),
                        id = GetDlgCtrlID(child),
                        text = text ?? "",
                    });
                }
                else if (isStatic && !string.IsNullOrEmpty(text))
                {
                    statics.Add(text);
                }
                return true;
            }, IntPtr.Zero);

            info.message = string.Join("\n", statics);
            return info;
        }

        private static string GetText(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public class DismissResult
        {
            public bool dismissed;
            public string error;
            public string clickedButton;
            public List<DialogInfo> remaining;
        }

        /// <summary>
        /// Dismiss a dialog. <paramref name="buttonText"/> supports several
        /// forms:
        ///   "[CLOSE]"  → send WM_CLOSE to the dialog (works on virtually any
        ///                window — the catch-all)
        ///   "[OK]"     → click the standard IDOK control (id=1)
        ///   "[CANCEL]" → click the standard IDCANCEL control (id=2)
        ///   "#NNN"     → click the child control with that numeric id
        ///   anything else → match by visible button caption (case-insensitive,
        ///                   '&amp;' accelerators stripped)
        /// If <paramref name="hwnd"/> is 0, the topmost dialog is used.
        /// Returns the remaining dialog list after a brief settle.
        /// </summary>
        public static DismissResult Dismiss(IntPtr mainHwnd, long hwnd, string buttonText)
        {
            var result = new DismissResult { remaining = new List<DialogInfo>() };
            var dialogs = List(mainHwnd);

            DialogInfo target;
            if (hwnd == 0)
            {
                target = dialogs.FirstOrDefault();
                if (target == null)
                {
                    result.error = "no_open_dialog";
                    result.remaining = dialogs;
                    return result;
                }
            }
            else
            {
                target = dialogs.FirstOrDefault(d => d.hwnd == hwnd);
                if (target == null)
                {
                    result.error = $"hwnd_not_open (open count={dialogs.Count})";
                    result.remaining = dialogs;
                    return result;
                }
            }

            string trimmed = (buttonText ?? "").Trim();
            string upper = trimmed.ToUpperInvariant();
            IntPtr targetHwnd = new IntPtr(target.hwnd);

            // --- Special form: [CLOSE] sends WM_CLOSE to the dialog ---
            if (upper == "[CLOSE]")
            {
                PostMessage(targetHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(200);
                result.dismissed = true;
                result.clickedButton = "[CLOSE]";
                result.remaining = List(mainHwnd);
                return result;
            }

            // --- Special forms: [OK] / [CANCEL] click standard IDs ---
            int? targetId = null;
            if (upper == "[OK]") targetId = IDOK;
            else if (upper == "[CANCEL]") targetId = IDCANCEL;
            else if (trimmed.StartsWith("#") && int.TryParse(trimmed.Substring(1), out int parsedId))
                targetId = parsedId;

            if (targetId.HasValue)
            {
                IntPtr ctrlHwnd = GetDlgItem(targetHwnd, targetId.Value);
                if (ctrlHwnd == IntPtr.Zero)
                {
                    // Fallback: try to find a button with this id in the
                    // already-enumerated list (covers non-direct-child cases)
                    var byId = target.buttons.FirstOrDefault(b => b.id == targetId.Value);
                    if (byId != null) ctrlHwnd = new IntPtr(byId.hwnd);
                }
                if (ctrlHwnd == IntPtr.Zero)
                {
                    result.error = $"control_id_not_found:{targetId.Value}";
                    result.remaining = dialogs;
                    return result;
                }
                SendMessage(ctrlHwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(200);
                result.dismissed = true;
                result.clickedButton = $"#{targetId.Value}";
                result.remaining = List(mainHwnd);
                return result;
            }

            // --- Default: match by button caption ---
            ButtonInfo btn = null;
            if (!string.IsNullOrEmpty(trimmed))
            {
                btn = target.buttons.FirstOrDefault(b =>
                    string.Equals(StripAccelerators(b.text), trimmed,
                        StringComparison.OrdinalIgnoreCase));
            }
            if (btn == null)
            {
                result.error = $"button_not_found:'{trimmed}'. Available: "
                    + string.Join(", ", target.buttons.Select(b =>
                        "'" + StripAccelerators(b.text) + "'#" + b.id))
                    + ". Symbolics: [CLOSE], [OK], [CANCEL], #<id>.";
                result.remaining = dialogs;
                return result;
            }

            SendMessage(new IntPtr(btn.hwnd), BM_CLICK, IntPtr.Zero, IntPtr.Zero);

            // Brief settle — give the dialog a moment to close before re-listing
            Thread.Sleep(200);

            result.dismissed = true;
            result.clickedButton = StripAccelerators(btn.text);
            result.remaining = List(mainHwnd);
            return result;
        }

        private static string StripAccelerators(string s) =>
            (s ?? "").Replace("&", "");
    }

    // =========================================================================
    // HANDLERS — the actual Civil 3D API calls
    // =========================================================================

    public static class Handlers
    {
        // Convenience accessors
        private static Document Doc =>
            Application.DocumentManager.MdiActiveDocument;
        private static Database Db => Doc.Database;
        private static Editor Ed => Doc.Editor;
        private static CivilDocument CivDoc =>
            CivilDocument.GetCivilDocument(Db);

        // ----- Drawing Info -----

        public static object GetDrawingInfo()
        {
            return new
            {
                filename = Doc.Name,
                path = Db.Filename,
                units = Db.Insunits.ToString()
            };
        }

        // ----- Drawing Settings -----

        public static object GetDrawingSettings()
        {
            var settings = CivDoc.Settings.DrawingSettings;
            return new
            {
                units = settings.UnitZoneSettings.DrawingUnits.ToString(),
                angularUnits = settings.UnitZoneSettings.AngularUnits.ToString(),
                scale = settings.UnitZoneSettings.DrawingScale,
                coordinateSystem = settings.UnitZoneSettings.CoordinateSystemCode
            };
        }

        public static object SetDrawingSettings(JObject p)
        {
            var settings = CivDoc.Settings.DrawingSettings;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                // Scale
                if (p["scale"] != null)
                    settings.UnitZoneSettings.DrawingScale = p["scale"].Value<double>();

                // Coordinate precision (Ambient Settings)
                if (p["coordinatePrecision"] != null)
                {
                    var ambient = settings.AmbientSettings;
                    ambient.Coordinate.Precision.Value =
                        p["coordinatePrecision"].Value<int>();
                }

                // Direction type
                if (p["directionType"] != null)
                {
                    var ambient = settings.AmbientSettings;
                    string dirType = p["directionType"].ToString();
                    if (dirType.ToLower().Contains("azimuth"))
                        ambient.Direction.MeasurementType.Value =
                            VectorMeasurementType.NorthAzimuth;
                    else
                        ambient.Direction.MeasurementType.Value =
                            VectorMeasurementType.Bearings;
                }

                // Abbreviations (2026 API uses method-based access)
                if (p["tangentCurveIntersect"] != null)
                    settings.AbbreviationsSettings
                        .AlignmentGeoPointText
                        .SetAlignmentAbbreviation(
                            AbbreviationAlignmentType.TangentCurveIntersect,
                            p["tangentCurveIntersect"].ToString());

                if (p["curveTangentIntersect"] != null)
                    settings.AbbreviationsSettings
                        .AlignmentGeoPointText
                        .SetAlignmentAbbreviation(
                            AbbreviationAlignmentType.CurveTangentIntersect,
                            p["curveTangentIntersect"].ToString());

                tr.Commit();
            }

            return new { updated = true };
        }

        // ----- Layer Management -----

        public static object ListLayers()
        {
            var layers = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    layers.Add(new
                    {
                        name = layer.Name,
                        color = layer.Color.ColorIndex,
                        isFrozen = layer.IsFrozen,
                        isOff = layer.IsOff
                    });
                }
                tr.Commit();
            }
            return layers;
        }

        public static object CreateLayer(string name, int color)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForWrite);
                if (!lt.Has(name))
                {
                    var ltr = new LayerTableRecord();
                    ltr.Name = name;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)color);
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
            return new { created = true, name };
        }

        public static object SetCurrentLayer(string name)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(name))
                {
                    Db.Clayer = lt[name];
                }
                else
                {
                    throw new ArgumentException($"Layer '{name}' not found");
                }
                tr.Commit();
            }
            return new { currentLayer = name };
        }

        // ----- Geometry: Lines -----

        public static object DrawLine(JObject p)
        {
            var startPt = ParsePoint3d(p["start"]);
            var endPt = ParsePoint3d(p["end"]);
            ObjectId lineId;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    Db.CurrentSpaceId, OpenMode.ForWrite);
                var line = new Line(startPt, endPt);
                lineId = btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }

            return new { entityId = lineId.Handle.Value.ToString(), type = "LINE" };
        }

        // ----- Geometry: Arcs -----

        public static object DrawArc(JObject p)
        {
            ObjectId arcId;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    Db.CurrentSpaceId, OpenMode.ForWrite);

                Arc arc;
                if (p["center"] != null && p["radius"] != null)
                {
                    // Center, radius, start angle, end angle
                    var center = ParsePoint3d(p["center"]);
                    double radius = p["radius"].Value<double>();
                    double startAngle = p["startAngle"]?.Value<double>() ?? 0;
                    double endAngle = p["endAngle"]?.Value<double>() ?? Math.PI;
                    arc = new Arc(center, radius, startAngle, endAngle);
                }
                else if (p["startPoint"] != null && p["midPoint"] != null && p["endPoint"] != null)
                {
                    // 3-point arc
                    var sp = ParsePoint3d(p["startPoint"]);
                    var mp = ParsePoint3d(p["midPoint"]);
                    var ep = ParsePoint3d(p["endPoint"]);
                    // Calculate center and radius from 3 points
                    arc = CreateArcFrom3Points(sp, mp, ep);
                }
                else
                {
                    throw new ArgumentException(
                        "Arc requires either {center,radius,startAngle,endAngle} " +
                        "or {startPoint,midPoint,endPoint}");
                }

                arcId = btr.AppendEntity(arc);
                tr.AddNewlyCreatedDBObject(arc, true);
                tr.Commit();
            }

            return new { entityId = arcId.Handle.Value.ToString(), type = "ARC" };
        }

        // ----- Geometry: Polyline -----

        public static object DrawPolyline(JObject p)
        {
            var points = p["points"] as JArray;
            if (points == null || points.Count < 2)
                throw new ArgumentException("Polyline needs at least 2 points");

            bool closed = p["closed"]?.Value<bool>() ?? false;
            ObjectId plId;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    Db.CurrentSpaceId, OpenMode.ForWrite);

                var pl = new Polyline();
                for (int i = 0; i < points.Count; i++)
                {
                    var pt = points[i] as JArray;
                    pl.AddVertexAt(i,
                        new Point2d(pt[0].Value<double>(), pt[1].Value<double>()),
                        0, 0, 0); // bulge=0, startWidth=0, endWidth=0
                }
                if (closed) pl.Closed = true;

                plId = btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
            }

            return new { entityId = plId.Handle.Value.ToString(), type = "POLYLINE" };
        }

        // ----- COGO Points -----

        public static object ListCogoPoints()
        {
            var points = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var cogoPoints = CivDoc.CogoPoints;
                foreach (ObjectId ptId in cogoPoints)
                {
                    var pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForRead);
                    points.Add(new
                    {
                        number = pt.PointNumber,
                        northing = pt.Northing,
                        easting = pt.Easting,
                        elevation = pt.Elevation,
                        rawDescription = pt.RawDescription,
                        fullDescription = pt.FullDescription,
                        pointName = pt.PointName
                    });
                }
                tr.Commit();
            }
            return points;
        }

        public static object GetCogoPoint(uint pointNumber)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId ptId in CivDoc.CogoPoints)
                {
                    var pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForRead);
                    if (pt.PointNumber == pointNumber)
                    {
                        tr.Commit();
                        return new
                        {
                            number = pt.PointNumber,
                            northing = pt.Northing,
                            easting = pt.Easting,
                            elevation = pt.Elevation,
                            rawDescription = pt.RawDescription,
                            fullDescription = pt.FullDescription
                        };
                    }
                }
                tr.Commit();
            }
            throw new ArgumentException($"Point {pointNumber} not found");
        }

        // ----- Parcels -----

        // List all parcels grouped by Site. The managed Parcel API exposes
        // Number/Name/Centroid but NO Area/Perimeter/segment geometry, so area
        // is probed reflectively (returned only if the runtime type has it).
        public static object ListParcels(JObject p)
        {
            string siteFilter = p["site_name"]?.ToString();
            var sites = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId siteId in CivDoc.GetSiteIds())
                {
                    var site = (Site)tr.GetObject(siteId, OpenMode.ForRead);
                    if (!string.IsNullOrEmpty(siteFilter) && site.Name != siteFilter)
                        continue;

                    var parcels = new List<object>();
                    foreach (ObjectId pid in site.GetParcelIds())
                    {
                        var parcel = (Parcel)tr.GetObject(pid, OpenMode.ForRead);

                        // Reflective hedge: the decompiled dump shows no Area, but
                        // probe the live type in case it surfaces one.
                        double? area = null;
                        try
                        {
                            var ap = parcel.GetType().GetProperty("Area");
                            if (ap != null && ap.PropertyType == typeof(double))
                                area = (double)ap.GetValue(parcel);
                        }
                        catch { }

                        var c = parcel.Centroid;
                        parcels.Add(new
                        {
                            handle = parcel.Handle.ToString(),
                            number = parcel.Number,
                            name = parcel.Name,
                            area_sq_m = area,
                            centroid = new { easting = c.X, northing = c.Y }
                        });
                    }

                    sites.Add(new
                    {
                        site_name = site.Name,
                        site_handle = site.Handle.ToString(),
                        parcel_count = parcels.Count,
                        parcels
                    });
                }
                tr.Commit();
            }
            return new { sites };
        }

        // Read the Parcel Layout sizing rules from the drawing Settings tree.
        // These are the design parameters behind a subdivision's lot dimensions.
        public static object GetParcelLayoutSettings(JObject p)
        {
            var s = CivDoc.Settings.GetSettings<SettingsCmdCreateParcelByLayout>();
            var ps = s.ParcelSizing;
            var al = s.AutomaticLayout;
            return new
            {
                minimum_area_sq_m = ps.MinimumArea.Value,
                minimum_frontage_m = ps.MinimumFrontage.Value,
                minimum_width_m = ps.MinimumWidth.Value,
                minimum_depth_m = ps.MinimumDepth.Value,
                maximum_depth_m = ps.MaximumDepth.Value,
                use_maximum_depth = ps.UseMaximumDepth.Value,
                frontage_offset_m = ps.FrontageOffset.Value,
                use_minimum_frontage_at_offset = ps.UseMinimumFrontageAtOffset.Value,
                solution_preference = ps.SolutionPreference.Value.ToString(),
                automatic_mode = al.AutomaticMode.Value,
                remainder_distribution = al.RemainderDistributionType.Value.ToString()
            };
        }

        public static object EditCogoPoint(JObject p)
        {
            uint pointNumber = p["number"]?.Value<uint>() ?? 0;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId ptId in CivDoc.CogoPoints)
                {
                    var pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForWrite);
                    if (pt.PointNumber == pointNumber)
                    {
                        if (p["rawDescription"] != null)
                            pt.RawDescription = p["rawDescription"].ToString();
                        if (p["elevation"] != null)
                            pt.Elevation = p["elevation"].Value<double>();
                        if (p["northing"] != null)
                            pt.Northing = p["northing"].Value<double>();
                        if (p["easting"] != null)
                            pt.Easting = p["easting"].Value<double>();

                        tr.Commit();
                        return new { updated = true, number = pointNumber };
                    }
                }
                tr.Commit();
            }
            throw new ArgumentException($"Point {pointNumber} not found");
        }

        public static object CreateCogoPoint(JObject p)
        {
            double northing = p["northing"]?.Value<double>() ?? 0;
            double easting = p["easting"]?.Value<double>() ?? 0;
            double elevation = p["elevation"]?.Value<double>() ?? 0;
            string description = p["rawDescription"]?.ToString() ?? "";

            ObjectId ptId;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                // Create point at easting (X), northing (Y), elevation (Z)
                ptId = CivDoc.CogoPoints.Add(
                    new Point3d(easting, northing, elevation), true);
                var pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForWrite);

                if (!string.IsNullOrEmpty(description))
                    pt.RawDescription = description;

                tr.Commit();
            }

            return new { created = true, entityId = ptId.Handle.Value.ToString() };
        }

        // ----- Bulk point import (Labs 4, 5, 6) -----
        // Import COGO points from a PNEZD/PENZD-style ASCII file. Auto-detects
        // comma vs. whitespace delimiter. 'format' gives the column order; D is the
        // trailing description field and may contain spaces/commas. Preserves the
        // point number (P) when that number is free, else auto-numbers.
        public static object ImportPointsFile(JObject p)
        {
            string path = p["path"]?.ToString()
                ?? throw new ArgumentException("path required");
            if (!System.IO.File.Exists(path))
                throw new ArgumentException($"File not found: {path}");
            string fmt = (p["format"]?.ToString() ?? "PNEZD").ToUpper();

            int iP = fmt.IndexOf('P'), iN = fmt.IndexOf('N'),
                iE = fmt.IndexOf('E'), iZ = fmt.IndexOf('Z'), iD = fmt.IndexOf('D');
            if (iN < 0 || iE < 0)
                throw new ArgumentException($"format '{fmt}' must contain N and E");
            int numericCols = (iD >= 0) ? iD : fmt.Length;  // numeric fields precede D

            int created = 0, skipped = 0;
            string[] lines = System.IO.File.ReadAllLines(path);

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0) continue;

                    bool comma = line.Contains(",");
                    string[] parts = comma
                        ? line.Split(',')
                        : line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < numericCols) { skipped++; continue; }

                    double northing, easting, elevation = 0;
                    if (!double.TryParse(parts[iN].Trim(), out northing)) { skipped++; continue; }
                    if (!double.TryParse(parts[iE].Trim(), out easting)) { skipped++; continue; }
                    if (iZ >= 0 && iZ < parts.Length)
                        double.TryParse(parts[iZ].Trim(), out elevation);

                    string desc = "";
                    if (iD >= 0 && iD < parts.Length)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int k = iD; k < parts.Length; k++)
                        {
                            if (sb.Length > 0) sb.Append(comma ? "," : " ");
                            sb.Append(parts[k].Trim());
                        }
                        desc = sb.ToString().Trim();
                    }

                    ObjectId ptId = CivDoc.CogoPoints.Add(
                        new Point3d(easting, northing, elevation), true);
                    var pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForWrite);
                    if (!string.IsNullOrEmpty(desc)) pt.RawDescription = desc;
                    if (iP >= 0 && iP < parts.Length)
                    {
                        uint pn;
                        if (uint.TryParse(parts[iP].Trim(), out pn))
                        {
                            try { pt.PointNumber = pn; } catch { /* number taken - keep auto */ }
                        }
                    }
                    created++;
                }
                tr.Commit();
            }

            return new { imported = true, created, skipped, format = fmt };
        }

        // ----- Surfaces -----

        // Resolve a surface ObjectId by display name. Surfaces live in the
        // AECC_SURFACES dictionary but it is keyed by an INTERNAL key, not the
        // display name, so a DBDictionary.Contains(name) lookup silently fails.
        // The documented path is CivilDocument.GetSurfaceIds() + match .Name.
        // Returns ObjectId.Null if not found; fills availableNames either way.
        private static ObjectId FindSurfaceIdByName(
            Transaction tr, string name, List<string> availableNames)
        {
            ObjectId found = ObjectId.Null;
            foreach (ObjectId sid in CivDoc.GetSurfaceIds())
            {
                var s = (Autodesk.Civil.DatabaseServices.Surface)
                    tr.GetObject(sid, OpenMode.ForRead);
                availableNames?.Add(s.Name);
                if (found == ObjectId.Null && string.Equals(
                        s.Name, name, StringComparison.OrdinalIgnoreCase))
                    found = sid;
            }
            return found;
        }

        // Create a TIN surface from COGO points already in the drawing. On
        // Civil 3D 2026 Projects.Create-style opaque failures are avoided by
        // guarding name collisions and throwing clear errors (the bridge turns
        // exceptions into MCP errors) rather than letting anything AV.
        public static object CreateSurfaceFromPoints(JObject p)
        {
            string name = p["name"]?.ToString()
                ?? throw new ArgumentException("name is required");
            string pointGroupName = p["point_group"]?.ToString();
            string styleName      = p["style"]?.ToString();
            string layer          = p["layer"]?.ToString();

            ObjectId surfaceId;
            string styleUsed = "(drawing default)";
            string pgUsed = null;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                // Guard duplicate surface name via the documented Civil API
                // (AECC_SURFACES is keyed by an internal key, not the display
                // name, so a dictionary Contains(name) check silently misses).
                if (FindSurfaceIdByName(tr, name, null) != ObjectId.Null)
                    throw new InvalidOperationException(
                        $"Surface name '{name}' already exists in this drawing.");

                // Resolve surface style by name (optional).
                ObjectId styleId = ObjectId.Null;
                if (!string.IsNullOrEmpty(styleName))
                {
                    var available = new List<string>();
                    foreach (ObjectId sid in CivDoc.Styles.SurfaceStyles)
                    {
                        var ss = (SurfaceStyle)tr.GetObject(sid, OpenMode.ForRead);
                        available.Add(ss.Name);
                        if (string.Equals(ss.Name, styleName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            styleId = sid;
                            styleUsed = ss.Name;
                        }
                    }
                    if (styleId == ObjectId.Null)
                        throw new InvalidOperationException(
                            $"Surface style '{styleName}' not found. Available styles: ["
                            + string.Join(", ", available) + "].");
                }

                surfaceId = (styleId != ObjectId.Null)
                    ? TinSurface.Create(name, styleId)
                    : TinSurface.Create(Db, name);

                var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForWrite);

                if (!string.IsNullOrEmpty(layer))
                {
                    var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(layer))
                        surface.Layer = layer;
                }

                // Resolve point group: named one, else fall back to "_All Points".
                ObjectId pgId = ObjectId.Null;
                if (!string.IsNullOrEmpty(pointGroupName))
                {
                    foreach (ObjectId id in CivDoc.PointGroups)
                    {
                        var pg = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
                        if (string.Equals(pg.Name, pointGroupName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            pgId = id; pgUsed = pg.Name; break;
                        }
                    }
                    if (pgId == ObjectId.Null)
                    {
                        var available = new List<string>();
                        foreach (ObjectId id in CivDoc.PointGroups)
                            available.Add(((PointGroup)tr.GetObject(id,
                                OpenMode.ForRead)).Name);
                        throw new InvalidOperationException(
                            $"Point group '{pointGroupName}' not found. Available groups: ["
                            + string.Join(", ", available) + "].");
                    }
                }
                else
                {
                    foreach (ObjectId id in CivDoc.PointGroups)
                    {
                        var pg = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
                        if (pg.Name == "_All Points")
                        {
                            pgId = id; pgUsed = "_All Points"; break;
                        }
                    }
                    if (pgId == ObjectId.Null)
                        pgUsed = "(none — no _All Points group found)";
                }

                if (pgId != ObjectId.Null)
                {
                    try { surface.PointGroupsDefinition.AddPointGroup(pgId); }
                    catch (System.Exception ex)
                    {
                        pgUsed = $"(AddPointGroup failed: {ex.Message})";
                    }
                }

                surface.Rebuild();
                tr.Commit();
            }

            // Read back stats (best-effort).
            double minElev = 0, maxElev = 0;
            int ptCount = 0;
            string layerOut = null;
            try
            {
                using (var tr2 = Db.TransactionManager.StartTransaction())
                {
                    var s2 = (TinSurface)tr2.GetObject(surfaceId, OpenMode.ForRead);
                    layerOut = s2.Layer;
                    var props = s2.GetGeneralProperties();
                    minElev = props.MinimumElevation;
                    maxElev = props.MaximumElevation;
                    ptCount = (int)props.NumberOfPoints;
                    tr2.Commit();
                }
            }
            catch { /* stats are best-effort */ }

            return new
            {
                created = true,
                name,
                handle = surfaceId.Handle.Value.ToString("X"),
                layer = layerOut,
                pointGroupUsed = pgUsed,
                pointCount = ptCount,
                minElevation = minElev,
                maxElevation = maxElev,
                styleUsed,
            };
        }

        // surface_volume: authoritative TIN volume of a comparison surface vs a
        // flat datum plane at a given elevation. Builds a flat base TinSurface
        // (4 corner vertices over the comparison's extent + pad), creates a TIN
        // volume surface, and reads its cut/fill/net. The 2026 vertex-add and
        // volume-stats API names are bound by runtime reflection so they
        // self-resolve (and self-report if absent) without a rebuild.
        public static object SurfaceVolume(JObject p)
        {
            string compName = p["comparison_surface"]?.ToString()
                ?? throw new ArgumentException("comparison_surface is required");
            // Two modes: an existing base_surface (surface-vs-surface) or an
            // elevation (surface-vs-flat-datum, the default).
            string baseSurfName = p["base_surface"]?.ToString();
            double? elev = (double?)p["elevation"]?.Value<double>();
            if (string.IsNullOrEmpty(baseSurfName) && elev == null)
                throw new ArgumentException("either base_surface or elevation is required");
            double pad = p["pad"]?.Value<double>() ?? 50.0;
            string tag = !string.IsNullOrEmpty(baseSurfName) ? baseSurfName : elev.Value.ToString("0.###");
            string baseName = p["base_name"]?.ToString() ?? ("LSDATUM_" + tag);
            string volName  = p["volume_name"]?.ToString() ?? ("LSVOL_" + tag);

            var bag = new Dictionary<string, double>();
            string statsVia = null, addVia = null;
            double exMinX, exMaxX, exMinY, exMaxY;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var avail = new List<string>();
                ObjectId compId = FindSurfaceIdByName(tr, compName, avail);
                if (compId == ObjectId.Null)
                    throw new InvalidOperationException(
                        $"Comparison surface '{compName}' not found. Available: ["
                        + string.Join(", ", avail) + "].");
                var comp = (TinSurface)tr.GetObject(compId, OpenMode.ForRead);
                var ext = comp.GeometricExtents;
                exMinX = ext.MinPoint.X - pad; exMaxX = ext.MaxPoint.X + pad;
                exMinY = ext.MinPoint.Y - pad; exMaxY = ext.MaxPoint.Y + pad;

                ObjectId baseId;
                if (!string.IsNullOrEmpty(baseSurfName))
                {
                    // Surface-vs-surface: use an existing surface as the base.
                    var bavail = new List<string>();
                    baseId = FindSurfaceIdByName(tr, baseSurfName, bavail);
                    if (baseId == ObjectId.Null)
                        throw new InvalidOperationException(
                            $"Base surface '{baseSurfName}' not found. Available: ["
                            + string.Join(", ", bavail) + "].");
                    baseName = baseSurfName;
                    addVia = "(existing surface)";
                }
                else
                {
                    // Surface-vs-flat-datum: build a flat base over the extent.
                    if (FindSurfaceIdByName(tr, baseName, null) != ObjectId.Null)
                        throw new InvalidOperationException($"Surface '{baseName}' already exists.");
                    baseId = TinSurface.Create(Db, baseName);
                    var bs = (TinSurface)tr.GetObject(baseId, OpenMode.ForWrite);

                    var pts = new Point3dCollection();
                    pts.Add(new Point3d(exMinX, exMinY, elev.Value));
                    pts.Add(new Point3d(exMaxX, exMinY, elev.Value));
                    pts.Add(new Point3d(exMaxX, exMaxY, elev.Value));
                    pts.Add(new Point3d(exMinX, exMaxY, elev.Value));

                    var bt = bs.GetType();
                    var mAddV = bt.GetMethod("AddVertices", new[] { typeof(Point3dCollection) });
                    if (mAddV != null) { mAddV.Invoke(bs, new object[] { pts }); addVia = "AddVertices"; }
                    else
                    {
                        var mAdd1 = bt.GetMethod("AddVertex", new[] { typeof(Point3d) });
                        if (mAdd1 != null) { foreach (Point3d pt in pts) mAdd1.Invoke(bs, new object[] { pt }); addVia = "AddVertex"; }
                        else throw new InvalidOperationException(
                            "No AddVertices/AddVertex on TinSurface. Candidates: "
                            + string.Join(", ", bt.GetMethods()
                                .Where(m => m.Name.IndexOf("Vert", StringComparison.OrdinalIgnoreCase) >= 0
                                         || m.Name.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0)
                                .Select(m => m.Name).Distinct()));
                    }
                    bs.Rebuild();
                }

                if (FindSurfaceIdByName(tr, volName, null) != ObjectId.Null)
                    throw new InvalidOperationException($"Surface '{volName}' already exists.");
                var tvsType = typeof(TinVolumeSurface);
                var mCreate = tvsType.GetMethod("Create", new[] { typeof(string), typeof(ObjectId), typeof(ObjectId) });
                if (mCreate == null)
                    throw new InvalidOperationException("TinVolumeSurface.Create(string,ObjectId,ObjectId) not found. Create overloads: "
                        + string.Join(" | ", tvsType.GetMethods().Where(m => m.Name == "Create").Select(m => m.GetParameters().Length + "args")));
                ObjectId volId = (ObjectId)mCreate.Invoke(null, new object[] { volName, baseId, compId });
                var vol = tr.GetObject(volId, OpenMode.ForRead);
                var mReb = vol.GetType().GetMethod("Rebuild", Type.EmptyTypes);
                if (mReb != null) mReb.Invoke(vol, null);

                object stats = null;
                foreach (var mn in new[] { "GetVolumeProperties", "GetVolumeStatistics" })
                {
                    var mi = vol.GetType().GetMethod(mn, Type.EmptyTypes);
                    if (mi != null) { try { stats = mi.Invoke(vol, null); statsVia = mn; } catch { } if (stats != null) break; }
                }
                if (stats != null)
                    foreach (var pi in stats.GetType().GetProperties())
                        if (pi.PropertyType == typeof(double)) { try { bag["stat_" + pi.Name] = (double)pi.GetValue(stats); } catch { } }
                foreach (var pi in vol.GetType().GetProperties())
                    if (pi.PropertyType == typeof(double) &&
                        (pi.Name.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0
                         || pi.Name.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0
                         || pi.Name.IndexOf("Fill", StringComparison.OrdinalIgnoreCase) >= 0))
                        { try { bag["prop_" + pi.Name] = (double)pi.GetValue(vol); } catch { } }

                tr.Commit();
            }

            return new
            {
                created = true,
                comparison = compName,
                baseSurface = baseName,
                volumeSurface = volName,
                elevation = elev,
                extent = new { minX = exMinX, maxX = exMaxX, minY = exMinY, maxY = exMaxY },
                addVertexVia = addVia,
                statsVia,
                stats = bag,
            };
        }

        // Configure contour display on an existing surface. Contours in Civil 3D
        // are a STYLE display property, not separate geometry. The managed
        // property path for the interval VALUES is not documented for 2026, so
        // we probe it via reflection (Track A) and, if that fails, still enable
        // contour visibility and return a note for manual interval entry (B).
        public static object GenerateContours(JObject p)
        {
            string surfaceName = p["surface"]?.ToString()
                ?? throw new ArgumentException("surface name is required");
            double minorInterval = p["minor_interval"]?.Value<double>()
                ?? throw new ArgumentException("minor_interval is required");
            double majorInterval = p["major_interval"]?.Value<double>()
                ?? throw new ArgumentException("major_interval is required");
            string styleName = p["style"]?.ToString();

            if (minorInterval <= 0)
                throw new ArgumentException("minor_interval must be > 0");
            if (majorInterval <= 0 || majorInterval < minorInterval)
                throw new ArgumentException(
                    "major_interval must be > 0 and >= minor_interval");

            bool minorSet = false, majorSet = false;
            bool minorVisible = false, majorVisible = false;
            string intervalNote = null;
            string styleUsed = null;
            string styleHandle = null;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                // Find surface by display name via the documented Civil API.
                var availableSurfaces = new List<string>();
                ObjectId surfaceId = FindSurfaceIdByName(
                    tr, surfaceName, availableSurfaces);
                if (surfaceId == ObjectId.Null)
                    throw new InvalidOperationException(
                        $"Surface '{surfaceName}' not found in this drawing. "
                        + "Available surfaces: ["
                        + string.Join(", ", availableSurfaces) + "].");

                var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForWrite);

                ObjectId styleId = surface.StyleId;
                if (!string.IsNullOrEmpty(styleName))
                {
                    var available = new List<string>();
                    ObjectId foundId = ObjectId.Null;
                    foreach (ObjectId sid in CivDoc.Styles.SurfaceStyles)
                    {
                        var ss = (SurfaceStyle)tr.GetObject(sid, OpenMode.ForRead);
                        available.Add(ss.Name);
                        if (string.Equals(ss.Name, styleName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            foundId = sid; styleUsed = ss.Name; break;
                        }
                    }
                    if (foundId == ObjectId.Null)
                        throw new InvalidOperationException(
                            $"Surface style '{styleName}' not found. Available styles: ["
                            + string.Join(", ", available) + "]. Create it first or "
                            + "omit the style parameter to use the current style.");
                    styleId = foundId;
                    surface.StyleId = styleId;
                }

                var style = (SurfaceStyle)tr.GetObject(styleId, OpenMode.ForWrite);
                if (styleUsed == null) styleUsed = style.Name;
                styleHandle = styleId.Handle.Value.ToString("X");

                // Track A: probe contour-interval property path via reflection.
                var bf = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance;
                try
                {
                    var contourStyleProp = style.GetType().GetProperty("ContourStyle", bf);
                    if (contourStyleProp != null)
                    {
                        var cs = contourStyleProp.GetValue(style);
                        var minP = cs?.GetType().GetProperty("MinorInterval", bf);
                        var majP = cs?.GetType().GetProperty("MajorInterval", bf);
                        if (cs != null && minP != null && majP != null)
                        {
                            minP.SetValue(cs, minorInterval);
                            majP.SetValue(cs, majorInterval);
                            minorSet = majorSet = true;
                        }
                    }
                }
                catch { }
                if (!minorSet)
                {
                    try
                    {
                        var minP = style.GetType().GetProperty("MinorContourInterval", bf);
                        var majP = style.GetType().GetProperty("MajorContourInterval", bf);
                        if (minP != null && majP != null)
                        {
                            minP.SetValue(style, minorInterval);
                            majP.SetValue(style, majorInterval);
                            minorSet = majorSet = true;
                        }
                    }
                    catch { }
                }
                if (!minorSet)
                    intervalNote =
                        $"Contour interval values could not be set via API "
                        + $"(property path unresolved on this build). Open Surface "
                        + $"Style '{styleUsed}' > Contours tab and set Minor "
                        + $"Interval={minorInterval}, Major Interval={majorInterval}.";

                // Always enable contour visibility in Plan view.
                try
                {
                    style.GetDisplayStylePlan(
                        SurfaceDisplayStyleType.MinorContour).Visible = true;
                    minorVisible = true;
                }
                catch { }
                try
                {
                    style.GetDisplayStylePlan(
                        SurfaceDisplayStyleType.MajorContour).Visible = true;
                    majorVisible = true;
                }
                catch { }

                tr.Commit();
            }

            return new
            {
                configured = true,
                surfaceName,
                styleUsed,
                styleHandle,
                minorIntervalSet = minorSet,
                majorIntervalSet = majorSet,
                minorInterval,
                majorInterval,
                minorContourVisible = minorVisible,
                majorContourVisible = majorVisible,
                contour_interval_note = intervalNote,
            };
        }

        // ----- Point Styles -----

        public static object ListPointStyles()
        {
            var styles = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in CivDoc.Styles.PointStyles)
                {
                    var style = (PointStyle)tr.GetObject(id, OpenMode.ForRead);
                    styles.Add(new
                    {
                        name = style.Name,
                        description = style.Description
                    });
                }
                tr.Commit();
            }
            return styles;
        }

        public static object CreatePointStyle(JObject p)
        {
            string name = p["name"]?.ToString()
                ?? throw new ArgumentException("name required");
            string description = p["description"]?.ToString() ?? "";
            string markerType = p["markerType"]?.ToString() ?? "plus"; // plus, x, circle, etc.
            double markerSize = p["markerSize"]?.Value<double>() ?? 2.0;
            bool useDrawingScale = p["useDrawingScale"]?.Value<bool>() ?? true;
            string blockName = p["blockName"]?.ToString(); // for block-based markers

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                // Add the style
                ObjectId styleId = CivDoc.Styles.PointStyles.Add(name);
                var style = (PointStyle)tr.GetObject(styleId, OpenMode.ForWrite);

                style.Description = description;

                // Marker configuration
                if (!string.IsNullOrEmpty(blockName))
                {
                    // Use AutoCAD block symbol for marker
                    style.MarkerType = PointMarkerDisplayType.UseSymbolForMarker;
                    style.MarkerSymbolName = blockName;
                }
                else
                {
                    // Use custom marker
                    style.MarkerType = PointMarkerDisplayType.UseCustomMarker;

                    // Set marker symbol based on type string
                    switch (markerType.ToLower())
                    {
                        case "plus":
                        case "+":
                            style.CustomMarkerStyle = CustomMarkerType.CustomMarkerPlus;
                            break;
                        case "x":
                        case "cross":
                            style.CustomMarkerStyle = CustomMarkerType.CustomMarkerX;
                            break;
                        case "dot":
                            style.CustomMarkerStyle = CustomMarkerType.CustomMarkerDot;
                            break;
                        case "vline":
                        case "|":
                            style.CustomMarkerStyle = CustomMarkerType.CustomMarkerVLine;
                            break;
                    }

                    // Size
                    if (useDrawingScale)
                        style.SizeType = MarkerSizeType.DrawingScale;
                    else
                        style.SizeType = MarkerSizeType.FixedScale;

                    style.MarkerSize = markerSize;
                }

                // 3D Geometry - default to Use Point Elevation
                style.Display3dType = PointDisplay3dType.UsePointElevation;

                // Marker rotation
                if (p["rotation"] != null)
                    style.MarkerRotationAngle = p["rotation"].Value<double>();

                tr.Commit();
                return new { created = true, name, styleId = styleId.Handle.Value.ToString() };
            }
        }

        // ----- Point Label Styles -----

        public static object ListPointLabelStyles()
        {
            var styles = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var labelStylesRoot = CivDoc.Styles.LabelStyles
                    .PointLabelStyles.LabelStyles;

                foreach (ObjectId id in labelStylesRoot)
                {
                    var style = (LabelStyle)tr.GetObject(id, OpenMode.ForRead);
                    styles.Add(new { name = style.Name });
                }
                tr.Commit();
            }
            return styles;
        }

        public static object CreatePointLabelStyle(JObject p)
        {
            string name = p["name"]?.ToString()
                ?? throw new ArgumentException("name required");
            string description = p["description"]?.ToString() ?? "";

            // Component definitions (array of components)
            var components = p["components"] as JArray;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var styleCol = CivDoc.Styles.LabelStyles
                    .PointLabelStyles.LabelStyles;

                ObjectId styleId = styleCol.Add(name);
                var style = (LabelStyle)tr.GetObject(styleId, OpenMode.ForWrite);

                // Set description via properties if available
                // (LabelStyle doesn't have a direct Description property;
                // it's on the Information tab)

                // Configure components
                if (components != null)
                {
                    foreach (JObject comp in components)
                    {
                        string compName = comp["name"]?.ToString() ?? "Text.1";
                        string compType = comp["type"]?.ToString() ?? "text";
                        string anchorPoint = comp["anchorPoint"]?.ToString() ?? "MiddleRight";
                        double textHeight = comp["textHeight"]?.Value<double>() ?? 2.0;
                        double xOffset = comp["xOffset"]?.Value<double>() ?? 0;
                        double yOffset = comp["yOffset"]?.Value<double>() ?? 0;
                        string contents = comp["contents"]?.ToString() ?? "";
                        string attachment = comp["attachment"]?.ToString() ?? "MiddleLeft";

                        // Note: The actual LabelStyleComponent API is more involved.
                        // This is a simplified version showing the pattern.
                        // Full implementation needs to handle:
                        //   style.GetComponents(LabelStyleComponentType.Text)
                        //   component.Name, component.Text.Contents,
                        //   component.General.AnchorPoint, etc.

                        // For a production implementation, you'd iterate the component
                        // collection and configure each property.
                    }
                }

                tr.Commit();
                return new { created = true, name };
            }
        }

        // ----- Line Label Styles -----

        public static object ListLineLabelStyles()
        {
            var styles = new List<object>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lineLabelRoot = CivDoc.Styles.LabelStyles
                    .GeneralLineLabelStyles;

                foreach (ObjectId id in lineLabelRoot)
                {
                    var style = (LabelStyle)tr.GetObject(id, OpenMode.ForRead);
                    styles.Add(new { name = style.Name });
                }
                tr.Commit();
            }
            return styles;
        }

        public static object CreateLineLabelStyle(JObject p)
        {
            string name = p["name"]?.ToString()
                ?? throw new ArgumentException("name required");

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lineLabelRoot = CivDoc.Styles.LabelStyles
                    .GeneralLineLabelStyles;

                ObjectId styleId = lineLabelRoot.Add(name);
                var style = (LabelStyle)tr.GetObject(styleId, OpenMode.ForWrite);

                // Configure direction/distance components based on style type
                // This would need detailed component manipulation similar to
                // point label styles above

                tr.Commit();
                return new { created = true, name };
            }
        }

        // ----- Label Application -----

        public static object AddLineLabel(JObject p)
        {
            // Place a GeneralSegmentLabel on a line or arc entity.
            //
            // History: prior versions passed the SAME line label style for both
            // the line-style and curve-style args of GeneralSegmentLabel.Create.
            // Civil 3D's API validates the type of the curve-style arg and
            // rejects a line label style with "Value does not fall within the
            // expected range", so EVERY label creation failed — even on line
            // entities — because the arg validation runs before the entity-
            // type check. Fix: resolve a curve label style separately. Most
            // shipped style names ("Bearing and Distance", "Azimuth over
            // Distance", etc.) exist in BOTH GeneralLineLabelStyles AND
            // GeneralCurveLabelStyles, so we look up the same name in each.
            string handleStr = p["entityHandle"]?.ToString()
                ?? throw new ArgumentException("entityHandle required");
            string styleName = p["styleName"]?.ToString() ?? "";
            // Optional override for the curve label style; defaults to same
            // name as the line style (matches Civil 3D's shipped style pairs).
            string curveStyleName = p["curveStyleName"]?.ToString() ?? styleName;

            long handleValue = Convert.ToInt64(handleStr, 16);
            Handle handle = new Handle(handleValue);
            ObjectId entityId = Db.GetObjectId(false, handle, 0);

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var labelStyles = CivDoc.Styles.LabelStyles;
                var lineLabelRoot  = labelStyles.GeneralLineLabelStyles;
                var curveLabelRoot = labelStyles.GeneralCurveLabelStyles;

                // --- Resolve line label style (required, must match by name) ---
                ObjectId lineStyleId = ObjectId.Null;
                var lineNames = new List<string>();
                foreach (ObjectId id in lineLabelRoot)
                {
                    var ls = (LabelStyle)tr.GetObject(id, OpenMode.ForRead);
                    lineNames.Add(ls.Name);
                    if (string.Equals(ls.Name, styleName, StringComparison.OrdinalIgnoreCase))
                        lineStyleId = id;
                }
                if (lineStyleId == ObjectId.Null)
                    throw new System.Exception(
                        $"Line label style '{styleName}' not found in "
                        + "GeneralLineLabelStyles. Available: ["
                        + string.Join(", ", lineNames) + "]");

                // --- Resolve curve label style (try same name; fall back) ---
                ObjectId curveStyleId = ObjectId.Null;
                ObjectId curveFallbackId = ObjectId.Null;
                var curveNames = new List<string>();
                foreach (ObjectId id in curveLabelRoot)
                {
                    var ls = (LabelStyle)tr.GetObject(id, OpenMode.ForRead);
                    curveNames.Add(ls.Name);
                    if (curveFallbackId == ObjectId.Null) curveFallbackId = id;
                    if (string.Equals(ls.Name, curveStyleName, StringComparison.OrdinalIgnoreCase))
                        curveStyleId = id;
                }
                bool curveResolvedByName = curveStyleId != ObjectId.Null;
                if (curveStyleId == ObjectId.Null)
                    curveStyleId = curveFallbackId; // best-effort fallback
                if (curveStyleId == ObjectId.Null)
                    throw new System.Exception(
                        $"No curve label style available. Tried name "
                        + $"'{curveStyleName}' in GeneralCurveLabelStyles. "
                        + $"Available: [{string.Join(", ", curveNames)}]");

                double ratio = p["ratio"]?.Value<double>() ?? 0.5;

                ObjectId labelId = GeneralSegmentLabel.Create(
                    entityId, ratio, lineStyleId, curveStyleId);

                string resolvedCurveName = ((LabelStyle)
                    tr.GetObject(curveStyleId, OpenMode.ForRead)).Name;

                tr.Commit();
                return new
                {
                    labeled = true,
                    labelId = labelId.Handle.Value.ToString("X"),
                    lineStyleName = styleName,
                    curveStyleName = resolvedCurveName,
                    curveStyleResolvedByName = curveResolvedByName,
                    ratio,
                };
            }
        }

        // ----- Offset -----

        public static object OffsetEntity(JObject p)
        {
            string handleStr = p["entityHandle"]?.ToString()
                ?? throw new ArgumentException("entityHandle required");
            double distance = p["distance"]?.Value<double>() ?? 0;
            string side = p["side"]?.ToString() ?? "right"; // right or left

            long handleValue = Convert.ToInt64(handleStr, 16);
            Handle handle = new Handle(handleValue);
            ObjectId entityId = Db.GetObjectId(false, handle, 0);

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(entityId, OpenMode.ForRead) as Curve;
                if (entity == null)
                    throw new ArgumentException("Entity is not a curve/line");

                var btr = (BlockTableRecord)tr.GetObject(
                    Db.CurrentSpaceId, OpenMode.ForWrite);

                // Get offset curves
                var offsetCurves = entity.GetOffsetCurves(
                    side == "left" ? -distance : distance);

                var createdIds = new List<string>();
                foreach (Autodesk.AutoCAD.DatabaseServices.Entity offsetEnt in offsetCurves)
                {
                    ObjectId newId = btr.AppendEntity(offsetEnt);
                    tr.AddNewlyCreatedDBObject(offsetEnt, true);
                    createdIds.Add(newId.Handle.Value.ToString());
                }

                tr.Commit();
                return new { created = createdIds };
            }
        }

        // ----- Document Management -----

        public static object OpenDrawing(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path required");

            var docManager = Application.DocumentManager;

            // Check if already open
            foreach (Document doc in docManager)
            {
                if (string.Equals(System.IO.Path.GetFileName(doc.Database.Filename),
                    System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                {
                    // Already open — send LISP to activate it
                    string safePath = path.Replace("\\", "\\\\");
                    Doc.SendStringToExecute(
                        $"(setq acdoc (vla-open (vla-get-documents (vlax-get-acad-object)) \"{safePath}\")) ",
                        true, false, false);
                    return new { opened = false, note = "Already open — LISP activation sent", filename = System.IO.Path.GetFileName(path) };
                }
            }

            // Not open — use LISP vla-open which runs on the command thread (no UI dialog)
            string safePathNew = path.Replace("\\", "\\\\");
            Doc.SendStringToExecute(
                $"(vl-load-com)(vla-open (vla-get-documents (vlax-get-acad-object)) \"{safePathNew}\") ",
                true, false, false);

            return new { opened = true, note = "LISP open command sent — drawing will open momentarily", filename = System.IO.Path.GetFileName(path) };
        }

        public static object SwitchDocument(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("filename required");

            var docManager = Application.DocumentManager;
            Document target = null;
            foreach (Document doc in docManager)
            {
                if (doc.Name.Equals(filename, StringComparison.OrdinalIgnoreCase) ||
                    System.IO.Path.GetFileName(doc.Database.Filename).Equals(filename, StringComparison.OrdinalIgnoreCase))
                {
                    target = doc;
                    break;
                }
            }
            if (target == null)
                throw new System.Exception($"Document '{filename}' not found in open documents. Open it first.");

            // Use LISP to switch — avoids cross-thread MdiActiveDocument assignment
            string safeName = target.Database.Filename.Replace("\\", "\\\\");
            Doc.SendStringToExecute(
                $"(vla-activate (vla-item (vla-get-documents (vlax-get-acad-object)) \"{safeName}\")) ",
                true, false, false);
            return new { switched = true, filename = target.Name };
        }

        // ----- Raster Image Attach (e.g. Lab 9 monument GIFs) -----
        //
        // Headless equivalent of IMAGEATTACH: build the RasterImageDef + RasterImage
        // via the .NET API so there is no command-line prompt sequence or modal
        // dialog (the send_command path could not do this reliably).
        //
        // VERIFY ON BUILD (members I could not compile-check here):
        //   - RasterImageDef.Size  -> Vector2d (image size in pixels). If your
        //     SDK names it differently, adjust the aspect-ratio calc below.
        //   - RasterImage.AssociateRasterDef(def) and RasterImage.EnableReactors(bool)
        //     exist in Autodesk.AutoCAD.DatabaseServices; ensure acdbmgd is referenced.
        public static object AttachRasterImage(JObject p)
        {
            string path = p["path"]?.ToString()
                ?? throw new ArgumentException("path required");
            if (!System.IO.File.Exists(path))
                throw new ArgumentException($"Image file not found: {path}");

            var pos = ParsePoint3d(p["insertion"]);              // lower-left corner
            double width = p["width"]?.Value<double>() ?? 100.0; // placed width, drawing units
            double rotDeg = p["rotation"]?.Value<double>() ?? 0.0;

            // Dictionary key from the file name; reused if this image is already attached.
            string key = SymbolUtilityServices.RepairSymbolName(
                System.IO.Path.GetFileNameWithoutExtension(path), false);

            string handle;
            double widthPx = 0, heightPx = 0;

            // The image dictionary is database-level; create it once if missing.
            ObjectId dictId = RasterImageDef.GetImageDictionary(Db);
            if (dictId.IsNull)
                dictId = RasterImageDef.CreateImageDictionary(Db);

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                ObjectId defId;
                RasterImageDef def;
                if (dict.Contains(key))
                {
                    defId = dict.GetAt(key);
                    def = (RasterImageDef)tr.GetObject(defId, OpenMode.ForWrite);
                    def.Load();
                }
                else
                {
                    def = new RasterImageDef();
                    def.SourceFileName = path;
                    def.Load();                       // throws if the file is unreadable
                    defId = dict.SetAt(key, def);
                    tr.AddNewlyCreatedDBObject(def, true);
                }

                // Preserve aspect ratio from the source pixel dimensions.
                Vector2d sizePx = def.Size;           // X = width px, Y = height px
                widthPx = sizePx.X;
                heightPx = sizePx.Y;
                double aspect = (sizePx.X > 0) ? (sizePx.Y / sizePx.X) : 1.0;
                double height = width * aspect;

                double rot = rotDeg * System.Math.PI / 180.0;
                double cos = System.Math.Cos(rot), sin = System.Math.Sin(rot);
                var u = new Vector3d(cos * width, sin * width, 0.0);    // bottom edge
                var v = new Vector3d(-sin * height, cos * height, 0.0); // left edge

                var img = new RasterImage();
                img.ImageDefId = defId;
                img.Orientation = new CoordinateSystem3d(pos, u, v);
                img.ShowImage = true;

                var btr = (BlockTableRecord)tr.GetObject(
                    Db.CurrentSpaceId, OpenMode.ForWrite);
                ObjectId imgId = btr.AppendEntity(img);
                tr.AddNewlyCreatedDBObject(img, true);

                // Without the def-reactor the image won't display and the def
                // becomes purgeable. This wires it up.
                RasterImage.EnableReactors(true);
                img.AssociateRasterDef(def);

                handle = imgId.Handle.Value.ToString();
                tr.Commit();
            }

            return new
            {
                attached = true,
                entityId = handle,   // decimal handle, like the other draw_* actions
                imageDef = key,
                widthPx,
                heightPx
            };
        }

        // ----- Entity Handle Query -----

        public static object GetEntityHandles(JObject p)
        {
            string layer = p["layer"]?.ToString()
                ?? throw new ArgumentException("layer required");

            var handles = new List<string>();
            var types = new List<string>();

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
                    if (ent != null && ent.Layer == layer)
                    {
                        handles.Add(id.Handle.Value.ToString("X"));
                        types.Add(ent.GetType().Name);
                    }
                }
                tr.Commit();
            }

            var result = new List<object>();
            for (int i = 0; i < handles.Count; i++)
                result.Add(new { handle = handles[i], type = types[i] });
            return result;
        }

        // ----- Text Style Font -----

        public static object SetTextStyleFont(JObject p)
        {
            string styleName = p["styleName"]?.ToString() ?? "Standard";
            string font = p["font"]?.ToString()
                ?? throw new ArgumentException("font required");

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var tt = (TextStyleTable)tr.GetObject(Db.TextStyleTableId, OpenMode.ForRead);
                if (!tt.Has(styleName))
                    throw new System.Exception($"Text style '{styleName}' not found");

                var ttr = (TextStyleTableRecord)tr.GetObject(tt[styleName], OpenMode.ForWrite);
                ttr.FileName = font;
                tr.Commit();
                return new { updated = true, styleName, font };
            }
        }

        // ----- Zoom to Point -----

        public static object ZoomToPoint(JObject p)
        {
            int pointNumber = p["pointNumber"]?.Value<int>()
                ?? throw new ArgumentException("pointNumber required");
            double zoomFactor = p["zoomFactor"]?.Value<double>() ?? 20.0;

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var points = CivDoc.CogoPoints;
                foreach (ObjectId pid in points)
                {
                    var pt = (CogoPoint)tr.GetObject(pid, OpenMode.ForRead);
                    if (pt.PointNumber != pointNumber) continue;

                    var center = new Point2d(pt.Easting, pt.Northing);
                    var view = Ed.GetCurrentView();
                    view.CenterPoint = center;
                    view.Height = zoomFactor;
                    view.Width = zoomFactor * (view.Width / view.Height);
                    Ed.SetCurrentView(view);

                    tr.Commit();
                    return new
                    {
                        zoomed = true,
                        pointNumber,
                        easting = pt.Easting,
                        northing = pt.Northing
                    };
                }
                throw new System.Exception($"Point {pointNumber} not found");
            }
        }

        // ----- Fallback: Send raw command -----

        public static object SendCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("command string required");

            // Send command to AutoCAD command line
            // This is the "escape hatch" for anything not covered by specific handlers
            Doc.SendStringToExecute(command + "\n", true, false, true);

            return new { sent = true, command };
        }

        // =====================================================================
        // SURVEY (COM Interop — AeccXUiSurveyLib v13.8 for Civil 3D 2026)
        // =====================================================================
        //
        // The Civil 3D Survey API has no managed .NET equivalent. There are
        // TWO interop assemblies:
        //   • Autodesk.AECC.Interop.Survey.dll     — low-level types
        //     (IAeccSurveyProjects, IAeccSurveyProject, IAeccSurveyNetwork…)
        //   • Autodesk.AECC.Interop.UiSurvey.dll   — UI-bound entry point
        //     (IAeccSurveyApplication, IAeccSurveyDocument)
        //
        // ENTRY CHAIN (critical):
        // When running INSIDE Civil 3D, the existing acadApp COM object
        // already implements both IAcadApplication and IAeccApplication
        // (Civil 3D extends AutoCAD's app object via COM aggregation).
        // We obtain IAeccApplication via a QueryInterface cast, not by
        // asking AcadApp.GetInterfaceObject to instantiate a new copy —
        // that call fails with "Problem in loading application" because
        // AutoCAD doesn't know how to register Civil 3D ProgIDs as
        // instantiable child modules.
        //
        //   1) (IAeccApplication)Application.AcadApplication
        //      => IAeccApplication (same RCW, QueryInterface vtable)
        //
        //   2) IAeccApplication.GetInterfaceObject(
        //          "AeccXUiSurvey.AeccSurveyDocument.13.x")     DispId 20
        //      => IAeccSurveyDocument (per-drawing survey wrapper)
        //      [This GetInterfaceObject is the Civil 3D one — different
        //      vtable slot from AutoCAD's — and it DOES know how to
        //      instantiate Civil 3D child modules.]
        //
        //   3) IAeccSurveyDocument.Projects  (DispId 50000100)
        //      => IAeccSurveyProjects (the SDB collection, rooted at the
        //         current survey working folder)
        //
        // We resolve every step via IDispatch (Type.InvokeMember) because
        // .NET 8 dynamic-dispatch to System.__ComObject no longer routes
        // through IDispatch. The final .Projects object IS the typed
        // IAeccSurveyProjects from Autodesk.AECC.Interop.Survey — we cast
        // it there and stay strongly-typed for all downstream calls.
        //
        // ProgID version tag: TypeLibVersion 13.8 = Civil 3D 2026.

        private static readonly string[] _surveyDocProgIds = new[]
        {
            "AeccXUiSurvey.AeccSurveyDocument.13.8",  // Civil 3D 2026
            "AeccXUiSurvey.AeccSurveyDocument.13.7",
            "AeccXUiSurvey.AeccSurveyDocument.13.6",
            "AeccXUiSurvey.AeccSurveyDocument.13.5",
        };

        /// <summary>
        /// Walks the document's COM IDispatch and returns a live
        /// IAeccSurveyDatabase bound to the active drawing. Throws with
        /// a diagnostic message if none of the known ProgIDs resolve.
        /// </summary>
        /// <summary>
        /// Late-bound IDispatch invocation for a method on a COM object.
        /// Necessary because on .NET 8, `dynamic` against System.__ComObject
        /// goes through the DLR which reflects on the managed type (nothing
        /// there) rather than routing to IDispatch::Invoke.
        /// </summary>
        private static object InvokeCom(object comObject, string member, params object[] args)
        {
            return comObject.GetType().InvokeMember(
                member,
                System.Reflection.BindingFlags.InvokeMethod
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                null,
                comObject,
                args);
        }

        private static object GetComProperty(object comObject, string member)
        {
            return comObject.GetType().InvokeMember(
                member,
                System.Reflection.BindingFlags.GetProperty
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                null,
                comObject,
                null);
        }

        private static void SetComProperty(object comObject, string member, object value)
        {
            comObject.GetType().InvokeMember(
                member,
                System.Reflection.BindingFlags.SetProperty
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                null,
                comObject,
                new object[] { value });
        }

        // --- ROT (Running Object Table) helpers for connecting to a running
        //     COM server instance. Marshal.GetActiveObject was removed in
        //     .NET 5+, so we reach into ole32 directly. ---
        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(uint reserved,
            out System.Runtime.InteropServices.ComTypes.IRunningObjectTable prot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(uint reserved,
            out System.Runtime.InteropServices.ComTypes.IBindCtx ppbc);

        /// <summary>
        /// .NET 5+ replacement for Marshal.GetActiveObject(progId). Looks up
        /// the CLSID for the ProgID, walks the Running Object Table, and
        /// returns the first running instance whose class matches. Returns
        /// null if no running instance is registered in ROT.
        /// </summary>
        private static object GetActiveObjectFromRot(string progId)
        {
            // Resolve ProgID -> CLSID
            Type type = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (type == null) return null;

            Guid clsid = type.GUID;

            // Create a binding context for moniker display-name comparison
            System.Runtime.InteropServices.ComTypes.IBindCtx bindCtx;
            if (CreateBindCtx(0, out bindCtx) != 0) return null;

            System.Runtime.InteropServices.ComTypes.IRunningObjectTable rot;
            if (GetRunningObjectTable(0, out rot) != 0) return null;

            System.Runtime.InteropServices.ComTypes.IEnumMoniker enumMoniker;
            rot.EnumRunning(out enumMoniker);
            enumMoniker.Reset();

            var monikers = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
            IntPtr fetched = IntPtr.Zero;
            while (enumMoniker.Next(1, monikers, fetched) == 0)
            {
                try
                {
                    // Most AutoCAD/Civil 3D moniker display names include the ProgID
                    // or CLSID; match both cases for robustness.
                    string displayName;
                    monikers[0].GetDisplayName(bindCtx, null, out displayName);
                    if (displayName != null &&
                        (displayName.IndexOf(progId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         displayName.IndexOf(clsid.ToString("B"), StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        object obj;
                        if (rot.GetObject(monikers[0], out obj) == 0)
                            return obj;
                    }
                }
                finally
                {
                    if (monikers[0] != null) Marshal.ReleaseComObject(monikers[0]);
                }
            }
            return null;
        }

        /// <summary>
        /// Probe multiple activation strategies to obtain a live object for
        /// the given ProgID. Returns the first strategy that succeeds along
        /// with a detailed telemetry trail of every attempt for diagnostics.
        /// The host argument (typically the AcadApplication) is used for
        /// the primary GetInterfaceObject strategy — this is the canonical
        /// Autodesk VBA pattern and the only one that properly wires the
        /// returned object to the running Civil 3D drawing context.
        /// </summary>
        private static object ResolveComObject(object host, string[] progIds, List<string> tried)
        {
            foreach (var progId in progIds)
            {
                // --- Strategy A: host.GetInterfaceObject(progId) ---
                // This is the documented Autodesk pattern. The host (usually
                // AcadApplication) knows how to instantiate Civil 3D child
                // modules AND wire them to the active drawing. Must be called
                // on the main STA thread — which our ExecuteOnMainThread
                // guarantees.
                if (host != null)
                {
                    try
                    {
                        object raw = InvokeCom(host, "GetInterfaceObject", progId);
                        if (raw != null)
                        {
                            tried.Add($"{progId} (host.GetInterfaceObject:OK)");
                            return raw;
                        }
                        tried.Add($"{progId} (host.GetInterfaceObject:null)");
                    }
                    catch (System.Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        tried.Add($"{progId} (host.GetInterfaceObject:{inner.GetType().Name}: {inner.Message})");
                    }
                }

                // --- Strategy B: Activator.CreateInstance via Type.GetTypeFromProgID ---
                // Standard CoCreateInstance. Produces a bare instance that
                // may not be bound to the running drawing — useful only as
                // a fallback when GetInterfaceObject is unavailable.
                try
                {
                    Type t = Type.GetTypeFromProgID(progId, throwOnError: false);
                    if (t != null)
                    {
                        object inst = Activator.CreateInstance(t);
                        if (inst != null)
                        {
                            tried.Add($"{progId} (CreateInstance:OK)");
                            return inst;
                        }
                        tried.Add($"{progId} (CreateInstance:null)");
                    }
                    else
                    {
                        tried.Add($"{progId} (CreateInstance:GetTypeFromProgID returned null)");
                    }
                }
                catch (System.Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    tried.Add($"{progId} (CreateInstance:{inner.GetType().Name}: {inner.Message})");
                }

                // --- Strategy C: ROT lookup (connect to running instance) ---
                // Marshal.GetActiveObject was removed in .NET 5+, so we reach
                // into the Running Object Table directly via ole32 P/Invoke.
                try
                {
                    object running = GetActiveObjectFromRot(progId);
                    if (running != null)
                    {
                        tried.Add($"{progId} (ROT:OK)");
                        return running;
                    }
                    tried.Add($"{progId} (ROT:not found)");
                }
                catch (System.Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    tried.Add($"{progId} (ROT:{inner.GetType().Name}: {inner.Message})");
                }
            }
            return null;
        }

        // Survey projects this bridge session has already opened. On Civil 3D
        // 2026 Projects.Create() returns an ALREADY-OPEN project, and a redundant
        // IAeccSurveyProject.OpenEx() double-opens the SDB -> native
        // AccessViolation that kills acad.exe (an uncatchable corrupted-state
        // exception — try/catch cannot save it). So we open each project at most
        // once per session. Cleared whenever the bridge assembly reloads.
        private static readonly HashSet<string> _openedSurveyProjects =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Open a survey project for read/write at most once per bridge session.
        ///
        /// CRITICAL (Civil 3D 2026): the dedup key is the project NAME (a string
        /// the caller already holds), NEVER a COM property like ProjectGUID.
        /// Reading ANY member of the AeccSurveyProject returned by Projects.Create
        /// — even get_ProjectGUID — triggers a native AccessViolation that tears
        /// down acad.exe (uncatchable corrupted-state exception). So this helper
        /// must touch the COM object ONLY to call OpenEx, and ONLY on a
        /// genuinely-closed, collection-RE-RESOLVED existing project (resolved via
        /// FindItem/index, never the Create() return). Callers must NEVER pass the
        /// Create() return here — it is poison to touch in any way.
        /// </summary>
        private static void OpenSurveyProjectOnce(
            AeccSurveyProject proj, string nameKey, bool alreadyOpen = false)
        {
            if (nameKey != null && _openedSurveyProjects.Contains(nameKey))
                return;
            if (!alreadyOpen)
                proj.OpenEx(false);
            if (nameKey != null)
                _openedSurveyProjects.Add(nameKey);
        }

        private static IAeccSurveyProjects GetSurveyProjects()
        {
            // Multi-strategy survey document resolution. AutoCAD's
            // GetInterfaceObject fails with "Problem in loading application"
            // on AeccXUi* ProgIDs, and QueryInterface on the AutoCAD app
            // returns E_NOINTERFACE. So we fall back to direct COM activation
            // (Activator.CreateInstance) and ROT binding (GetActiveObject),
            // then attempt to walk .Projects off the result via IDispatch.
            object acadAppObj = Application.AcadApplication;
            if (acadAppObj == null)
                throw new InvalidOperationException(
                    "Application.AcadApplication is null — is Civil 3D fully initialized?");

            var tried = new List<string>();

            // --- ATTEMPT 1: acadApp.GetInterfaceObject (canonical Autodesk path) ---
            // Only works if we're on the main STA thread, which
            // ExecuteOnMainThread guarantees.
            object surveyDoc = ResolveComObject(acadAppObj, _surveyDocProgIds, tried);

            // --- ATTEMPT 2: If direct fails, try the Civil 3D app shell path ---
            // AeccApplication.GetInterfaceObject is the Civil 3D shell's
            // activation function, which may resolve child module ProgIDs
            // even when AcadApp.GetInterfaceObject doesn't.
            if (surveyDoc == null)
            {
                var aeccAppProgIds = new[]
                {
                    "AeccXUiLand.AeccApplication.13.8",
                    "AeccXUiLand.AeccApplication.13.7",
                    "AeccXUiLand.AeccApplication.13.6",
                    "AeccXUiLand.AeccApplication.13.5",
                };

                object aeccApp = ResolveComObject(acadAppObj, aeccAppProgIds, tried);
                if (aeccApp != null)
                {
                    // Try again with aeccApp as the host
                    surveyDoc = ResolveComObject(aeccApp, _surveyDocProgIds, tried);
                }
            }

            if (surveyDoc == null)
            {
                throw new InvalidOperationException(
                    "Could not obtain IAeccSurveyDocument via any strategy. Trail: "
                    + string.Join("; ", tried));
            }

            // --- Init the survey doc with the active AcadDocument ---
            // IAeccSurveyDocument inherits Init(AcadDocument) from IAeccDocument
            // at DispId 196864. Even when GetInterfaceObject returns a wired-up
            // instance, property access (like .Projects) fails with
            // "Unspecified error" until Init is called explicitly.
            try
            {
                object activeAcadDoc = GetComProperty(acadAppObj, "ActiveDocument");
                if (activeAcadDoc != null)
                {
                    InvokeCom(surveyDoc, "Init", activeAcadDoc);
                    tried.Add("Init(ActiveDocument):OK");
                }
                else
                {
                    tried.Add("Init: ActiveDocument was null");
                }
            }
            catch (System.Exception ex)
            {
                // Log but don't fail — attempt .Projects anyway so we can see
                // whether Init was actually needed.
                var inner = ex.InnerException ?? ex;
                tried.Add($"Init: {inner.GetType().Name}: {inner.Message}");
            }

            // --- Walk .Projects (DispId 50000100) ---
            object projectsObj;
            try
            {
                projectsObj = GetComProperty(surveyDoc, "Projects");
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    "Resolved survey doc but failed to walk .Projects: "
                    + $"{inner.GetType().Name}: {inner.Message}. "
                    + $"Doc runtime type: {surveyDoc.GetType().FullName}. "
                    + "Resolution trail: " + string.Join("; ", tried));
            }

            if (projectsObj == null)
                throw new InvalidOperationException(
                    "Survey doc .Projects returned null. Trail: " + string.Join("; ", tried));

            try
            {
                return (IAeccSurveyProjects)projectsObj;
            }
            catch (InvalidCastException ice)
            {
                throw new InvalidOperationException(
                    ".Projects is not castable to IAeccSurveyProjects. Runtime type: "
                    + projectsObj.GetType().FullName + " — " + ice.Message);
            }
        }

        public static object GetSurveyWorkingFolder()
        {
            var projects = GetSurveyProjects();
            string folder = projects.GetWorkingFolder();
            return new { workingFolder = folder };
        }

        public static object SetSurveyWorkingFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required");

            // Civil 3D refuses to set a non-existent folder. Create if missing.
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);

            var projects = GetSurveyProjects();
            projects.SetWorkingFolder(path);
            string confirmed = projects.GetWorkingFolder();

            return new
            {
                workingFolder = confirmed,
                requested = path,
                matched = string.Equals(
                    System.IO.Path.GetFullPath(confirmed).TrimEnd('\\'),
                    System.IO.Path.GetFullPath(path).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        public static object CreateSurveyProject(string name, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");

            var projects = GetSurveyProjects();

            // Does it already exist? FindItem(variant) works by name OR index.
            AeccSurveyProject existing = null;
            try { existing = projects.FindItem(name); } catch { /* not found */ }
            bool preExisting = existing != null;

            if (!preExisting)
            {
                // Civil 3D 2026: Projects.Create() writes AND opens a complete SDB
                // on disk, then returns a COM pointer that is POISON — touching ANY
                // member of it (OpenEx, get_ProjectGUID, get_Name, get_Path)
                // triggers a native AccessViolation that kills acad.exe (an
                // uncatchable corrupted-state exception; try/catch cannot save it).
                // So we DISCARD the return value and never touch it. Metadata
                // (GUID/path) is read later via list_survey_projects, which uses a
                // fresh, collection-indexed resolution that is safe.
                //
                // v36: do NOT register the name as "opened" here. Create() writes
                // the SDB to disk but does NOT leave a re-resolvable open handle:
                // a later ResolveSurveyProject re-resolves a DIFFERENT, genuinely
                // CLOSED object via FindItem, which must be OpenEx'd before
                // Networks.Create works ("survey database is not open" otherwise).
                // Registering on create made ResolveSurveyProject SKIP that needed
                // open. OpenEx on a FindItem-resolved (collection) object is
                // expected safe — the AV poison is specific to the Create() RETURN,
                // not to collection-resolved AeccSurveyProject objects (same basis
                // as list_survey_projects safely reading .ProjectGUID).
                projects.Create(name);
            }

            // Description set is best-effort and requires touching the project COM
            // object (GetProjectSettings/UpdateProjectSettings). For a freshly
            // created project that is poison to touch, so we SKIP it — only attempt
            // it on a pre-existing, re-resolved project.
            bool descriptionSet = false;
            if (preExisting && !string.IsNullOrEmpty(description))
            {
                try
                {
                    var settings = existing.GetProjectSettings();
                    SetComProperty(settings, "Description", description);
                    existing.UpdateProjectSettings(settings);
                    descriptionSet = true;
                }
                catch
                {
                    // Swallow — description is best-effort, not lab-critical.
                }
            }

            // Return name-string metadata only — do NOT read back from any COM
            // project object in this same call.
            return new
            {
                name,
                preExisting,
                descriptionSet,
                note = preExisting
                    ? "already existed"
                    : "created — read GUID/path via list_survey_projects"
            };
        }

        public static object ListSurveyProjects()
        {
            var projects = GetSurveyProjects();

            var list = new List<object>();
            for (int i = 0; i < projects.Count; i++)
            {
                var proj = projects[i];
                list.Add(new
                {
                    name = proj.Name,
                    path = proj.Path,
                    projectGuid = proj.ProjectGUID
                });
            }

            return new
            {
                workingFolder = projects.GetWorkingFolder(),
                count = projects.Count,
                projects = list
            };
        }

        // ----- Survey network + FBK import -----

        /// <summary>
        /// Resolve a live <see cref="AeccSurveyProject"/> by name, opening it
        /// for read/write if needed so Networks/Figures/etc. become usable.
        /// </summary>
        private static AeccSurveyProject ResolveSurveyProject(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("projectName is required");

            var projects = GetSurveyProjects();

            AeccSurveyProject proj = null;
            try { proj = projects.FindItem(projectName); }
            catch { /* FindItem throws on not-found — fall through to linear scan */ }

            if (proj == null)
            {
                for (int i = 0; i < projects.Count; i++)
                {
                    var candidate = projects[i];
                    if (string.Equals(candidate.Name, projectName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        proj = candidate;
                        break;
                    }
                }
            }

            if (proj == null)
                throw new InvalidOperationException(
                    $"Survey project '{projectName}' not found in working folder "
                    + $"'{projects.GetWorkingFolder()}'.");

            // Open for read/write so Networks.Create / enumeration works — but
            // only once per session (re-opening an already-open project AVs).
            // Key by the name string, never by a COM property getter.
            OpenSurveyProjectOnce(proj, projectName);
            return proj;
        }

        public static object CreateSurveyNetwork(
            string projectName, string networkName, string description)
        {
            if (string.IsNullOrWhiteSpace(networkName))
                throw new ArgumentException("networkName is required");

            var proj = ResolveSurveyProject(projectName);
            var networks = proj.Networks;

            // Idempotent: if a network with this name already exists, return it.
            AeccSurveyNetwork existing = null;
            try { existing = networks.FindItem(networkName); } catch { /* not found */ }

            if (existing == null)
            {
                for (int i = 0; i < networks.Count; i++)
                {
                    var candidate = networks[i];
                    if (string.Equals(candidate.Name, networkName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        existing = candidate;
                        break;
                    }
                }
            }

            AeccSurveyNetwork net = existing ?? networks.Create(networkName);

            bool descriptionSet = false;
            if (!string.IsNullOrEmpty(description))
            {
                try { net.Description = description; descriptionSet = true; }
                catch { /* best-effort */ }
            }

            // Persist so the network is visible to subsequent commands.
            try { net.Save(); } catch { /* ignore */ }

            return new
            {
                projectName = proj.Name,
                networkName = net.Name,
                description = SafeGet(() => net.Description),
                path = SafeGet(() => net.Path),
                id = SafeGet(() => (int?)net.ID),
                preExisting = existing != null,
                descriptionSet
            };
        }

        public static object ListSurveyNetworks(string projectName)
        {
            var proj = ResolveSurveyProject(projectName);
            var networks = proj.Networks;

            var list = new List<object>();
            for (int i = 0; i < networks.Count; i++)
            {
                var n = networks[i];
                list.Add(new
                {
                    name = n.Name,
                    description = SafeGet(() => n.Description),
                    id = SafeGet(() => (int?)n.ID),
                    path = SafeGet(() => n.Path),
                });
            }

            return new
            {
                projectName = proj.Name,
                count = networks.Count,
                networks = list
            };
        }

        /// <summary>
        /// Import a Civil 3D Field Book (.fbk) file into the specified survey
        /// network. FBK import is NOT exposed via the Civil 3D COM type library
        /// (verified against decompiled AECC.Interop.Survey and
        /// AECC.Interop.UiSurvey) — the only scriptable path is the AutoCAD
        /// command <c>IMPORTFIELDBOOK</c>, driven via
        /// <see cref="Document.SendStringToExecute"/> with <c>FILEDIA</c>
        /// temporarily set to 0 so the file-picker becomes a command-line
        /// prompt. Prerequisite: the network must already exist (we validate
        /// by name via COM before queuing the command).
        /// </summary>
        public static object ImportFbk(
            string projectName,
            string networkName,
            string fbkPath,
            bool? insertIntoDrawing,
            bool? showErrors,
            bool? showWarnings,
            int? timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(fbkPath))
                throw new ArgumentException("fbkPath is required");
            if (!System.IO.File.Exists(fbkPath))
                throw new System.IO.FileNotFoundException(
                    $"FBK file not found: {fbkPath}", fbkPath);
            if (string.IsNullOrWhiteSpace(networkName))
                throw new ArgumentException("networkName is required");

            // 1) Precondition: project+network exist via COM.
            var proj = ResolveSurveyProject(projectName);
            AeccSurveyNetwork net = null;
            try { net = proj.Networks.FindItem(networkName); } catch { }
            if (net == null)
            {
                for (int i = 0; i < proj.Networks.Count; i++)
                {
                    var c = proj.Networks[i];
                    if (string.Equals(c.Name, networkName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        net = c; break;
                    }
                }
            }
            if (net == null)
                throw new InvalidOperationException(
                    $"Network '{networkName}' does not exist in project "
                    + $"'{proj.Name}'. Call create_survey_network first.");

            int figuresBefore = SafeGetInt(() => proj.Figures.Count, -1);

            // 2) Normalize path and options.
            string normalized = System.IO.Path.GetFullPath(fbkPath)
                .Replace("\\", "/");  // AutoCAD prefers forward slashes in scripted file prompts
            bool optInsert = insertIntoDrawing ?? true;
            bool optErrors = showErrors ?? false;
            bool optWarnings = showWarnings ?? false;

            // 3) Script the command. Civil 3D's IMPORTFIELDBOOK, when run with
            //    FILEDIA=0, drops into command-line prompts:
            //       - Select survey network: <name>
            //       - Fieldbook file: <path>
            //       - Show errors? [Yes/No]
            //       - Show warnings? [Yes/No]
            //       - Insert figures into drawing? [Yes/No]
            //    The exact prompt text varies by Civil 3D version; we answer
            //    conservatively with explicit Y/N tokens rather than empty
            //    <enter> accepts so responses are stable across releases.
            string yes(bool v) => v ? "Y " : "N ";
            string script =
                "FILEDIA 0 " +
                $"IMPORTFIELDBOOK \"{networkName}\" \"{normalized}\" " +
                yes(optErrors) + yes(optWarnings) + yes(optInsert) +
                "FILEDIA 1 ";

            // Send as a single SendStringToExecute. AutoCAD queues the script
            // to run AFTER the current command context ends, which means this
            // handler MUST return before IMPORTFIELDBOOK actually starts
            // chewing on the FBK. We can't block-and-wait here on the main
            // thread — doing so would prevent the queued command from ever
            // running. The caller is expected to poll
            // list_survey_networks / figures / points after a few seconds.
            Doc.SendStringToExecute(script, true, false, true);

            // figuresAfter is captured pre-dispatch purely so the caller sees
            // a stable baseline; the actual delta only shows up after a poll.
            int figuresAfter = figuresBefore;
            int waitMs = 0; // retained for payload shape, always 0 now

            return new
            {
                projectName = proj.Name,
                networkName = net.Name,
                fbkPath = System.IO.Path.GetFullPath(fbkPath),
                scriptQueued = script,
                options = new
                {
                    insertIntoDrawing = optInsert,
                    showErrors = optErrors,
                    showWarnings = optWarnings,
                    waitMs
                },
                figures = new
                {
                    before = figuresBefore,
                    after = figuresAfter,
                    delta = (figuresBefore >= 0 && figuresAfter >= 0)
                        ? figuresAfter - figuresBefore
                        : (int?)null
                },
                note = "FBK import queued via IMPORTFIELDBOOK. If figures.delta "
                     + "is 0 or null, poll list_survey_networks / figures after "
                     + "a few seconds — AutoCAD may still be processing."
            };
        }

        // =====================================================================
        // FBK DIRECT IMPORT (parser + COM Create, bypassing IMPORTFIELDBOOK)
        // =====================================================================
        //
        // Civil 3D's IMPORTFIELDBOOK command is not scriptable — it always
        // pops modal dialogs that freeze the command processor. The COM type
        // library exposes no FBK import method either. So we parse the
        // FieldGenius .fbk format ourselves and drive the Survey COM API
        // directly (ControlPoints.Create, NonControlPoints.Create,
        // Setups.Create, Setup.Observations.Create).
        //
        // Supported FBK subset (everything TopoSurvey2023 uses):
        //   JOB/UNIT/EDM/SCALE/HORIZ/VERT  — ignored (header noise)
        //   !                              — comment, skipped
        //   NEZ   pt n e z "desc"          — coordinate point (control if
        //                                    seen before first STN, otherwise
        //                                    treated as non-control)
        //   STN   pt hi ["desc"]           — station setup, instrument height
        //   AZ    from to dms              — azimuth from station to target
        //   BS    pt circle                — backsight pt + face1 circle
        //   PRISM height                   — sticky target height
        //   F1/F2 [VA|ZA] pt ha sd va "desc" — observation (face1/face2)
        //
        // Angles in FBK are DMS-packed as `DDD.MMSSsss`:
        //   integer part   → degrees
        //   first 2 decs   → minutes
        //   next 2 decs    → seconds
        //   remaining decs → fractional seconds
        //   e.g. 86.43340  → 86° 43' 34.0"
        //
        // NEZ in FBK is Northing, Easting, Elevation — but the COM
        // Create(pt, name, desc, E, N, Z) methods take Easting FIRST. We
        // swap the arg order when handing values to COM.

        public enum FbkRecordType
        {
            Nez, Stn, Bs, Az, Prism, F1, F2, Job, Unit, Ignored, Unknown
        }

        public class FbkRecord
        {
            public int LineNumber;
            public string Raw;
            public FbkRecordType Type;
            public int? PointNumber;
            public double? N;
            public double? E;
            public double? Z;
            public double? InstrumentHeight;
            public double? Circle;          // BS circle reading (dms packed)
            public double? AzimuthDms;      // AZ packed dms
            public int? AzFrom;
            public int? AzTo;
            public double? PrismHeight;
            public string AngleFlavor;      // "VA" or "ZA"
            public double? Ha;              // dms packed
            public double? Sd;
            public double? Va;              // dms packed
            public string Description;
            public string Warning;
        }

        public class FbkSetupBlock
        {
            public int StationPoint;
            public double InstrumentHeight;
            public string Description;
            public int? BacksightPoint;
            public double? BacksightCircleDms;
            public double? AzimuthDms;
            public int? AzFrom;
            public int? AzTo;
            public List<FbkRecord> Observations = new List<FbkRecord>();
        }

        public class FbkParseResult
        {
            public string FilePath;
            public int TotalLines;
            public List<FbkRecord> Records = new List<FbkRecord>();
            public List<FbkRecord> ControlPoints = new List<FbkRecord>();
            public List<FbkSetupBlock> Setups = new List<FbkSetupBlock>();
            public List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// DMS-packed angle (DDD.MMSSsss) to radians.
        /// </summary>
        private static double DmsPackedToRadians(double packed)
        {
            double sign = packed < 0 ? -1.0 : 1.0;
            double abs = Math.Abs(packed);
            int deg = (int)Math.Floor(abs);
            double rest = (abs - deg) * 100.0;   // MM.SSsss
            int min = (int)Math.Floor(rest + 1e-9);
            double sec = (rest - min) * 100.0;   // SS.sss
            double decDeg = deg + min / 60.0 + sec / 3600.0;
            return sign * decDeg * Math.PI / 180.0;
        }

        /// <summary>
        /// Parse a single FBK line into an FbkRecord. Returns null for
        /// blank / comment lines.
        /// </summary>
        private static FbkRecord ParseFbkLine(string line, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("!")) return null;  // comment

            // Split while honoring the trailing quoted description.
            // We pull out the quoted section first if present.
            string quoted = null;
            string body = trimmed;
            int q1 = trimmed.IndexOf('"');
            if (q1 >= 0)
            {
                int q2 = trimmed.IndexOf('"', q1 + 1);
                if (q2 > q1)
                {
                    quoted = trimmed.Substring(q1 + 1, q2 - q1 - 1);
                    body = (trimmed.Substring(0, q1) + " " +
                            trimmed.Substring(q2 + 1)).Trim();
                }
            }

            string[] tok = body.Split(new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) return null;

            var rec = new FbkRecord
            {
                LineNumber = lineNumber,
                Raw = line,
                Description = quoted,
                Type = FbkRecordType.Unknown
            };

            System.Globalization.CultureInfo ci =
                System.Globalization.CultureInfo.InvariantCulture;

            string kw = tok[0].ToUpperInvariant();
            try
            {
                switch (kw)
                {
                    case "JOB":
                        rec.Type = FbkRecordType.Job;
                        break;
                    case "UNIT":
                    case "EDM":
                    case "SCALE":
                    case "HORIZ":
                    case "VERT":
                        rec.Type = FbkRecordType.Ignored;
                        break;

                    case "NEZ":
                        // NEZ pt N E Z
                        rec.Type = FbkRecordType.Nez;
                        rec.PointNumber = int.Parse(tok[1], ci);
                        rec.N = double.Parse(tok[2], ci);
                        rec.E = double.Parse(tok[3], ci);
                        rec.Z = double.Parse(tok[4], ci);
                        break;

                    case "STN":
                        // STN pt HI ["desc"]
                        rec.Type = FbkRecordType.Stn;
                        rec.PointNumber = int.Parse(tok[1], ci);
                        rec.InstrumentHeight = tok.Length > 2
                            ? double.Parse(tok[2], ci) : 0.0;
                        break;

                    case "BS":
                        // BS pt [circle]
                        rec.Type = FbkRecordType.Bs;
                        rec.PointNumber = int.Parse(tok[1], ci);
                        rec.Circle = tok.Length > 2
                            ? double.Parse(tok[2], ci) : 0.0;
                        break;

                    case "AZ":
                        // AZ from to dms
                        rec.Type = FbkRecordType.Az;
                        rec.AzFrom = int.Parse(tok[1], ci);
                        rec.AzTo = int.Parse(tok[2], ci);
                        rec.AzimuthDms = double.Parse(tok[3], ci);
                        break;

                    case "PRISM":
                        // PRISM height
                        rec.Type = FbkRecordType.Prism;
                        rec.PrismHeight = double.Parse(tok[1], ci);
                        break;

                    case "F1":
                    case "F2":
                        // F1 [VA|ZA] pt ha sd va
                        rec.Type = kw == "F1" ? FbkRecordType.F1 : FbkRecordType.F2;
                        int idx = 1;
                        // Optional angle flavor token
                        if (tok.Length > 1 &&
                            (tok[1].Equals("VA", StringComparison.OrdinalIgnoreCase) ||
                             tok[1].Equals("ZA", StringComparison.OrdinalIgnoreCase) ||
                             tok[1].Equals("ZE", StringComparison.OrdinalIgnoreCase)))
                        {
                            rec.AngleFlavor = tok[1].ToUpperInvariant();
                            idx = 2;
                        }
                        rec.PointNumber = int.Parse(tok[idx++], ci);
                        rec.Ha = double.Parse(tok[idx++], ci);
                        rec.Sd = double.Parse(tok[idx++], ci);
                        rec.Va = double.Parse(tok[idx++], ci);
                        break;

                    default:
                        rec.Type = FbkRecordType.Unknown;
                        rec.Warning = $"Unrecognized keyword: {kw}";
                        break;
                }
            }
            catch (System.Exception ex)
            {
                rec.Warning = $"Parse error ({kw}): {ex.Message}";
            }
            return rec;
        }

        /// <summary>
        /// Parse a full FBK file into structured records + inferred setups.
        /// Pure — does NO COM calls.
        /// </summary>
        private static FbkParseResult ParseFbkFile(string fbkPath)
        {
            if (string.IsNullOrWhiteSpace(fbkPath))
                throw new ArgumentException("fbkPath is required");
            if (!System.IO.File.Exists(fbkPath))
                throw new System.IO.FileNotFoundException(
                    $"FBK file not found: {fbkPath}", fbkPath);

            var result = new FbkParseResult
            {
                FilePath = System.IO.Path.GetFullPath(fbkPath)
            };
            var lines = System.IO.File.ReadAllLines(fbkPath);
            result.TotalLines = lines.Length;

            double currentPrism = 0.0;
            FbkSetupBlock currentSetup = null;
            bool seenAnyStn = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var rec = ParseFbkLine(lines[i], i + 1);
                if (rec == null) continue;
                result.Records.Add(rec);

                if (!string.IsNullOrEmpty(rec.Warning))
                    result.Warnings.Add($"Line {rec.LineNumber}: {rec.Warning}");

                switch (rec.Type)
                {
                    case FbkRecordType.Nez:
                        // Pre-STN NEZ = control point. Post-STN NEZ would be
                        // a direct-entry non-control point (rare). We record
                        // everything in ControlPoints for import to try first.
                        result.ControlPoints.Add(rec);
                        break;

                    case FbkRecordType.Stn:
                        seenAnyStn = true;
                        // If current setup has zero observations, overwrite it
                        // rather than emit a dead setup (FieldGenius does this
                        // when the station is reconfirmed before any shots).
                        if (currentSetup != null && currentSetup.Observations.Count == 0)
                        {
                            currentSetup.StationPoint = rec.PointNumber ?? 0;
                            currentSetup.InstrumentHeight = rec.InstrumentHeight ?? 0;
                            currentSetup.Description = rec.Description;
                            currentSetup.BacksightPoint = null;
                            currentSetup.BacksightCircleDms = null;
                            currentSetup.AzimuthDms = null;
                            currentSetup.AzFrom = null;
                            currentSetup.AzTo = null;
                        }
                        else
                        {
                            currentSetup = new FbkSetupBlock
                            {
                                StationPoint = rec.PointNumber ?? 0,
                                InstrumentHeight = rec.InstrumentHeight ?? 0,
                                Description = rec.Description
                            };
                            result.Setups.Add(currentSetup);
                        }
                        break;

                    case FbkRecordType.Bs:
                        if (currentSetup == null)
                        {
                            result.Warnings.Add(
                                $"Line {rec.LineNumber}: BS before STN, ignored");
                            break;
                        }
                        currentSetup.BacksightPoint = rec.PointNumber;
                        currentSetup.BacksightCircleDms = rec.Circle;
                        break;

                    case FbkRecordType.Az:
                        if (currentSetup == null)
                        {
                            result.Warnings.Add(
                                $"Line {rec.LineNumber}: AZ before STN, ignored");
                            break;
                        }
                        currentSetup.AzimuthDms = rec.AzimuthDms;
                        currentSetup.AzFrom = rec.AzFrom;
                        currentSetup.AzTo = rec.AzTo;
                        break;

                    case FbkRecordType.Prism:
                        currentPrism = rec.PrismHeight ?? 0.0;
                        break;

                    case FbkRecordType.F1:
                    case FbkRecordType.F2:
                        if (currentSetup == null)
                        {
                            result.Warnings.Add(
                                $"Line {rec.LineNumber}: observation before STN, ignored");
                            break;
                        }
                        // Stamp the current prism height on the observation
                        // so import_fbk_parsed doesn't need to re-walk state.
                        if (rec.PrismHeight == null)
                            rec.PrismHeight = currentPrism;
                        currentSetup.Observations.Add(rec);
                        break;
                }
            }

            // If NEZ records appeared after setups began, demote them to
            // "floating NEZ" with a warning — we still try to create them.
            if (seenAnyStn)
            {
                // no-op marker; kept for future refinement
            }

            return result;
        }

        /// <summary>
        /// Safety valve: externally cancel whatever AutoCAD command the
        /// bridge is stuck on by queuing N escape characters. This uses a
        /// SEPARATE code path from regular handlers — it does NOT go
        /// through ExecuteInCommandContextAsync, because that's exactly
        /// what gets wedged when a modal dialog is open. Instead, it calls
        /// SendStringToExecute directly (which just queues a script and
        /// returns immediately, even when the main thread is busy).
        /// </summary>
        public static object CancelCommand(int? escCount)
        {
            int n = Math.Max(1, escCount ?? 5);
            string script = new string('\u001b', n);  // ESC characters
            try
            {
                Doc.SendStringToExecute(script, true, false, true);
                return new { queuedEscapes = n, ok = true };
            }
            catch (System.Exception ex)
            {
                return new { queuedEscapes = 0, ok = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Dry-run FBK parser: reads a .fbk file and returns the parsed
        /// records + inferred setups as JSON. Does NOT touch any COM
        /// object. Safe to call repeatedly while iterating on the parser.
        /// </summary>
        public static object ParseFbk(string fbkPath)
        {
            var r = ParseFbkFile(fbkPath);

            var records = new List<object>();
            foreach (var rec in r.Records)
            {
                records.Add(new
                {
                    line = rec.LineNumber,
                    type = rec.Type.ToString(),
                    pt = rec.PointNumber,
                    n = rec.N,
                    e = rec.E,
                    z = rec.Z,
                    hi = rec.InstrumentHeight,
                    circle = rec.Circle,
                    azFrom = rec.AzFrom,
                    azTo = rec.AzTo,
                    azDms = rec.AzimuthDms,
                    prismHeight = rec.PrismHeight,
                    angleFlavor = rec.AngleFlavor,
                    ha = rec.Ha,
                    sd = rec.Sd,
                    va = rec.Va,
                    desc = rec.Description,
                    warning = rec.Warning
                });
            }

            var setups = new List<object>();
            foreach (var s in r.Setups)
            {
                setups.Add(new
                {
                    stationPt = s.StationPoint,
                    instrumentHeight = s.InstrumentHeight,
                    description = s.Description,
                    backsightPt = s.BacksightPoint,
                    backsightCircleDms = s.BacksightCircleDms,
                    azimuthDms = s.AzimuthDms,
                    azFrom = s.AzFrom,
                    azTo = s.AzTo,
                    observationCount = s.Observations.Count,
                    observationLines = s.Observations.ConvertAll(o => o.LineNumber)
                });
            }

            var cps = new List<object>();
            foreach (var cp in r.ControlPoints)
            {
                cps.Add(new
                {
                    pt = cp.PointNumber,
                    n = cp.N, e = cp.E, z = cp.Z,
                    desc = cp.Description
                });
            }

            return new
            {
                filePath = r.FilePath,
                totalLines = r.TotalLines,
                recordCount = r.Records.Count,
                controlPointCount = r.ControlPoints.Count,
                setupCount = r.Setups.Count,
                observationCount = r.Setups.Sum(s => s.Observations.Count),
                warnings = r.Warnings,
                controlPoints = cps,
                setups = setups,
                records = records
            };
        }

        /// <summary>
        /// Real FBK import: parses the file, then drives the Civil 3D
        /// Survey COM API directly. Creates control points from pre-STN
        /// NEZ lines, then for each STN block creates a Setup, sets
        /// instrument height / backsight, and calls Observations.Create
        /// for every F1/F2 line. Observations on unknown point numbers
        /// cause Civil 3D to compute and add non-control points
        /// automatically.
        /// </summary>
        public static object ImportFbkParsed(
            string projectName,
            string networkName,
            string fbkPath,
            bool? dryRun,
            bool? stopOnError)
        {
            bool optDry = dryRun ?? false;
            bool optStop = stopOnError ?? false;

            var parsed = ParseFbkFile(fbkPath);

            var log = new List<string>();
            int cpCreated = 0, cpSkipped = 0;
            int setupCreated = 0;
            int obsCreated = 0, obsFailed = 0;
            var errors = new List<string>();

            AeccSurveyProject proj = null;
            AeccSurveyNetwork net = null;
            if (!optDry)
            {
                proj = ResolveSurveyProject(projectName);
                try { net = proj.Networks.FindItem(networkName); } catch { }
                if (net == null)
                {
                    for (int i = 0; i < proj.Networks.Count; i++)
                    {
                        var c = proj.Networks[i];
                        if (string.Equals(c.Name, networkName,
                            StringComparison.OrdinalIgnoreCase))
                        { net = c; break; }
                    }
                }
                if (net == null)
                    throw new InvalidOperationException(
                        $"Network '{networkName}' not found in project "
                        + $"'{proj.Name}'. Call create_survey_network first.");
            }

            // -------- 1) Control points --------
            var existingPts = new HashSet<int>();
            if (!optDry)
            {
                try
                {
                    for (int i = 0; i < net.ControlPoints.Count; i++)
                    {
                        var cp = net.ControlPoints[i];
                        try { existingPts.Add(cp.PointNumber); } catch { }
                    }
                }
                catch { /* empty collection */ }
            }

            foreach (var cp in parsed.ControlPoints)
            {
                int pt = cp.PointNumber ?? 0;
                if (pt <= 0) continue;

                if (existingPts.Contains(pt))
                {
                    cpSkipped++;
                    log.Add($"CP {pt}: already exists, skipped");
                    continue;
                }

                if (optDry)
                {
                    cpCreated++;
                    log.Add($"CP {pt}: (dry) would create at E={cp.E} N={cp.N} Z={cp.Z}");
                    continue;
                }

                try
                {
                    // NEZ -> Create(pt, name, desc, E, N, Z)
                    net.ControlPoints.Create(
                        pt,
                        cp.Description ?? ("PT" + pt),
                        cp.Description ?? "",
                        cp.E ?? 0,
                        cp.N ?? 0,
                        cp.Z ?? 0);
                    cpCreated++;
                    existingPts.Add(pt);
                    log.Add($"CP {pt}: created");
                }
                catch (System.Exception ex)
                {
                    string msg = $"CP {pt} create failed: {ex.Message}";
                    errors.Add(msg);
                    log.Add(msg);
                    if (optStop) goto DONE;
                }
            }

            // -------- 2) Setups + observations --------
            foreach (var s in parsed.Setups)
            {
                AeccSurveySetup comSetup = null;
                if (optDry)
                {
                    setupCreated++;
                    log.Add($"SETUP on {s.StationPoint} (HI={s.InstrumentHeight}, "
                          + $"BS={s.BacksightPoint}): (dry) would create "
                          + $"{s.Observations.Count} obs");
                }
                else
                {
                    try
                    {
                        comSetup = net.Setups.Create(
                            s.StationPoint,
                            s.BacksightPoint ?? 0);
                        try { comSetup.InstrumentHeight = s.InstrumentHeight; }
                        catch (System.Exception ex)
                        { errors.Add($"setup {s.StationPoint} HI: {ex.Message}"); }

                        if (s.BacksightCircleDms.HasValue)
                        {
                            try
                            {
                                comSetup.BacksightFace1 =
                                    DmsPackedToRadians(s.BacksightCircleDms.Value);
                            }
                            catch (System.Exception ex)
                            { errors.Add($"setup {s.StationPoint} BS circle: {ex.Message}"); }
                        }

                        if (s.AzimuthDms.HasValue)
                        {
                            try
                            {
                                comSetup.BacksightOrientation =
                                    DmsPackedToRadians(s.AzimuthDms.Value);
                            }
                            catch (System.Exception ex)
                            { errors.Add($"setup {s.StationPoint} BS orient: {ex.Message}"); }
                        }

                        setupCreated++;
                        log.Add($"SETUP on {s.StationPoint} created (HI={s.InstrumentHeight}, "
                              + $"BS={s.BacksightPoint})");
                    }
                    catch (System.Exception ex)
                    {
                        string msg = $"setup {s.StationPoint} create failed: {ex.Message}";
                        errors.Add(msg);
                        log.Add(msg);
                        if (optStop) goto DONE;
                        continue;
                    }
                }

                foreach (var obs in s.Observations)
                {
                    if (optDry)
                    {
                        obsCreated++;
                        continue;
                    }
                    try
                    {
                        double haRad = DmsPackedToRadians(obs.Ha ?? 0);
                        double vaRad = DmsPackedToRadians(obs.Va ?? 0);
                        double sd = obs.Sd ?? 0;
                        double ph = obs.PrismHeight ?? 0;

                        var angleType = obs.Type == FbkRecordType.F1
                            ? AeccSurveyAngleType.aeccSurveyAngleTypeFace1Angle
                            : AeccSurveyAngleType.aeccSurveyAngleTypeFace2Angle;

                        comSetup.Observations.Create(
                            obs.PointNumber ?? 0,
                            angleType,
                            haRad,
                            AeccSurveyDistanceType.aeccSurveyDistanceTypeSlope,
                            sd,
                            AeccSurveyVerticalType.aeccSurveyVerticalTypeVerticalAngle,
                            vaRad,
                            AeccSurveyTargetType.aeccSurveyTargetTypePrism,
                            ph,
                            obs.Description ?? "");
                        obsCreated++;
                    }
                    catch (System.Exception ex)
                    {
                        obsFailed++;
                        string msg = $"OBS line {obs.LineNumber} pt {obs.PointNumber} failed: {ex.Message}";
                        errors.Add(msg);
                        if (optStop) goto DONE;
                    }
                }
            }

        DONE:
            if (!optDry && net != null)
            {
                try { net.Save(); } catch { }
            }

            return new
            {
                projectName = optDry ? projectName : proj.Name,
                networkName = optDry ? networkName : net.Name,
                filePath = parsed.FilePath,
                dryRun = optDry,
                parsed = new
                {
                    totalLines = parsed.TotalLines,
                    recordCount = parsed.Records.Count,
                    controlPointCount = parsed.ControlPoints.Count,
                    setupCount = parsed.Setups.Count,
                    observationCount = parsed.Setups.Sum(s => s.Observations.Count),
                    parseWarnings = parsed.Warnings
                },
                imported = new
                {
                    controlPointsCreated = cpCreated,
                    controlPointsSkipped = cpSkipped,
                    setupsCreated = setupCreated,
                    observationsCreated = obsCreated,
                    observationsFailed = obsFailed
                },
                errors = errors,
                log = log
            };
        }

        // =====================================================================
        // SURVEY READ-SIDE HANDLERS — enumerate what's actually in the network
        // so we can verify FBK imports against the underlying COM data. Uses
        // dynamic property access with SafeGet so minor Civil 3D version
        // differences in AECC.Interop.Survey don't blow up enumeration.
        // =====================================================================

        private static AeccSurveyNetwork ResolveSurveyNetwork(
            string projectName, string networkName)
        {
            if (string.IsNullOrWhiteSpace(networkName))
                throw new ArgumentException("networkName is required");
            var proj = ResolveSurveyProject(projectName);
            AeccSurveyNetwork net = null;
            try { net = proj.Networks.FindItem(networkName); } catch { }
            if (net == null)
            {
                for (int i = 0; i < proj.Networks.Count; i++)
                {
                    var c = proj.Networks[i];
                    if (string.Equals(c.Name, networkName,
                        StringComparison.OrdinalIgnoreCase))
                    { net = c; break; }
                }
            }
            if (net == null)
                throw new InvalidOperationException(
                    $"Network '{networkName}' not found in project '{proj.Name}'.");
            return net;
        }

        public static object ListSurveyControlPoints(
            string projectName, string networkName)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            var list = new List<object>();
            int count = SafeGetInt(() => net.ControlPoints.Count, 0);
            for (int i = 0; i < count; i++)
            {
                var cp = net.ControlPoints[i];
                list.Add(new
                {
                    index = i,
                    number  = SafeGet(() => (int?)cp.PointNumber),
                    name    = SafeGet<string>(() => cp.Name),
                    description = SafeGet<string>(() => cp.Description),
                    easting = SafeGet(() => (double?)cp.Easting),
                    northing = SafeGet(() => (double?)cp.Northing),
                    elevation = SafeGet(() => (double?)cp.Elevation),
                    isAdjusted = SafeGet(() => (bool?)cp.IsAdjusted)
                });
            }
            return new
            {
                networkName = net.Name,
                count = count,
                controlPoints = list
            };
        }

        public static object ListSurveyNonControlPoints(
            string projectName, string networkName)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            var list = new List<object>();
            int count = SafeGetInt(() => net.NonControlPoints.Count, 0);
            for (int i = 0; i < count; i++)
            {
                var p = net.NonControlPoints[i];
                list.Add(new
                {
                    index = i,
                    number  = SafeGet(() => (int?)p.PointNumber),
                    name    = SafeGet<string>(() => p.Name),
                    description = SafeGet<string>(() => p.Description),
                    easting = SafeGet(() => (double?)p.Easting),
                    northing = SafeGet(() => (double?)p.Northing),
                    elevation = SafeGet(() => (double?)p.Elevation)
                });
            }
            return new
            {
                networkName = net.Name,
                count = count,
                nonControlPoints = list
            };
        }

        public static object ListSurveySetups(
            string projectName, string networkName)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            var list = new List<object>();
            int count = SafeGetInt(() => net.Setups.Count, 0);
            for (int i = 0; i < count; i++)
            {
                var s = net.Setups[i];
                int obsCount = SafeGetInt(() => s.Observations.Count, 0);
                list.Add(new
                {
                    index = i,
                    name = SafeGet<string>(() => s.Name),
                    stationPointNumber = SafeGet(() => (int?)s.StationPointNumber),
                    backsightPointNumber = SafeGet(() => (int?)s.BacksightPointNumber),
                    instrumentHeight = SafeGet(() => (double?)s.InstrumentHeight),
                    instrumentElevation = SafeGet(() => (double?)s.InstrumentElevation),
                    easting = SafeGet(() => (double?)s.Easting),
                    northing = SafeGet(() => (double?)s.Northing),
                    elevation = SafeGet(() => (double?)s.Elevation),
                    backsightDirectionRad = SafeGet(() => (double?)s.BacksightDirection),
                    backsightOrientationRad = SafeGet(() => (double?)s.BacksightOrientation),
                    backsightFace1Rad = SafeGet(() => (double?)s.BacksightFace1),
                    backsightFace2Rad = SafeGet(() => (double?)s.BacksightFace2),
                    azimuthFlag = SafeGet(() => (bool?)s.AzimuthFlag),
                    userAzimuth = SafeGet(() => (bool?)s.UserAzimuth),
                    observationCount = obsCount
                });
            }
            return new
            {
                networkName = net.Name,
                count = count,
                setups = list
            };
        }

        public static object ListSurveyObservations(
            string projectName, string networkName,
            int? setupIndex, int? stationPoint)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            int setupCount = SafeGetInt(() => net.Setups.Count, 0);
            var setupsOut = new List<object>();

            for (int i = 0; i < setupCount; i++)
            {
                var s = net.Setups[i];

                if (setupIndex.HasValue && i != setupIndex.Value) continue;

                if (stationPoint.HasValue)
                {
                    int sp = SafeGetInt(() => s.StationPointNumber, -1);
                    if (sp != stationPoint.Value) continue;
                }

                var obsList = new List<object>();
                int obsCount = SafeGetInt(() => s.Observations.Count, 0);
                for (int j = 0; j < obsCount; j++)
                {
                    var o = s.Observations[j];
                    obsList.Add(new
                    {
                        index = j,
                        pointNumber = SafeGet(() => (int?)o.PointNumber),
                        description = SafeGet<string>(() => o.Description),
                        angleRad = SafeGet(() => (double?)o.Angle),
                        angleType = SafeGet(() => (int)o.AngleType),
                        distance = SafeGet(() => (double?)o.Distance),
                        distanceType = SafeGet(() => (int)o.DistanceType),
                        verticalRad = SafeGet(() => (double?)o.Vertical),
                        verticalType = SafeGet(() => (int)o.VerticalType),
                        targetHeight = SafeGet(() => (double?)o.TargetHeight),
                        targetType = SafeGet(() => (int)o.TargetType),
                        northing = SafeGet(() => (double?)o.Northing),
                        easting = SafeGet(() => (double?)o.Easting),
                        elevation = SafeGet(() => (double?)o.Elevation)
                    });
                }

                setupsOut.Add(new
                {
                    setupIndex = i,
                    stationPointNumber = SafeGet(() => (int?)s.StationPointNumber),
                    backsightPointNumber = SafeGet(() => (int?)s.BacksightPointNumber),
                    observationCount = obsCount,
                    observations = obsList
                });
            }

            return new
            {
                networkName = net.Name,
                matchedSetups = setupsOut.Count,
                setups = setupsOut
            };
        }

        /// <summary>
        /// Get the active document's COM AcadDatabase object, needed as an arg
        /// to AeccSurveyNetwork methods like AddPointsToDrawing / IsInDrawing.
        /// </summary>
        private static Autodesk.AutoCAD.Interop.Common.AcadDatabase GetComActiveDatabase()
        {
            var acadApp = (Autodesk.AutoCAD.Interop.AcadApplication)
                Application.AcadApplication;
            return (Autodesk.AutoCAD.Interop.Common.AcadDatabase)
                acadApp.ActiveDocument.Database;
        }

        public static object SurveyNetworkSummary(
            string projectName, string networkName)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            int cpCount = SafeGetInt(() => net.ControlPoints.Count, 0);
            int ncpCount = SafeGetInt(() => net.NonControlPoints.Count, 0);
            int setupCount = SafeGetInt(() => net.Setups.Count, 0);
            int totalObs = 0;
            for (int i = 0; i < setupCount; i++)
            {
                try { totalObs += net.Setups[i].Observations.Count; }
                catch { }
            }
            bool isIn = false;
            try
            {
                var comDb = GetComActiveDatabase();
                isIn = net.IsInDrawing(comDb);
            }
            catch { }
            return new
            {
                networkName = net.Name,
                description = SafeGet<string>(() => net.Description),
                path = SafeGet<string>(() => net.Path),
                needsUpdate = SafeGet(() => (bool?)net.NeedsUpdate),
                isInDrawing = isIn,
                controlPointCount = cpCount,
                nonControlPointCount = ncpCount,
                setupCount = setupCount,
                totalObservationCount = totalObs
            };
        }

        /// <summary>
        /// Materialize a survey network's points (and optionally observations /
        /// figures) into the active drawing. Uses the COM network's built-in
        /// <c>AddPointsToDrawing()</c> or <c>AddToDrawing()</c> method directly —
        /// much cleaner than walking the collections and re-creating CogoPoints
        /// manually, and it respects the project's drawing settings.
        /// </summary>
        public static object AddSurveyPointsToDrawing(
            string projectName, string networkName,
            bool? includeControl, bool? includeNonControl)
        {
            // includeControl / includeNonControl are advisory for future
            // selective materialization; AddPointsToDrawing adds all points
            // associated with the network.
            var net = ResolveSurveyNetwork(projectName, networkName);

            var comDb = GetComActiveDatabase();

            int cogoBefore;
            int cogoAfter;
            bool wasInDrawingBefore;
            try { wasInDrawingBefore = net.IsInDrawing(comDb); }
            catch { wasInDrawingBefore = false; }

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                cogoBefore = 0;
                foreach (ObjectId id in CivDoc.CogoPoints) cogoBefore++;
                tr.Commit();
            }

            string method;
            long addResult = 0;
            try
            {
                net.AddPointsToDrawing(comDb);
                method = "AddPointsToDrawing";
            }
            catch (System.Exception exPts)
            {
                try
                {
                    addResult = net.AddToDrawing(comDb);
                    method = "AddToDrawing";
                }
                catch (System.Exception exAll)
                {
                    throw new InvalidOperationException(
                        $"Both AddPointsToDrawing and AddToDrawing failed. "
                        + $"Pts: {exPts.Message}. All: {exAll.Message}");
                }
            }

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                cogoAfter = 0;
                foreach (ObjectId id in CivDoc.CogoPoints) cogoAfter++;
                tr.Commit();
            }

            bool isInDrawingNow = false;
            try { isInDrawingNow = net.IsInDrawing(comDb); } catch { }

            return new
            {
                networkName = net.Name,
                method = method,
                addToDrawingResult = addResult,
                wasInDrawingBefore = wasInDrawingBefore,
                isInDrawingNow = isInDrawingNow,
                cogoPointsBefore = cogoBefore,
                cogoPointsAfter = cogoAfter,
                cogoPointsAdded = cogoAfter - cogoBefore
            };
        }

        // =====================================================================
        // DIAGNOSTIC & REPAIR HANDLERS
        // Investigate why Setup.InstrumentHeight / BacksightOrientation were
        // silently swallowed during FBK import, and provide a fix path to
        // reapply them without re-running the whole import.
        // =====================================================================

        /// <summary>
        /// Diagnostic: for a single setup, read HI/BF1/BS-orientation, attempt
        /// to write them, read immediately, call setup.Save(), read again,
        /// call net.Save(), read again. Captures any thrown exceptions. Used
        /// to locate the exact step where the silent failure occurs.
        /// </summary>
        public static object DebugSetupParamSet(
            string projectName, string networkName,
            int setupIndex, double testHi,
            double? testBsOrient, double? testBsFace1)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            int count = SafeGetInt(() => net.Setups.Count, 0);
            if (setupIndex < 0 || setupIndex >= count)
                throw new ArgumentOutOfRangeException(
                    $"setupIndex {setupIndex} out of range (0..{count - 1})");

            var s = net.Setups[setupIndex];
            var log = new List<object>();

            double Read(string label)
            {
                double hi = double.NaN, bf1 = double.NaN, bo = double.NaN;
                try { hi = s.InstrumentHeight; } catch { }
                try { bf1 = s.BacksightFace1; } catch { }
                try { bo  = s.BacksightOrientation; } catch { }
                log.Add(new {
                    stage = label,
                    instrumentHeight = hi,
                    backsightFace1 = bf1,
                    backsightOrientation = bo
                });
                return hi;
            }

            Read("initial");

            // --- Try setting HI ---
            string hiSetError = null;
            try { s.InstrumentHeight = testHi; }
            catch (System.Exception ex) { hiSetError = ex.GetType().Name + ": " + ex.Message; }
            Read("after set HI (pre-save)");

            // --- Try setting BacksightFace1 ---
            string bf1SetError = null;
            if (testBsFace1.HasValue)
            {
                try { s.BacksightFace1 = testBsFace1.Value; }
                catch (System.Exception ex) { bf1SetError = ex.GetType().Name + ": " + ex.Message; }
                Read("after set BacksightFace1");
            }

            // --- Try setting BacksightOrientation ---
            string boSetError = null;
            if (testBsOrient.HasValue)
            {
                try { s.BacksightOrientation = testBsOrient.Value; }
                catch (System.Exception ex) { boSetError = ex.GetType().Name + ": " + ex.Message; }
                Read("after set BacksightOrientation");
            }

            // --- Setup-level save ---
            string setupSaveError = null;
            try { s.Save(); }
            catch (System.Exception ex) { setupSaveError = ex.GetType().Name + ": " + ex.Message; }
            Read("after setup.Save()");

            // --- Network-level save ---
            string netSaveError = null;
            try { net.Save(); }
            catch (System.Exception ex) { netSaveError = ex.GetType().Name + ": " + ex.Message; }
            Read("after net.Save()");

            // --- Setup-level reload (forces re-read from SDB) ---
            string reloadError = null;
            try { s.Reload(); }
            catch (System.Exception ex) { reloadError = ex.GetType().Name + ": " + ex.Message; }
            Read("after setup.Reload()");

            return new
            {
                networkName = net.Name,
                setupIndex = setupIndex,
                stationPointNumber = SafeGetInt(() => s.StationPointNumber, -1),
                testHi = testHi,
                testBsOrient = testBsOrient,
                testBsFace1 = testBsFace1,
                errors = new
                {
                    hiSet = hiSetError,
                    bf1Set = bf1SetError,
                    boSet = boSetError,
                    setupSave = setupSaveError,
                    netSave = netSaveError,
                    setupReload = reloadError
                },
                log = log
            };
        }

        /// <summary>
        /// Fix: walk a network's setups and reapply HI / BacksightFace1 /
        /// BacksightOrientation from a freshly-parsed FBK file, keyed by
        /// station point number. Useful when the original import silently
        /// dropped these values.
        /// </summary>
        public static object FixSurveySetups(
            string projectName, string networkName,
            string fbkPath, bool? updateNetwork)
        {
            if (string.IsNullOrWhiteSpace(fbkPath))
                throw new ArgumentException("fbkPath is required");
            if (!System.IO.File.Exists(fbkPath))
                throw new System.IO.FileNotFoundException(
                    $"FBK file not found: {fbkPath}", fbkPath);

            var parsed = ParseFbkFile(fbkPath);

            // Key parsed setups by (station, backsight) to disambiguate
            // multiple setups on the same station.
            var byKey = new Dictionary<string, FbkSetupBlock>(parsed.Setups.Count);
            foreach (var ps in parsed.Setups)
            {
                string k = ps.StationPoint + "/" + (ps.BacksightPoint ?? 0);
                if (!byKey.ContainsKey(k)) byKey[k] = ps;
            }

            var net = ResolveSurveyNetwork(projectName, networkName);
            int setupCount = SafeGetInt(() => net.Setups.Count, 0);
            var log = new List<object>();
            var errors = new List<string>();
            int updated = 0;

            for (int i = 0; i < setupCount; i++)
            {
                var s = net.Setups[i];
                int sp = SafeGetInt(() => s.StationPointNumber, -1);
                int bp = SafeGetInt(() => s.BacksightPointNumber, -1);
                string k = sp + "/" + bp;

                if (!byKey.TryGetValue(k, out var ps))
                {
                    log.Add(new { setupIndex = i, stationPointNumber = sp, match = "NO_MATCH" });
                    continue;
                }

                double beforeHi  = double.NaN, beforeBF1 = double.NaN, beforeBO = double.NaN;
                try { beforeHi  = s.InstrumentHeight; } catch { }
                try { beforeBF1 = s.BacksightFace1; } catch { }
                try { beforeBO  = s.BacksightOrientation; } catch { }

                // --- Apply HI ---
                try
                {
                    s.InstrumentHeight = ps.InstrumentHeight;
                }
                catch (System.Exception ex)
                {
                    errors.Add($"setup[{i}] HI set: {ex.Message}");
                }

                // --- Apply BacksightFace1 ---
                if (ps.BacksightCircleDms.HasValue)
                {
                    try
                    {
                        s.BacksightFace1 =
                            DmsPackedToRadians(ps.BacksightCircleDms.Value);
                    }
                    catch (System.Exception ex)
                    {
                        errors.Add($"setup[{i}] BF1 set: {ex.Message}");
                    }
                }

                // --- Apply BacksightOrientation ---
                if (ps.AzimuthDms.HasValue)
                {
                    try
                    {
                        s.BacksightOrientation =
                            DmsPackedToRadians(ps.AzimuthDms.Value);
                    }
                    catch (System.Exception ex)
                    {
                        errors.Add($"setup[{i}] BO set: {ex.Message}");
                    }
                }

                // --- Save this setup individually ---
                try { s.Save(); }
                catch (System.Exception ex)
                {
                    errors.Add($"setup[{i}] save: {ex.Message}");
                }

                double afterHi  = double.NaN, afterBF1 = double.NaN, afterBO = double.NaN;
                try { afterHi  = s.InstrumentHeight; } catch { }
                try { afterBF1 = s.BacksightFace1; } catch { }
                try { afterBO  = s.BacksightOrientation; } catch { }

                log.Add(new
                {
                    setupIndex = i,
                    stationPointNumber = sp,
                    backsightPointNumber = bp,
                    targetHi = ps.InstrumentHeight,
                    beforeHi = beforeHi,
                    afterHi  = afterHi,
                    beforeBF1 = beforeBF1,
                    afterBF1  = afterBF1,
                    beforeBO  = beforeBO,
                    afterBO   = afterBO,
                    hiStuck = Math.Abs(afterHi - ps.InstrumentHeight) < 1e-9
                });
                updated++;
            }

            // --- Network-level save ---
            try { net.Save(); }
            catch (System.Exception ex)
            { errors.Add("net.Save: " + ex.Message); }

            bool updateRan = false;
            string updateError = null;
            if (updateNetwork ?? true)
            {
                try
                {
                    var comDb = GetComActiveDatabase();
                    net.UpdateNetwork(comDb);
                    updateRan = true;
                }
                catch (System.Exception ex)
                { updateError = ex.GetType().Name + ": " + ex.Message; }
            }

            return new
            {
                networkName = net.Name,
                setupsMatched = updated,
                updateNetworkRan = updateRan,
                updateNetworkError = updateError,
                errors = errors,
                log = log
            };
        }

        /// <summary>
        /// Call <c>IAeccSurveyNetwork.UpdateNetwork</c> to force re-reduction
        /// of all observations and re-compute of derived points / elevations.
        /// </summary>
        public static object UpdateSurveyNetwork(
            string projectName, string networkName)
        {
            var net = ResolveSurveyNetwork(projectName, networkName);
            var comDb = GetComActiveDatabase();
            try { net.UpdateNetwork(comDb); }
            catch (System.Exception ex)
            {
                return new { ok = false, error = ex.GetType().Name + ": " + ex.Message };
            }
            try { net.Save(); } catch { }
            return new { ok = true, networkName = net.Name };
        }

        // =====================================================================
        // DIALOG INSPECTION / DISMISSAL
        // =====================================================================

        /// <summary>
        /// Get the Civil 3D main window handle for DialogProbe. Falls back
        /// to Process.MainWindowHandle if Application.MainWindow is unavailable.
        /// Never throws — callers tolerate IntPtr.Zero.
        /// </summary>
        internal static IntPtr GetCadMainHwnd()
        {
            try
            {
                var w = Application.MainWindow;
                if (w != null) return w.Handle;
            }
            catch { }
            try { return Process.GetCurrentProcess().MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// Enumerate currently-open modal dialogs in the Civil 3D process.
        /// Empty list means nothing is blocking. See DialogProbe for filter
        /// rules (PID-strict, main-window-disabled signal).
        /// </summary>
        public static object ListDialogs()
        {
            var dialogs = DialogProbe.List(GetCadMainHwnd());
            return new { count = dialogs.Count, dialogs = dialogs };
        }

        /// <summary>
        /// Click a button on an open dialog. Params: hwnd (long, optional —
        /// 0/null = topmost dialog), button (string, required — case-insensitive
        /// match against button text with '&amp;' accelerators stripped).
        /// </summary>
        public static object DismissDialog(JObject p)
        {
            long hwnd = p["hwnd"]?.Value<long?>() ?? 0L;
            string buttonText = p["button"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(buttonText))
                throw new ArgumentException("button is required");
            return DialogProbe.Dismiss(GetCadMainHwnd(), hwnd, buttonText);
        }

        private static T SafeGet<T>(Func<T> getter)
        {
            try { return getter(); } catch { return default(T); }
        }

        private static int SafeGetInt(Func<int> getter, int fallback)
        {
            try { return getter(); } catch { return fallback; }
        }

        // ----- Helpers -----

        private static Point3d ParsePoint3d(JToken token)
        {
            if (token is JArray arr)
            {
                double x = arr[0].Value<double>();
                double y = arr[1].Value<double>();
                double z = arr.Count > 2 ? arr[2].Value<double>() : 0;
                return new Point3d(x, y, z);
            }
            if (token is JObject obj)
            {
                return new Point3d(
                    obj["x"]?.Value<double>() ?? obj["easting"]?.Value<double>() ?? 0,
                    obj["y"]?.Value<double>() ?? obj["northing"]?.Value<double>() ?? 0,
                    obj["z"]?.Value<double>() ?? obj["elevation"]?.Value<double>() ?? 0
                );
            }
            throw new ArgumentException("Cannot parse point from: " + token?.ToString());
        }

        private static Arc CreateArcFrom3Points(Point3d p1, Point3d p2, Point3d p3)
        {
            // Circumscribed circle calculation from 3 points
            using (var cArc = new CircularArc3d(p1, p2, p3))
            {
                var center = cArc.Center;
                var radius = cArc.Radius;
                var normal = cArc.Normal;

                // Calculate angles
                double startAngle = Math.Atan2(
                    p1.Y - center.Y, p1.X - center.X);
                double endAngle = Math.Atan2(
                    p3.Y - center.Y, p3.X - center.X);

                return new Arc(center, normal, radius, startAngle, endAngle);
            }
        }

        // =====================================================================
        // v19 NEW HANDLERS — added 2026-05-12
        //   Survey User Settings, Figure Prefix DBs, Figures (list/props/elev),
        //   PNEZD-to-SDB import.
        // =====================================================================

        /// <summary>
        /// Resolves the surveyDoc COM root object using the same multi-strategy
        /// resolution as GetSurveyProjects but returns the raw IDispatch object
        /// instead of casting to a specific child collection. Used by handlers
        /// that need to walk other properties (UserSettings, FigurePrefixDatabases,
        /// FigureStyles, ...).
        /// </summary>
        private static object GetSurveyDocumentRoot()
        {
            object acadAppObj = Application.AcadApplication;
            if (acadAppObj == null)
                throw new InvalidOperationException(
                    "Application.AcadApplication is null. Is Civil 3D fully initialized?");

            var tried = new List<string>();
            object surveyDoc = ResolveComObject(acadAppObj, _surveyDocProgIds, tried);
            if (surveyDoc == null)
            {
                var aeccAppProgIds = new[]
                {
                    "AeccXUiLand.AeccApplication.13.8",
                    "AeccXUiLand.AeccApplication.13.7",
                    "AeccXUiLand.AeccApplication.13.6",
                    "AeccXUiLand.AeccApplication.13.5",
                };
                object aeccApp = ResolveComObject(acadAppObj, aeccAppProgIds, tried);
                if (aeccApp != null)
                    surveyDoc = ResolveComObject(aeccApp, _surveyDocProgIds, tried);
            }
            if (surveyDoc == null)
                throw new InvalidOperationException(
                    "Could not obtain IAeccSurveyDocument via any strategy. Trail: "
                    + string.Join("; ", tried));

            try
            {
                object activeAcadDoc = GetComProperty(acadAppObj, "ActiveDocument");
                if (activeAcadDoc != null)
                    InvokeCom(surveyDoc, "Init", activeAcadDoc);
            }
            catch
            {
                // Already initialized OR no active document; downstream
                // property walks will produce a more specific error if needed.
            }

            return surveyDoc;
        }

        // -------------------- Survey User Settings --------------------

        public static object GetSurveyUserSettingsInfo()
        {
            object surveyDoc = GetSurveyDocumentRoot();
            object us = GetComProperty(surveyDoc, "UserSettings");
            if (us == null)
                throw new InvalidOperationException(
                    "surveyDoc.UserSettings returned null. Doc type: "
                    + surveyDoc.GetType().FullName);

            object Read(string prop)
            {
                try { return GetComProperty(us, prop); }
                catch { return null; }
            }

            return new
            {
                figurePrefixDatabasePath = Read("FigurePrefixDatabasePath") as string,
                equipmentDatabasePath = Read("EquipmentDatabasePath") as string,
                currentFigurePrefixDatabase = Read("CurrentFigurePrefixDatabase") as string,
                currentEquipmentDatabase = Read("CurrentEquipmentDatabase") as string,
                defaultFigureLayer = Read("DefaultFigureLayer") as string,
                defaultFigureStyle = Read("DefaultFigureStyle") as string,
                insertFigureObjects = Read("InsertFigureObjects"),
                insertCivil3DPoints = Read("InsertCivil3DPoints"),
                insertNetworkObject = Read("InsertNetworkObject"),
                siteForLotlineFigures = Read("SiteForLotlineFigures") as string,
            };
        }

        public static object SetSurveyUserSettingsInfo(
            string figurePrefixDatabasePath,
            string equipmentDatabasePath,
            string currentFigurePrefixDatabase,
            string currentEquipmentDatabase,
            string defaultFigureLayer,
            string defaultFigureStyle,
            bool? insertFigureObjects,
            bool? insertCivil3DPoints,
            bool? insertNetworkObject)
        {
            object surveyDoc = GetSurveyDocumentRoot();
            object us = GetComProperty(surveyDoc, "UserSettings");
            if (us == null)
                throw new InvalidOperationException("surveyDoc.UserSettings returned null.");

            var applied = new List<string>();
            void TrySet(string prop, object val)
            {
                if (val == null) return;
                if (val is string s && string.IsNullOrEmpty(s)) return;
                try { SetComProperty(us, prop, val); applied.Add(prop); }
                catch (System.Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    applied.Add($"{prop}:ERR({inner.GetType().Name}: {inner.Message})");
                }
            }

            TrySet("FigurePrefixDatabasePath", figurePrefixDatabasePath);
            TrySet("EquipmentDatabasePath", equipmentDatabasePath);
            TrySet("CurrentFigurePrefixDatabase", currentFigurePrefixDatabase);
            TrySet("CurrentEquipmentDatabase", currentEquipmentDatabase);
            TrySet("DefaultFigureLayer", defaultFigureLayer);
            TrySet("DefaultFigureStyle", defaultFigureStyle);
            TrySet("InsertFigureObjects", insertFigureObjects);
            TrySet("InsertCivil3DPoints", insertCivil3DPoints);
            TrySet("InsertNetworkObject", insertNetworkObject);

            try { InvokeCom(us, "_InternalSaveBack"); } catch { /* best effort */ }

            return new { applied, current = GetSurveyUserSettingsInfo() };
        }

        // -------------------- Figure Prefix Databases --------------------

        private static object GetFigurePrefixDatabasesRaw()
        {
            object surveyDoc = GetSurveyDocumentRoot();
            object dbs = GetComProperty(surveyDoc, "FigurePrefixDatabases");
            if (dbs == null)
                throw new InvalidOperationException(
                    "surveyDoc.FigurePrefixDatabases returned null.");
            return dbs;
        }

        public static object ListFigurePrefixDbs()
        {
            object dbs = GetFigurePrefixDatabasesRaw();
            int count = Convert.ToInt32(GetComProperty(dbs, "Count"));
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                object db = InvokeCom(dbs, "get_Item", i);
                int entryCount = 0;
                try { entryCount = Convert.ToInt32(GetComProperty(db, "Count")); } catch { }
                list.Add(new
                {
                    name = GetComProperty(db, "Name") as string,
                    path = GetComProperty(db, "Path") as string,
                    entryCount,
                });
            }
            return new { count, databases = list };
        }

        public static object CreateFigurePrefixDb(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");
            object dbs = GetFigurePrefixDatabasesRaw();

            object existing = null;
            try { existing = InvokeCom(dbs, "FindItem", name); }
            catch { /* not found */ }

            object db = existing ?? InvokeCom(dbs, "Create", name);
            return new
            {
                name = GetComProperty(db, "Name") as string,
                path = GetComProperty(db, "Path") as string,
                preExisting = existing != null,
            };
        }

        public static object ListFigurePrefixDbEntries(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("dbName is required");
            object dbs = GetFigurePrefixDatabasesRaw();
            object db = null;
            try { db = InvokeCom(dbs, "FindItem", dbName); }
            catch
            {
                throw new InvalidOperationException(
                    $"Figure Prefix Database '{dbName}' not found.");
            }
            int count = Convert.ToInt32(GetComProperty(db, "Count"));
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                object entry = InvokeCom(db, "get_Item", i);
                list.Add(new
                {
                    name = GetComProperty(entry, "Name") as string,
                    isBreakline = SafeBool(() => Convert.ToBoolean(GetComProperty(entry, "IsBreakline"))),
                    isLotLine = SafeBool(() => Convert.ToBoolean(GetComProperty(entry, "IsLotLine"))),
                    layer = GetComProperty(entry, "Layer") as string,
                    style = GetComProperty(entry, "Style") as string,
                    site = GetComProperty(entry, "Site") as string,
                });
            }
            return new { dbName, count, entries = list };
        }

        public static object AddFigurePrefixDbEntry(
            string dbName,
            string prefix,
            bool? isBreakline,
            bool? isLotLine,
            string layer,
            string style,
            string site)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("dbName is required");
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("prefix is required");

            object dbs = GetFigurePrefixDatabasesRaw();
            object db = null;
            try { db = InvokeCom(dbs, "FindItem", dbName); }
            catch
            {
                throw new InvalidOperationException(
                    $"Figure Prefix Database '{dbName}' not found. "
                    + "Call create_figure_prefix_db first.");
            }

            object entry = null;
            try { entry = InvokeCom(db, "FindItem", prefix); }
            catch { /* not found */ }

            bool preExisting = entry != null;
            if (entry == null)
                entry = InvokeCom(db, "Create", prefix);

            if (isBreakline.HasValue)
                try { SetComProperty(entry, "IsBreakline", isBreakline.Value); } catch { }
            if (isLotLine.HasValue)
                try { SetComProperty(entry, "IsLotLine", isLotLine.Value); } catch { }
            if (!string.IsNullOrEmpty(layer))
                try { SetComProperty(entry, "Layer", layer); } catch { }
            if (!string.IsNullOrEmpty(style))
                try { SetComProperty(entry, "Style", style); } catch { }
            if (!string.IsNullOrEmpty(site))
                try { SetComProperty(entry, "Site", site); } catch { }

            try { InvokeCom(entry, "Save"); } catch { /* may be auto-saved */ }

            return new
            {
                dbName,
                prefix = GetComProperty(entry, "Name") as string,
                isBreakline = SafeBool(() => Convert.ToBoolean(GetComProperty(entry, "IsBreakline"))),
                isLotLine = SafeBool(() => Convert.ToBoolean(GetComProperty(entry, "IsLotLine"))),
                layer = GetComProperty(entry, "Layer") as string,
                style = GetComProperty(entry, "Style") as string,
                site = GetComProperty(entry, "Site") as string,
                preExisting,
            };
        }

        // -------------------- Figures (list / properties / elevations) ----

        private static AeccSurveyFigure FindFigureByName(AeccSurveyProject proj, string figureName)
        {
            if (string.IsNullOrWhiteSpace(figureName))
                throw new ArgumentException("figureName is required");
            var figures = proj.Figures;
            try { return figures.FindItem(figureName); }
            catch { /* fall through */ }
            for (int i = 0; i < figures.Count; i++)
            {
                var f = figures[i];
                if (string.Equals(f.Name, figureName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            throw new InvalidOperationException(
                $"Figure '{figureName}' not found in project '{proj.Name}'.");
        }

        public static object ListFigures(string projectName)
        {
            var proj = ResolveSurveyProject(projectName);
            var figures = proj.Figures;
            int count = figures.Count;
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                var f = figures[i];
                list.Add(new
                {
                    name = f.Name,
                    layer = f.Layer,
                    style = f.Style,
                    site = f.Site,
                    isBreakline = f.IsBreakline,
                    isClosed = f.IsClosed,
                    isLotLine = f.IsLotLine,
                    pointCount = f.PointCount,
                });
            }
            return new { projectName = proj.Name, count, figures = list };
        }

        public static object GetFigureProperties(string projectName, string figureName)
        {
            var proj = ResolveSurveyProject(projectName);
            var f = FindFigureByName(proj, figureName);

            // Compute 2D length / area from figure nodes. Straight-segment
            // approximation — works exactly for orthogonal building outlines
            // (BLDG with RECT), close enough for everything else in this lab.
            var nodes = f.FigureNodes;
            int n = nodes.Count;
            double length = 0;
            var pts = new List<(double x, double y)>();
            for (int i = 0; i < n; i++)
            {
                var nd = nodes[i];
                pts.Add((nd.X, nd.Y));
            }
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].x - pts[i - 1].x;
                double dy = pts[i].y - pts[i - 1].y;
                length += Math.Sqrt(dx * dx + dy * dy);
            }
            // If closed and the last point != first, add the closing segment.
            bool closed = false;
            try { closed = f.IsClosed; } catch { }
            double area = 0;
            if (closed && pts.Count >= 3)
            {
                if (Math.Abs(pts[pts.Count - 1].x - pts[0].x) > 1e-9 ||
                    Math.Abs(pts[pts.Count - 1].y - pts[0].y) > 1e-9)
                {
                    double dx = pts[0].x - pts[pts.Count - 1].x;
                    double dy = pts[0].y - pts[pts.Count - 1].y;
                    length += Math.Sqrt(dx * dx + dy * dy);
                }
                // Shoelace
                double sum = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    sum += a.x * b.y - b.x * a.y;
                }
                area = Math.Abs(sum) / 2.0;
            }

            int networkId = -1;
            try { networkId = f.NetworkId; } catch { }
            string networkName = null;
            try
            {
                for (int i = 0; i < proj.Networks.Count; i++)
                {
                    var net = proj.Networks[i];
                    // No direct .Id on IAeccSurveyNetwork in the interop dump.
                    // Best-effort: if there's only one network, report its name.
                    if (proj.Networks.Count == 1) { networkName = net.Name; break; }
                }
            }
            catch { }

            return new
            {
                projectName = proj.Name,
                name = f.Name,
                layer = f.Layer,
                style = f.Style,
                site = f.Site,
                isBreakline = f.IsBreakline,
                isClosed = closed,
                isLotLine = f.IsLotLine,
                pointCount = f.PointCount,
                length,
                area,
                networkId,
                networkName,
            };
        }

        public static object GetFigureElevations(string projectName, string figureName)
        {
            var proj = ResolveSurveyProject(projectName);
            var f = FindFigureByName(proj, figureName);
            var nodes = f.FigureNodes;
            int n = nodes.Count;
            var list = new List<object>();
            double cum = 0;
            (double x, double y, double z)? prev = null;
            for (int i = 0; i < n; i++)
            {
                var nd = nodes[i];
                if (prev.HasValue)
                {
                    double dx = nd.X - prev.Value.x;
                    double dy = nd.Y - prev.Value.y;
                    cum += Math.Sqrt(dx * dx + dy * dy);
                }
                double? gradeOutPct = null;
                if (i + 1 < n)
                {
                    var nxt = nodes[i + 1];
                    double dx = nxt.X - nd.X;
                    double dy = nxt.Y - nd.Y;
                    double horiz = Math.Sqrt(dx * dx + dy * dy);
                    if (horiz > 1e-9)
                        gradeOutPct = (nxt.Z - nd.Z) / horiz * 100.0;
                }
                list.Add(new
                {
                    nodeNumber = SafeInt(() => nd.NodeNumber, i + 1),
                    station = cum,
                    x = nd.X,
                    y = nd.Y,
                    z = nd.Z,
                    gradeOutPct,
                });
                prev = (nd.X, nd.Y, nd.Z);
            }
            return new
            {
                projectName = proj.Name,
                figureName = f.Name,
                pointCount = n,
                totalLength = cum,
                vertices = list,
            };
        }

        public static object SetFigureElevations(
            string projectName,
            string figureName,
            double? uniform,
            JArray perVertex)
        {
            var proj = ResolveSurveyProject(projectName);
            var f = FindFigureByName(proj, figureName);
            var nodes = f.FigureNodes;
            int n = nodes.Count;

            var report = new List<object>();
            for (int i = 0; i < n; i++)
            {
                var nd = nodes[i];
                double before = nd.Z;
                double after;
                if (uniform.HasValue)
                    after = uniform.Value;
                else if (perVertex != null && i < perVertex.Count && perVertex[i] != null)
                    after = perVertex[i].Value<double>();
                else
                    after = before;

                if (Math.Abs(after - before) > 1e-12)
                    nd.Z = after;

                report.Add(new
                {
                    nodeNumber = SafeInt(() => nd.NodeNumber, i + 1),
                    before,
                    after,
                });
            }

            try { f.RecalculateFigure(); } catch { /* best effort */ }
            try { f.Save(); } catch { }

            return new
            {
                projectName = proj.Name,
                figureName = f.Name,
                updatedCount = report.Count,
                vertices = report,
            };
        }

        // -------------------- PNEZD-to-SDB import --------------------

        public static object ImportPnezdToSdb(
            string projectName,
            string networkName,
            string pnezdPath)
        {
            if (string.IsNullOrWhiteSpace(pnezdPath))
                throw new ArgumentException("pnezdPath is required");
            if (!System.IO.File.Exists(pnezdPath))
                throw new System.IO.FileNotFoundException(
                    $"PNEZD file not found: {pnezdPath}", pnezdPath);
            if (string.IsNullOrWhiteSpace(networkName))
                throw new ArgumentException("networkName is required");

            var proj = ResolveSurveyProject(projectName);
            AeccSurveyNetwork net = null;
            try { net = proj.Networks.FindItem(networkName); } catch { }
            if (net == null)
            {
                for (int i = 0; i < proj.Networks.Count; i++)
                {
                    var c = proj.Networks[i];
                    if (string.Equals(c.Name, networkName, StringComparison.OrdinalIgnoreCase))
                    {
                        net = c; break;
                    }
                }
            }
            if (net == null)
                throw new InvalidOperationException(
                    $"Network '{networkName}' not found in project '{proj.Name}'.");

            int figuresBefore = SafeInt(() => proj.Figures.Count, -1);
            string normalized = System.IO.Path.GetFullPath(pnezdPath).Replace("\\", "/");

            // SURVEYIMPORT with FILEDIA=0 is fragile across versions. We feed
            // network name, file path, and PNEZD format string. Linework
            // processing + insert behavior come from Survey User Settings.
            string script =
                "FILEDIA 0 " +
                $"SURVEYIMPORT \"{networkName}\" \"{normalized}\" " +
                "\"PNEZD (comma delimited)\" " +
                "FILEDIA 1 ";

            Doc.SendStringToExecute(script, true, false, true);

            return new
            {
                projectName = proj.Name,
                networkName = net.Name,
                pnezdPath = System.IO.Path.GetFullPath(pnezdPath),
                scriptQueued = script,
                figures = new { before = figuresBefore },
                note = "SURVEYIMPORT queued. Poll list_figures / list_survey_non_control_points after a few seconds.",
            };
        }

        // -------------------- v19 utility helpers --------------------

        private static int SafeInt(Func<int> fn, int fallback)
        {
            try { return fn(); } catch { return fallback; }
        }

        private static bool SafeBool(Func<bool> fn)
        {
            try { return fn(); } catch { return false; }
        }

        // =====================================================================
        // v20 NEW HANDLERS — added 2026-05-12
        //   load_assembly  : ExtensionLoader.Load — skip NETLOAD entirely
        //   com_discover   : walk IDispatch from surveyDoc; probe member names
        //   getvar/setvar  : direct AutoCAD system variable access (SECURELOAD,
        //                    TRUSTEDPATHS, FILEDIA, ...)
        // =====================================================================

        /// <summary>
        /// Loads a .NET assembly from disk directly via Autodesk's
        /// ExtensionLoader, bypassing the NETLOAD command, FILEDIA file
        /// dialogs, and the SECURELOAD user-trust prompt. The assembly's
        /// [assembly: CommandClass] attribute registers its commands
        /// automatically on load.
        /// </summary>
        public static object LoadAssembly(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required");
            string fullPath = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(fullPath))
                throw new System.IO.FileNotFoundException(
                    $"Assembly file not found: {fullPath}", fullPath);

            System.Reflection.Assembly asm;
            try
            {
                asm = Autodesk.AutoCAD.Runtime.ExtensionLoader.Load(fullPath);
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    $"ExtensionLoader.Load failed: {inner.GetType().Name}: {inner.Message}");
            }

            // Enumerate [CommandMethod] attributes so the caller can see
            // exactly what got registered.
            var commands = new List<string>();
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    foreach (var method in type.GetMethods(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic))
                    {
                        var attrs = method.GetCustomAttributes(
                            typeof(Autodesk.AutoCAD.Runtime.CommandMethodAttribute), false);
                        foreach (Autodesk.AutoCAD.Runtime.CommandMethodAttribute cma in attrs)
                            commands.Add(cma.GlobalName);
                    }
                }
            }
            catch
            {
                // Best-effort enumeration — assembly may have load-time
                // dependencies we don't fully reflect. The actual commands
                // are still registered via the [CommandClass] attribute.
            }
            commands.Sort();

            return new
            {
                loaded = true,
                path = fullPath,
                assemblyFullName = asm.FullName,
                commandCount = commands.Count,
                commands,
                note = "Run the new MCPSTART<version> command to swap bridges.",
            };
        }

        /// <summary>
        /// Walks an IDispatch property chain from the survey doc root,
        /// dumping member info at each step. Use to discover where
        /// collections like UserSettings or FigurePrefixDatabases actually
        /// live in the object graph when DISP_E_UNKNOWNNAME comes back.
        /// </summary>
        public static object ComDiscover(JArray chain, JArray probeNames)
        {
            object current = GetSurveyDocumentRoot();
            var path = new List<string> { "surveyDoc" };

            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    string step = chain[i]?.ToString();
                    if (string.IsNullOrEmpty(step)) continue;
                    try
                    {
                        current = GetComProperty(current, step);
                        path.Add(step);
                    }
                    catch (System.Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        return new
                        {
                            walked = path,
                            failedStep = step,
                            error = $"{inner.GetType().Name}: {inner.Message}",
                        };
                    }
                    if (current == null)
                    {
                        return new
                        {
                            walked = path,
                            note = "step returned null; cannot probe further",
                        };
                    }
                }
            }

            // Default probe set — covers the common Civil 3D Survey COM
            // surfaces. Caller may supply their own list to narrow the
            // search.
            string[] defaults = new[]
            {
                "Application", "Document", "ActiveDocument",
                "Projects", "Project", "ActiveProject",
                "UserSettings", "SurveyUserSettings", "ProjectSettings",
                "Settings", "SettingsRoot", "SettingsSurvey",
                "FigurePrefixDatabases", "FigurePrefixDB", "FigurePrefixDBs",
                "FigureStyles", "Figures", "LineworkCodeSets", "CodeSets",
                "EquipmentDatabases", "EquipmentDB",
                "Name", "Path", "Count", "Item",
            };
            var names = new List<string>();
            if (probeNames != null)
            {
                foreach (var t in probeNames)
                {
                    var s = t?.ToString();
                    if (!string.IsNullOrEmpty(s)) names.Add(s);
                }
            }
            if (names.Count == 0) names.AddRange(defaults);

            var members = new List<object>();
            foreach (var n in names)
            {
                try
                {
                    var v = GetComProperty(current, n);
                    members.Add(new
                    {
                        name = n,
                        present = true,
                        isNull = v == null,
                        typeName = v?.GetType().FullName,
                        value = (v is string || v is int || v is bool || v is double)
                            ? v
                            : null,
                    });
                }
                catch (System.Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    members.Add(new
                    {
                        name = n,
                        present = false,
                        error = inner.Message,
                    });
                }
            }

            return new
            {
                walked = path,
                runtimeType = current.GetType().FullName,
                probedMembers = members,
            };
        }

        /// <summary>
        /// Returns the value of an AutoCAD system variable (FILEDIA,
        /// SECURELOAD, TRUSTEDPATHS, etc.).
        /// </summary>
        public static object GetSysVar(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");
            object val = Application.GetSystemVariable(name);
            return new
            {
                name,
                value = val,
                valueType = val?.GetType().Name,
            };
        }

        /// <summary>
        /// Sets an AutoCAD system variable. Coerces JSON int → Int16
        /// (AutoCAD's most common integer flavor), float → double, anything
        /// else → string.
        /// </summary>
        public static object SetSysVar(string name, JToken value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");
            if (value == null)
                throw new ArgumentException("value is required");

            object coerced;
            switch (value.Type)
            {
                case JTokenType.Integer:
                    int iv = value.Value<int>();
                    if (iv >= short.MinValue && iv <= short.MaxValue)
                        coerced = (short)iv;
                    else
                        coerced = iv;
                    break;
                case JTokenType.Float:
                    coerced = value.Value<double>();
                    break;
                case JTokenType.Boolean:
                    coerced = (short)(value.Value<bool>() ? 1 : 0);
                    break;
                default:
                    coerced = value.ToString();
                    break;
            }

            try
            {
                Application.SetSystemVariable(name, coerced);
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    $"SetSystemVariable({name}) failed: {inner.GetType().Name}: {inner.Message}");
            }

            object readBack = null;
            try { readBack = Application.GetSystemVariable(name); } catch { }

            return new
            {
                name,
                set = coerced,
                readBack,
            };
        }

        // =====================================================================
        // v21 NEW HANDLERS / REPLACEMENTS — added 2026-05-12
        //   - surveyDoc caching + Init-once (prevents re-Init destabilization)
        //   - typed FigurePrefixDB access (fixes get_Item IDispatch failures)
        //   - com_invoke endpoint (call methods, not just read properties)
        // =====================================================================

        // Cached surveyDoc — populated by GetSurveyDocumentRootCached on first
        // access, reused thereafter. Init is called once on the first resolution.
        private static object _cachedSurveyDoc;
        private static long _cachedSurveyDocForDocHandle;

        /// <summary>
        /// Returns the surveyDoc COM root, resolving and Init-ing only once
        /// per active AcadDocument. Re-Init'ing the survey doc on every call
        /// causes the IAeccSurveyDocument IDispatch table to fall out of sync
        /// (manifests as DISP_E_UNKNOWNNAME on FigurePrefixDatabases and
        /// similar collections).
        /// </summary>
        private static object GetSurveyDocumentRootCached()
        {
            object acadAppObj = Application.AcadApplication;
            if (acadAppObj == null)
                throw new InvalidOperationException(
                    "Application.AcadApplication is null. Is Civil 3D fully initialized?");

            // Track the active drawing's Database handle so we know when to
            // re-Init (e.g. user switched drawings or opened a new one).
            long currentDocHandle = 0;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null) currentDocHandle = doc.Database.UnmanagedObject.ToInt64();
            }
            catch { /* no active doc — fall through with handle 0 */ }

            if (_cachedSurveyDoc != null && currentDocHandle == _cachedSurveyDocForDocHandle)
                return _cachedSurveyDoc;

            var tried = new List<string>();
            object surveyDoc = ResolveComObject(acadAppObj, _surveyDocProgIds, tried);
            if (surveyDoc == null)
            {
                var aeccAppProgIds = new[]
                {
                    "AeccXUiLand.AeccApplication.13.8",
                    "AeccXUiLand.AeccApplication.13.7",
                    "AeccXUiLand.AeccApplication.13.6",
                    "AeccXUiLand.AeccApplication.13.5",
                };
                object aeccApp = ResolveComObject(acadAppObj, aeccAppProgIds, tried);
                if (aeccApp != null)
                    surveyDoc = ResolveComObject(aeccApp, _surveyDocProgIds, tried);
            }
            if (surveyDoc == null)
                throw new InvalidOperationException(
                    "Could not obtain IAeccSurveyDocument via any strategy. Trail: "
                    + string.Join("; ", tried));

            try
            {
                object activeAcadDoc = GetComProperty(acadAppObj, "ActiveDocument");
                if (activeAcadDoc != null)
                    InvokeCom(surveyDoc, "Init", activeAcadDoc);
            }
            catch { /* Init failure is recoverable — properties may still work */ }

            _cachedSurveyDoc = surveyDoc;
            _cachedSurveyDocForDocHandle = currentDocHandle;
            return surveyDoc;
        }

        /// <summary>
        /// Returns the FigurePrefixDatabases collection, strongly typed.
        /// Casting to IAeccSurveyFigurePrefixDatabases lets us iterate with
        /// dbs[i] rather than InvokeCom("get_Item", i) — the latter fails
        /// because IDispatch doesn't expose .NET accessor names like
        /// "get_Item", only the property "Item" which is parameterized.
        /// </summary>
        private static IAeccSurveyFigurePrefixDatabases GetFigurePrefixDatabasesTyped()
        {
            object surveyDoc = GetSurveyDocumentRootCached();
            object dbsObj = GetComProperty(surveyDoc, "FigurePrefixDatabases");
            if (dbsObj == null)
                throw new InvalidOperationException(
                    "surveyDoc.FigurePrefixDatabases returned null.");
            return (IAeccSurveyFigurePrefixDatabases)dbsObj;
        }

        public static object ListFigurePrefixDbsTyped()
        {
            var dbs = GetFigurePrefixDatabasesTyped();
            int count = dbs.Count;
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                var db = dbs[i];
                int entryCount = 0;
                try { entryCount = db.Count; } catch { }
                list.Add(new
                {
                    name = db.Name,
                    path = db.Path,
                    entryCount,
                });
            }
            return new { count, databases = list };
        }

        public static object CreateFigurePrefixDbTyped(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");
            var dbs = GetFigurePrefixDatabasesTyped();

            IAeccSurveyFigurePrefixDatabase existing = null;
            try { existing = (IAeccSurveyFigurePrefixDatabase)dbs.FindItem(name); }
            catch { /* not found */ }

            var db = existing ?? (IAeccSurveyFigurePrefixDatabase)dbs.Create(name);
            return new
            {
                name = db.Name,
                path = db.Path,
                preExisting = existing != null,
            };
        }

        public static object ListFigurePrefixDbEntriesTyped(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("dbName is required");
            var dbs = GetFigurePrefixDatabasesTyped();
            IAeccSurveyFigurePrefixDatabase db;
            try { db = (IAeccSurveyFigurePrefixDatabase)dbs.FindItem(dbName); }
            catch
            {
                throw new InvalidOperationException(
                    $"Figure Prefix Database '{dbName}' not found.");
            }
            int count = db.Count;
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                var entry = db[i];
                list.Add(new
                {
                    name = entry.Name,
                    isBreakline = entry.IsBreakline,
                    isLotLine = entry.IsLotLine,
                    layer = entry.Layer,
                    style = entry.Style,
                    site = entry.Site,
                });
            }
            return new { dbName = db.Name, count, entries = list };
        }

        public static object AddFigurePrefixDbEntryTyped(
            string dbName,
            string prefix,
            bool? isBreakline,
            bool? isLotLine,
            string layer,
            string style,
            string site)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("dbName is required");
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("prefix is required");

            var dbs = GetFigurePrefixDatabasesTyped();
            IAeccSurveyFigurePrefixDatabase db;
            try { db = (IAeccSurveyFigurePrefixDatabase)dbs.FindItem(dbName); }
            catch
            {
                throw new InvalidOperationException(
                    $"Figure Prefix Database '{dbName}' not found. "
                    + "Call create_figure_prefix_db first.");
            }

            IAeccSurveyFigurePrefix entry = null;
            try { entry = (IAeccSurveyFigurePrefix)db.FindItem(prefix); }
            catch { /* not found */ }
            bool preExisting = entry != null;
            if (entry == null)
                entry = (IAeccSurveyFigurePrefix)db.Create(prefix);

            if (isBreakline.HasValue) entry.IsBreakline = isBreakline.Value;
            if (isLotLine.HasValue) entry.IsLotLine = isLotLine.Value;
            if (!string.IsNullOrEmpty(layer)) entry.Layer = layer;
            if (!string.IsNullOrEmpty(style)) entry.Style = style;
            if (!string.IsNullOrEmpty(site)) entry.Site = site;

            try { entry.Save(); } catch { /* may auto-save */ }

            return new
            {
                dbName = db.Name,
                prefix = entry.Name,
                isBreakline = entry.IsBreakline,
                isLotLine = entry.IsLotLine,
                layer = entry.Layer,
                style = entry.Style,
                site = entry.Site,
                preExisting,
            };
        }

        /// <summary>
        /// Invokes a method (with args) on an arbitrary COM object reached by
        /// walking properties from surveyDoc. Use when com_discover's
        /// property-only probe isn't enough and you need to call a function
        /// like FindItem("Some Item Name") to test reachability.
        /// </summary>
        public static object ComInvoke(JArray chain, string methodName, JArray args)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("methodName is required");

            object current = GetSurveyDocumentRootCached();
            var path = new List<string> { "surveyDoc" };
            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    string step = chain[i]?.ToString();
                    if (string.IsNullOrEmpty(step)) continue;
                    current = GetComProperty(current, step);
                    path.Add(step);
                    if (current == null)
                        throw new InvalidOperationException(
                            $"chain step '{step}' returned null");
                }
            }

            // Coerce JSON args into something COM can swallow.
            var argList = new List<object>();
            if (args != null)
            {
                foreach (var t in args)
                {
                    object v;
                    if (t == null || t.Type == JTokenType.Null) v = null;
                    else if (t.Type == JTokenType.Integer) v = t.Value<int>();
                    else if (t.Type == JTokenType.Float) v = t.Value<double>();
                    else if (t.Type == JTokenType.Boolean) v = t.Value<bool>();
                    else v = t.ToString();
                    argList.Add(v);
                }
            }

            object result;
            try
            {
                result = InvokeCom(current, methodName, argList.ToArray());
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    $"InvokeCom({methodName}) on {string.Join(".", path)} failed: "
                    + $"{inner.GetType().Name}: {inner.Message}");
            }

            return new
            {
                walked = path,
                method = methodName,
                resultIsNull = result == null,
                resultTypeName = result?.GetType().FullName,
                resultValue = (result is string || result is int || result is bool || result is double || result is short)
                    ? result : null,
            };
        }

        // =====================================================================
        // v22 NEW HANDLERS — added 2026-05-12
        //   Survey User Settings access via surveyDoc.GetUserSettings() method
        //   (it's a method, not a property — com_discover's property-only
        //   probe missed it earlier).
        // =====================================================================

        private static object GetSurveyUserSettingsCom()
        {
            var surveyDoc = GetSurveyDocumentRootCached();
            var us = InvokeCom(surveyDoc, "GetUserSettings");
            if (us == null)
                throw new InvalidOperationException(
                    "surveyDoc.GetUserSettings() returned null");
            return us;
        }

        public static object GetSurveyUserSettingsV2()
        {
            var us = GetSurveyUserSettingsCom();
            object Read(string p)
            {
                try { return GetComProperty(us, p); } catch { return null; }
            }
            return new
            {
                figurePrefixDatabasePath = Read("FigurePrefixDatabasePath") as string,
                currentFigurePrefixDatabase = Read("CurrentFigurePrefixDatabase") as string,
                equipmentDatabasePath = Read("EquipmentDatabasePath") as string,
                currentEquipmentDatabase = Read("CurrentEquipmentDatabase") as string,
                defaultFigureLayer = Read("DefaultFigureLayer") as string,
                defaultFigureStyle = Read("DefaultFigureStyle") as string,
                insertFigureObjects = Read("InsertFigureObjects"),
                insertCivil3DPoints = Read("InsertCivil3DPoints"),
                insertNetworkObject = Read("InsertNetworkObject"),
                siteForLotlineFigures = Read("SiteForLotlineFigures") as string,
                eraseAllFigures = Read("EraseAllFigures"),
                eraseAllObservations = Read("EraseAllObservations"),
                eraseAllSurveyPoints = Read("EraseAllSurveyPoints"),
            };
        }

        public static object SetCurrentFigurePrefixDb(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required");
            var us = GetSurveyUserSettingsCom();
            try { SetComProperty(us, "CurrentFigurePrefixDatabase", name); }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    $"Set CurrentFigurePrefixDatabase failed: {inner.GetType().Name}: {inner.Message}");
            }
            try { InvokeCom(us, "_InternalSaveBack"); } catch { /* best effort */ }
            string verify = null;
            try { verify = GetComProperty(us, "CurrentFigurePrefixDatabase") as string; } catch { }
            return new { requested = name, currentFigurePrefixDatabase = verify };
        }

        public static object SetSurveyUserSettingsV2(JObject payload)
        {
            if (payload == null) throw new ArgumentException("payload is required");
            var us = GetSurveyUserSettingsCom();
            var applied = new List<string>();
            string[] settableProps = new[]
            {
                "FigurePrefixDatabasePath", "CurrentFigurePrefixDatabase",
                "EquipmentDatabasePath", "CurrentEquipmentDatabase",
                "DefaultFigureLayer", "DefaultFigureStyle",
                "InsertFigureObjects", "InsertCivil3DPoints", "InsertNetworkObject",
                "EraseAllFigures", "EraseAllObservations", "EraseAllSurveyPoints",
                "SiteForLotlineFigures",
            };
            foreach (var prop in settableProps)
            {
                if (payload[prop] == null) continue;
                var tok = payload[prop];
                object value;
                if (tok.Type == JTokenType.Boolean) value = tok.Value<bool>();
                else if (tok.Type == JTokenType.Integer) value = tok.Value<int>();
                else value = tok.ToString();
                try { SetComProperty(us, prop, value); applied.Add(prop); }
                catch (System.Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    applied.Add($"{prop}:ERR({inner.Message})");
                }
            }
            try { InvokeCom(us, "_InternalSaveBack"); } catch { }
            return new { applied, current = GetSurveyUserSettingsV2() };
        }

        // =====================================================================
        // v23 NEW HANDLER — added 2026-05-14
        //   set_figure_elevations_persistent — uses managed
        //   Autodesk.Civil.DatabaseServices.SurveyFigure on the drawing-side
        //   entity in a transaction. The COM IAeccSurveyFigureNode.Z setter
        //   doesn't write back to the SDB; this one does.
        // =====================================================================

        public static object SetFigureElevationsPersistent(
            string projectName,
            string figureName,
            double? uniform,
            JArray perVertex)
        {
            var proj = ResolveSurveyProject(projectName);
            var fig = FindFigureByName(proj, figureName);

            // Resolve the drawing-side ObjectId.
            ObjectId oid = ObjectId.Null;
            try
            {
                object oidObj = InvokeCom(fig, "GetObjectId");
                if (oidObj is ObjectId direct) oid = direct;
                else if (oidObj != null)
                {
                    long lng = Convert.ToInt64(oidObj);
                    var handle = new Autodesk.AutoCAD.DatabaseServices.Handle(lng);
                    Doc.Database.TryGetObjectId(handle, out oid);
                }
            }
            catch { /* fall through to ModelSpace scan */ }
            if (oid.IsNull)
            {
                using (var rtx = Doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)rtx.GetObject(Doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)rtx.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        var e = rtx.GetObject(id, OpenMode.ForRead);
                        if (e == null) continue;
                        var typeName = e.GetType().FullName;
                        if (typeName == null || !typeName.Contains("SurveyFigure")) continue;
                        var nameProp = e.GetType().GetProperty("Name");
                        var entName = nameProp?.GetValue(e) as string;
                        if (string.Equals(entName, figureName, StringComparison.OrdinalIgnoreCase))
                        {
                            oid = id;
                            break;
                        }
                    }
                    rtx.Commit();
                }
            }

            if (oid.IsNull)
                throw new InvalidOperationException(
                    $"Figure '{figureName}' has no resolvable drawing ObjectId.");

            var report = new List<object>();
            var doc = Doc;
            using (doc.LockDocument())
            using (var tx = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = tx.GetObject(oid, OpenMode.ForRead);
                if (ent == null)
                    throw new InvalidOperationException(
                        $"ObjectId resolved but tx.GetObject returned null. ObjectId={oid}");

                // The managed SurveyFigure inherits from Polyline3d-like base.
                // Vertices are accessed by iterating the entity for ObjectIds of
                // SurveyFigureNode (each a vertex). Open each ForWrite, modify
                // Position with new Z, then commit.
                var entType = ent.GetType();
                string entTypeName = entType.FullName;

                // SurveyFigure inherits from FeatureLine, which exposes
                // SetPointElevation(int index, double z). PIPointsCount tells
                // us how many PI points (the figure's polyline vertices) exist.
                int pipCount = 0;
                var pipCountProp = ent.GetType().GetProperty("PIPointsCount");
                if (pipCountProp != null)
                    pipCount = Convert.ToInt32(pipCountProp.GetValue(ent));

                var setPointElev = ent.GetType().GetMethod(
                    "SetPointElevation",
                    new[] { typeof(int), typeof(double) });
                if (setPointElev == null)
                    throw new InvalidOperationException(
                        "SetPointElevation(int, double) not found on " + ent.GetType().FullName);

                // Also need GetPointAtIndex or similar to read current elevation.
                // FeatureLine.GetPoints(FeatureLinePointType.PIPoint) returns Point3dCollection.
                var getPointsMethod = ent.GetType().GetMethod("GetPoints");
                Point3dCollection pipPoints = null;
                if (getPointsMethod != null)
                {
                    // Find FeatureLinePointType.PIPoint enum
                    var pointTypeEnum = ent.GetType().Assembly
                        .GetType("Autodesk.Civil.DatabaseServices.FeatureLinePointType");
                    if (pointTypeEnum != null)
                    {
                        var piPointVal = Enum.Parse(pointTypeEnum, "PIPoint");
                        pipPoints = getPointsMethod.Invoke(ent, new[] { piPointVal }) as Point3dCollection;
                    }
                }

                // Reopen ForWrite to mutate.
                ent.UpgradeOpen();

                int updated = 0;
                for (int i = 0; i < pipCount; i++)
                {
                    double before = 0;
                    double x = 0, y = 0;
                    if (pipPoints != null && i < pipPoints.Count)
                    {
                        before = pipPoints[i].Z;
                        x = pipPoints[i].X;
                        y = pipPoints[i].Y;
                    }
                    double after;
                    if (uniform.HasValue) after = uniform.Value;
                    else if (perVertex != null && i < perVertex.Count && perVertex[i] != null)
                        after = perVertex[i].Value<double>();
                    else after = before;

                    if (Math.Abs(after - before) > 1e-12)
                    {
                        try
                        {
                            setPointElev.Invoke(ent, new object[] { i, after });
                            updated++;
                        }
                        catch (System.Exception ex)
                        {
                            var inner = ex.InnerException ?? ex;
                            report.Add(new
                            {
                                vertexIndex = i, x, y, before, after,
                                error = inner.GetType().Name + ": " + inner.Message,
                            });
                            continue;
                        }
                    }
                    report.Add(new
                    {
                        vertexIndex = i, x, y, before, after,
                    });
                }

                tx.Commit();
                return new
                {
                    projectName = proj.Name,
                    figureName = fig.Name,
                    entityType = ent.GetType().FullName,
                    pipPointsCount = pipCount,
                    updatedCount = updated,
                    vertices = report,
                };
            }
        }









        // =====================================================================
        // v26 — runtime reflection on a drawing entity to discover its API
        //   surface. Used to figure out how to mutate SurveyFigure vertices.
        // =====================================================================

        public static object InspectFigureEntity(string projectName, string figureName)
        {
            var proj = ResolveSurveyProject(projectName);
            var fig = FindFigureByName(proj, figureName);

            ObjectId oid = ObjectId.Null;
            try
            {
                object oidObj = InvokeCom(fig, "GetObjectId");
                if (oidObj is ObjectId direct) oid = direct;
                else if (oidObj != null)
                {
                    long lng = Convert.ToInt64(oidObj);
                    var handle = new Autodesk.AutoCAD.DatabaseServices.Handle(lng);
                    Doc.Database.TryGetObjectId(handle, out oid);
                }
            }
            catch { }

            if (oid.IsNull)
            {
                using (var rtx = Doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)rtx.GetObject(Doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)rtx.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        var e = rtx.GetObject(id, OpenMode.ForRead);
                        if (e == null) continue;
                        var tn = e.GetType().FullName;
                        if (tn == null || !tn.Contains("SurveyFigure")) continue;
                        var nameProp = e.GetType().GetProperty("Name");
                        var entName = nameProp?.GetValue(e) as string;
                        if (string.Equals(entName, figureName, StringComparison.OrdinalIgnoreCase))
                        {
                            oid = id;
                            break;
                        }
                    }
                    rtx.Commit();
                }
            }

            if (oid.IsNull)
                throw new InvalidOperationException("Could not resolve drawing entity for figure.");

            using (var tx = Doc.Database.TransactionManager.StartTransaction())
            {
                var ent = tx.GetObject(oid, OpenMode.ForRead);
                var t = ent.GetType();
                var props = new List<object>();
                foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    object val = null;
                    string err = null;
                    try { val = p.GetValue(ent); }
                    catch (System.Exception ex) { err = (ex.InnerException ?? ex).Message; }
                    props.Add(new
                    {
                        name = p.Name,
                        type = p.PropertyType.FullName,
                        canRead = p.CanRead,
                        canWrite = p.CanWrite,
                        value = (val is string || val is int || val is bool || val is double || val is long || val is short)
                            ? val.ToString() : (val != null ? val.GetType().FullName : null),
                        readError = err,
                    });
                }
                var methods = new List<object>();
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
                    methods.Add(new
                    {
                        name = m.Name,
                        returns = m.ReturnType.FullName,
                        parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)),
                    });
                }
                // Walk inheritance chain (only Declared methods above; include inherited too)
                var inheritedMethods = new List<string>();
                var bt2 = t.BaseType;
                while (bt2 != null && bt2 != typeof(object))
                {
                    foreach (var m in bt2.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
                        var sig = bt2.Name + "::" + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")";
                        inheritedMethods.Add(sig);
                    }
                    bt2 = bt2.BaseType;
                }

                tx.Commit();
                return new
                {
                    figureName = fig.Name,
                    entityType = t.FullName,
                    baseTypeChain = GetBaseTypeChain(t),
                    propertyCount = props.Count,
                    properties = props,
                    declaredMethodCount = methods.Count,
                    declaredMethods = methods,
                    inheritedMethods,
                };
            }
        }

        private static List<string> GetBaseTypeChain(Type t)
        {
            var chain = new List<string>();
            var c = t;
            while (c != null && c != typeof(object))
            {
                chain.Add(c.FullName);
                c = c.BaseType;
            }
            return chain;
        }


    }
}












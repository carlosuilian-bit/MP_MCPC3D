using System.Text.Json;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Sdk;

// Live smoke client for the accumulated phases: 1D, 1E, 1F and 2G.
//
// Talks to the bridge pipe directly, because a modal blocks the idle loop, which
// stops the heartbeat, which gets the endpoint descriptor pruned. Discovery by
// registry is exactly what stops working in the case these phases address.
//
// Usage:
//   <pipeName> <instanceId> inspect
//   <pipeName> <instanceId> decide "<dialog title>" "<button text>" <Default|Once>
//   <pipeName> <instanceId> dismiss "<dialog title>"
//   <pipeName> <instanceId> objects

if (args.Length < 3)
{
    Console.WriteLine("usage: <pipeName> <instanceId> <inspect|decide|dismiss|objects> [...]");
    return 1;
}

var pipeName = args[0];
var instanceId = args[1];
var command = args[2].ToLowerInvariant();
var sessionId = Guid.NewGuid().ToString("N");
var client = new NamedPipeRpcClient();
var timeout = TimeSpan.FromSeconds(120);

switch (command)
{
    case "inspect":
        await Inspect();
        break;
    case "decide":
        await Decide(args[3], args[4], args[5]);
        break;
    case "dismiss":
        await Dismiss(args[3]);
        break;
    case "objects":
        await Objects();
        break;
    case "roadway":
        await Roadway();
        break;
    case "psfind":
        await PropertySetFind(args[3]);
        break;
    case "psbatch":
        await PropertySetBatch(args[3]);
        break;
    case "psset":
        await PropertySetSet(args[3], args[4], args[5], args[6]);
        break;
    case "blocks":
        await Blocks();
        break;
    default:
        Console.WriteLine($"unknown command '{command}'");
        return 1;
}

return 0;

async Task Inspect()
{
    var list = await client.SendAsync(pipeName, "tools/list", null, timeout);
    if (list.Error is not null)
    {
        Console.WriteLine($"tools/list failed: {list.Error.Code} {list.Error.Message}");
        return;
    }

    var manifest = list.Result.Deserialize<ToolManifest>(JsonContractOptions.Default)!;
    Console.WriteLine($"tool_count={manifest.Tools.Count}");
    foreach (var name in new[] { "object_get", "object_properties", "dialog_decision_list", "dialog_decision_set", "dismiss_dialog" })
    {
        var tool = manifest.Tools.FirstOrDefault(t => t.Name == name);
        Console.WriteLine($"  {name}: {(tool is null ? "MISSING" : $"{tool.OperationClass}/{tool.Risk}")}");
    }

    Print("list_dialogs", await Execute("list_dialogs", null));
    Print("dialog_decision_list", await Execute("dialog_decision_list", null));
}

async Task Decide(string title, string button, string scope)
{
    var response = await Execute("dialog_decision_set", new JsonObject
    {
        ["dialog_title"] = title,
        ["button_text"] = button,
        ["scope"] = scope,
        ["note"] = "live smoke 1E/1F"
    });
    Print("dialog_decision_set", response);
}

async Task Dismiss(string title)
{
    Print("dismiss_dialog", await Execute("dismiss_dialog", new JsonObject { ["expected_title"] = title }));
    Print("list_dialogs[after]", await Execute("list_dialogs", null));
}

async Task Objects()
{
    var documents = await Execute("document_list", null);
    Print("document_list", documents);
    if (!documents.Success || documents.Data is null)
    {
        return;
    }

    // Core document contracts serialize camelCase; the tool contracts use
    // snake_case. Accept both rather than assume.
    var active = documents.Data["documents"]?.AsArray()
        ?.FirstOrDefault(doc => (doc?["is_active"] ?? doc?["isActive"])?.GetValue<bool>() == true);
    if (active is null)
    {
        Console.WriteLine("no active document");
        return;
    }

    var documentId = (active["document_id"] ?? active["documentId"])!.GetValue<string>();
    Console.WriteLine($"document={active["name"]}");

    var handle = await FindHandle(documentId, "alignment_list", "alignments")
              ?? await FindHandle(documentId, "cogo_list", "cogo_points")
              ?? await FindHandle(documentId, "surface_list", "surfaces");

    if (handle is null)
    {
        Console.WriteLine("no handle found");
        return;
    }

    Console.WriteLine($"source={handle.Value.Tool} name={handle.Value.Name} handle={handle.Value.Handle}");

    Print("object_get", await Execute("object_get", Doc(documentId, ("handle", handle.Value.Handle))));
    Print("object_properties", await Execute("object_properties", Doc(documentId, ("handle", handle.Value.Handle))));
    Print("object_properties[sta]", await Execute("object_properties", Doc(documentId, ("handle", handle.Value.Handle), ("name_contains", "sta"))));

    var missing = await Execute("object_get", Doc(documentId, ("handle", "DEADBEEF")));
    Console.WriteLine($"-- object_get[DEADBEEF] success={missing.Success} code={missing.Error?.Code}");
}

async Task<string?> ActiveDocumentId()
{
    var documents = await Execute("document_list", null);
    var active = documents.Data?["documents"]?.AsArray()
        ?.FirstOrDefault(doc => (doc?["is_active"] ?? doc?["isActive"])?.GetValue<bool>() == true);
    return active is null ? null : (active["document_id"] ?? active["documentId"])!.GetValue<string>();
}

// Phase 6A: batch station/offset. Uses alignment_point_location to obtain a
// point that is genuinely on the alignment, then feeds it back through the batch
// alongside a point that is obviously off it.
async Task Roadway()
{
    var documentId = await ActiveDocumentId();
    if (documentId is null) { Console.WriteLine("no active document"); return; }

    var list = await Execute("alignment_list", Doc(documentId));
    var first = list.Data?["alignments"]?.AsArray()?.FirstOrDefault();
    if (first is null) { Console.WriteLine("no alignments"); return; }
    var name = first["name"]!.GetValue<string>();
    Console.WriteLine($"alignment={name}");

    // Take the real station range from the alignment rather than assuming one.
    var detail = await Execute("alignment_get", Doc(documentId, ("name", name)));
    var info = detail.Data?["alignment"];
    Console.WriteLine("alignment_get: " + (info?.ToJsonString() ?? "(null)"));
    var startStation = info?["starting_station"]?.GetValue<double>() ?? 0;
    var endStation = info?["ending_station"]?.GetValue<double>() ?? startStation;
    var midStation = startStation + (endStation - startStation) / 2;
    Console.WriteLine($"station_range={startStation}..{endStation} mid={midStation}");

    var located = await Execute("alignment_point_location", Doc(documentId,
        ("name", name), ("station", midStation), ("offset", 0.0)));
    Print("alignment_point_location", located);
    if (!located.Success) { return; }

    var x = located.Data!["x"]!.GetValue<double>();
    var y = located.Data["y"]!.GetValue<double>();

    var batch = new JsonObject
    {
        ["document_id"] = documentId,
        ["alignment"] = name,
        ["points"] = new JsonArray(
            new JsonObject { ["x"] = x, ["y"] = y, ["label"] = "sobre o eixo" },
            new JsonObject { ["x"] = x + 500000, ["y"] = y + 500000, ["label"] = "longe do eixo" })
    };
    Print("roadway_station_offset_batch", await Execute("roadway_station_offset_batch", batch));

    Print("roadway[eixo inexistente]", await Execute("roadway_station_offset_batch", new JsonObject
    {
        ["document_id"] = documentId,
        ["alignment"] = "NAO_EXISTE",
        ["points"] = new JsonArray(new JsonObject { ["x"] = x, ["y"] = y })
    }));
}

// Phase 2H: find the objects carrying a property set.
async Task PropertySetFind(string setName)
{
    var documentId = await ActiveDocumentId();
    if (documentId is null) { Console.WriteLine("no active document"); return; }

    var all = await Execute("propertyset_find", Doc(documentId, ("set_name", setName), ("limit", "5")));
    Console.WriteLine($"-- propertyset_find success={all.Success} code={all.Error?.Code}");
    if (all.Data is not null)
    {
        Console.WriteLine($"   count={all.Data["count"]} matched={all.Data["matched_count"]} scanned={all.Data["scanned_count"]} truncated={all.Data["truncated"]}");
        var first = all.Data["objects"]?.AsArray()?.FirstOrDefault();
        Console.WriteLine("   primeiro: " + (first?.ToJsonString() ?? "(nenhum)"));
    }

    var filtered = await Execute("propertyset_find", new JsonObject
    {
        ["document_id"] = documentId,
        ["set_name"] = setName,
        ["match"] = new JsonArray(new JsonObject { ["name"] = "Secao", ["equals"] = "530" })
    });
    Console.WriteLine($"-- find[Secao=530] success={filtered.Success} count={filtered.Data?["count"]} matched={filtered.Data?["matched_count"]}");
    Console.WriteLine("   " + (filtered.Data?["objects"]?.AsArray()?.FirstOrDefault()?["handle"]?.ToJsonString() ?? "(nenhum)"));

    var none = await Execute("propertyset_find", new JsonObject
    {
        ["document_id"] = documentId,
        ["set_name"] = setName,
        ["match"] = new JsonArray(new JsonObject { ["name"] = "Secao", ["equals"] = "999999" })
    });
    Console.WriteLine($"-- find[sem match] success={none.Success} count={none.Data?["count"]} scanned={none.Data?["scanned_count"]}");

    var bad = await Execute("propertyset_find", new JsonObject
    {
        ["document_id"] = documentId,
        ["set_name"] = setName,
        ["match"] = new JsonArray(new JsonObject { ["name"] = "Secao" })
    });
    Console.WriteLine($"-- find[filtro sem criterio] success={bad.Success} code={bad.Error?.Code}");
}

// Phase 6B: atomic batch write, including the rollback path.
async Task PropertySetBatch(string setName)
{
    var documentId = await ActiveDocumentId();
    if (documentId is null) { Console.WriteLine("no active document"); return; }

    var found = await Execute("propertyset_find", Doc(documentId, ("set_name", setName), ("limit", "3")));
    var handles = found.Data?["objects"]?.AsArray()?.Select(o => o!["handle"]!.GetValue<string>()).ToArray() ?? [];
    Console.WriteLine($"handles={string.Join(",", handles)}");
    if (handles.Length < 3) { Console.WriteLine("precisa de 3 handles"); return; }

    JsonObject Batch(IEnumerable<string> hs, string suffix) => new()
    {
        ["document_id"] = documentId,
        ["set_name"] = setName,
        ["objects"] = new JsonArray(hs.Select(h => (JsonNode)new JsonObject
        {
            ["handle"] = h,
            ["properties"] = new JsonArray(new JsonObject { ["name"] = "Ponto_B", ["value"] = "LOTE-" + suffix })
        }).ToArray())
    };

    var first = await Execute("roadway_propertyset_write", Batch(handles, "A"));
    Console.WriteLine($"-- lote[sem token] success={first.Success} code={first.Error?.Code}");
    Console.WriteLine($"   preview: {first.Error?.Message}");

    var token = first.Error?.Details?["confirmation_token"];
    if (token is null) { Console.WriteLine("   SEM TOKEN"); return; }

    var ok = await Execute("roadway_propertyset_write", Batch(handles, "A"), token.Deserialize<ConfirmationToken>(JsonContractOptions.Default));
    Console.WriteLine($"-- lote[confirmado] success={ok.Success} applied={ok.Data?["applied"]} changed={ok.Data?["changed_count"]} skipped={ok.Data?["skipped_count"]}");

    // O caso que so um lote consegue provar: handle invalido no meio.
    var mixed = handles.Take(1).Append("DEADBEEF").Concat(handles.Skip(1)).ToArray();
    var bad = await Execute("roadway_propertyset_write", Batch(mixed, "B"));
    Console.WriteLine($"-- lote[handle invalido no meio] success={bad.Success} code={bad.Error?.Code}");

    var after = await Execute("propertyset_get", Doc(documentId, ("handle", handles[0]), ("name", setName)));
    var pontoB = after.Data?["property_set"]?["properties"]?.AsArray()
        ?.FirstOrDefault(x => x!["name"]!.GetValue<string>() == "Ponto_B")?["value"];
    Console.WriteLine($"   Ponto_B de {handles[0]} apos a falha = {pontoB}  (esperado LOTE-A, nunca LOTE-B)");
}

// Phase 3E: the two-step confirmation flow.
async Task PropertySetSet(string handle, string setName, string property, string value)
{
    var documentId = await ActiveDocumentId();
    if (documentId is null) { Console.WriteLine("no active document"); return; }

    Print("propertyset_get[antes]", await Execute("propertyset_get", Doc(documentId, ("handle", handle), ("name", setName))));

    JsonObject Payload() => new()
    {
        ["document_id"] = documentId,
        ["handle"] = handle,
        ["set_name"] = setName,
        ["properties"] = new JsonArray(new JsonObject { ["name"] = property, ["value"] = value })
    };

    var first = await Execute("propertyset_set", Payload());
    Console.WriteLine($"-- propertyset_set[sem token] success={first.Success} code={first.Error?.Code}");
    Console.WriteLine($"   preview: {first.Error?.Message}");

    var token = first.Error?.Details?["confirmation_token"];
    if (token is null) { Console.WriteLine("   SEM TOKEN no details - fluxo quebrado"); return; }

    var confirmed = await Execute("propertyset_set", Payload(), token.Deserialize<ConfirmationToken>(JsonContractOptions.Default));
    Print("propertyset_set[confirmado]", confirmed);
    Print("propertyset_get[depois]", await Execute("propertyset_get", Doc(documentId, ("handle", handle), ("name", setName))));
}

// Block reads: block_list, block_reference_list, block_get_attributes.
// Prints summaries rather than full JSON, because a production drawing can
// carry thousands of inserts.
async Task Blocks()
{
    var documentId = await ActiveDocumentId();
    if (documentId is null) { Console.WriteLine("no active document"); return; }

    // 1. Definitions, default view and full view.
    var list = await Execute("block_list", Doc(documentId));
    Console.WriteLine($"-- block_list success={list.Success} code={list.Error?.Code} count={list.Data?["count"]} ms={list.Duration?.TotalMilliseconds:F0}");
    var definitions = list.Data?["blocks"]?.AsArray() ?? new JsonArray();
    foreach (var block in definitions.Take(25))
    {
        var tags = string.Join(",", (block?["attribute_definitions"]?.AsArray() ?? new JsonArray())
            .Select(a => (a?["constant"]?.GetValue<bool>() == true ? "#" : "") + a?["tag"]?.GetValue<string>()));
        Console.WriteLine($"   {block?["name"],-40} refs={block?["reference_count"],-5} dyn={block?["is_dynamic"],-5} xref={block?["is_xref"],-5} attrs=[{tags}]");
    }

    var all = await Execute("block_list", Doc(documentId, ("include_anonymous", true), ("include_layouts", true)));
    var allBlocks = all.Data?["blocks"]?.AsArray() ?? new JsonArray();
    Console.WriteLine($"-- block_list[all] count={all.Data?["count"]} anonymous={allBlocks.Count(b => b?["is_anonymous"]?.GetValue<bool>() == true)} layouts={allBlocks.Count(b => b?["is_layout"]?.GetValue<bool>() == true)}");

    // 2. Every insert, default view.
    var refs = await Execute("block_reference_list", Doc(documentId));
    var references = refs.Data?["references"]?.AsArray() ?? new JsonArray();
    Console.WriteLine($"-- block_reference_list success={refs.Success} code={refs.Error?.Code} count={refs.Data?["count"]} scanned={refs.Data?["scanned_count"]} truncated={refs.Data?["truncated"]} by={refs.Data?["truncated_by"]} ms={refs.Duration?.TotalMilliseconds:F0}");
    foreach (var group in references.GroupBy(r => r?["block_name"]?.GetValue<string>()).OrderByDescending(g => g.Count()).Take(12))
    {
        Console.WriteLine($"   {group.Key,-40} x{group.Count()}");
    }

    Console.WriteLine($"   dynamic inserts={references.Count(r => r?["is_dynamic"]?.GetValue<bool>() == true)}  spaces=[{string.Join(",", references.Select(r => r?["space"]?.GetValue<string>()).Distinct())}]");

    var anonymous = await Execute("block_reference_list", Doc(documentId, ("include_anonymous", true)));
    Console.WriteLine($"-- block_reference_list[include_anonymous] count={anonymous.Data?["count"]} truncated={anonymous.Data?["truncated"]} by={anonymous.Data?["truncated_by"]}");

    // 3. A dynamic insert, found by its real name while its definition is anonymous.
    var dynamic = references.FirstOrDefault(r => r?["is_dynamic"]?.GetValue<bool>() == true);
    if (dynamic is not null)
    {
        var name = dynamic["block_name"]!.GetValue<string>();
        var byName = await Execute("block_reference_list", Doc(documentId, ("block_name", name)));
        var hits = byName.Data?["references"]?.AsArray() ?? new JsonArray();
        Console.WriteLine($"-- block_reference_list[block_name={name}] count={byName.Data?["count"]} definitions=[{string.Join(",", hits.Select(h => h?["definition_name"]?.GetValue<string>()).Distinct())}]");
    }
    else
    {
        Console.WriteLine("-- no dynamic insert in this drawing");
    }

    // 4. Attributes: the first insert that carries any, then with include_attributes.
    var withAttributes = references.FirstOrDefault(r => (r?["attribute_count"]?.GetValue<int>() ?? 0) > 0);
    var constantBlock = definitions.FirstOrDefault(b => (b?["attribute_definitions"]?.AsArray() ?? new JsonArray()).Any(a => a?["constant"]?.GetValue<bool>() == true));
    var constantInsert = constantBlock is null ? null : references.FirstOrDefault(r => r?["block_name"]?.GetValue<string>() == constantBlock["name"]?.GetValue<string>());
    foreach (var (label, target) in new[] { ("attrs", withAttributes), ("constant", constantInsert) })
    {
        if (target is null) { Console.WriteLine($"-- block_get_attributes[{label}] no candidate insert"); continue; }
        var handle = target["handle"]!.GetValue<string>();
        var read = await Execute("block_get_attributes", Doc(documentId, ("handle", handle)));
        Console.WriteLine($"-- block_get_attributes[{label}] handle={handle} block={read.Data?["block_name"]} success={read.Success} count={read.Data?["count"]}");
        foreach (var attribute in (read.Data?["attributes"]?.AsArray() ?? new JsonArray()).Take(10))
        {
            Console.WriteLine($"   {attribute?["tag"],-20} = {attribute?["value"],-30} constant={attribute?["constant"]} handle={attribute?["handle"]}");
        }
    }

    if (withAttributes is not null)
    {
        var name = withAttributes["block_name"]!.GetValue<string>();
        var inline = await Execute("block_reference_list", Doc(documentId, ("block_name", name), ("include_attributes", true)));
        var first = inline.Data?["references"]?.AsArray().FirstOrDefault();
        Console.WriteLine($"-- block_reference_list[include_attributes, {name}] count={inline.Data?["count"]} first_attrs={first?["attributes"]?.AsArray().Count}");
    }

    // 5. Negative paths.
    var definitionHandle = definitions.FirstOrDefault()?["handle"]?.GetValue<string>();
    if (definitionHandle is not null)
    {
        var wrong = await Execute("block_get_attributes", Doc(documentId, ("handle", definitionHandle)));
        Console.WriteLine($"-- block_get_attributes[definition handle {definitionHandle}] success={wrong.Success} code={wrong.Error?.Code} message={wrong.Error?.Message}");
    }

    var missing = await Execute("block_get_attributes", Doc(documentId, ("handle", "DEADBEEF")));
    Console.WriteLine($"-- block_get_attributes[DEADBEEF] success={missing.Success} code={missing.Error?.Code}");

    var capped = await Execute("block_reference_list", Doc(documentId, ("max_scan", 100)));
    Console.WriteLine($"-- block_reference_list[max_scan=100] success={capped.Success} code={capped.Error?.Code} scanned={capped.Data?["scanned_count"]} truncated={capped.Data?["truncated"]} by={capped.Data?["truncated_by"]}");
}

async Task<(string Tool, string Name, string Handle)?> FindHandle(string documentId, string tool, string collection)
{
    var response = await Execute(tool, Doc(documentId));
    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"-- {tool} success={response.Success} code={response.Error?.Code}");
        return null;
    }

    var items = response.Data[collection]?.AsArray();
    if (items is null || items.Count == 0)
    {
        return null;
    }

    foreach (var item in items)
    {
        var itemHandle = item?["handle"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(itemHandle))
        {
            return (tool, item?["name"]?.GetValue<string>() ?? "(unnamed)", itemHandle);
        }
    }

    return null;
}

static JsonObject Doc(string documentId, params (string Key, object Value)[] extra)
{
    var payload = new JsonObject { ["document_id"] = documentId };
    foreach (var (key, value) in extra)
    {
        payload[key] = value switch
        {
            bool flag => JsonValue.Create(flag),
            string text => JsonValue.Create(text),
            double number => JsonValue.Create(number),
            int whole => JsonValue.Create(whole),
            _ => JsonValue.Create(value.ToString())
        };
    }

    return payload;
}

async Task<ToolResponse> Execute(string tool, JsonObject? arguments, ConfirmationToken? confirmation = null)
{
    var payload = arguments?.DeepClone().AsObject() ?? new JsonObject();
    payload["instance_id"] = instanceId;
    var documentId = payload["document_id"]?.GetValue<string>();

    var request = new ToolRequest
    {
        CorrelationId = "live-" + tool,
        SessionId = sessionId,
        InstanceId = instanceId,
        DocumentId = documentId,
        ToolName = tool,
        Arguments = payload,
        ConfirmationToken = confirmation
    };

    var response = await client.SendAsync(pipeName, "tools/execute", request, timeout);
    if (response.Error is not null)
    {
        return new ToolResponse
        {
            Success = false,
            Tool = tool,
            CorrelationId = request.CorrelationId,
            Error = new ErrorEnvelope { Code = response.Error.Code, Message = response.Error.Message }
        };
    }

    return response.Result.Deserialize<ToolResponse>(JsonContractOptions.Default)!;
}

static void Print(string label, ToolResponse response)
{
    Console.WriteLine($"-- {label} success={response.Success} code={response.Error?.Code} message={response.Error?.Message}");
    if (response.Data is not null)
    {
        var json = response.Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json.Length <= 3000 ? json : json[..3000] + $"...[{json.Length} chars]");
    }
}

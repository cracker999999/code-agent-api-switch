using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using APISwitch.Models;
using APISwitch.Services;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static JsonObject Json(string value) => JsonNode.Parse(value)!.AsObject();
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static JsonObject Request(bool stream = false) => new()
    {
        ["model"] = "models/gemini-3-flash-preview",
        ["max_tokens"] = 1024,
        ["stream"] = stream,
        ["messages"] = new JsonArray(Json("""{"role":"user","content":"你好"}"""))
    };

    private const string Answer = """
        {"responseId":"response-1","modelVersion":"gemini-3-flash-preview",
         "candidates":[{"content":{"role":"model","parts":[{"text":"你好"}]},"finishReason":"STOP"}],
         "usageMetadata":{"promptTokenCount":20,"cachedContentTokenCount":5,"candidatesTokenCount":3,"thoughtsTokenCount":4,"totalTokenCount":27}}
        """;

    public static async Task Main()
    {
        var directory = Directory.CreateTempSubdirectory("apiswitch-proxy-tests-");
        try
        {
            RequestMapping();
            ThinkingMapping();
            ToolRoundTrip();
            ToolHistoryChanges();
            StreamMapping();
            ChatStreamMapping();
            var database = DatabaseMigration(Path.Combine(directory.FullName, "test.db"));
            await ProxyAndProbeAsync(database);
            Console.WriteLine("PASS: Gemini 请求、签名工具回合、SSE/JSON、错误、数据库迁移、模型分页、连通测试及 Chat 适配回归。");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    private static void RequestMapping()
    {
        foreach (var (baseUrl, expected) in new[]
        {
            ("https://example.test", "/v1beta/models/m:generateContent"),
            ("https://example.test/v1beta/", "/v1beta/models/m:generateContent"),
            ("https://example.test/gateway/v1", "/gateway/v1/models/m:generateContent"),
            ("https://example.test/gateway/v2alpha3", "/gateway/v2alpha3/models/m:generateContent"),
            ("https://example.test/gateway/V1BETA", "/gateway/V1BETA/models/m:generateContent"),
            ("https://example.test/gateway/v1preview", "/gateway/v1preview/v1beta/models/m:generateContent"),
            ("https://example.test/gateway", "/gateway/v1beta/models/m:generateContent")
        })
            Assert(GeminiNativeApi.BuildUrl(baseUrl, "models/m:generateContent").AbsolutePath == expected, "版本路径重复或丢失网关前缀");
        Assert(GeminiNativeApi.BuildUrl("https://example.test/v1beta?tenant=a&alt=json", "models/m:streamGenerateContent", "alt", "sse").Query
            == "?tenant=a&alt=sse", "SSE 查询参数必须替换 alt 并保留其他参数");
        var queryUrl = GeminiNativeApi.BuildUrl(
            "https://example.test?tenant=a%2Bb%26c%3Dd+e&tag=one&tag=two&alt=json&%61lt=xml",
            "models/m:streamGenerateContent", "alt", "sse");
        var query = HttpUtility.ParseQueryString(queryUrl.Query);
        Assert(query["tenant"] == "a+b&c=d e" && query.GetValues("tag")!.SequenceEqual(new[] { "one", "two" }) &&
            query.GetValues("alt")!.SequenceEqual(new[] { "sse" }), "替换查询参数不能破坏编码值或其他重复参数");
        const string pageToken = "next/token+with=空 格&value";
        var pageUrl = GeminiNativeApi.BuildUrl(queryUrl.AbsoluteUri, "models", "pageToken", pageToken);
        Assert(HttpUtility.ParseQueryString(pageUrl.Query)["pageToken"] == pageToken, "分页令牌必须只编码一次");

        var body = Request(true);
        body["system"] = JsonNode.Parse("""[{"type":"text","text":"系统","cache_control":{"type":"ephemeral"}}]""");
        body["tools"] = JsonNode.Parse("""
            [{"name":"read","input_schema":{"type":"object","properties":{"path":{"type":["string","null"]}},
              "required":["path"],"additionalProperties":false}}]
            """);
        body["tool_choice"] = Json("""{"type":"tool","name":"read"}""");
        body["thinking"] = Json("""{"type":"adaptive"}""");
        body["output_config"] = Json("""{"effort":"medium"}""");
        body["messages"]![0]!["content"] = JsonNode.Parse("""
            [{"type":"text","text":"图片"},{"type":"image","source":{"type":"base64","media_type":"image/png","data":"aGVsbG8="}}]
            """);
        var before = body.ToJsonString();
        var converted = ClaudeGeminiTransform.ToGenerateContent(body);
        Assert(converted.Model == "gemini-3-flash-preview" && converted.Stream, "模型前缀或 stream 丢失");
        Assert(converted.Body["model"] is null && converted.Body["stream"] is null, "Gemini 请求体不应带路由字段");
        Assert(converted.Body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>() == "系统", "system 丢失");
        Assert(converted.Body["contents"]![0]!["parts"]![1]!["inlineData"]!["mimeType"]!.GetValue<string>() == "image/png", "图片丢失");
        Assert(converted.Body["tools"]![0]!["functionDeclarations"]![0]!["parametersJsonSchema"]!["additionalProperties"]!.GetValue<bool>() == false, "schema 约束丢失");
        Assert(converted.Body["toolConfig"]!["functionCallingConfig"]!["allowedFunctionNames"]![0]!.GetValue<string>() == "read", "指定工具丢失");
        Assert(converted.Body["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>() == "medium", "Gemini 3 思考档位错误");
        Assert(before == body.ToJsonString(), "转换不能修改调用方请求");
        body["model"] = "gemini-2.5-pro";
        body["thinking"] = Json("""{"type":"disabled"}""");
        var thinking = ClaudeGeminiTransform.ToGenerateContent(body).Body["generationConfig"]!["thinkingConfig"]!;
        Assert(thinking["thinkingBudget"]!.GetValue<int>() == 128 && thinking["thinkingLevel"] is null, "2.5 Pro 应使用最低思考预算");
        body["messages"] = JsonNode.Parse("""[{"role":"user","content":[{"type":"tool_result","tool_use_id":"missing","content":"ok"}]}]""");
        ExpectInvalid(() => ClaudeGeminiTransform.ToGenerateContent(body), "无匹配工具的结果必须报错");
    }

    private static void ThinkingMapping()
    {
        var cases = new (string? Type, string? Effort, long? Budget, string? Level)[]
        {
            ("adaptive", "low", null, "low"),
            ("adaptive", "medium", null, "medium"),
            ("adaptive", "high", null, "high"),
            ("adaptive", "xhigh", null, "high"),
            ("adaptive", "max", null, "high"),
            ("adaptive", null, null, "high"),
            ("enabled", null, 3999, "low"),
            ("enabled", null, 4000, "medium"),
            ("enabled", null, 15999, "medium"),
            ("enabled", null, 16000, "high"),
            ("enabled", "high", 1024, "high"),
            ("disabled", "high", null, "low"),
            (null, null, null, null)
        };
        foreach (var model in new[] { "models/gemini-4-neon", "gemini-4-carbon" })
        {
            foreach (var (type, effort, budget, level) in cases)
            {
                var body = Request();
                body["model"] = model;
                if (type is not null)
                    body["thinking"] = new JsonObject { ["type"] = type };
                if (budget is not null)
                    body["thinking"]!["budget_tokens"] = budget.Value;
                if (effort is not null)
                    body["output_config"] = new JsonObject { ["effort"] = effort };
                var thinking = ClaudeGeminiTransform.ToGenerateContent(body).Body["generationConfig"]!["thinkingConfig"];
                Assert(level is null ? thinking is null : thinking?["thinkingLevel"]?.GetValue<string>() == level,
                    $"{model} 思考档位错误：type={type}, effort={effort}, budget={budget}");
                Assert(thinking?["thinkingBudget"] is null, "Gemini 4 不应发送 thinkingBudget");
            }
        }
    }

    private static void ToolRoundTrip()
    {
        var store = new GeminiToolCallStore();
        var upstream = Json("""
            {"candidates":[{"content":{"parts":[
                {"thought":true,"text":"内部摘要"},
                {"functionCall":{"id":"upstream-call","name":"read","args":{"path":"a"}},"thoughtSignature":"signed"},
                {"functionCall":{"name":"read","args":{"path":"b"}}}
            ]},"finishReason":"STOP"}]}
            """);
        var message = ClaudeGeminiTransform.ToMessages(upstream, "gemini-3-flash-preview", store, "provider/model");
        var content = message["content"]!.AsArray();
        Assert(content.Count == 2 && message["stop_reason"]!.GetValue<string>() == "tool_use", "并行工具调用映射失败");
        Assert(content[0]!["id"]!.GetValue<string>() != content[1]!["id"]!.GetValue<string>(), "无上游 ID 的工具调用必须生成唯一 ID");
        var body = Request();
        body["messages"]!.AsArray().Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
        foreach (var tool in content)
            body["messages"]!.AsArray().Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result", ["tool_use_id"] = tool!["id"]!.DeepClone(),
                    ["content"] = "result", ["is_error"] = true
                })
            });
        var converted = ClaudeGeminiTransform.ToGenerateContent(body, store, "provider/model").Body;
        var parts = converted["contents"]![1]!["parts"]!;
        Assert(parts[0]!["thoughtSignature"]!.GetValue<string>() == "signed", "后续轮次必须原样回传 thoughtSignature");
        Assert(parts[0]!["functionCall"]!["id"]!.GetValue<string>() == "upstream-call", "真实上游 ID 丢失");
        Assert(parts[1]!["functionCall"]!["id"] is null, "不能把合成的 Claude ID 发给 Gemini");
        var results = converted["contents"]![2]!["parts"]!.AsArray();
        Assert(results.Count == 2 && results[0]!["functionResponse"]!["name"]!.GetValue<string>() == "read", "并行结果必须合并且还原工具名称");
        Assert(results[0]!["functionResponse"]!["response"]!["error"] is not null, "工具错误信息丢失");
        Assert(results[0]!["functionResponse"]!["id"]!.GetValue<string>() == "upstream-call", "functionResponse 应匹配真实 ID");
        Assert(results[1]!["functionResponse"]!["id"] is null, "functionResponse 不应发送合成 ID");
        foreach (var model in new[] { "gemini-3-flash-preview", "gemini-4-neon", "models/gemini-4-carbon" })
        {
            var replay = body.DeepClone().AsObject();
            replay["model"] = model;
            foreach (var (cache, scope) in new[] { (new GeminiToolCallStore(), "provider/model"), (store, "other-provider") })
            {
                var restored = ClaudeGeminiTransform.ToGenerateContent(replay, cache, scope).Body;
                Assert(restored["contents"]![1]!["parts"]![0]!["thoughtSignature"]?.GetValue<string>() == "skip_thought_signature_validator",
                    $"{model} 在重启或切换供应商后必须补充工具签名占位值");
            }
        }
        body["messages"]![1]!["content"]![0]!["name"] = "other";
        ExpectInvalid(() => ClaudeGeminiTransform.ToGenerateContent(body, store, "provider/model"), "历史工具名称改变必须报错");
    }

    private static void ToolHistoryChanges()
    {
        foreach (var model in new[] { "gemini-3-flash-preview", "gemini-4-neon", "gemini-4-carbon", "gemini-2.5-pro" })
        foreach (var hasId in new[] { false, true })
        {
            var store = new GeminiToolCallStore();
            var part = Json("""
                {"functionCall":{"name":"Edit","args":{"file_path":"a.txt","old_string":"old","new_string":"new"}},
                 "thoughtSignature":"signed"}
                """);
            if (hasId)
                part["functionCall"]!["id"] = "upstream-call";
            var tool = ClaudeGeminiTransform.ToolUse(part, store, "scope");
            var body = Request();
            body["model"] = model;
            body["messages"]!.AsArray().Add(new JsonObject
            {
                ["role"] = "assistant", ["content"] = new JsonArray(tool.DeepClone())
            });
            body["messages"]!.AsArray().Add(new JsonObject
            {
                ["role"] = "user", ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result", ["tool_use_id"] = tool["id"]!.DeepClone(), ["content"] = "updated"
                })
            });
            foreach (var changedInput in new[]
            {
                Json("""{"file_path":"a.txt","old_string":"old","new_string":"new","replace_all":false}"""),
                Json("""{"file_path":"E:\\work\\a.txt","old_string":"old","new_string":"new"}"""),
                Json("""{"file_path":"a.txt","old_string":"old","new_string":"changed"}""")
            })
            {
                var replay = body.DeepClone().AsObject();
                replay["messages"]![1]!["content"]![0]!["input"] = changedInput.DeepClone();
                var converted = ClaudeGeminiTransform.ToGenerateContent(replay, store, "scope").Body;
                var replayedPart = converted["contents"]![1]!["parts"]![0]!;
                var result = converted["contents"]![2]!["parts"]![0]!["functionResponse"]!;
                Assert(JsonNode.DeepEquals(replayedPart["functionCall"]!["args"], changedInput), "历史回放必须使用当前工具参数");
                Assert(replayedPart["thoughtSignature"]?.GetValue<string>() ==
                    (model.StartsWith("gemini-2.5", StringComparison.Ordinal) ? null : "skip_thought_signature_validator"),
                    "参数变化后不能沿用旧签名，Gemini 3/4 必须使用无签名历史占位值");
                Assert(JsonNode.DeepEquals(replayedPart["functionCall"]!["id"], part["functionCall"]!["id"]) &&
                    JsonNode.DeepEquals(result["id"], part["functionCall"]!["id"]) &&
                    result["name"]!.GetValue<string>() == "Edit", "重建调用与工具结果必须保留同一上游 ID 和工具名称");
                Assert(JsonNode.DeepEquals(converted, ClaudeGeminiTransform.ToGenerateContent(replay, store, "scope").Body),
                    "同一历史重试必须得到一致请求");
            }
            Assert(JsonNode.DeepEquals(store.Get("scope", tool["id"]!.GetValue<string>()), part), "历史回放不能修改原签名缓存");
            var unchanged = ClaudeGeminiTransform.ToGenerateContent(body, store, "scope").Body;
            Assert(JsonNode.DeepEquals(unchanged["contents"]![1]!["parts"]![0], part), "未改变的历史仍须原样回传签名");
            body["messages"]![1]!["content"]![0]!["input"] = new JsonArray();
            ExpectInvalid(() => ClaudeGeminiTransform.ToGenerateContent(body, store, "scope"), "参数变化兼容不能绕过 input 对象校验");
        }
    }

    private static void StreamMapping()
    {
        var store = new GeminiToolCallStore();
        var state = new GeminiToClaudeStream("gemini-3-flash-preview", store, "scope");
        var frames = new List<string>();
        frames.AddRange(state.Handle(Json("""{"candidates":[{"content":{"parts":[{"text":"你"}]}}]}""")));
        frames.AddRange(state.Handle(Json("""
            {"candidates":[{"content":{"parts":[{"text":"好"},
             {"functionCall":{"id":"call","name":"read","args":{"path":"a"}},"thoughtSignature":"signature"},
             {"functionCall":{"name":"read","args":{"path":"b"}}}
            ]},"finishReason":"STOP"}]}
            """)));
        frames.AddRange(state.Handle(Json("""{"usageMetadata":{"promptTokenCount":20,"cachedContentTokenCount":5,"totalTokenCount":27}}""")));
        frames.AddRange(state.Complete());
        var events = Events(string.Concat(frames));
        Assert(events[0]["type"]!.GetValue<string>() == "message_start" &&
            events[^1]["type"]!.GetValue<string>() == "message_stop", "SSE 生命周期不完整");
        Assert(string.Concat(events.Where(e => e["delta"]?["type"]?.GetValue<string>() == "text_delta")
            .Select(e => e["delta"]!["text"]!.GetValue<string>())) == "你好", "流式文本应逐段保留");
        var start = events.Where(e => e["type"]!.GetValue<string>() == "content_block_start").ToList();
        var stop = events.Where(e => e["type"]!.GetValue<string>() == "content_block_stop").ToList();
        Assert(start.Count == 3 && stop.Count == 3 &&
            start.Select(e => e["index"]!.GetValue<int>()).SequenceEqual(new[] { 0, 1, 2 }), "内容块索引或关闭事件错误");
        var final = events.Single(e => e["type"]!.GetValue<string>() == "message_delta");
        Assert(final["usage"]!["input_tokens"]!.GetValue<int>() == 15 &&
            final["usage"]!["cache_read_input_tokens"]!.GetValue<int>() == 5 &&
            final["usage"]!["output_tokens"]!.GetValue<int>() == 7, "尾帧 usage 应扣缓存并包含思考 token");
        var toolId = start[1]["content_block"]!["id"]!.GetValue<string>();
        Assert(store.Get("scope", toolId)!["thoughtSignature"]!.GetValue<string>() == "signature", "流式工具签名没有保存");
        Assert(!state.Complete().Any(), "终态不应重复输出");
        var truncated = new GeminiToClaudeStream("m", store, "scope");
        truncated.Handle(Json("""{"candidates":[{"content":{"parts":[{"functionCall":{"name":"run","args":{}}}]}}]}""")).ToList();
        var failure = string.Concat(truncated.Complete());
        Assert(failure.Contains("event: error") && !failure.Contains("tool_use") && !failure.Contains("message_stop"), "半截流不能执行工具或宣告成功");
        var refusal = ClaudeGeminiTransform.ToMessages(Json("""{"promptFeedback":{"blockReason":"SAFETY"}}"""), "m", store, "scope");
        Assert(refusal["stop_reason"]!.GetValue<string>() == "refusal", "安全拒绝必须保留");
        ExpectInvalid(() => ClaudeGeminiTransform.ToMessages(Json("{}"), "m", store, "scope"), "空响应不能当成功");
    }

    private static void ChatStreamMapping()
    {
        var state = new ChatToClaudeStream("chat-model");
        var frames = state.Handle(Json("""
            {"choices":[{"delta":{"content":"你好\nSSE","tool_calls":[
              {"index":0,"id":"call-1","function":{"name":"read","arguments":"{\"path\":\"a\"}"}}
            ]},"finish_reason":"tool_calls"}]}
            """)).Concat(state.Complete()).ToList();
        foreach (var frame in frames)
        {
            var body = Events(frame).Single();
            Assert(frame.StartsWith($"event: {body["type"]}\ndata: ", StringComparison.Ordinal) &&
                frame.EndsWith("\n\n", StringComparison.Ordinal), "SSE 帧名称或边界错误");
        }
        var events = Events(string.Concat(frames));
        var starts = events.Where(e => e["type"]!.GetValue<string>() == "content_block_start").ToList();
        var stops = events.Where(e => e["type"]!.GetValue<string>() == "content_block_stop").ToList();
        Assert(starts.Select(e => e["index"]!.GetValue<int>()).SequenceEqual(new[] { 0, 1 }) &&
            stops.Select(e => e["index"]!.GetValue<int>()).SequenceEqual(new[] { 0, 1 }), "Chat 内容块必须成对关闭");
        Assert(starts[1]["content_block"]!["name"]!.GetValue<string>() == "read" &&
            events.Single(e => e["delta"]?["type"]?.GetValue<string>() == "input_json_delta")["delta"]!["partial_json"]!
                .GetValue<string>() == "{\"path\":\"a\"}", "Chat 工具参数必须完整保留");
        Assert(events.Single(e => e["delta"]?["type"]?.GetValue<string>() == "text_delta")["delta"]!["text"]!
            .GetValue<string>() == "你好\nSSE" && events[^1]["type"]!.GetValue<string>() == "message_stop", "Chat 文本转义或终态错误");
    }

    private static DatabaseService DatabaseMigration(string path)
    {
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Providers (Id INTEGER PRIMARY KEY, ToolType INTEGER, Name TEXT, BaseUrl TEXT, ApiKey TEXT,
                    IsActive INTEGER, SortOrder INTEGER, TestStatus INTEGER, UseChatCompletions INTEGER NOT NULL DEFAULT 0);
                INSERT INTO Providers VALUES (1,1,'existing','https://example.test','fake',1,0,1,1);
                """;
            command.ExecuteNonQuery();
        }
        var database = new DatabaseService(path);
        database.Initialize();
        var old = database.GetProviders(1).Single();
        Assert(old.UseChatCompletions && !old.UseGeminiNative && old.IsActive, "数据库迁移不能改变旧供应商");
        old.UseChatCompletions = false;
        old.UseGeminiNative = true;
        database.UpdateProvider(old);
        Assert(database.GetProviders(1).Single().UseGeminiNative, "编辑没有保存 Gemini 开关");
        var id = database.AddProvider(new Provider { ToolType = 1, Name = "Gemini", UseGeminiNative = true });
        Assert(database.GetProviders(1).Single(p => p.Id == id).UsesProxy, "新增没有保存 Gemini 开关");
        var clipboard = JsonSerializer.Deserialize<ProviderClipboardData>(JsonSerializer.Serialize(
            new ProviderClipboardData { UseGeminiNative = true }))!;
        Assert(clipboard.UseGeminiNative && !JsonSerializer.Deserialize<ProviderClipboardData>("{}")!.UseGeminiNative, "剪贴板新旧格式兼容失败");
        return database;
    }

    private static async Task ProxyAndProbeAsync(DatabaseService database)
    {
        var upstreamUrl = FreeUrl();
        using var upstream = new HttpListener();
        upstream.Prefixes.Add(upstreamUrl + "/");
        upstream.Start();
        var proxyUrl = FreeUrl();
        using var proxy = new ClaudeProxyService(proxyUrl);
        var provider = new Provider
        {
            Id = 7, ToolType = 1, BaseUrl = upstreamUrl + "/gateway", ApiKey = "fake-test-key",
            TestModel = "gemini-3-flash-preview", UseGeminiNative = true
        };
        proxy.Start(provider);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        async Task<HttpResponseMessage> Exchange(JsonObject body, string reply, string mime = "application/json",
            int status = 200, Action<HttpListenerRequest, JsonObject>? check = null)
        {
            var serve = Serve(upstream, reply, mime, status, check);
            var send = client.PostAsync(proxyUrl + "/v1/messages?beta=true",
                new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            await Task.WhenAll(serve, send);
            return await send;
        }
        using (var response = await Exchange(Request(), Answer, check: (request, body) =>
        {
            Assert(request.Url!.AbsolutePath == "/gateway/v1beta/models/gemini-3-flash-preview:generateContent", "非流式路径错误");
            Assert(request.Headers["x-goog-api-key"] == "fake-test-key" && request.Headers["Authorization"] is null, "Gemini 鉴权头错误");
            Assert(body["contents"] is JsonArray && body["messages"] is null, "代理没有转换请求");
        }))
        {
            var message = Json(await response.Content.ReadAsStringAsync());
            Assert(response.IsSuccessStatusCode && message["content"]![0]!["text"]!.GetValue<string>() == "你好", "HTTP JSON 转换失败");
        }
        using (var response = await Exchange(Request(true), "data: " + Answer.ReplaceLineEndings("") + "\n\n", "text/event-stream",
            check: (request, _) => Assert(request.Url!.AbsolutePath.EndsWith(":streamGenerateContent") && request.Url.Query == "?alt=sse", "流式地址错误")))
        {
            var events = Events(await response.Content.ReadAsStringAsync());
            Assert(response.Content.Headers.ContentType!.MediaType == "text/event-stream" &&
                events[^1]["type"]!.GetValue<string>() == "message_stop", "HTTP SSE 转换失败");
        }
        using (var response = await Exchange(Request(true), Answer))
            Assert((await response.Content.ReadAsStringAsync()).Contains("event: message_stop"), "上游 JSON 响应必须能转换为 SSE");
        using (var response = await Exchange(Request(), """{"error":{"code":429,"message":"quota"}}""", status: 429))
            Assert((int)response.StatusCode == 429 && (await response.Content.ReadAsStringAsync()).Contains("quota"), "上游错误状态或信息丢失");
        using (var response = await Exchange(Request(true), "data: invalid-json\n\n", "text/event-stream"))
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert(text.Contains("event: error") && !text.Contains("message_stop"), "损坏 SSE 不能伪装成功");
        }
        var apiTest = new ApiTestService(new AppSettingsService(database));
        var invalidModelProbe = await apiTest.TestProviderAsync(new Provider
        {
            ToolType = 1, BaseUrl = provider.BaseUrl, ApiKey = provider.ApiKey,
            TestModel = "models/", UseGeminiNative = true
        });
        Assert(!invalidModelProbe.Success && invalidModelProbe.Message.Contains("model") && invalidModelProbe.ResponseTimeMs is null,
            "无效 Gemini 模型名称必须返回失败结果和校验信息，不能向 UI 抛出异常");
        var models = new ModelDiscoveryService(apiTest);
        var discoveryServe = Serve(upstream, """{"models":[{"name":"models/gemini-3-flash-preview"}],"nextPageToken":"next/token"}""",
            check: (request, _) => Assert(request.Url!.AbsolutePath == "/gateway/v1beta/models", "模型发现路径错误"));
        var discovery = models.GetModelsAsync(provider);
        await discoveryServe;
        await Serve(upstream, """{"models":[{"name":"models/gemini-2.5-pro"}]}""",
            check: (request, _) => Assert(HttpUtility.ParseQueryString(request.Url!.Query)["pageToken"] == "next/token" &&
                request.Headers["x-goog-api-key"] == "fake-test-key", "分页令牌或鉴权错误"));
        var discovered = await discovery;
        Assert(discovered.Success && discovered.Models.SequenceEqual(new[] { "gemini-3-flash-preview", "gemini-2.5-pro" }), "模型分页有遗漏");
        var fallbackServe1 = Serve(upstream, """{"error":{"message":"Invalid URL (GET /v1beta/models)"}}""", status: 404,
            check: (request, _) => Assert(request.Url!.AbsolutePath == "/gateway/v1beta/models", "回退前应先请求 Gemini 模型列表"));
        var fallbackDiscovery = models.GetModelsAsync(provider);
        await fallbackServe1;
        await Serve(upstream, """{"data":[{"id":"gemini-3-flash-preview"}]}""",
            check: (request, _) => Assert(request.Url!.AbsolutePath == "/gateway/v1/models" &&
                request.Headers["Authorization"] == "Bearer fake-test-key" && request.Headers["x-goog-api-key"] is null, "404 回退路径或鉴权错误"));
        var fallbackResult = await fallbackDiscovery;
        Assert(fallbackResult.Success && fallbackResult.Models.SequenceEqual(new[] { "gemini-3-flash-preview" }), "404 回退解析失败");
        var sseAnswer = ": heartbeat\n\nevent: message\nid: 1\nretry: 1000\ndata: " + Answer.ReplaceLineEndings("") + "\n\n";
        var multilineAnswer = "data: " + Answer.ReplaceLineEndings("\ndata: ") + "\n\n";
        foreach (var (reply, mime) in new[]
        {
            (Answer, "application/json"), (sseAnswer, "text/event-stream"), (multilineAnswer, "text/event-stream")
        })
        {
            var probeServe = Serve(upstream, reply, mime, check: (request, body) =>
                Assert(request.Url!.AbsolutePath.EndsWith(":streamGenerateContent") && body["contents"] is JsonArray, "连通测试没有使用 Gemini 协议"));
            var probe = apiTest.TestProviderAsync(provider);
            await Task.WhenAll(probeServe, probe);
            Assert((await probe).Success, "有效 Gemini JSON 或 SSE（含心跳、控制字段、多行 JSON）应测试成功");
        }
        foreach (var (reply, mime) in new[]
        {
            ("""{"promptFeedback":{"blockReason":"SAFETY"}}""", "application/json"),
            ("{}", "application/json"),
            ("""{"candidates":[{"finishReason":"FINISH_REASON_UNSPECIFIED"}]}""", "application/json"),
            ("""{"candidates":[{"content":{"parts":[null]},"finishReason":"STOP"}]}""", "application/json"),
            ("""{"candidates":[{"content":{"parts":[{"thought":"invalid"}]},"finishReason":"STOP"}]}""", "application/json"),
            ("""{"candidates":[{"content":{"parts":[{"functionCall":{"name":"read","args":[]}}]},"finishReason":"STOP"}]}""", "application/json"),
            ("""{"candidates":[{"content":{"parts":[{"functionCall":{"args":{}}}]},"finishReason":"STOP"}]}""", "application/json"),
            ("""{"candidates":[{"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":"invalid"}}""", "application/json"),
            ("data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial\"}]}}]}\n\n", "text/event-stream")
        })
        {
            var failedServe = Serve(upstream, reply, mime);
            var failedProbe = apiTest.TestProviderAsync(provider);
            await Task.WhenAll(failedServe, failedProbe);
            Assert(!(await failedProbe).Success, "Gemini 拒绝或提前结束不能显示测试成功");
        }
        foreach (var finish in new[] { "SAFETY", "RECITATION", "SPII", "BLOCKLIST", "PROHIBITED_CONTENT", "MALFORMED_FUNCTION_CALL", "UNEXPECTED_TOOL_CALL" })
        {
            var payload = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["finishReason"] = finish }) }.ToJsonString();
            foreach (var mime in new[] { "application/json", "text/event-stream" })
            {
                var reply = mime == "application/json" ? payload : sseAnswer + $"data: {payload}\n\n";
                var serve = Serve(upstream, reply, mime);
                var probe = apiTest.TestProviderAsync(provider);
                await Task.WhenAll(serve, probe);
                Assert(!probe.Result.Success && probe.Result.Message.Contains(finish), "JSON 和 SSE 必须保留拒绝或工具错误原因，终态不能掩盖尾帧错误");
            }
        }
        var errorServe = Serve(upstream, sseAnswer + "data: {\"error\":{\"message\":\"late-error\"}}\n\n", "text/event-stream");
        var errorProbe = apiTest.TestProviderAsync(provider);
        await Task.WhenAll(errorServe, errorProbe);
        Assert(!errorProbe.Result.Success && errorProbe.Result.Message.Contains("late-error"), "终态后也必须校验上游错误");
        foreach (var payload in new[] { "{broken", "invalid-json", "null", "[]", "" })
        {
            var corruptFrame = $"data: {payload}\n\n";
            foreach (var reply in new[] { corruptFrame + sseAnswer, sseAnswer + corruptFrame })
            {
                var failedServe = Serve(upstream, reply, "text/event-stream");
                var failedProbe = apiTest.TestProviderAsync(provider);
                await Task.WhenAll(failedServe, failedProbe);
                var failure = await failedProbe;
                Assert(!failure.Success && failure.Message.Contains("JSON"),
                    $"损坏 Gemini 数据帧 {payload} 不能被正常终态掩盖，必须返回 JSON 错误");
                using var response = await Exchange(Request(true), reply, "text/event-stream");
                var events = await response.Content.ReadAsStringAsync();
                Assert(events.Contains("event: error") && !events.Contains("event: message_stop"),
                    "同一损坏 Gemini 流在测试与实际代理中都必须失败");
            }
        }
        provider.UseGeminiNative = false;
        provider.UseChatCompletions = true;
        proxy.Start(provider);
        using var chatResponse = await Exchange(Request(),
            """{"id":"chat-1","choices":[{"message":{"role":"assistant","content":"chat"},"finish_reason":"stop"}]}""",
            check: (request, body) => Assert(request.Url!.AbsolutePath == "/gateway/chat/completions" &&
                request.Headers["Authorization"] == "Bearer fake-test-key" && body["messages"] is JsonArray, "原 Chat 适配被改变"));
        Assert(Json(await chatResponse.Content.ReadAsStringAsync())["content"]![0]!["text"]!.GetValue<string>() == "chat", "原 Chat 响应转换回归");
        foreach (var (reply, mime) in new[]
        {
            ("""{"choices":[{"message":{"content":"chat"},"finish_reason":"stop"}]}""", "application/json"),
            ("data: {\"choices\":[{\"delta\":{\"content\":\"chat\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", "text/event-stream")
        })
        {
            using var response = await Exchange(Request(true), reply, mime);
            var events = Events(await response.Content.ReadAsStringAsync());
            Assert(response.IsSuccessStatusCode && response.Content.Headers.ContentType!.MediaType == "text/event-stream" &&
                events.Single(e => e["delta"]?["type"]?.GetValue<string>() == "text_delta")["delta"]!["text"]!.GetValue<string>() == "chat" &&
                events[^1]["type"]!.GetValue<string>() == "message_stop", "Chat SSE 和 JSON 回退都必须完整输出文本及终态");
        }
        foreach (var stream in new[] { false, true })
        {
            using var response = await Exchange(Request(stream), "null");
            Assert(response.StatusCode == HttpStatusCode.BadGateway, "Chat JSON 校验必须覆盖非流式和流式回退路径");
        }
    }

    private static async Task Serve(HttpListener listener, string reply, string mime = "application/json", int status = 200,
        Action<HttpListenerRequest, JsonObject>? check = null)
    {
        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            check?.Invoke(context.Request, body.Length == 0 ? new JsonObject() : Json(body));
            context.Response.StatusCode = status;
            context.Response.ContentType = mime;
            var bytes = Encoding.UTF8.GetBytes(reply);
            context.Response.ContentLength64 = bytes.Length;
            // 拆开 UTF-8 和 SSE 边界，检查传输层不会依赖网络分块位置。
            for (var offset = 0; offset < bytes.Length; offset += 17)
                await context.Response.OutputStream.WriteAsync(bytes.AsMemory(offset, Math.Min(17, bytes.Length - offset)));
        }
        finally { context.Response.Close(); }
    }

    private static string FreeUrl()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    private static List<JsonObject> Events(string value) => value.Split('\n')
        .Where(line => line.StartsWith("data: ", StringComparison.Ordinal)).Select(line => Json(line[6..])).ToList();

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
}

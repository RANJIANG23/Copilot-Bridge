using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CopilotBridge.Mcp;

internal static class McpHost
{
    private const string ServerInstructions =
        "The GUI alone determines collaboration mode and model priority. Never retry when send status is uncertain or when canRetrySafely is false. A follow-up must reuse the returned consultationId unless the user explicitly requests a new conversation. Copilot provides advice only; Codex must verify facts, adjudicate recommendations, and perform all execution. Check copilot_bridge_status before consulting. Use trigger=user_explicit only when the user requested the consultation; obey the configured policy for automatic checkpoints.";

    internal static async Task<int> RunAsync()
    {
        await using var tools = new CopilotBridgeTools();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var toolOptions = new McpServerToolCreateOptions
        {
            SerializerOptions = jsonOptions
        };
        var toolCollection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var method in typeof(CopilotBridgeTools)
                     .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            toolCollection.Add(McpServerTool.Create(method, tools, toolOptions));
        }

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "CopilotBridge",
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"
            },
            ServerInstructions = ServerInstructions,
            ToolCollection = toolCollection
        };
        var loggerFactory = NullLoggerFactory.Instance;
        await using var transport = new StdioServerTransport(options, loggerFactory);
        await using var server = McpServer.Create(transport, options, loggerFactory);
        await server.RunAsync();
        return 0;
    }
}

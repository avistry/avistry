using System.Runtime.CompilerServices;

// The maintainers' test assembly (not part of the package) drives the window through its Auto* methods.
[assembly: InternalsVisibleTo("Avistry.Textures.Harness")]
// The optional bridge to MCP for Unity (Editor/McpBridge) only translates tool calls to AgentApi.
[assembly: InternalsVisibleTo("Avistry.Textures.McpBridge")]

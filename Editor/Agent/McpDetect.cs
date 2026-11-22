using System;
using UnityEditor.PackageManager;

namespace Avistry.Textures
{
    /// <summary>
    /// Finds out whether MCP for Unity is installed and whether our optional bridge to it is loaded, by reflection only.
    /// The main assembly never references either of them, so the tool works the same without them.
    /// </summary>
    internal static class McpDetect
    {
        internal const string ToolAttributeType = "MCPForUnity.Editor.Tools.McpForUnityToolAttribute, MCPForUnity.Editor";
        internal const string BridgeType = "Avistry.Textures.McpBridge.AvistryReadTool, Avistry.Textures.McpBridge";
        internal const string PackageName = "com.coplaydev.unity-mcp";
        internal const int SupportedMajor = 10;

        /// <summary>The address Package Manager's "Add package from git URL" takes. Check it against the upstream README at release time.</summary>
        internal const string InstallAddress = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";
        internal const string PageUrl = "https://github.com/CoplayDev/unity-mcp";

        static Type s_attribute;
        static bool s_looked;

        static Type ToolType
        {
            get
            {
                if (!s_looked) { s_attribute = Type.GetType(ToolAttributeType, false); s_looked = true; }
                return s_attribute;
            }
        }

        /// <summary>MCP for Unity's tool attribute is loaded.</summary>
        internal static bool Installed => ToolType != null;

        /// <summary>The installed version, or null when it is not installed or Package Manager does not know it.</summary>
        internal static string Version => ToolType == null ? null : PackageInfo.FindForAssembly(ToolType.Assembly)?.version;

        /// <summary>Installed, and a version we have been checked against (10.x).</summary>
        internal static bool Supported
        {
            get
            {
                string v = Version;
                if (v == null) return false;
                int dot = v.IndexOf('.');
                return int.TryParse(dot < 0 ? v : v.Substring(0, dot), out int major) && major == SupportedMajor;
            }
        }

        /// <summary>Our bridge assembly compiled and loaded, which only happens with a supported MCP for Unity.</summary>
        internal static bool BridgeLoaded => Type.GetType(BridgeType, false) != null;

        /// <summary>"not_installed", "unsupported" or "ready".</summary>
        internal static string StateWord => !Installed ? "not_installed" : Supported && BridgeLoaded ? "ready" : "unsupported";
    }
}

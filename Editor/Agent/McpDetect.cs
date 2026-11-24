using System;
using UnityEditor.PackageManager;

namespace Avistry.Textures
{
    internal static class McpDetect
    {
        internal const string ToolAttributeType = "MCPForUnity.Editor.Tools.McpForUnityToolAttribute, MCPForUnity.Editor";
        internal const string BridgeType = "Avistry.Textures.McpBridge.AvistryReadTool, Avistry.Textures.McpBridge";
        internal const string PackageName = "com.coplaydev.unity-mcp";
        internal const int SupportedMajor = 10;

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

        internal static bool Installed => ToolType != null;

        internal static string Version => ToolType == null ? null : PackageInfo.FindForAssembly(ToolType.Assembly)?.version;

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

        internal static bool BridgeLoaded => Type.GetType(BridgeType, false) != null;

        internal static string StateWord => !Installed ? "not_installed" : Supported && BridgeLoaded ? "ready" : "unsupported";
    }
}

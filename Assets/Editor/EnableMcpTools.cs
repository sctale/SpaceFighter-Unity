#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AuroraRift.EditorTools
{
    /// <summary>
    /// 无头环境下把 MCP 工具全部启用（工具开关是 EditorPrefs 布尔键，
    /// 缺省值来自 metadata.AutoRegister，多数工具默认 false）。
    /// </summary>
    public static class EnableMcpTools
    {
        static readonly string[] Tools =
        {
            "batch_execute","debug_request_context","execute_menu_item","find_gameobjects","find_in_file",
            "manage_asset","manage_build","manage_camera","manage_components","manage_editor",
            "manage_gameobject","manage_graphics","manage_material","manage_packages","manage_physics",
            "manage_prefabs","manage_scene","refresh_unity","apply_text_edits","create_script",
            "delete_script","validate_script","manage_script","manage_script_capabilities","get_sha",
            "manage_tools","read_console","script_apply_edits","set_active_instance",
        };

        /// <summary>启用全部工具并在同一进程内直接把 MCP 桥接拉起来（EditorPrefs 不落盘，只能同进程生效）。</summary>
        public static void RunAndServe()
        {
            RunCore();
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var ty = asm.GetType("MCPForUnity.Editor.McpCiBoot");
                if (ty == null) continue;
                var m = ty.GetMethod("StartStdioForCi");
                if (m != null)
                {
                    m.Invoke(null, null);
                    Debug.Log("[MCP] bridge started in-process");
                }
                break;
            }
        }

        static void RunCore()
        {
            const string prefix = "MCPForUnity.ToolEnabled.";
            int n = 0;
            foreach (var t in Tools) { EditorPrefs.SetBool(prefix + t, true); n++; }
            Debug.Log($"[MCP] enabled {n} tools via EditorPrefs (prefix={prefix})");
        }

        public static void Run()
        {
            RunCore();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif

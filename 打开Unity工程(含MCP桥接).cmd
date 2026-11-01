@echo off
rem Open the Aurora Rift Unity project. Run this from Explorer (not as Administrator):
rem Unity shows a blocking warning when it starts elevated, and the MCP bridge won't come up
rem until that dialog is dismissed.
rem The -executeMethod call starts the MCP stdio bridge on 127.0.0.1:6400 for AI clients.
start "" "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" -projectPath "D:\V-Coding\space-fighter-unity" -executeMethod MCPForUnity.Editor.McpCiBoot.StartStdioForCi

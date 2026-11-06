using System.Linq;
using UnityEditor;
using UnityEngine;
public static class FlareVisualFinish
{
    public static object Run()
    {
        var shader=Shader.Find("Starframe/Combat/VoxelGlow");
        var messages=ShaderUtil.GetShaderMessages(shader).Select(m=>new{m.message,severity=m.severity.ToString()}).ToArray();
        TraceDocsExporter.ExportForAutomation();
        return new{playing=Application.isPlaying,compilationFailed=EditorUtility.scriptCompilationFailed,shaderMessages=messages};
    }
}

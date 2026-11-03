using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class ReferenceMapBuildPlayer
{
    public static object Build()
    {
        const string scene="Assets/_Starframe/Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity";
        const string output="Builds/ReferenceMap3500/ReferenceMap3500.exe";
        if(!File.Exists(scene))throw new InvalidOperationException("Reference map scene is not authored yet.");
        bool timing=PlayerSettings.enableFrameTimingStats;
        bool background=PlayerSettings.runInBackground;
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        try
        {
            PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},target=BuildTarget.StandaloneWindows64,locationPathName=output,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Reference map build failed: "+report.summary.result+"; errors="+report.summary.totalErrors);
            return new {result=report.summary.result.ToString(),path=output,bytes=report.summary.totalSize,seconds=report.summary.totalTime.TotalSeconds,warnings=report.summary.totalWarnings,errors=report.summary.totalErrors};
        }
        finally{PlayerSettings.enableFrameTimingStats=timing;PlayerSettings.runInBackground=background;}
    }
}

// Offline-editor tool only. Alternate five warm-cache verification runs per version.
using System;
using System.IO;
namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        public static string Benchmark()
        {
            string folder=EvidenceRoot+"/bench";
            if(Directory.Exists(folder))throw new Exception("No-overwrite: benchmark folder exists");
            Directory.CreateDirectory(folder);
            for(int i=0;i<5;i++)foreach(string version in new[]{"v03","v04"})
            {
                string root="Assets/_Starframe/Maps/MiningStationRound2/Generated_"+version;
                VerifyAssets(root+"/Assets",root+"/Layouts/LocalDensityPatch.asset",folder+"/"+version+"-"+i+".json");
            }
            return "10 alternating warm-cache runs completed; summarize per-probe median and range, not FPS.";
        }
    }
}

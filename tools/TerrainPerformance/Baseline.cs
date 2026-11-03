namespace Starframe.MiningRound2 {
public static partial class MiningRound2 {
 public static string TerrainBaseline() {
  const string folder="Documentation/TerrainPerformance-2026-11-01/baseline";
  if(System.IO.Directory.Exists(folder)) throw new System.Exception("Baseline already exists; preserve it.");
  System.IO.Directory.CreateDirectory(folder);
  string root="Assets/_Starframe/Maps/MiningStationRound2/Generated_v04";
  for(int i=0;i<5;i++) VerifyAssets(root+"/Assets",root+"/Layouts/LocalDensityPatch.asset",folder+"/run-"+i+".json");
  return "Five unchanged-runtime warm-cache baseline runs completed. CPU ms, not FPS.";
 }
}
}

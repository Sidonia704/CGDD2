using UnityEditor;
public static class ImportBatchWindow
{
 static double until;static bool active;
 public static string Start(int seconds=30)
 {
  if(active)return "Already batching";
  active=true;until=EditorApplication.timeSinceStartup+seconds;
  AssetDatabase.StartAssetEditing();EditorApplication.update+=Tick;
  return "Batching native asset imports for at most "+seconds+" seconds; automatic release registered";
 }
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<until)return;
  EditorApplication.update-=Tick;
  try{AssetDatabase.StopAssetEditing();}finally{active=false;}
 }
}

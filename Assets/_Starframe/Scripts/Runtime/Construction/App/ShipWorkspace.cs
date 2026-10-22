// @role Preserve the working ship and independent deployment snapshots across scenes.
// @calls BlueprintRepository, DeploymentValidator, MissionResults, FlightSession

using System;
using System.IO;
using System.Text;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.App
{
    /// <summary>Editable workspace, separate from named blueprints and battle instances.</summary>
    public static class ShipWorkspace
    {
        [Serializable] sealed class Snapshot{public int version=1;public BlueprintData ship;public bool dirty;public string sourceFile;}
        static Snapshot current;
        static string storageRoot;
        static string StorageRoot=>storageRoot??Application.persistentDataPath;
        public static string BlueprintDirectory=>Path.Combine(StorageRoot,"Blueprints");
        public static string WorkspacePath=>Path.Combine(StorageRoot,"Workspace","current-ship.json");
        public static BlueprintRepository Repository=>new BlueprintRepository(BlueprintDirectory);
        public static BlueprintData Ship{get{EnsureLoaded();return Clone(current.ship);}}
        public static bool Dirty{get{EnsureLoaded();return current.dirty;}}
        public static string SourceFile{get{EnsureLoaded();return current.sourceFile;}}
        public static event Action Changed;
        public static BlueprintData Clone(BlueprintData data)=>data==null?null:JsonUtility.FromJson<BlueprintData>(JsonUtility.ToJson(data));
        public static void EnsureLoaded()
        {
            if(current!=null)return;
            foreach(var path in new[]{WorkspacePath,WorkspacePath+".bak"})try{
                if(File.Exists(path)){
                    var info=new FileInfo(path);if(info.Length>16*1024*1024)throw new InvalidDataException("Workspace exceeds size limit.");
                    var saved=JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
                    if(saved?.version==1&&saved.ship!=null){saved.ship=BlueprintRepository.Upgrade(saved.ship);current=saved;return;}
                }
            }catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){}
            string[] files;try{files=Repository.List();}catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){files=Array.Empty<string>();}
            foreach(var file in files)try{current=new Snapshot{ship=Repository.Load(file),sourceFile=file};return;}catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){}
            current=new Snapshot{ship=FlightRangeBootstrap.DefaultShip(),dirty=true};
        }
        public static void Select(string file)
        {
            var data=Repository.Load(file);Set(data,false,file);
        }
        public static void Set(BlueprintData data,bool dirty,string sourceFile=null)
        {
            if(data==null)throw new ArgumentNullException(nameof(data));
            var copy=BlueprintRepository.Upgrade(Clone(data));
            var next=new Snapshot{ship=copy,dirty=dirty,sourceFile=sourceFile};Write(next);current=next;Changed?.Invoke();
        }
        public static void Persist()
        {
            if(current!=null)Write(current);
        }
        static void Write(Snapshot value)
        {
            string path=WorkspacePath,temp=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path));
            try{File.WriteAllText(temp,JsonUtility.ToJson(value,true),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static void Reload(){current=null;EnsureLoaded();}
        public static IDisposable UseStorageRoot(string root)
        {
            if(string.IsNullOrWhiteSpace(root))throw new ArgumentException(nameof(root));
            var scope=new StorageScope{oldRoot=storageRoot,oldSnapshot=current};storageRoot=Path.GetFullPath(root);current=null;return scope;
        }
        sealed class StorageScope:IDisposable
        {
            public string oldRoot;public Snapshot oldSnapshot;bool disposed;
            public void Dispose(){if(disposed)return;disposed=true;storageRoot=oldRoot;current=oldSnapshot;}
        }
    }
    public enum AppPage{Home,Deployment,WeaponGroups,RangeSetup,Settings,Help,MissionReady,Results}
    public static class AppNavigation
    {
        public const string HomeScene="Hangar";
        public static AppPage ReturnPage=AppPage.Home;
        public static string SelectedMissionId;
        public static MissionDifficulty Difficulty=MissionDifficulty.Standard;
        public static bool HasAppContext;
        public static bool RangeReturnToEditor;
        public static bool SettingsReturnToEditor;
        public static bool WeaponGroupsReturnToEditor;
        public static AppPage EditorResumePage=AppPage.Home;
        public static void OpenHome(AppPage page=AppPage.Home)
        {
            HasAppContext=true;ReturnPage=page;GameInput.Suspended=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            UnityEngine.SceneManagement.SceneManager.LoadScene(HomeScene);
        }
        public static void OpenEditor()
        {
            HasAppContext=true;FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);FlightSession.Return();
            GameInput.Suspended=false;UnityEngine.SceneManagement.SceneManager.LoadScene(FlightSession.EditorScene);
        }
    }
    [Serializable] public sealed class DeploymentRequest
    {
        public string missionId;public MissionDifficulty difficulty;public BlueprintData ship;public float budget,cost;
        public string CreatedUtc;
        public DeploymentRequest Copy()=>JsonUtility.FromJson<DeploymentRequest>(JsonUtility.ToJson(this));
    }
    public static class MissionSession
    {
        static DeploymentRequest current;
        public static DeploymentRequest Current=>current?.Copy();
        public static event Action<DeploymentRequest> LaunchRequested;
        public static bool Confirm(BlueprintData ship,ModuleDatabase modules,MissionDefinition mission,MissionDifficulty difficulty,out DeploymentReport report)
        {
            current=null;MissionResults.Clear();report=DeploymentValidator.Validate(ship,modules,mission,difficulty);if(!report.IsValid)return false;
            BlueprintData snapshot;try{snapshot=BlueprintRepository.Upgrade(ShipWorkspace.Clone(ship));}catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException){report.Errors.Add(new DeploymentIssue("deploy.error.invalidBlueprint"));return false;}
            current=new DeploymentRequest{missionId=mission.id,difficulty=difficulty,ship=snapshot,budget=report.Budget,cost=report.Cost,CreatedUtc=DateTime.UtcNow.ToString("O")};
            LaunchRequested?.Invoke(current.Copy());return true;
        }
        public static void Clear(){current=null;MissionResults.Clear();}
    }
}

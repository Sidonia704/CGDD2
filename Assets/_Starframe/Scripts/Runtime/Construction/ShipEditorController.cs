// @role Handle placement, rotation, deletion and blueprint editing input.
// @calls ShipAssembly, WorkbenchOrbit, ShipEditorUI, BlueprintRepository, FlightSession

using System;
using System.IO;
using UnityEngine;
using Starframe.Presentation;
using UnityEngine.InputSystem;
namespace Starframe.Construction
{
    public sealed class ShipEditorController : MonoBehaviour
    {
        [SerializeField] ShipAssembly assembly;
        [SerializeField] WorkbenchOrbit orbit;
        [SerializeField] ShipEditorUI ui;
        [SerializeField] Material ghostMaterial;
        [SerializeField] Material ghostEdgeMaterial;
        [SerializeField] Material deleteMaterial;
        [SerializeField] Material blockedMaterial;
        [SerializeField] bool showStarterAssembly=true;
        [SerializeField] WorkbenchEnvironment environment;
        BlueprintRepository repository;
        Starframe.Combat.RangeTargetRoster targetRoster;
        GameObject ghost;
        MeshFilter ghostFilter,ghostEdgeFilter;
        MeshRenderer ghostRenderer;
        Transform clearancePreview;
        GameObject corePreviewRay;
        BlockView hover;
        Vector3Int previewCell;
        Pose previewPose;
        Vector3 attachmentNormal=Vector3.up;
        bool validPreview;
        bool dirty;
        bool inputReady;
        int rotation;
        BlockShape selected;
        bool deleting;
        string currentName;
        public ShipAssembly Assembly=>assembly;
        public WorkbenchOrbit Orbit=>orbit;
        MeshRenderer shieldPreview;
        public bool DeleteMode=>deleting;
        public BlockShape SelectedShape=>selected;
        public Vector3Int PreviewCell=>previewCell;
        public bool HasValidPreview=>validPreview;
        public Pose PreviewPose=>previewPose;
        public Vector3 AttachmentNormal=>attachmentNormal;
        public bool Dirty=>dirty;
        public string BlueprintDirectory=>Starframe.App.AppNavigation.HasAppContext?Starframe.App.ShipWorkspace.BlueprintDirectory:Path.Combine(Application.persistentDataPath,"Blueprints");
        void Start()
        {
            repository=new BlueprintRepository(BlueprintDirectory);
            targetRoster=new Starframe.Combat.RangeTargetRoster(BlueprintDirectory);
            assembly.Configure(ui.Database);
            ui.Initialize(this,orbit.Camera);
            if(environment)environment.Initialize(ui.Font);
            orbit.BindUI(ui);
            CreateGhost();gameObject.AddComponent<AssemblyShieldPreview>().Initialize(assembly,ui.Database);
            if(Starframe.Flight.FlightSession.ReturningToEditor&&Starframe.Flight.FlightSession.EditorDraft!=null){
                assembly.Import(Starframe.Flight.FlightSession.EditorDraft);ui.BlueprintName=Starframe.Flight.FlightSession.EditorDraft.name;
                dirty=Starframe.Flight.FlightSession.EditorDirty;Starframe.Flight.FlightSession.Restored();
            }
            else if(showStarterAssembly)
            {
                assembly.Place(BlockShape.Cube,new Vector3Int(0,0,0));
                assembly.Place(BlockShape.Cube,new Vector3Int(0,1,0));
                assembly.Place(BlockShape.Cube,new Vector3Int(0,1,1));
                assembly.Place(BlockShape.Cube,new Vector3Int(1,0,0));
                assembly.Place(BlockShape.Wedge,new Vector3Int(2,0,0),3);
            }
            assembly.Changed+=OnAssemblyChanged;
            ui.SetShape(selected);ui.SetMode(false);ui.UpdateStats(assembly.Count,dirty);
            RefreshFiles();ui.SetStatusKey("editor.status.ready");
        }
        void CreateGhost()
        {
            ghost=new GameObject("PlacementPreview");ghost.layer=2;
            ghostFilter=ghost.AddComponent<MeshFilter>();
            ghostRenderer=ghost.AddComponent<MeshRenderer>();ghostRenderer.sharedMaterial=ghostMaterial;
            ghostRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var edges=new GameObject("PreviewEdges");edges.layer=2;edges.transform.SetParent(ghost.transform,false);
            ghostEdgeFilter=edges.AddComponent<MeshFilter>();
            var renderer=edges.AddComponent<MeshRenderer>();renderer.sharedMaterial=ghostEdgeMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            corePreviewRay=new GameObject("CoreDirectionPreview");corePreviewRay.layer=2;corePreviewRay.transform.SetParent(ghost.transform,false);
            var ray=corePreviewRay.AddComponent<LineRenderer>();ray.useWorldSpace=false;ray.positionCount=2;ray.SetPositions(new[]{Vector3.forward*.51f,Vector3.forward*45});
            ray.startWidth=ray.endWidth=.01f;ray.sharedMaterial=assembly.DirectionMaterial;
            ray.startColor=ray.endColor=new Color(.55f,1,1,.65f);ray.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            UpdateGhostShape();ghost.SetActive(false);
        }
        void OnAssemblyChanged(){dirty=true;ui.UpdateStats(assembly.Count,true);}
        public void SelectShape(BlockShape shape)
        {selected=shape;deleting=false;ResetHover();UpdateGhostShape();ui.SetShape(shape);ui.SetMode(false);}
        void UpdateGhostShape()
        {
            if(!ghostFilter)return;if(corePreviewRay)corePreviewRay.SetActive(selected==BlockShape.Core);ghostFilter.sharedMesh=assembly.Solid(selected);ghostEdgeFilter.sharedMesh=assembly.Outline(selected);
            if(clearancePreview){clearancePreview.gameObject.SetActive(false);Destroy(clearancePreview.gameObject);}
            if(shieldPreview){shieldPreview.gameObject.SetActive(false);Destroy(shieldPreview.gameObject);shieldPreview=null;}
            var definition=ui.Database?ui.Database.Get(selected):null;
            if(definition&&definition.shieldCapacity>0&&definition.shieldSurfaceMaterial)
            {
                shieldPreview=Starframe.Combat.ShipShieldVisual.CreateSurface(ghost.transform,"ShieldPlacementRange",definition.shieldSurfaceMesh);
                shieldPreview.sharedMaterial=definition.shieldSurfaceMaterial;shieldPreview.transform.localScale=Vector3.one*(definition.shieldRadius*2);
            }
            clearancePreview=new GameObject("ExhaustClearance").transform;clearancePreview.SetParent(ghost.transform,false);
            for(int i=0;i<BlockModules.Clearance(selected);i++){
                var cell=new GameObject("EmptyExhaustCell");cell.layer=2;cell.transform.SetParent(clearancePreview,false);
                cell.transform.localPosition=Vector3.up*(BlockModules.BodyLength(selected)+i);
                cell.AddComponent<MeshFilter>().sharedMesh=assembly.Outline(BlockShape.Cube);
                var renderer=cell.AddComponent<MeshRenderer>();renderer.sharedMaterial=ghostEdgeMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        public void ToggleDelete(){deleting=!deleting;ResetHover();ui.SetMode(deleting);ui.SetStatusKey(deleting?"editor.status.deleteMode":"editor.status.buildMode");Starframe.App.GameAudio.Click();}
        void ResetHover(){if(hover && hover.Solid)hover.Solid.sharedMaterial=assembly.MaterialFor(hover.Record.shape);hover=null;}
        void Update()
        {
            if(!repositoryInitialized)return;
            var mouse=Mouse.current;var keyboard=Keyboard.current;
            bool editing=ui.EditingName;
            // Do not consume the editor's Play-button mouse press as a construction click.
            if(!inputReady)
            {
                if(mouse==null || (!mouse.leftButton.isPressed && !mouse.middleButton.isPressed))inputReady=true;
                ghost.SetActive(false);ResetHover();return;
            }
            if(!editing && keyboard!=null)
            {
                if(keyboard.xKey.wasPressedThisFrame)ToggleDelete();
                if(keyboard.rKey.wasPressedThisFrame&&selected!=BlockShape.Core)RotatePreview();
                if(keyboard.fKey.wasPressedThisFrame)orbit.Frame(assembly.ContentBounds());
            }
            ResetHover();validPreview=false;ghost.SetActive(false);
            if(mouse==null || editing || !ui.WorkspaceContains(mouse.position.ReadValue()) || mouse.middleButton.isPressed)
            {ui.SetHover(null);return;}
            var ray=orbit.Camera.ScreenPointToRay(mouse.position.ReadValue());
            BlockView hitPart=null;RaycastHit hit=default;
            if(Physics.Raycast(ray,out hit,1000,1))hitPart=hit.collider.GetComponent<BlockView>();
            if(keyboard!=null&&keyboard.rKey.wasPressedThisFrame&&selected==BlockShape.Core){
                if(hitPart&&hitPart.Record.shape==BlockShape.Core){assembly.RotateCore(hitPart.Record.id);ui.SetStatusKey("editor.status.forward");Starframe.App.GameAudio.Play(Starframe.App.GameSound.RotatePart);}
                else RotatePreview();
            }
            if(deleting)
            {
                if(hitPart)
                {
                    hover=hitPart;hover.Solid.sharedMaterial=deleteMaterial;
                    ui.SetHover(hitPart.Record);
                    if(mouse.leftButton.wasPressedThisFrame){var id=hitPart.Record.id;ResetHover();if(assembly.Remove(id)){ui.SetStatusKey("editor.status.deletedPart");Starframe.App.GameAudio.Play(Starframe.App.GameSound.RemovePart);}else{ui.SetStatusKey("editor.status.cannotDelete",true);Starframe.App.GameAudio.Play(Starframe.App.GameSound.InvalidPlacement);}}
                }
                else ui.SetHover(null);
                return;
            }

            if(hitPart)
            {
                if(!BlockPlacement.TryOnFace(hitPart.Record.shape,hitPart.transform,hit.point,hit.normal,rotation,out previewPose))
                {ui.SetHover(hitPart.Record);return;}
                attachmentNormal=previewPose.rotation*Vector3.up;
            }
            else
            {
                var plane=new Plane(Vector3.up,Vector3.zero);
                if(!plane.Raycast(ray,out float distance)){ui.SetHover(null);return;}
                previewPose=BlockPlacement.OnGround(ray.GetPoint(distance),rotation);
                attachmentNormal=Vector3.up;
            }
            var localPosition=assembly.transform.InverseTransformPoint(previewPose.position);
            var localRotation=Quaternion.Inverse(assembly.transform.rotation)*previewPose.rotation;
            previewCell=BlockPlacement.GridHint(localPosition);
            validPreview=assembly.CanPlace(selected,localPosition,localRotation);
            ghost.transform.SetPositionAndRotation(previewPose.position,previewPose.rotation);
            ghostRenderer.sharedMaterial=validPreview?ghostMaterial:blockedMaterial;ghost.SetActive(true);
            ui.SetHover(new BlockRecord{shape=selected,cell=previewCell,position=localPosition,rotation=localRotation,
                yawQuarterTurns=rotation,id=hitPart?hitPart.Record.id:null});
            if(mouse.leftButton.wasPressedThisFrame && validPreview)
            {
                var placed=assembly.Place(selected,localPosition,localRotation,rotation);
                if(placed){Starframe.App.GameAudio.Play(selected==BlockShape.Core?Starframe.App.GameSound.CorePowerOn:(BlockModules.IsThruster(selected)||selected==BlockShape.Laser?Starframe.App.GameSound.PlaceModule:Starframe.App.GameSound.PlaceHull));ui.SetStatusKey("editor.status.placed",false,L10n.Value("module."+selected),localPosition.ToString("F2"));}
            }
            else if(mouse.leftButton.wasPressedThisFrame){ui.SetStatusKey(L10n.LegacyKey(assembly.PlacementFailure)??"editor.status.cannotPlace",true);Starframe.App.GameAudio.Play(Starframe.App.GameSound.InvalidPlacement);}
        }
        public void RotatePreview(){rotation=(rotation+1)%4;ui.SetRotation(rotation*90);Starframe.App.GameAudio.Play(Starframe.App.GameSound.RotatePart);}
        bool repositoryInitialized=>repository!=null;
        public static string ShapeName(BlockShape shape)=>L10n.T("module."+shape);
        public bool BlueprintExists(string name)=>repository!=null&&repository.NameExists(name);
        public bool SaveBlueprint(bool overwrite=false)
        {
            try
            {
                currentName=ui.BlueprintName.Trim();
                var path=repository.Save(assembly.Export(currentName),overwrite);
                dirty=false;ui.UpdateStats(assembly.Count,false);
                RefreshFiles();ui.SelectFile(Path.GetFileName(path));ui.SetStatusKey("editor.status.saved",false,currentName);Starframe.App.GameAudio.Play(Starframe.App.GameSound.SaveBlueprint);return true;
            }
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){ui.SetStatusKey(L10n.ErrorKey(e),true);return false;}
        }
        public bool DeleteBlueprint(string file)
        {
            try
            {
                if(!repository.Delete(file)){ui.SetStatusKey("error.fileMissing",true);return false;}
                targetRoster.SetCount(file,0);RefreshFiles();ui.SetStatusKey("editor.status.deleted",false,Path.GetFileNameWithoutExtension(file));return true;
            }
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){ui.SetStatusKey(L10n.ErrorKey(e),true);return false;}
        }
        public bool LoadBlueprint(string file)
        {
            try
            {
                var data=repository.Load(file);
                ResetHover();assembly.Import(data);
                currentName=data.name;ui.BlueprintName=currentName;
                dirty=false;ui.UpdateStats(assembly.Count,false);ui.SetStatusKey("editor.status.loaded",false,currentName);
                orbit.Frame(assembly.ContentBounds());Starframe.App.GameAudio.Play(Starframe.App.GameSound.Respawn);return true;
            }
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){ui.SetStatusKey(L10n.ErrorKey(e),true);return false;}
        }
        public void EnterFlightRange()
        {
            try{
                var data=assembly.Export(ui.BlueprintName);
                if(!data.hasCore){ui.SetStatusKey("editor.status.needCore",true);return;}
                Starframe.Flight.FlightSession.Begin(data,dirty);
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(Starframe.Flight.FlightSession.RangeScene);
            }catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){ui.SetStatusKey(L10n.ErrorKey(e),true);}
        }
        public void RefreshFiles(){ui.SetFiles(repository.List());}
        public int TargetCount(string file)=>targetRoster?.GetCount(file)??0;
        public bool SetTargetCount(string file,int count)
        {
            try{targetRoster.SetCount(file,count);if(count>0)ui.SetStatusKey("editor.status.targetSet",false,Path.GetFileNameWithoutExtension(file),count);else ui.SetStatusKey("editor.status.targetCleared");return true;}
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){ui.SetStatusKey(L10n.ErrorKey(e),true);return false;}
        }
        void OnDestroy(){ResetHover();if(assembly)assembly.Changed-=OnAssemblyChanged;if(ghost)Destroy(ghost);}
    }
}

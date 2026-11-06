// @role Show the real module meshes as a selectable three-dimensional catalogue with an independent camera view.
// @calls ShipEditorUI, ShipEditorController, WorkbenchOrbit, ShipAssembly

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starframe.Construction
{
    public sealed class AssemblyModuleCatalogue:MonoBehaviour
    {
        ShipEditorController editor;ShipEditorUI ui;GameObject root;Material orange;
        readonly Dictionary<Collider,BlockShape> entries=new Dictionary<Collider,BlockShape>();
        readonly Dictionary<BlockShape,MeshRenderer> renderers=new Dictionary<BlockShape,MeshRenderer>();
        readonly Dictionary<Renderer,bool> hiddenRenderers=new Dictionary<Renderer,bool>();
        readonly Dictionary<Collider,bool> hiddenColliders=new Dictionary<Collider,bool>();
        WorkbenchOrbit.ViewState assemblyView,catalogueView;bool hasCatalogueView;
        Vector3 catalogueCenter;float catalogueSize=18;
        public bool IsOpen{get;private set;}
        public void Initialize(ShipEditorController controller,ShipEditorUI view){editor=controller;ui=view;}
        void Build()
        {
            root=new GameObject("NativeModuleCatalogue");root.transform.SetParent(transform,false);
            orange=new Material(editor.Assembly.BodyMaterial){name="CatalogueSelectionOrange"};
            if(orange.HasProperty("_BaseColor"))orange.SetColor("_BaseColor",new Color32(255,149,25,255));if(orange.HasProperty("_Color"))orange.SetColor("_Color",new Color32(255,149,25,255));
            if(orange.HasProperty("_EmissionColor")){orange.EnableKeyword("_EMISSION");orange.SetColor("_EmissionColor",new Color(.6f,.16f,0));}
            var groups=new[]{
                new[]{BlockShape.Cube,BlockShape.Wedge,BlockShape.Slab,BlockShape.Pyramid,BlockShape.Block5,BlockShape.Block6,BlockShape.Block7,BlockShape.Block8},
                new[]{BlockShape.Thruster1,BlockShape.Thruster2,BlockShape.Thruster3,BlockShape.Thruster4,BlockShape.Thruster5,BlockShape.Thruster6},
                new[]{BlockShape.Core,BlockShape.Power,BlockShape.Power2,BlockShape.Shield,BlockShape.Shield2,BlockShape.Armor1,BlockShape.Armor2,BlockShape.Armor3,BlockShape.Armor4,BlockShape.FlareLauncher},
                new[]{BlockShape.Gun1,BlockShape.Gun2,BlockShape.Gun3,BlockShape.Laser,BlockShape.Laser2,BlockShape.Laser3,BlockShape.Railgun1,BlockShape.Railgun2,BlockShape.Railgun3,BlockShape.Missile1,BlockShape.Missile2,BlockShape.Missile3,BlockShape.VLS2,BlockShape.VLS1,BlockShape.Sword1,BlockShape.Sword2}
            };
            for(int row=0;row<groups.Length;row++){
                float cursor=0;var rowRoot=new GameObject("CatalogueRow"+row).transform;rowRoot.SetParent(root.transform,false);
                foreach(var shape in groups[row]){
                    var mesh=editor.Assembly.Solid(shape);if(!mesh)continue;
                    var go=new GameObject("Module_"+shape);go.transform.SetParent(rowRoot,false);
                    var size=mesh.bounds.size;float width=Mathf.Max(1,size.x);go.transform.localPosition=new Vector3(cursor+width*.5f-mesh.bounds.center.x,-mesh.bounds.min.y,0);
                    cursor+=width+1.5f;
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=editor.Assembly.MaterialFor(shape);renderers[shape]=renderer;
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;entries[collider]=shape;
                }
                rowRoot.localPosition=new Vector3(-cursor*.5f,0,(1.5f-row)*6);
            }
            Bounds bounds=default;bool first=true;foreach(var renderer in renderers.Values){if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
            catalogueCenter=bounds.center;
            var inverse=Quaternion.Inverse(Quaternion.Euler(38,-35,0));Vector3 minimum=Vector3.positiveInfinity,maximum=Vector3.negativeInfinity;
            for(int ix=0;ix<2;ix++)for(int iy=0;iy<2;iy++)for(int iz=0;iz<2;iz++){
                var corner=new Vector3(ix==0?bounds.min.x:bounds.max.x,iy==0?bounds.min.y:bounds.max.y,iz==0?bounds.min.z:bounds.max.z);var local=inverse*(corner-bounds.center);minimum=Vector3.Min(minimum,local);maximum=Vector3.Max(maximum,local);
            }
            var projected=maximum-minimum;catalogueSize=Mathf.Clamp(Mathf.Max(projected.y/(2*.69f),projected.x/(2*Mathf.Max(.1f,editor.Orbit.Camera.aspect)*.72f)),12,60);
            root.SetActive(false);
        }
        public void SetOpen(bool value)
        {
            if(IsOpen==value)return;if(!root)Build();IsOpen=value;
            if(value){
                assemblyView=editor.Orbit.Capture();hiddenRenderers.Clear();hiddenColliders.Clear();
                foreach(var r in editor.Assembly.GetComponentsInChildren<Renderer>(true)){hiddenRenderers[r]=r.enabled;r.enabled=false;}
                foreach(var c in editor.Assembly.GetComponentsInChildren<Collider>(true)){hiddenColliders[c]=c.enabled;c.enabled=false;}
                root.SetActive(true);
                if(hasCatalogueView)editor.Orbit.Restore(catalogueView);else editor.Orbit.SetView(catalogueCenter,-35,38,catalogueSize);
                Highlight(editor.SelectedShape);
            }else{
                catalogueView=editor.Orbit.Capture();hasCatalogueView=true;root.SetActive(false);
                foreach(var pair in hiddenRenderers)if(pair.Key)pair.Key.enabled=pair.Value;
                foreach(var pair in hiddenColliders)if(pair.Key)pair.Key.enabled=pair.Value;
                hiddenRenderers.Clear();hiddenColliders.Clear();editor.Orbit.Restore(assemblyView);
            }
        }
        public void Highlight(BlockShape selected)
        {foreach(var pair in renderers)if(pair.Value)pair.Value.sharedMaterial=pair.Key==selected?orange:editor.Assembly.MaterialFor(pair.Key);}
        void Update()
        {
            if(!IsOpen||ui.HasEditorFocus)return;var mouse=Mouse.current;var keys=Keyboard.current;
            if(mouse==null||!mouse.leftButton.wasPressedThisFrame||!ui.WorkspaceContains(mouse.position.ReadValue())||(keys!=null&&(keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed)))return;
            var ray=editor.Orbit.Camera.ScreenPointToRay(mouse.position.ReadValue());
            if(Physics.Raycast(ray,out var hit,1000,1,QueryTriggerInteraction.Ignore)&&entries.TryGetValue(hit.collider,out var shape)){editor.SelectShape(shape);Highlight(shape);}
        }
        void OnDestroy()
        {
            foreach(var pair in hiddenRenderers)if(pair.Key)pair.Key.enabled=pair.Value;
            foreach(var pair in hiddenColliders)if(pair.Key)pair.Key.enabled=pair.Value;
            if(IsOpen&&editor&&editor.Orbit)editor.Orbit.Restore(assemblyView);
            if(root)Destroy(root);if(orange)Destroy(orange);
        }
    }
}

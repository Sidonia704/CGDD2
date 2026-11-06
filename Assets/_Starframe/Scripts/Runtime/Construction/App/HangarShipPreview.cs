// @role Display and orbit the selected blueprint in the hangar.
// @calls ShipAssembly, BlockView, ModuleGlowAccents

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.App
{
    public sealed class HangarShipPreview:MonoBehaviour
    {
        public ShipAssembly Assembly{get;private set;}
        public Camera View{get;private set;}
        ShipAssembly prefab;FlightTuning tuning;Bounds bounds;float yaw=-35,pitch=25,size=8,focus=.67f,horizontal=.60f;
        bool rotating;string shownBlueprint;public bool AllowOrbit=true;
        readonly Dictionary<string,RenderTexture> thumbnails=new Dictionary<string,RenderTexture>();
        readonly Queue<string> thumbnailOrder=new Queue<string>();
        public FlightTuning Tuning=>tuning;
        public void Initialize(ShipAssembly source,FlightTuning values,Camera camera)
        {
            prefab=source;tuning=values;View=camera;View.orthographic=true;View.clearFlags=CameraClearFlags.Skybox;
        }
        public void Show(BlueprintData data)
        {
            string snapshot=JsonUtility.ToJson(data);
            if(Assembly&&shownBlueprint==snapshot)return;
            shownBlueprint=snapshot;
            if(Assembly){Assembly.gameObject.SetActive(false);Destroy(Assembly.gameObject);}
            Assembly=Instantiate(prefab,transform);Assembly.name="HangarShip";Assembly.Configure(tuning.database);Assembly.Import(data);
            foreach(var child in Assembly.GetComponentsInChildren<Transform>(true))if(child.name=="ShipForwardRay"||child.name.Contains("Probe"))child.gameObject.SetActive(false);
            bounds=Assembly.ContentBounds();size=Mathf.Max(3,bounds.extents.magnitude*1.5f);UpdateCamera();
        }
        public void SetFocus(float yNormalized,float xNormalized=.60f)
        {
            focus=yNormalized;horizontal=xNormalized;UpdateCamera();
        }
        public void ResetView(){yaw=-35;pitch=25;size=Mathf.Max(3,bounds.extents.magnitude*1.5f);UpdateCamera();}
        public void FitViewport(float widthFraction,float heightFraction)
        {
            var rotation=Quaternion.Inverse(Quaternion.Euler(pitch,yaw,0));
            Vector3 extents=Vector3.zero;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var corner=rotation*Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                extents=Vector3.Max(extents,new Vector3(Mathf.Abs(corner.x),Mathf.Abs(corner.y),Mathf.Abs(corner.z)));
            }
            size=Mathf.Max(3,extents.y/Mathf.Max(.1f,heightFraction),extents.x/(Mathf.Max(.5f,View.aspect)*Mathf.Max(.1f,widthFraction)))*1.08f;UpdateCamera();
        }
        /// <summary>Render the actual saved design once; the thumbnail never changes the working preview.</summary>
        public Texture Thumbnail(BlueprintData data)
        {
            if(data==null||!Assembly||!View)return null;
            string key=(tuning&&tuning.database?tuning.database.Revision:0)+":"+JsonUtility.ToJson(data);
            if(thumbnails.TryGetValue(key,out var cached)&&cached)
            {
                int count=thumbnailOrder.Count;for(int i=0;i<count;i++){var item=thumbnailOrder.Dequeue();if(item!=key)thumbnailOrder.Enqueue(item);}thumbnailOrder.Enqueue(key);return cached;
            }
            const int layer=30;
            GameObject sample=null;Camera camera=null;RenderTexture output=null;
            try
            {
                // Thumbnails share the exact visual meshes/materials, but need no placement validation or physics.
                sample=new GameObject("Blueprint Thumbnail Sample");sample.layer=layer;sample.transform.position=new Vector3(10000,10000,10000);
                var box=new Bounds();bool first=true;
                foreach(var record in data.parts)
                {
                    var module=tuning.database?tuning.database.Get(record.shape):null;var mesh=Assembly.Solid(record.shape);
                    var part=new GameObject("Thumbnail_"+record.shape);part.layer=layer;part.transform.SetParent(sample.transform,false);
                    var block=part.AddComponent<BlockView>();block.Initialize(record,mesh,module&&!module.drawOutline?null:Assembly.Outline(record.shape),Assembly.MaterialFor(record.shape),Assembly.EdgeMaterial,false);
                    ModuleGlowAccents.Attach(part.transform,record.shape,mesh,null,module?module.muzzleLocalPosition:(Vector3?)null,module&&module.weapon?module.weapon.muzzleOffsets:null);
                    if(first){box=block.Solid.bounds;first=false;}else box.Encapsulate(block.Solid.bounds);
                }
                foreach(var child in sample.GetComponentsInChildren<Transform>(true))child.gameObject.layer=layer;
                if(first)box=new Bounds(sample.transform.position,Vector3.one);
                var rotation=Quaternion.Euler(25,-35,0);
                Vector3 projected=Vector3.zero;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var v=Quaternion.Inverse(rotation)*Vector3.Scale(box.extents,new Vector3(x,y,z));
                    projected=Vector3.Max(projected,new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z)));
                }
                output=new RenderTexture(384,240,24,RenderTextureFormat.ARGB32){name="Blueprint Thumbnail",antiAliasing=2};output.Create();
                camera=new GameObject("Blueprint Thumbnail Camera").AddComponent<Camera>();camera.CopyFrom(View);
                camera.enabled=false;camera.targetTexture=output;camera.aspect=384f/240;camera.orthographic=true;
                camera.orthographicSize=Mathf.Max(.75f,projected.y,projected.x/camera.aspect)*1.16f;
                camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(27/255f,31/255f,45/255f,1);camera.nearClipPlane=.1f;
                float distance=Mathf.Max(100,projected.z*2+10);camera.farClipPlane=distance+projected.z*2+100;
                camera.transform.SetPositionAndRotation(box.center-rotation*Vector3.forward*distance,rotation);
                camera.Render();
                thumbnails[key]=output;thumbnailOrder.Enqueue(key);
                while(thumbnailOrder.Count>48){string oldest=thumbnailOrder.Dequeue();if(thumbnails.TryGetValue(oldest,out var retired)){retired.Release();Destroy(retired);}thumbnails.Remove(oldest);}
                return output;
            }
            catch
            {
                if(output){output.Release();Destroy(output);}throw;
            }
            finally
            {
                if(camera){camera.targetTexture=null;Destroy(camera.gameObject);}
                if(sample){sample.SetActive(false);Destroy(sample);}
            }
        }
        void OnDestroy()
        {
            foreach(var texture in thumbnails.Values)if(texture){texture.Release();Destroy(texture);}
            thumbnails.Clear();thumbnailOrder.Clear();
        }
        void Update()
        {
            if(!View||!Assembly||!AllowOrbit)return;var mouse=Mouse.current;if(mouse==null)return;
            bool over=EventSystem.current&&EventSystem.current.IsPointerOverGameObject();
            if(mouse.middleButton.wasPressedThisFrame&&!over)rotating=true;if(!mouse.middleButton.isPressed)rotating=false;
            if(rotating){var delta=mouse.delta.ReadValue();yaw+=delta.x*.22f;pitch=Mathf.Clamp(pitch-delta.y*.22f,-70,80);}
            if(!over&&mouse.scroll.ReadValue().y!=0)size=Mathf.Clamp(size*Mathf.Exp(-mouse.scroll.ReadValue().y/120f*.18f),2,Mathf.Max(80,bounds.extents.magnitude*4));
            UpdateCamera();
        }
        void UpdateCamera()
        {
            if(!View)return;var rotation=Quaternion.Euler(pitch,yaw,0);float aspect=Mathf.Max(.5f,View.aspect);
            var target=transform.TransformPoint(bounds.center)-rotation*Vector3.up*((focus-.5f)*size*2)-rotation*Vector3.right*((horizontal-.5f)*size*2*aspect);
            View.transform.SetPositionAndRotation(target-rotation*Vector3.forward*100,rotation);View.orthographicSize=size;
        }
    }
}


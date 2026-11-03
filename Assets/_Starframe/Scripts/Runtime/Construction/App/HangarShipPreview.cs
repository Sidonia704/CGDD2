// @role Display and orbit the selected blueprint in the hangar.
// @calls ShipAssembly

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

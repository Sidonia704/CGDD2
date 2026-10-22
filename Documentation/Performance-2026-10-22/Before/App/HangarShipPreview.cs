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
        bool rotating;public bool AllowOrbit=true;
        public void Initialize(ShipAssembly source,FlightTuning values,Camera camera)
        {
            prefab=source;tuning=values;View=camera;View.orthographic=true;View.clearFlags=CameraClearFlags.Skybox;
        }
        public void Show(BlueprintData data)
        {
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
        void Update()
        {
            if(!View||!Assembly||!AllowOrbit)return;var mouse=Mouse.current;if(mouse==null)return;
            bool over=EventSystem.current&&EventSystem.current.IsPointerOverGameObject();
            if(mouse.middleButton.wasPressedThisFrame&&!over)rotating=true;if(!mouse.middleButton.isPressed)rotating=false;
            if(rotating){var delta=mouse.delta.ReadValue();yaw+=delta.x*.22f;pitch=Mathf.Clamp(pitch-delta.y*.22f,-70,80);}
            if(!over&&mouse.scroll.ReadValue().y!=0)size=Mathf.Clamp(size*Mathf.Exp(-mouse.scroll.ReadValue().y*.16f),2,80);
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

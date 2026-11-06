// @role Orbit, pan and zoom the construction camera.
// @calls ShipEditorUI

using UnityEngine;
using UnityEngine.InputSystem;
namespace Starframe.Construction
{
    [DefaultExecutionOrder(-100)]
    public sealed class WorkbenchOrbit : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] Vector3 pivot=new Vector3(.4f,.7f,0);
        [SerializeField] float yaw=-42,pitch=35,size=5.8f;
        [SerializeField] float zoomSensitivity=.18f;
        ShipEditorUI ui;
        bool dragging;
        public struct ViewState { public Vector3 pivot;public float yaw,pitch,size; }
        public Camera Camera=>viewCamera;
        public Vector3 Pivot=>pivot;
        public float Size=>size;
        public ViewState Capture()=>new ViewState{pivot=pivot,yaw=yaw,pitch=pitch,size=size};
        public void Restore(ViewState state){pivot=state.pivot;yaw=state.yaw;pitch=state.pitch;size=state.size;dragging=false;UpdateTransform();}
        public void SetView(Vector3 center,float heading,float elevation,float distance){pivot=center;yaw=heading;pitch=elevation;size=distance;dragging=false;UpdateTransform();}
        public void BindUI(ShipEditorUI value)=>ui=value;
        void LateUpdate(){UpdateTransform();}
        void Update()
        {
            if(ui!=null&&ui.IsDialogOpen){dragging=false;return;}
            var mouse=Mouse.current;if(mouse==null)return;
            bool over=ui==null || ui.WorkspaceContains(mouse.position.ReadValue());
            if(mouse.middleButton.wasPressedThisFrame && over)dragging=true;
            if(!mouse.middleButton.isPressed)dragging=false;
            var keys=Keyboard.current;
            bool shifting=keys!=null&&(keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed);
            if(dragging)
            {
                if(shifting)Pan(mouse.delta.ReadValue());
                else Orbit(mouse.delta.ReadValue());
            }
            else if(over&&shifting&&mouse.leftButton.isPressed)Pan(mouse.delta.ReadValue());
            if(over && mouse.scroll.ReadValue().y!=0)Zoom(mouse.scroll.ReadValue().y);
            UpdateTransform();
        }
        public void Orbit(Vector2 delta){yaw+=delta.x*.23f;pitch=Mathf.Clamp(pitch-delta.y*.23f,-85,85);UpdateTransform();}
        public void Pan(Vector2 delta){pivot-= (viewCamera.transform.right*delta.x+viewCamera.transform.up*delta.y)*(2*size/Mathf.Max(Screen.height,1));UpdateTransform();}
        public void Zoom(float amount){size=Mathf.Clamp(size*Mathf.Exp(-amount*zoomSensitivity),1.5f,60f);UpdateTransform();}
        public void Frame(Bounds bounds)
        {
            var inverse=Quaternion.Inverse(Quaternion.Euler(pitch,yaw,0));Vector3 projected=Vector3.zero;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){var v=inverse*Vector3.Scale(bounds.extents,new Vector3(x,y,z));projected=Vector3.Max(projected,new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z)));}
            pivot=bounds.center;size=Mathf.Clamp(Mathf.Max(3.5f,projected.y/.55f,projected.x/(Mathf.Max(.5f,viewCamera.aspect)*.64f)),1.5f,60f);UpdateTransform();
        }
        public void ResetView(){pivot=new Vector3(.4f,.7f,0);yaw=-42;pitch=35;size=5.8f;UpdateTransform();}
        void UpdateTransform()
        {
            if(!viewCamera)return;
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var offset=rotation*Vector3.right*(size*viewCamera.aspect*.16f);
            viewCamera.transform.SetPositionAndRotation(pivot+offset-rotation*Vector3.forward*60,rotation);
            viewCamera.orthographic=true;viewCamera.orthographicSize=size;
        }
    }
}

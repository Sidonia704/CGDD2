// @role Place the native construction grid beneath the live hangar ship and cover the camera's visible floor as the preview orbits or zooms.
// @calls HangarShipPreview, ShipAssembly

using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.App
{
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed class FigmaHangarGrid:MonoBehaviour
    {
        [SerializeField] Material gridMaterial;
        HangarShipPreview preview;
        GameObject floor;
        Mesh mesh;
        Material ownedMaterial;
        MeshRenderer floorRenderer;
        Starframe.Construction.ShipAssembly observedAssembly;
        Bounds shipBounds;

        public void Initialize(HangarShipPreview value)
        {
            preview=value;
            if(!floor)CreateFloor();
            LateUpdate();
        }

        void CreateFloor()
        {
            var shader=gridMaterial?gridMaterial.shader:Shader.Find("Starframe/Construction/Grid");
            if(!shader)return;
            floor=new GameObject("FigmaNativeHangarGrid");floor.transform.SetParent(transform,false);
            floor.layer=0;
            mesh=new Mesh{name="FigmaHangarGridQuad"};
            mesh.SetVertices(new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)});
            mesh.SetTriangles(new[]{0,1,2,0,2,3},0);mesh.RecalculateBounds();
            floor.AddComponent<MeshFilter>().sharedMesh=mesh;
            floorRenderer=floor.AddComponent<MeshRenderer>();
            if(gridMaterial)floorRenderer.sharedMaterial=gridMaterial;
            else{ownedMaterial=new Material(shader){name="FigmaHangarGridRuntime"};floorRenderer.sharedMaterial=ownedMaterial;}
            floorRenderer.shadowCastingMode=ShadowCastingMode.Off;floorRenderer.receiveShadows=false;
            floorRenderer.lightProbeUsage=LightProbeUsage.Off;floorRenderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }

        void LateUpdate()
        {
            if(!floorRenderer)return;
            bool visible=preview&&preview.View&&preview.Assembly&&preview.Assembly.gameObject.activeInHierarchy;
            floorRenderer.enabled=visible;if(!visible)return;
            if(observedAssembly!=preview.Assembly){if(observedAssembly)observedAssembly.Changed-=RefreshBounds;observedAssembly=preview.Assembly;observedAssembly.Changed+=RefreshBounds;RefreshBounds();}
            var camera=preview.View;var bounds=shipBounds;
            var center=bounds.center;float height=bounds.min.y-.035f;
            // Orthographic rays meet the XZ floor farther away at shallow orbit angles.
            // Cover their entire footprint, while the shader keeps its one-cell world spacing.
            float slope=Mathf.Max(.08f,Mathf.Abs(camera.transform.forward.y));
            float span=camera.orthographic?camera.orthographicSize:Mathf.Max(8,Vector3.Distance(camera.transform.position,center)*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad));
            float radius=Mathf.Clamp(span*(Mathf.Max(1,camera.aspect)+1)/slope+bounds.extents.magnitude,30,5000);
            var plane=new Plane(Vector3.up,new Vector3(0,height,0));
            var ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));
            if(plane.Raycast(ray,out float distance))center=ray.GetPoint(distance);
            floor.transform.position=new Vector3(center.x,height,center.z);
            floor.transform.rotation=Quaternion.identity;floor.transform.localScale=new Vector3(radius,1,radius);
        }
        void RefreshBounds(){if(observedAssembly)shipBounds=observedAssembly.ContentBounds();}
        void OnDisable(){if(floorRenderer)floorRenderer.enabled=false;}
        void OnDestroy(){if(observedAssembly)observedAssembly.Changed-=RefreshBounds;if(floor)Destroy(floor);if(mesh)Destroy(mesh);if(ownedMaterial)Destroy(ownedMaterial);}
    }
}

// @role Build sparse, distance-culled spatial references and start the range's bounded atmosphere effects.
// @calls FlightTuning, RangeAtmosphere

using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Flight
{
    public sealed class RangeEnvironment:MonoBehaviour
    {
        sealed class Plane {public Transform transform;public Renderer renderer;public int axis,offset;public bool boundary;public float side;}
        readonly List<Plane> planes=new List<Plane>();MaterialPropertyBlock block;
        Transform player;FlightTuning tuning;Mesh gridMesh,boundaryMesh;Material material;
        public void Initialize(Transform target,FlightTuning values,Camera view=null)
        {
            player=target;tuning=values;block=new MaterialPropertyBlock();gridMesh=MeshPlane(values.gridPatchHalfExtent);boundaryMesh=MeshPlane(values.arenaHalfExtent);
            material=new Material(Shader.Find("Starframe/Flight/RangeGrid")){name="RangeGrid"};
            for(int axis=0;axis<3;axis++){
                for(int offset=-1;offset<=1;offset++)Add(axis,offset,false,0);
                Add(axis,0,true,-1);Add(axis,0,true,1);
            }Update();
            var atmosphere=GetComponent<RangeAtmosphere>();if(atmosphere)atmosphere.Initialize(target,view);
        }
        void Add(int axis,int offset,bool boundary,float side)
        {
            var go=new GameObject(boundary?"Boundary":"ReferenceGrid");go.transform.SetParent(transform,false);
            go.transform.rotation=axis==0?Quaternion.Euler(0,90,0):axis==1?Quaternion.Euler(90,0,0):Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh=boundary?boundaryMesh:gridMesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            planes.Add(new Plane{transform=go.transform,renderer=renderer,axis=axis,offset=offset,boundary=boundary,side=side});
        }
        void Update()
        {
            if(!player||!tuning||block==null)return;var p=player.position;float spacing=Mathf.Max(1,tuning.gridWallSpacing);
            foreach(var plane in planes)
            {
                float coordinate=plane.boundary?plane.side*tuning.arenaHalfExtent:
                    (Mathf.Floor(p[plane.axis]/spacing)+.5f+plane.offset)*spacing;
                float fade=plane.boundary?tuning.boundaryFadeDistance:tuning.gridFadeFar;
                plane.renderer.enabled=(plane.boundary||Mathf.Abs(coordinate)<tuning.arenaHalfExtent)&&Mathf.Abs(coordinate-p[plane.axis])<fade;
                if(!plane.renderer.enabled)continue;
                // Keep the patch centred tangentially. Shader lines remain fixed in world space.
                var center=plane.boundary?Vector3.zero:p;
                center[plane.axis]=coordinate;plane.transform.position=center;
                block.Clear();block.SetFloat("_PlaneAxis",plane.axis);block.SetFloat("_Boundary",plane.boundary?1:0);
                block.SetVector("_PlayerPosition",p);block.SetFloat("_CellSize",plane.boundary?40:tuning.gridCellSize);
                block.SetFloat("_Opacity",plane.boundary?1:tuning.gridOpacity);
                block.SetFloat("_FadeNear",tuning.gridFadeNear);block.SetFloat("_FadeFar",tuning.gridFadeFar);
                block.SetFloat("_BoundaryFadeDistance",tuning.boundaryFadeDistance);plane.renderer.SetPropertyBlock(block);
            }
        }
        static Mesh MeshPlane(float extent)
        {
            var m=new Mesh{name="RangeReferencePlane"};m.vertices=new[]{new Vector3(-extent,-extent,0),new Vector3(extent,-extent,0),new Vector3(extent,extent,0),new Vector3(-extent,extent,0)};
            m.triangles=new[]{0,1,2,0,2,3};m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        void OnDestroy(){if(material)Destroy(material);if(gridMesh)Destroy(gridMesh);if(boundaryMesh)Destroy(boundaryMesh);}
    }
}

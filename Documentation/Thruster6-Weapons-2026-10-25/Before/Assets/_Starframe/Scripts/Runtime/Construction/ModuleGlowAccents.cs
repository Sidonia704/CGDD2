// @role Attach shared engine-nozzle and weapon-bore glow accents scaled to each module.
// @calls BlockModules, VoxelFxGeometry

using UnityEngine;
using Starframe.Combat;

namespace Starframe.Construction
{
    public static class ModuleGlowAccents
    {
        const string AccentName="ModuleGlowAccent";
        static Material sharedBlue,sharedAmber;

        public static Transform Attach(Transform parent,BlockShape shape,Mesh visualMesh,Shader serializedGlowShader=null,Vector3? muzzle=null)
        {
            bool thruster=BlockModules.IsThruster(shape),weapon=BlockModules.IsWeapon(shape);
            if(!parent||!visualMesh||(!thruster&&!weapon))return null;
            var existing=parent.Find(AccentName);if(existing)return existing;
            bool amber=((int)shape>=(int)BlockShape.Gun1&&(int)shape<=(int)BlockShape.Gun3)||((int)shape>=(int)BlockShape.Railgun1&&(int)shape<=(int)BlockShape.Missile3);
            Material glow=amber?sharedAmber:sharedBlue;
            if(!glow)
            {
                glow=VoxelFxGeometry.CreateGlow(serializedGlowShader);if(!glow)return null;
                glow.name=amber?"Shared amber weapon bore":"Shared blue module nozzle";
                glow.SetColor("_Color",amber?new Color(1,.48f,.055f,1):new Color(.045f,.62f,.95f,1));
                glow.SetFloat("_Intensity",amber?1.1f:1.65f);glow.SetFloat("_Opacity",1);
                if(amber)sharedAmber=glow;else sharedBlue=glow;
            }
            var bounds=visualMesh.bounds;var centre=bounds.center;const float thickness=.025f;Vector3 size;
            if(thruster)
            {
                centre.y=bounds.max.y+thickness*.5f+.001f;
                size=new Vector3(Mathf.Clamp(bounds.size.x*.54f,.12f,2),thickness,Mathf.Clamp(bounds.size.z*.54f,.12f,2));
            }
            else
            {
                float width=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.y)*.3f,.1f,.6f);
                centre=muzzle??centre;
                if(!muzzle.HasValue)centre.z=bounds.max.z+thickness*.5f+.001f;
                size=new Vector3(width,width,thickness);
            }
            var go=new GameObject(AccentName,typeof(MeshFilter),typeof(MeshRenderer));go.layer=parent.gameObject.layer;
            go.transform.SetParent(parent,false);go.transform.localPosition=centre;go.transform.localScale=size;
            go.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;
            VoxelFxGeometry.Configure(go.GetComponent<MeshRenderer>(),glow);return go.transform;
        }
    }
}

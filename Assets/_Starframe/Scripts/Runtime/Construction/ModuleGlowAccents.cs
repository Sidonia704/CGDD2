// @role Attach shared glow accents to every physical engine nozzle and to every weapon bore, not just the averaged muzzle.
// @calls BlockModules, VoxelFxGeometry

using UnityEngine;
using Starframe.Combat;

namespace Starframe.Construction
{
    public static class ModuleGlowAccents
    {
        const string AccentName="ModuleGlowAccent";
        static Material sharedBlue,sharedAmber;

        public static Transform Attach(Transform parent,BlockShape shape,Mesh visualMesh,Shader serializedGlowShader=null,Vector3? muzzle=null,Vector3[] muzzleOffsets=null)
        {
            bool thruster=BlockModules.IsThruster(shape),weapon=BlockModules.IsWeapon(shape);
            if(!parent||!visualMesh||(!thruster&&!weapon))return null;
            var existing=parent.Find(AccentName);if(existing)return existing;
            bool amber=((int)shape>=(int)BlockShape.Gun1&&(int)shape<=(int)BlockShape.Gun3)||((int)shape>=(int)BlockShape.Railgun1&&(int)shape<=(int)BlockShape.Missile3)||BlockModules.IsVerticalLaunch(shape);
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
            if(thruster&&BlockModules.LocalExhaustDirections(shape).Length>1)
            {
                var root=new GameObject(AccentName);root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
                var directions=BlockModules.LocalExhaustDirections(shape);
                for(int index=0;index<directions.Length;index++)
                {
                    // Keep the established accent name so wreckage extraction excludes all effect meshes.
                    var accent=new GameObject(AccentName,typeof(MeshFilter),typeof(MeshRenderer));accent.layer=parent.gameObject.layer;accent.transform.SetParent(root.transform,false);
                    accent.transform.localPosition=BlockModules.LocalNozzlePosition(shape,index,visualMesh);
                    accent.transform.localRotation=Quaternion.FromToRotation(Vector3.up,directions[index].normalized);
                    var aperture=BlockModules.LocalNozzleAperture(shape,index,visualMesh);accent.transform.localScale=new Vector3(aperture.x*.54f,thickness,aperture.y*.54f);
                    accent.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;VoxelFxGeometry.Configure(accent.GetComponent<MeshRenderer>(),glow);
                }
                return root.transform;
            }
            if(weapon&&muzzleOffsets!=null&&muzzleOffsets.Length>0)
            {
                var boreRoot=new GameObject(AccentName);boreRoot.layer=parent.gameObject.layer;boreRoot.transform.SetParent(parent,false);
                float width=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.y)*.3f,.1f,.6f);
                var origin=muzzle??centre;
                for(int index=0;index<muzzleOffsets.Length;index++)
                {
                    // One accent per real barrel; the averaged muzzle would sit between the bores.
                    var bore=new GameObject(AccentName,typeof(MeshFilter),typeof(MeshRenderer));bore.layer=parent.gameObject.layer;bore.transform.SetParent(boreRoot.transform,false);
                    bore.transform.localPosition=origin+muzzleOffsets[index];
                    bore.transform.localScale=new Vector3(width,width,thickness);
                    bore.GetComponent<MeshFilter>().sharedMesh=VoxelFxGeometry.Cube;VoxelFxGeometry.Configure(bore.GetComponent<MeshRenderer>(),glow);
                }
                return boreRoot.transform;
            }
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

// @role Control shield-surface renderer visibility for the active camera.

using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Combat
{
    // Shared by combat, assembled shields and the cursor preview; preserve hit/charge properties.
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ShieldSurfaceVisibility:MonoBehaviour
    {
        static readonly List<ShieldSurfaceVisibility> surfaces=new List<ShieldSurfaceVisibility>();
        static int updatedFrame=-1;
        MeshRenderer surface;MaterialPropertyBlock properties;
        Vector3 center;float radius;int overlaps;bool visible;
        void Awake(){surface=GetComponent<MeshRenderer>();properties=new MaterialPropertyBlock();}
        void OnEnable(){if(!surface)surface=GetComponent<MeshRenderer>();properties??=new MaterialPropertyBlock();if(!surfaces.Contains(this))surfaces.Add(this);updatedFrame=-1;}
        void OnDisable(){surfaces.Remove(this);updatedFrame=-1;}
        void LateUpdate()
        {
            if(updatedFrame==Time.frameCount)return;updatedFrame=Time.frameCount;
            foreach(var item in surfaces)
            {
                item.visible=item.surface&&item.surface.enabled;
                if(!item.visible)continue;
                item.center=item.transform.position;var scale=item.transform.lossyScale;
                item.radius=Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)))*.5f;
                item.overlaps=1;
            }
            for(int i=0;i<surfaces.Count;i++)
            {
                var a=surfaces[i];if(!a.visible)continue;
                for(int j=i+1;j<surfaces.Count;j++)
                {
                    var b=surfaces[j];if(!b.visible)continue;
                    float reach=a.radius+b.radius;
                    if((a.center-b.center).sqrMagnitude>=reach*reach)continue;
                    a.overlaps++;b.overlaps++;
                }
            }
            foreach(var item in surfaces)if(item.visible)
            {
                item.surface.GetPropertyBlock(item.properties);
                item.properties.SetFloat("_LayerOpacityScale",1f/item.overlaps);
                item.surface.SetPropertyBlock(item.properties);
            }
        }
    }
}

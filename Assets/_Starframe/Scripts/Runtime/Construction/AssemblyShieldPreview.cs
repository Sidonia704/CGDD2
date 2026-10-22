// @role Preview shield-generator coverage while editing a ship.
// @calls ShipAssembly, ModuleDatabase, ShipShieldVisual

using System.Collections.Generic;
using Starframe.Combat;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Construction
{
    public sealed class AssemblyShieldPreview:MonoBehaviour
    {
        struct Surface {public MeshRenderer renderer;public ModuleDefinition definition;}
        readonly List<Surface> surfaces=new List<Surface>();
        ShipAssembly assembly;ModuleDatabase database;
        public int SurfaceCount=>surfaces.Count;
        public void Initialize(ShipAssembly value,ModuleDatabase definitions)
        {
            if(assembly)assembly.Changed-=Rebuild;
            assembly=value;database=definitions;assembly.Changed+=Rebuild;Rebuild();
        }
        void Rebuild()
        {
            foreach(var surface in surfaces)if(surface.renderer){surface.renderer.gameObject.SetActive(false);Destroy(surface.renderer.gameObject);}
            surfaces.Clear();if(!assembly||!database)return;
            foreach(var block in assembly.Blocks)
            {
                var definition=database.Get(block.Record.shape);
                if(!definition||definition.shieldCapacity<=0||!definition.shieldSurfaceMaterial)continue;
                var renderer=ShipShieldVisual.CreateSurface(block.transform,"AssemblyShieldRange",definition.shieldSurfaceMesh);
                surfaces.Add(new Surface{renderer=renderer,definition=definition});
            }
            LateUpdate();
        }
        void LateUpdate()
        {
            foreach(var surface in surfaces)if(surface.renderer&&surface.definition)
            {surface.renderer.sharedMaterial=surface.definition.shieldSurfaceMaterial;surface.renderer.transform.localScale=Vector3.one*(surface.definition.shieldRadius*2);}
        }
        void OnDestroy(){if(assembly)assembly.Changed-=Rebuild;}
    }
}

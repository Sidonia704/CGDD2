using UnityEngine;
using Starframe.Flight;
namespace Starframe.Combat
{
    public sealed class ShipPartHitbox:MonoBehaviour
    {
        public FlightShipRuntime Ship{get;private set;}
        public ShipPartState Part{get;private set;}
        public DebrisPartHitbox DetachedReplacement{get;internal set;}
        public Vector3[] Vertices{get;private set;}
        public int[] Triangles{get;private set;}
        public void Initialize(FlightShipRuntime ship,ShipPartState part){Ship=ship;Part=part;var collider=GetComponent<MeshCollider>();var filter=GetComponent<MeshFilter>();var mesh=collider&&collider.sharedMesh?collider.sharedMesh:filter?filter.sharedMesh:null;if(mesh){Vertices=mesh.vertices;Triangles=mesh.triangles;}}
    }
}

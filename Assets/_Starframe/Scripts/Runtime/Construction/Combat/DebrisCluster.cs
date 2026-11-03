// @role Track detached-part clusters and stable collision bounds from freely spinning physics poses.
// @calls DebrisWorld

using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;
namespace Starframe.Combat
{
    public sealed class DebrisCluster:MonoBehaviour
    {
        internal readonly List<DebrisPartHitbox> parts=new List<DebrisPartHitbox>();
        internal DebrisWorld world;
        internal double expiresAt;
        internal Bounds localBounds;
        internal readonly List<Collider> ignoredSourceColliders=new List<Collider>();
        internal int sourceRevision;
        public FlightShipRuntime SourceShip{get;internal set;}
        public bool SuppressSourceCollisions{get;internal set;}
        public Bounds WorldBounds=>BoundsForPhysicsPose(localBounds,Body.position,Body.rotation,transform.lossyScale);
        public Rigidbody Body{get;internal set;}
        public IReadOnlyList<DebrisPartHitbox> Parts=>parts;
        public int PartCount{get{int count=0;foreach(var part in parts)if(part&&part.Part.present)count++;return count;}}
        public static Bounds BoundsForPhysicsPose(Bounds local,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            // Long-running native angular integration can drift a few ulps from unit length. TRS asserts on
            // those otherwise valid poses. Normalize only this matrix input; never rewrite the physical body.
            return DebrisWorld.TransformBounds(local,Matrix4x4.TRS(position,Quaternion.Normalize(rotation),scale));
        }
        internal void RemovePart(DebrisPartHitbox part)
        {
            world.EmitDestroyedPart(part,Body.GetPointVelocity(part.transform.position));
            part.gameObject.SetActive(false);world.MarkChanged(this);
            if(Application.isPlaying)Destroy(part.gameObject);else DestroyImmediate(part.gameObject);
        }
    }
}

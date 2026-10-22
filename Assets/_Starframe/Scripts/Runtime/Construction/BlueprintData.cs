// @role Store editable ship parts and weapon groups separately from battle damage.

using System;
using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public enum BlockShape { Cube=0, Wedge=1, Thruster1=2, Thruster2=3, Slab=4, Pyramid=5, Core=6, Laser=7, Power=8, Shield=9 }
    [Serializable] public sealed class BlockRecord
    {
        public string id;
        public BlockShape shape;
        public Vector3Int cell; // Legacy grid coordinate; full pose is authoritative in v2.
        public int yawQuarterTurns; // Legacy yaw / current number of spins about the attachment face.
        public Vector3 position;
        public Quaternion rotation=Quaternion.identity;
    }
    [Serializable] public sealed class BlueprintData
    {
        public int version=2;
        public string name;
        public bool hasCore;
        public string corePartId;
        public Vector3 shipForward=Vector3.forward;
        public List<BlockRecord> parts=new List<BlockRecord>();
        // Optional v2 extension. Missing fields migrate to one left-mouse group.
        public List<WeaponGroupRecord> weaponGroups=new List<WeaponGroupRecord>();
        public List<WeaponGroupAssignment> weaponAssignments=new List<WeaponGroupAssignment>();
    }
}

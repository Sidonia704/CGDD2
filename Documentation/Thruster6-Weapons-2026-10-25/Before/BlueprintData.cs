// @role Store editable ship parts and weapon groups separately from battle damage.

using System;
using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public enum BlockShape { Cube=0, Wedge=1, Thruster1=2, Thruster2=3, Slab=4, Pyramid=5, Core=6, Laser=7, Power=8, Shield=9, Block5=10, Block6=11, Block7=12, Block8=13, Thruster3=14, Thruster4=15, Thruster5=16, Power2=17, Shield2=18, Armor1=19, Armor2=20, Armor3=21, Armor4=22, Gun1=23, Gun2=24, Gun3=25, Laser2=26, Laser3=27, Railgun1=28, Railgun2=29, Railgun3=30, Missile1=31, Missile2=32, Missile3=33, Sword1=34, Sword2=35 }
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

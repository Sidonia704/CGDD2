using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class MirrorEditingTests
    {
        GameObject host;ShipAssembly assembly;ModuleDatabase previous;
        [SetUp]public void Setup()
        {
            previous=BlockModules.ConfiguredDatabase;host=new GameObject("Mirror editing fixture");assembly=host.AddComponent<ShipAssembly>();
            assembly.Configure(AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset"));
        }
        [TearDown]public void Cleanup(){Object.DestroyImmediate(host);BlockModules.Configure(previous);}
        void Put(BlockShape shape,int x,int y=0,int z=0,Quaternion? q=null)
        {Assert.That(assembly.Place(shape,new Vector3(x,y+.5f,z),q??Quaternion.identity),Is.True,assembly.PlacementFailure);}
        void CoreAndArms(bool rotated=false)
        {
            Put(BlockShape.Core,0,q:rotated?Quaternion.Euler(0,90,0):Quaternion.identity);
            Put(BlockShape.Cube,rotated?0:1,z:rotated?1:0);Put(BlockShape.Cube,rotated?0:-1,z:rotated?-1:0);
        }
        [TestCase(false)][TestCase(true)]public void PairUsesCoreAxisAndEmitsOneAtomicChange(bool rotated)
        {
            CoreAndArms(rotated);int changes=0;assembly.Changed+=()=>changes++;
            var p=rotated?new Vector3(0,.5f,2):new Vector3(2,.5f,0);
            Assert.That(assembly.PlaceMirrored(BlockShape.Cube,p,Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Count,Is.EqualTo(5));Assert.That(changes,Is.EqualTo(1));
            var positions=assembly.Blocks.Select(b=>b.Record.position).ToArray();
            CollectionAssert.Contains(positions,p);CollectionAssert.Contains(positions,new Vector3(-p.x,p.y,-p.z));
        }
        [Test]public void OccupiedOppositeSideRejectsBothWithoutPartialPlacement()
        {
            CoreAndArms();Put(BlockShape.Cube,-2);int before=assembly.Count,changes=0;assembly.Changed+=()=>changes++;
            Assert.That(assembly.CanPlaceMirrored(BlockShape.Cube,new Vector3(2,.5f,0),Quaternion.identity),Is.False);
            Assert.That(assembly.PlaceMirrored(BlockShape.Cube,new Vector3(2,.5f,0),Quaternion.identity),Is.False);
            Assert.That(assembly.Count,Is.EqualTo(before));Assert.That(changes,Is.Zero);
            Assert.That(assembly.Blocks.Any(b=>b.Record.position==new Vector3(2,.5f,0)),Is.False);
        }
        [Test]public void CenterPlaneSelfSymmetricPartIsPlacedOnlyOnce()
        {
            Put(BlockShape.Core,0);Assert.That(assembly.PlaceMirrored(BlockShape.Cube,new Vector3(0,1.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Count,Is.EqualTo(2));
        }
        [Test]public void AsymmetricCenterPlaneWedgeCannotOverlapItsReflection()
        {
            Put(BlockShape.Core,0);
            Assert.That(assembly.PlaceMirrored(BlockShape.Wedge,new Vector3(0,1.5f,0),Quaternion.Euler(0,90,0)),Is.False);
            Assert.That(assembly.Count,Is.EqualTo(1));
        }
        [TestCase(BlockShape.VLS1)][TestCase(BlockShape.Gun2)][TestCase(BlockShape.Power2)]
        public void MultiCellAnchorsReflectTheirEntireFootprint(BlockShape shape)
        {
            Put(BlockShape.Core,0);
            var original=new BlockRecord{shape=shape,position=new Vector3(4,.5f,2),rotation=Quaternion.identity};
            Assert.That(assembly.TryMirrorPose(shape,original.position,original.rotation,out var pose,out bool same),Is.True,assembly.PlacementFailure);
            Assert.That(same,Is.False);
            var expected=BlockModules.PlacementCells(original).Select(c=>new Vector3Int(-c.x,c.y,c.z));
            CollectionAssert.AreEquivalent(expected,BlockModules.PlacementCells(new BlockRecord{shape=shape,position=pose.position,rotation=pose.rotation}));
        }
        [Test]public void WedgeOrientationReflectsActualSlopeVertices()
        {
            Put(BlockShape.Core,0);var p=new Vector3(3,.5f,1);var rotation=Quaternion.Euler(0,90,0);
            Assert.That(assembly.TryMirrorPose(BlockShape.Wedge,p,rotation,out var mirror,out _),Is.True);
            var wanted=new HashSet<Vector3Int>();var actual=new HashSet<Vector3Int>();
            foreach(var vertex in assembly.Solid(BlockShape.Wedge).vertices)
            {
                var point=p+rotation*vertex;wanted.Add(Vector3Int.RoundToInt(new Vector3(-point.x,point.y,point.z)*1000));
                actual.Add(Vector3Int.RoundToInt((mirror.position+mirror.rotation*vertex)*1000));
            }
            CollectionAssert.AreEquivalent(wanted,actual);
        }
        [Test]public void MirroredDeleteRemovesOnlyEquivalentCounterpartTogether()
        {
            CoreAndArms();assembly.PlaceMirrored(BlockShape.Cube,new Vector3(2,.5f,0),Quaternion.identity);
            string id=assembly.Blocks.First(b=>b.Record.position==new Vector3(2,.5f,0)).Record.id;
            int changes=0;assembly.Changed+=()=>changes++;
            Assert.That(assembly.RemoveMirrored(id),Is.True);Assert.That(assembly.Count,Is.EqualTo(3));Assert.That(changes,Is.EqualTo(1));
        }
        [Test]public void MirroredDeleteCannotLeaveEitherOuterArmDisconnected()
        {
            CoreAndArms();assembly.PlaceMirrored(BlockShape.Cube,new Vector3(2,.5f,0),Quaternion.identity);
            string id=assembly.Blocks.First(b=>b.Record.position==new Vector3(1,.5f,0)).Record.id;
            Assert.That(assembly.RemoveMirrored(id),Is.False);Assert.That(assembly.Count,Is.EqualTo(5));
        }
        [Test]public void CoreRemainsSingleAndMirrorRequiresCoreForOtherParts()
        {
            Assert.That(assembly.PlaceMirrored(BlockShape.Cube,Vector3.up*.5f,Quaternion.identity),Is.False);
            Assert.That(assembly.PlaceMirrored(BlockShape.Core,Vector3.up*.5f,Quaternion.identity),Is.True);
            Assert.That(assembly.Count,Is.EqualTo(1));
        }
    }
}

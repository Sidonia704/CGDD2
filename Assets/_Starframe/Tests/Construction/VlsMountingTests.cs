using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
namespace Starframe.Construction.Tests
{
    public sealed class VlsMountingTests
    {
        Starframe.Flight.ModuleDatabase previous;
        [SetUp]public void Setup(){previous=BlockModules.ConfiguredDatabase;BlockModules.Configure(null);}
        [TearDown]public void Cleanup()=>BlockModules.Configure(previous);
        [TestCase(BlockShape.VLS1,8,4)][TestCase(BlockShape.VLS2,4,2)]
        public void UnitOccupancyAndCompleteLaunchSurfaceAreReserved(BlockShape shape,int count,int mouths)
        {
            var p=Part(shape,Vector3.zero,Quaternion.identity);
            Assert.That(BlockModules.LocalCells(shape).Length,Is.EqualTo(count));
            Assert.That(BlockModules.ExhaustCells(p).Count,Is.EqualTo(mouths));
            Assert.That(BlockModules.PlacementCells(p).Count,Is.EqualTo(count+mouths));
            Assert.That(BlockModules.IsWeapon(shape),Is.True);
        }
        [TestCase(BlockShape.VLS1)][TestCase(BlockShape.VLS2)]
        public void BottomAndSidesMountWhileLaunchLidNeverMounts(BlockShape shape)
        {
            foreach(var n in new[]{Vector3Int.down,Vector3Int.left,Vector3Int.right,Vector3Int.forward,Vector3Int.back})
                Assert.That(BlockModules.HasAttachmentSurface(shape,Vector3Int.zero,n),Is.True);
            Assert.That(BlockModules.HasAttachmentSurface(shape,Vector3Int.zero,Vector3Int.up),Is.False);
            Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.Euler(0,180,90),Vector3.forward),Is.True);
        }
        [TestCase(0,0)][TestCase(0,90)][TestCase(90,0)][TestCase(180,0)]
        public void FullLaunchColumnBlockingRotatesWithModule(float pitch,float roll)
        {
            var rotation=Quaternion.Euler(pitch,0,roll);var p=Part(BlockShape.VLS1,Vector3.zero,rotation);
            var blocked=Part(BlockShape.Cube,rotation*new Vector3(1,6,1),Quaternion.identity);
            var side=Part(BlockShape.Cube,rotation*new Vector3(3,6,1),Quaternion.identity);
            Assert.That(WeaponClearance.VerticalLaunchPathBlockedBy(p,blocked),Is.True);
            Assert.That(WeaponClearance.VerticalLaunchPathBlockedBy(p,side),Is.False);
        }
        static BlockRecord Part(BlockShape shape,Vector3 delta,Quaternion rotation)=>new BlockRecord{shape=shape,position=Vector3.up*.5f+delta,rotation=rotation};
    }
}

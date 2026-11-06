using NUnit.Framework;
using UnityEngine;
namespace Starframe.Construction.Tests
{
    public sealed class VlsSideAttachmentTests
    {
        [TestCase(0)][TestCase(90)][TestCase(180)][TestCase(270)]
        public void EveryNonLaunchFaceSupportsActualConnectivityAtAnyGridOrientation(int angle)
        {
            BlockModules.Configure(null);
            foreach(var shape in new[]{BlockShape.VLS1,BlockShape.VLS2})
            {
                var rotation=Quaternion.Euler(0,0,angle);var candidate=new BlockRecord{shape=shape,position=Vector3.up*.5f,rotation=rotation};
                int width=shape==BlockShape.VLS1?2:1;
                foreach(var offset in new[]{new Vector3(-1,0,0),new Vector3(width,0,0),new Vector3(0,-1,0),new Vector3(0,0,-1),new Vector3(0,0,2)})
                {
                    var support=new BlockRecord{shape=BlockShape.Cube,position=candidate.position+rotation*offset,rotation=rotation};
                    Assert.That(BlockConnectivity.Touches(candidate,support),Is.True,shape+" "+offset);
                    Assert.That(BlockConnectivity.HasCompleteMount(candidate,new[]{support}),Is.True);
                }
                var lid=new BlockRecord{shape=BlockShape.Cube,position=candidate.position+rotation*new Vector3(0,2,0),rotation=rotation};
                Assert.That(BlockConnectivity.Touches(candidate,lid),Is.False);
                Assert.That(BlockConnectivity.HasCompleteMount(candidate,new[]{lid}),Is.False);
            }
        }
    }
}

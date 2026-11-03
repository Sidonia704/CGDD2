using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Starframe.Combat;

namespace Starframe.Tests
{
    public sealed class DebrisBoundsTests
    {
        [Test]
        public void LongSpinningWreckBoundsAcceptSlightlyNonUnitNativeRotationsWithoutPoseMutation()
        {
            var local=new Bounds(new Vector3(2,-3,1),new Vector3(13,5,21));
            var scale=new Vector3(1.3f,.7f,2);
            var position=new Vector3(187,-412,993);
            Quaternion rotation=Quaternion.Euler(17,43,91);
            for(int i=0;i<2000;i++)
            {
                rotation=rotation*Quaternion.Euler(.17f,-.31f,.23f);
                // Match and exceed the observed squared length of 1.000010 without an invalid physical pose.
                Quaternion unit=Quaternion.Normalize(rotation);
                float drift=1+(i%7+1)*.000002f;
                var native=new Quaternion(unit.x*drift,unit.y*drift,unit.z*drift,unit.w*drift);
                var snapshot=native;
                Bounds actual=DebrisCluster.BoundsForPhysicsPose(local,position,native,scale);
                Bounds expected=new Bounds();
                for(int c=0;c<8;c++)
                {
                    var corner=local.center+Vector3.Scale(local.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));
                    var point=position+Quaternion.Normalize(native)*Vector3.Scale(corner,scale);
                    if(c==0)expected=new Bounds(point,Vector3.zero);else expected.Encapsulate(point);
                }
                Assert.That(Vector3.Distance(actual.center,expected.center),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(actual.size,expected.size),Is.LessThan(.001f));
                Assert.That(native.x,Is.EqualTo(snapshot.x));Assert.That(native.w,Is.EqualTo(snapshot.w));
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}

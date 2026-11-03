using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
namespace Starframe.Construction.Tests
{
    public sealed class TracerReadabilityTests
    {
        GameObject cameraObject;RenderTexture target;
        [TearDown]public void Cleanup()
        {
            if(cameraObject)Object.DestroyImmediate(cameraObject);
            if(target)Object.DestroyImmediate(target);
        }
        Camera Camera()
        {
            cameraObject=new GameObject("Tracer projection test camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=60;camera.orthographicSize=100;
            target=new RenderTexture(1280,720,0);camera.targetTexture=target;return camera;
        }
        [TestCase(false)][TestCase(true)]
        public void ReadableWidthProjectsToRequestedPixelsInBothCameraModes(bool orthographic)
        {
            var camera=Camera();camera.orthographic=orthographic;var center=new Vector3(0,0,100);
            float width=LaserVisualPool.MinimumReadableWidth(camera,center,.2f,3);
            float projected=Vector3.Distance(camera.WorldToScreenPoint(center-camera.transform.right*width*.5f),
                camera.WorldToScreenPoint(center+camera.transform.right*width*.5f));
            Assert.That(projected,Is.EqualTo(3).Within(.02f),"Thickness should remain readable at the actual render-target resolution.");
            Assert.That(width,Is.GreaterThan(.2f));Assert.That(width,Is.LessThanOrEqualTo(2));
        }
        [Test]public void ExtremeDistanceCapsExpansionAndZeroPixelsRetainsAuthoredWidth()
        {
            var camera=Camera();var far=new Vector3(0,0,10000);
            Assert.That(LaserVisualPool.MinimumReadableWidth(camera,far,.2f,3),Is.EqualTo(2).Within(.0001f));
            Assert.That(LaserVisualPool.MinimumReadableWidth(camera,far,.2f,0),Is.EqualTo(.2f));
            Assert.That(LaserVisualPool.MinimumReadableWidth(camera,Vector3.back*20,.2f,3),Is.EqualTo(.2f));
        }
    }
}

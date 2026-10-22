using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Construction;
namespace Starframe.Tests
{
    public sealed class BlueprintRepositoryTests
    {
        string directory;BlueprintRepository repository;
        [SetUp] public void SetUp(){directory=Path.Combine(Application.dataPath,"..","Temp","BlueprintTests",Guid.NewGuid().ToString("N"));repository=new BlueprintRepository(directory);}
        [TearDown] public void TearDown(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        static BlueprintData Example(string name="舰船样例")=>new BlueprintData{name=name,parts=new System.Collections.Generic.List<BlockRecord>{
            new BlockRecord{id="core-shape",shape=BlockShape.Cube,cell=new Vector3Int(-1,0,2),position=new Vector3(-1,.5f,2),rotation=Quaternion.identity},
            new BlockRecord{id="slope-shape",shape=BlockShape.Wedge,cell=new Vector3Int(0,0,2),yawQuarterTurns=3,position=new Vector3(0,.5f,2),rotation=Quaternion.Euler(90,0,0)}
        }};
        [Test] public void RoundTripPreservesFullThreeDimensionalPose()
        {
            var data=Example();string path=repository.Save(data);var loaded=repository.Load(Path.GetFileName(path));
            Assert.AreEqual(2,loaded.version);Assert.AreEqual(data.name,loaded.name);
            Assert.AreEqual("slope-shape",loaded.parts[1].id);Assert.AreEqual(BlockShape.Wedge,loaded.parts[1].shape);
            Assert.AreEqual(data.parts[1].position,loaded.parts[1].position);
            Assert.Less(Quaternion.Angle(data.parts[1].rotation,loaded.parts[1].rotation),.001f);
        }
        [Test] public void LegacyBlueprintMigrationPreservesGeometryWithoutMutatingSource()
        {
            var data=Example();data.version=1;
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"legacy.json");File.WriteAllText(path,JsonUtility.ToJson(data));
            var loaded=repository.Load("legacy.json");
            Assert.AreEqual(2,loaded.version);Assert.AreEqual(new Vector3(-1,.5f,2),loaded.parts[0].position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0,270,0),loaded.parts[1].rotation),.001f);
            var upgrade=BlueprintRepository.Upgrade(data);Assert.AreEqual(1,data.version);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(90,0,0),data.parts[1].rotation),.001f);
        }
        [Test] public void DuplicateNameNeverOverwritesExistingBlueprint()
        {
            var data=Example("装配 / Alpha");string path=repository.Save(data);string original=File.ReadAllText(path);
            data.parts.RemoveAt(1);
            Assert.Throws<IOException>(()=>repository.Save(data));
            data.name="装配 / alpha";Assert.Throws<IOException>(()=>repository.Save(data));
            Assert.AreEqual(original,File.ReadAllText(path));Assert.AreEqual(1,Directory.GetFiles(directory).Length);
        }
        [Test] public void DuplicatePoseRejectedBeforeChangingExistingBlueprint()
        {
            var data=Example();string path=repository.Save(data);string before=File.ReadAllText(path);
            data.parts[1].shape=data.parts[0].shape;data.parts[1].position=data.parts[0].position;
            data.parts[1].rotation=new Quaternion(0,0,0,-1);
            Assert.Throws<InvalidDataException>(()=>repository.Save(data));Assert.AreEqual(before,File.ReadAllText(path));
        }
        [Test] public void TraversalUnsupportedShapeAndInvalidQuaternionAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>repository.Load("../outside.json"));
            var data=Example();data.parts[0].shape=(BlockShape)42;
            Assert.Throws<InvalidDataException>(()=>repository.Save(data));
            data=Example();data.parts[0].rotation=new Quaternion(0,0,0,0);
            Assert.Throws<InvalidDataException>(()=>repository.Save(data));Assert.IsFalse(Directory.Exists(directory));
        }
        [Test] public void EmptyBlueprintAndUnsupportedVersion()
        {
            var data=new BlueprintData{name="空蓝图"};string path=repository.Save(data);Assert.AreEqual(0,repository.Load(Path.GetFileName(path)).parts.Count);
            data.version=3;Assert.Throws<InvalidDataException>(()=>repository.Save(data));
        }
        [TestCase(BlockShape.Cube,1f)][TestCase(BlockShape.Wedge,.5f)]
        public void PrimitiveMeshHasExpectedVolume(BlockShape shape,float expected)
        {
            var mesh=BlockGeometry.CreateSolid(shape);
            try{var v=mesh.vertices;var t=mesh.triangles;float volume=0;
                for(int i=0;i<t.Length;i+=3)volume+=Vector3.Dot(v[t[i]],Vector3.Cross(v[t[i+1]],v[t[i+2]]))/6f;
                Assert.AreEqual(expected,Mathf.Abs(volume),.0001f);Assert.AreEqual(Vector3.one,mesh.bounds.size);}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        [TestCase(1,0,0)][TestCase(-1,0,0)][TestCase(0,1,0)][TestCase(0,-1,0)][TestCase(0,0,1)][TestCase(0,0,-1)]
        public void BaseAndQuarterTurnUseTheSelectedFaceNormal(int x,int y,int z)
        {
            var n=new Vector3(x,y,z);var a=BlockPlacement.FaceRotation(n,0);var b=BlockPlacement.FaceRotation(n,1);
            Assert.Greater(Vector3.Dot(a*Vector3.down,-n),.99999f);
            Assert.Greater(Vector3.Dot(b*Vector3.up,n),.99999f);
            Assert.Less(Quaternion.Angle(Quaternion.AngleAxis(90,n),b*Quaternion.Inverse(a)),.001f);
        }
        [Test] public void SlopedFaceIsRejectedButAxisFaceUsesAdjacentCell()
        {
            var support=new GameObject("GridSupport");
            try{
                support.transform.position=new Vector3(0,.5f,0);
                Assert.IsFalse(BlockPlacement.TryOnFace(BlockShape.Wedge,support.transform,new Vector3(0,1,-1).normalized,0,out _));
                Assert.IsTrue(BlockPlacement.TryOnFace(BlockShape.Cube,support.transform,Vector3.right,1,out var pose));
                Assert.AreEqual(new Vector3(1,.5f,0),pose.position);
                Assert.IsTrue(BlockPlacement.IsCellAligned(pose.position,pose.rotation));
            }finally{UnityEngine.Object.DestroyImmediate(support);}
        }
        [Test] public void NormalizedWheelZoomExceedsFivefoldMinimum()
        {
            var cameraObject=new GameObject("ZoomCamera");var host=new GameObject("ZoomController");
            try{
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
                var orbit=host.AddComponent<WorkbenchOrbit>();
                typeof(WorkbenchOrbit).GetField("viewCamera",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(orbit,camera);
                float before=orbit.Size;orbit.Zoom(1);
                Assert.AreEqual(before*Mathf.Exp(-.18f),orbit.Size,.00001f);
                Assert.Greater(before-orbit.Size,5*(before-before*Mathf.Exp(-.001f)));
            }finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
        [Test] public void RealColliderProbesAllowContactAndRejectPenetration()
        {
            var host=new GameObject("CollisionAssembly");
            try{
                var assembly=host.AddComponent<ShipAssembly>();
                if(assembly.Solid(BlockShape.Cube)==null)typeof(ShipAssembly).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(assembly,null);
                Assert.IsTrue(assembly.Place(BlockShape.Cube,new Vector3Int(0,0,0)));
                Assert.IsTrue(assembly.CanPlace(BlockShape.Cube,new Vector3(1,.5f,0),Quaternion.identity));
                Assert.IsFalse(assembly.CanPlace(BlockShape.Cube,new Vector3(.25f,.5f,0),Quaternion.identity));
                var invalid=new BlueprintData{name="overlap",parts=new System.Collections.Generic.List<BlockRecord>{
                    new BlockRecord{id="a",shape=BlockShape.Cube,position=new Vector3(0,.5f,0),rotation=Quaternion.identity},
                    new BlockRecord{id="b",shape=BlockShape.Cube,position=new Vector3(.25f,.5f,0),rotation=Quaternion.identity}
                }};
                Assert.Throws<InvalidDataException>(()=>assembly.Import(invalid));Assert.AreEqual(1,assembly.Count);
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
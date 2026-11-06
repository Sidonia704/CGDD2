using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class ExpandedHullPlacementTests
    {
        readonly List<Object> owned=new List<Object>();
        ModuleDatabase Database=>AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        [SetUp]public void Setup(){BlockModules.Configure(Database);}
        [TearDown]public void Cleanup(){BlockModules.Configure(null);VoxelTemplateCache.Clear();foreach(var item in owned)if(item)Object.DestroyImmediate(item);owned.Clear();}
        static BlockRecord Part(BlockShape shape,Vector3Int cell,Quaternion? rotation=null)=>new BlockRecord{shape=shape,position=(Vector3)cell+Vector3.up*.5f,rotation=rotation??Quaternion.identity};
        [TestCase(BlockShape.Slab)][TestCase(BlockShape.Block7)]
        public void ThinTopHasNoFloatingConnectionWhileSideAndBottomRemainMountable(BlockShape shape)
        {
            var slab=Part(shape,Vector3Int.zero);
            Assert.That(BlockConnectivity.Touches(slab,Part(BlockShape.Cube,Vector3Int.up)),Is.False);
            Assert.That(BlockConnectivity.Touches(slab,Part(BlockShape.Cube,Vector3Int.down)),Is.True);
            Assert.That(BlockConnectivity.Touches(slab,Part(BlockShape.Cube,Vector3Int.right)),Is.True);
        }
        [TestCase(BlockShape.Laser)][TestCase(BlockShape.Gun1)][TestCase(BlockShape.Railgun1)][TestCase(BlockShape.Missile1)][TestCase(BlockShape.Sword1)]
        public void WeaponMountsOnlyThroughItsBottom(BlockShape shape)
        {
            var weapon=Part(shape,Vector3Int.zero);
            Assert.That(BlockConnectivity.Touches(weapon,Part(BlockShape.Cube,Vector3Int.down)),Is.True,shape+" bottom");
            Assert.That(BlockConnectivity.Touches(weapon,Part(BlockShape.Cube,Vector3Int.up)),Is.False,shape+" top");
            Assert.That(BlockConnectivity.Touches(weapon,Part(BlockShape.Cube,Vector3Int.left)),Is.False,shape+" side");
            Assert.That(BlockConnectivity.Touches(weapon,Part(shape,Vector3Int.up)),Is.False,"Weapons cannot stack on weapons.");
        }
        [Test]public void OpposingWeaponMountsCannotBypassEquipmentStackingRule()
        {
            Assert.That(BlockConnectivity.Touches(Part(BlockShape.Laser,Vector3Int.zero),Part(BlockShape.Laser,Vector3Int.down,Quaternion.Euler(0,0,180))),Is.False);
        }
        [TestCase(BlockShape.Armor1)][TestCase(BlockShape.Armor2)][TestCase(BlockShape.Armor3)][TestCase(BlockShape.Armor4)]
        public void ArmorHasOneMountingFaceInEveryOrientation(BlockShape shape)
        {
            var directions=new[]{Vector3Int.up,Vector3Int.down,Vector3Int.left,Vector3Int.right,Vector3Int.forward,Vector3Int.back};
            foreach(var mounting in directions)
            {
                var rotation=BlockPlacement.FaceRotation(mounting,1);var armor=Part(shape,Vector3Int.zero,rotation);
                var offset=Vector3Int.RoundToInt(rotation*(Vector3)BlockModules.MountingCell(shape));
                foreach(var side in directions)Assert.That(BlockConnectivity.Touches(armor,Part(BlockShape.Cube,offset+side)),Is.EqualTo(side==-mounting),shape+" "+mounting+" "+side);
            }
        }
        [TestCase(BlockShape.Armor3)]
        public void ActualArmorFootIsAlignedToClickedCubeForEveryMountingRotation(BlockShape shape)
        {
            var host=new GameObject("Armor mounting cursor fixture");owned.Add(host);var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(Database);
            Assert.That(assembly.Place(BlockShape.Cube,Vector3.up*.5f,Quaternion.identity),Is.True);
            Assert.That(BlockModules.MountingCell(shape),Is.EqualTo(new Vector3Int(0,0,2)),"Sloped plates mount at their flat rear foot.");
            Assert.That(BlockConnectivity.Touches(Part(shape,Vector3Int.zero),Part(BlockShape.Cube,Vector3Int.down)),Is.False,"The forward sloped underside cannot float above a support.");
            foreach(var normal in new[]{Vector3Int.up,Vector3Int.down,Vector3Int.left,Vector3Int.right,Vector3Int.forward,Vector3Int.back})
                for(int turn=0;turn<4;turn++)
                {
                    var contact=new Pose(Vector3.up*.5f+(Vector3)normal,BlockPlacement.FaceRotation(normal,turn));
                    var pose=BlockPlacement.AlignMount(shape,contact);
                    Assert.That(assembly.CanPlace(shape,pose.position,pose.rotation),Is.True,shape+" "+normal+" "+turn+": "+assembly.PlacementFailure);
                    Assert.That(Vector3.Distance(pose.position+pose.rotation*(Vector3)BlockModules.MountingCell(shape),contact.position),Is.LessThan(.0001f));
                }
        }
        [TestCase(BlockShape.Gun2)][TestCase(BlockShape.Armor4)]
        public void WideWeaponOrArmorRequiresSupportUnderBothBottomColumns(BlockShape shape)
        {
            var part=Part(shape,Vector3Int.zero);
            var left=Part(BlockShape.Cube,new Vector3Int(0,-1,2));
            var right=Part(BlockShape.Cube,new Vector3Int(1,-1,2));
            Assert.That(BlockConnectivity.HasCompleteMount(part,new[]{left}),Is.False,shape+" must reject a single bottom support.");
            Assert.That(BlockConnectivity.HasCompleteMount(part,new[]{left,right}),Is.True,shape+" must accept both bottom columns.");
        }
        [TestCase(BlockShape.Power)][TestCase(BlockShape.Power2)]
        public void PowerTopCarriesARegularBlock(BlockShape shape)
        {
            int top=shape==BlockShape.Power2?1:0;
            var power=Part(shape,Vector3Int.zero);var cube=Part(BlockShape.Cube,new Vector3Int(0,top+1,0));
            Assert.That(BlockModules.HasAttachmentSurface(shape,new Vector3Int(0,top,0),Vector3Int.up),Is.True,shape+" top face");
            Assert.That(BlockConnectivity.Touches(power,cube),Is.True,shape+" carries a block on top.");
        }
        [TestCase(BlockShape.Thruster3)][TestCase(BlockShape.Thruster4)][TestCase(BlockShape.Thruster5)]
        public void LargeEngineReservesTheWholeNozzleWidthAndOnlyBeyondItsBody(BlockShape shape)
        {
            var engine=Part(shape,Vector3Int.zero);var body=BlockModules.BodyCells(engine);var clearance=BlockModules.ExhaustCells(engine);
            int x=body.Max(c=>c.x)+1,z=body.Max(c=>c.z)+1,y=body.Max(c=>c.y)+1;
            Assert.That(clearance.Count,Is.EqualTo(x*z*BlockModules.Clearance(shape)));
            Assert.That(clearance.All(c=>c.y>=y),Is.True);
            Assert.That(BlockModules.CellsOverlap(engine,Part(BlockShape.Cube,new Vector3Int(x-1,y,z-1))),Is.True,"Far nozzle corner must also remain empty.");
        }
        [TestCase(BlockShape.Block7,2)][TestCase(BlockShape.Block8,8)]
        public void MultiCellMapHullKeepsRotatedDimensionsAndModelOffsetsThroughSerialization(BlockShape shape,int count)
        {
            var turns=new Vector3Int(1,2,3);var anchor=new Vector3Int(4,5,6);
            var piece=VoxelAuthoring.HullPiece(shape,anchor,turns,0,Database);
            Assert.That(piece.Count,Is.EqualTo(count));
            Assert.That(piece.Select(c=>c.position).Distinct().Count(),Is.EqualTo(count));
            foreach(var cell in piece)
            {
                var copy=JsonUtility.FromJson<VoxelCell>(JsonUtility.ToJson(cell.Copy()));
                Assert.That(copy.modelOffset,Is.EqualTo(cell.modelOffset));
                Assert.That(copy.position,Is.EqualTo(anchor+Vector3Int.RoundToInt(copy.Rotation*(Vector3)copy.modelOffset)));
            }
            var asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();owned.Add(asset);asset.hullModules=Database;asset.cells=piece;
            Assert.That(asset.Validate(out var error),Is.True,error);
            var go=new GameObject("Expanded map hull fixture");owned.Add(go);var runtime=go.AddComponent<VoxelStructureRuntime>();runtime.Initialize(asset,true);
            Assert.That(runtime.AliveCount,Is.EqualTo(count));
            var source=Database.Get(shape).mesh;var bounds=go.GetComponent<MeshFilter>().sharedMesh.bounds;
            var expected=new Bounds();bool first=true;
            foreach(var p in source.vertices){var v=(Vector3)anchor+piece[0].Rotation*p;if(first){expected=new Bounds(v,Vector3.zero);first=false;}else expected.Encapsulate(v);}
            Assert.That(Vector3.Distance(bounds.center,expected.center),Is.LessThan(.001f));
            Assert.That(Vector3.Distance(bounds.size,expected.size),Is.LessThan(.001f));
            
            Assert.That(runtime.AliveCount,Is.EqualTo(count));
            var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount,Is.GreaterThan(0));Assert.That(mesh.bounds.size.magnitude,Is.LessThanOrEqualTo(bounds.size.magnitude+.001f));
            runtime.DisposeResources();
        }
        [Test]public void PerCellPartitionStaysClosedAndInsideItsOwnOccupiedCell()
        {
            foreach(var cell in VoxelAuthoring.HullPiece(BlockShape.Block8,Vector3Int.zero,Vector3Int.zero,0,Database))
            {
                var mesh=VoxelHullAppearance.CreateCellMesh(Database,cell.shape,cell.modelOffset);owned.Add(mesh);
                Assert.That(mesh.vertexCount,Is.GreaterThan(0));
                foreach(var p in mesh.vertices)Assert.That(Mathf.Max(Mathf.Abs(p.x),Mathf.Max(Mathf.Abs(p.y),Mathf.Abs(p.z))),Is.LessThanOrEqualTo(.5001f));
                var vertices=mesh.vertices;var normals=mesh.normals;var indices=mesh.triangles;
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                    if(n.sqrMagnitude>.0000001f)Assert.That(Vector3.Dot(n.normalized,(normals[a]+normals[b]+normals[c]).normalized),Is.GreaterThan(.5f));
                }
            }
        }
    }
}


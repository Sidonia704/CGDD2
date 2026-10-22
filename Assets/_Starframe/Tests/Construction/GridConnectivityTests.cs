using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class GridConnectivityTests
    {
        GameObject host;
        static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
        [TearDown]public void TearDown(){if(host)Object.DestroyImmediate(host);BlockModules.Configure(null);}

        [TestCase(BlockShape.Wedge)][TestCase(BlockShape.Slab)][TestCase(BlockShape.Pyramid)]
        public void VisualShapeDoesNotRemoveStandardCellFaces(BlockShape shape)
        {
            BlockModules.Configure(null);
            foreach(var normal in Directions)for(int turn=0;turn<4;turn++){
                var part=Record("part",shape,Vector3Int.zero);part.rotation=BlockPlacement.FaceRotation(normal,turn);
                foreach(var side in Directions){
                    Assert.That(BlockConnectivity.Touches(part,Record("neighbor",BlockShape.Cube,side)),Is.True,$"{shape}, {normal}, rotation {turn}, face {side}");
                }
            }
            Assert.That(BlockConnectivity.Touches(Record("a",shape,Vector3Int.zero),Record("b",BlockShape.Cube,new Vector3Int(1,1,0))),Is.False,"An edge does not provide structural support.");
        }

        [Test]public void MultiCellBodyConnectsAtItsFarEndButExhaustClearanceDoesNot()
        {
            BlockModules.Configure(null);
            Assert.That(BlockConnectivity.Touches(Record("laser",BlockShape.Laser,Vector3Int.zero),Record("hull",BlockShape.Cube,new Vector3Int(1,0,2))),Is.True);
            Assert.That(BlockConnectivity.Touches(Record("engine",BlockShape.Thruster1,Vector3Int.zero),Record("hull",BlockShape.Cube,new Vector3Int(0,3,0))),Is.False);
        }

        [Test]public void DestroyingOneBridgeKeepsHullConnectedThroughTheSlabCell()
        {
            var data=new BlueprintData{name="two independent structural paths",parts=new List<BlockRecord>{
                Record("core",BlockShape.Core,new Vector3Int(0,0,0)),
                Record("bridge",BlockShape.Cube,new Vector3Int(1,0,0)),
                Record("hull",BlockShape.Cube,new Vector3Int(2,0,0)),
                Record("backup",BlockShape.Slab,new Vector3Int(0,0,1)),
                Record("upper-a",BlockShape.Cube,new Vector3Int(0,1,1)),
                Record("upper-b",BlockShape.Cube,new Vector3Int(1,1,1)),
                Record("upper-c",BlockShape.Cube,new Vector3Int(2,1,1)),
                Record("end",BlockShape.Cube,new Vector3Int(2,0,1))}};
            var runtime=Create(data);var bridge=runtime.Parts.Find(p=>p.record.id=="bridge");
            runtime.ApplyDamage("bridge",bridge.maxHp);
            foreach(var part in runtime.Parts)Assert.That(part.present,Is.EqualTo(part.record.id!="bridge"),part.record.id);
            var backup=runtime.Parts.Find(p=>p.record.id=="backup");runtime.ApplyDamage("backup",backup.maxHp);
            Assert.That(runtime.Parts.FindAll(p=>p.present).Count,Is.EqualTo(1),"Cutting both paths really does isolate the core.");
            Assert.That(runtime.IsAlive,Is.True);
        }

        [Test]public void ProductionTestShipDamageMatchesIndependentCellFloodFill()
        {
            var source=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Starframe/Data/RangeTargets/test.json");
            var runtime=Create(JsonUtility.FromJson<BlueprintData>(source.text));
            var random=new System.Random(241017);int core=runtime.Parts.FindIndex(p=>p.record.shape==BlockShape.Core);
            for(int step=0;step<30;step++){
                var candidates=new List<int>();for(int i=0;i<runtime.Parts.Count;i++)if(i!=core&&runtime.Parts[i].present)candidates.Add(i);
                if(candidates.Count==0)break;int hit=candidates[random.Next(candidates.Count)];
                var cells=new Dictionary<Vector3Int,int>();
                for(int i=0;i<runtime.Parts.Count;i++)if(i!=hit&&runtime.Parts[i].present)foreach(var cell in BlockModules.BodyCells(runtime.Parts[i].record))cells.Add(cell,i);
                var expected=new HashSet<int>{core};var queue=new Queue<int>();queue.Enqueue(core);
                while(queue.Count>0){int current=queue.Dequeue();foreach(var cell in BlockModules.BodyCells(runtime.Parts[current].record))foreach(var side in Directions)
                    if(cells.TryGetValue(cell+side,out int next)&&expected.Add(next))queue.Enqueue(next);}
                runtime.ApplyDamage(runtime.Parts[hit].record.id,runtime.Parts[hit].maxHp);
                for(int i=0;i<runtime.Parts.Count;i++)Assert.That(runtime.Parts[i].present,Is.EqualTo(expected.Contains(i)),"damage step "+step+", part "+runtime.Parts[i].record.id);
            }
        }

        FlightShipRuntime Create(BlueprintData data)
        {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            host=new GameObject("Connectivity regression fixture");var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
            data=BlueprintRepository.Upgrade(data);assembly.Import(data);var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,data,tuning);return runtime;
        }
        static BlockRecord Record(string id,BlockShape shape,Vector3Int cell)=>new BlockRecord{id=id,shape=shape,position=(Vector3)cell+Vector3.up*.5f,cell=cell,rotation=Quaternion.identity};
    }
}

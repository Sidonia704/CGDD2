// Execute BuildEnemyFleet.Main through Unity run_script after gameplay sources compile.
// Writes only the generated enemy catalog, native TextAssets and behavior profiles.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class BuildEnemyFleet
{
    const string Root="Assets/_Starframe/Data/Enemies";
    const string Evidence="Documentation/ArcadeMission-2026-11-02/Fleet";
    sealed class Spec
    {
        public string id,zh,en; public EnemySize size; public BotCombatRole role;
        public int width,length,score,armor; public BlockShape engine;
        public int[] thrusterRows; public BlockShape[] guns;
        public int[] gunX; public bool lowerDeck; public int powerCount=1,shieldCount=1;
    }
    sealed class State
    {
        public Spec spec; public BlueprintData ship; public ModuleDatabase db;
        public HashSet<Vector3Int> reserved=new HashSet<Vector3Int>();
        public Dictionary<string,int> groups=new Dictionary<string,int>();
    }
    [Serializable] public sealed class FleetSummary
    {
        public string id,name,blueprint; public int parts,weapons; public float cost,massKg,hp,shield,generation,storage,sustainedWeaponPower;
        public float[] positiveAcceleration,negativeAcceleration; public float turnDegreesPerSecond;
    }

    public static string Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before regenerating enemy assets.");
        var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        if(!db||!tuning)throw new FileNotFoundException("Native module database/flight tuning missing.");
        var prior=BlockModules.ConfiguredDatabase;BlockModules.Configure(db);
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Evidence);
        var specs=new[]{
            new Spec{id="needle",zh="针刺截击机",en="Needle Interceptor",size=EnemySize.Small,role=BotCombatRole.Interceptor,width=3,length=5,score=100,armor=2,engine=BlockShape.Thruster1,thrusterRows=new[]{-3,0},guns=new[]{BlockShape.Gun1,BlockShape.Gun1},gunX=new[]{-1,1},shieldCount=0,powerCount=2},
            new Spec{id="viper",zh="蝰蛇突击机",en="Viper Assault Fighter",size=EnemySize.Small,role=BotCombatRole.Assault,width=4,length=5,score=150,armor=6,engine=BlockShape.Thruster2,thrusterRows=new[]{-3,0},guns=new[]{BlockShape.Laser,BlockShape.Gun1},gunX=new[]{-1,1},powerCount=3},
            new Spec{id="wasp",zh="黄蜂导弹机",en="Wasp Missile Fighter",size=EnemySize.Small,role=BotCombatRole.Missile,width=3,length=6,score=200,armor=2,engine=BlockShape.Thruster1,thrusterRows=new[]{-4,-1},guns=new[]{BlockShape.Missile1,BlockShape.Gun1},gunX=new[]{-1,1},powerCount=2},
            new Spec{id="bastion",zh="堡垒重装炮艇",en="Bastion Gunship",size=EnemySize.Large,role=BotCombatRole.Gunship,width=5,length=10,score=600,armor=18,engine=BlockShape.Thruster3,thrusterRows=new[]{-7,-4,-1},guns=new[]{BlockShape.Gun2,BlockShape.Laser2,BlockShape.Railgun1},gunX=new[]{-4,0,3},lowerDeck=true,powerCount=2,shieldCount=2},
            new Spec{id="harrier",zh="鹞式导弹护卫舰",en="Harrier Missile Frigate",size=EnemySize.Large,role=BotCombatRole.Frigate,width=5,length=11,score=800,armor=16,engine=BlockShape.Thruster3,thrusterRows=new[]{-8,-5,-2},guns=new[]{BlockShape.Missile2,BlockShape.Gun2,BlockShape.Missile2},gunX=new[]{-4,-1,2},lowerDeck=true,powerCount=2,shieldCount=2},
            new Spec{id="halberd",zh="长戟强袭巡洋舰",en="Halberd Assault Cruiser",size=EnemySize.Large,role=BotCombatRole.Cruiser,width=6,length=11,score=1000,armor=24,engine=BlockShape.Thruster5,thrusterRows=new[]{-8,-5,-2},guns=new[]{BlockShape.Laser2,BlockShape.Railgun2,BlockShape.Gun2,BlockShape.Missile1},gunX=new[]{-5,-2,0,4},lowerDeck=true,powerCount=3,shieldCount=2},
            new Spec{id="citadel",zh="壁垒主力战舰",en="Citadel Capital Ship",size=EnemySize.Capital,role=BotCombatRole.Capital,width=9,length=15,score=5000,armor=52,engine=BlockShape.Thruster5,thrusterRows=new[]{-12,-9,-6,-3,0},guns=new[]{BlockShape.Laser3,BlockShape.Railgun3,BlockShape.Gun3,BlockShape.Missile2,BlockShape.Missile2},gunX=new[]{-8,-5,-2,2,6},lowerDeck=true,powerCount=5,shieldCount=4}
        };
        var summaries=new List<FleetSummary>();
        try
        {
            foreach(var spec in specs)
            {
                var data=Build(spec,db);ValidateBlueprint(data,db);
                string path=Root+"/"+spec.id+".json";
                File.WriteAllText(path,JsonUtility.ToJson(data,true),new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var definition=LoadOrCreate<EnemyArchetypeDefinition>(Root+"/"+spec.id+".asset");
                var profile=LoadOrCreate<BotBehaviorProfile>(Root+"/"+spec.id+"-behavior.asset");
                profile.ConfigureRole(spec.role);EditorUtility.SetDirty(profile);
                definition.id=spec.id;definition.nameZh=spec.zh;definition.nameEn=spec.en;definition.size=spec.size;
                definition.score=spec.score;definition.blueprint=AssetDatabase.LoadAssetAtPath<TextAsset>(path);definition.behavior=profile;
                if(spec.size==EnemySize.Capital)
                {
                    var alternate=Build(spec,db,true);ValidateBlueprint(alternate,db);
                    string alt=Root+"/"+spec.id+"-siege.json";
                    File.WriteAllText(alt,JsonUtility.ToJson(alternate,true),new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(alt,ImportAssetOptions.ForceSynchronousImport);
                    definition.alternateBlueprint=AssetDatabase.LoadAssetAtPath<TextAsset>(alt);
                    summaries.Add(Summarize(spec.id+"-siege",alt,alternate,db,tuning));
                }
                EditorUtility.SetDirty(definition);
                summaries.Add(Summarize(spec.id,path,data,db,tuning));
            }
            AssetDatabase.SaveAssets();
            string result=Serialize(summaries);
            File.WriteAllText(Evidence+"/summary.json",result,new UTF8Encoding(false));
            return result;
        }
        finally{BlockModules.Configure(prior);}
    }

    static T LoadOrCreate<T>(string path)where T:ScriptableObject
    {var value=AssetDatabase.LoadAssetAtPath<T>(path);if(!value){value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);}return value;}

    static BlueprintData Build(Spec spec,ModuleDatabase db,bool alternate=false)
    {
        var s=new State{spec=spec,db=db,ship=new BlueprintData{name=spec.en+(alternate?" Siege":"")}};
        Add(s,BlockShape.Core,new Vector3Int(0,0,0),Quaternion.identity);
        if(spec.size==EnemySize.Small)
        {
            for(int i=0;i<spec.powerCount;i++)Add(s,BlockShape.Power,new Vector3Int(i-1,0,-2),Quaternion.identity);
            if(spec.shieldCount>0)Add(s,BlockShape.Shield,new Vector3Int(0,0,1),Quaternion.identity);
        }
        else
        {
            var powerCells=new[]{new Vector3Int(-3,0,-4),new Vector3Int(1,0,-4),new Vector3Int(-3,0,1),new Vector3Int(1,0,1),new Vector3Int(-1,0,-8)};
            for(int i=0;i<spec.powerCount;i++)Add(s,BlockShape.Power2,powerCells[i],Quaternion.identity);
            var shieldCells=new[]{new Vector3Int(-4,0,-1),new Vector3Int(3,0,-1),new Vector3Int(-4,0,5),new Vector3Int(3,0,5)};
            for(int i=0;i<spec.shieldCount;i++)Add(s,spec.size==EnemySize.Capital?BlockShape.Shield2:BlockShape.Shield,shieldCells[i],Quaternion.identity);
        }
        // Engines sit on exposed wing surfaces; their side and vertical nozzle cells remain empty.
        foreach(int z in spec.thrusterRows)foreach(int sign in new[]{-1,1})
        {
            Add(s,BlockShape.Thruster6,new Vector3Int(sign*spec.width,1,z),Quaternion.identity);
            Add(s,BlockShape.Thruster6,new Vector3Int(sign*spec.width,-1,z),Quaternion.AngleAxis(180,Vector3.right));
        }
        int[] mains=spec.size==EnemySize.Capital?new[]{-5,0,5}:new[]{0};
        foreach(int x in mains)Add(s,spec.engine,new Vector3Int(x,0,-spec.length),Quaternion.AngleAxis(-90,Vector3.right));
        for(int i=0;i<spec.guns.Length;i++)
        {
            var shape=alternate&&spec.guns[i]==BlockShape.Railgun3?BlockShape.Missile3:spec.guns[i];
            var part=Add(s,shape,new Vector3Int(spec.gunX[i],1,spec.length-4),Quaternion.identity);
            s.groups[part.id]=shape>=BlockShape.Missile1&&shape<=BlockShape.Missile3?2:shape>=BlockShape.Railgun1&&shape<=BlockShape.Railgun3?3:shape>=BlockShape.Gun1&&shape<=BlockShape.Gun3?1:0;
        }
        // A tapered forward deck, broad wing roots and a low spine make each size readable.
        for(int z=-spec.length;z<=spec.length;z++)for(int x=-spec.width;x<=spec.width;x++)
        {
            int taper=z>spec.length-3?z-(spec.length-3):0;
            int half=Mathf.Max(1,spec.width-taper);
            if(Mathf.Abs(x)>half)continue;
            TryAdd(s,z==spec.length&&Mathf.Abs(x)==half?BlockShape.Wedge:BlockShape.Cube,new Vector3Int(x,0,z),Quaternion.identity);
        }
        if(spec.lowerDeck)
        {
            int halfWidth=spec.size==EnemySize.Capital?6:spec.width-2;
            int halfLength=spec.size==EnemySize.Capital?9:4;
            for(int z=-halfLength;z<=halfLength;z++)for(int x=-halfWidth;x<=halfWidth;x++)
                TryAdd(s,BlockShape.Cube,new Vector3Int(x,-1,z),Quaternion.identity);
        }
        // All detail sits behind the forward battery; no raised prow can cross a firing lane.
        int armor=0;
        for(int z=-spec.length+2;z<spec.length-5;z++)for(int x=-spec.width+2;x<=spec.width-2;x++)
        {
            if(armor>=spec.armor)break;
            if(x==0&&z==0)continue;
            if(TryAdd(s,BlockShape.Armor1,new Vector3Int(x,1,z),Quaternion.identity))armor++;
        }
        // Central bridge is structurally supported independently of the armor plating.
        TryAdd(s,BlockShape.Cube,new Vector3Int(0,1,0),Quaternion.identity);
        Add(s,BlockShape.FlareLauncher,new Vector3Int(0,2,0),Quaternion.identity);
        if(spec.size==EnemySize.Capital)
            for(int z=-6;z<=2;z++)for(int x=-2;x<=2;x++)
            {
                if(x==0&&z==0)continue;
                TryAdd(s,BlockShape.Cube,new Vector3Int(x,2,z),Quaternion.identity);
            }
        // The command deck's upper layer must have direct full faces below, not armor-only supports.
        PruneUnconnectedDecorations(s);
        WeaponGroups.Normalize(s.ship,db);
        WeaponGroups.ConfigureGroup(s.ship,0,"Laser",WeaponInputBinding.MouseLeft(),db);
        WeaponGroups.AddGroup(s.ship,"Kinetic",new WeaponInputBinding{source=WeaponInputSource.MouseMiddle},db);
        WeaponGroups.AddGroup(s.ship,"Missile",new WeaponInputBinding{source=WeaponInputSource.MouseRight},db);
        WeaponGroups.AddGroup(s.ship,"Heavy",WeaponInputBinding.Keyboard(Key.C),db);
        foreach(var entry in s.groups)if(!WeaponGroups.Assign(s.ship,entry.Key,entry.Value,db))throw new InvalidDataException("Weapon group assignment failed.");
        return BlueprintRepository.Upgrade(s.ship);
    }

    static void PruneUnconnectedDecorations(State state)
    {
        // Build true planar adjacency once, then discard only optional floating decorative pieces.
        var cells=new Dictionary<Vector3Int,BlockRecord>();
        foreach(var p in state.ship.parts)foreach(var c in BlockModules.BodyCells(p))cells.Add(c,p);
        var seen=new HashSet<string>();var todo=new Queue<BlockRecord>();var core=state.ship.parts.First(p=>p.shape==BlockShape.Core);
        seen.Add(core.id);todo.Enqueue(core);var axes=new[]{Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
        while(todo.Count>0){var a=todo.Dequeue();foreach(var c in BlockModules.BodyCells(a))foreach(var d in axes)
            if(cells.TryGetValue(c+d,out var b)&&!seen.Contains(b.id)&&BlockConnectivity.Touches(a,b)){seen.Add(b.id);todo.Enqueue(b);}}
        foreach(var p in state.ship.parts.Where(p=>!seen.Contains(p.id)).ToArray())
        {
            if(p.shape!=BlockShape.Cube&&p.shape!=BlockShape.Wedge)throw new InvalidDataException(state.ship.name+" disconnected equipment "+p.shape+" "+p.cell);
            state.ship.parts.Remove(p);
        }
    }

    static BlockRecord Add(State state,BlockShape shape,Vector3Int cell,Quaternion rotation)
    {
        var part=new BlockRecord{id=state.spec.id+"-"+shape+"-"+cell.x+"-"+cell.y+"-"+cell.z,shape=shape,cell=cell,position=(Vector3)cell+Vector3.up*.5f,rotation=rotation.normalized};
        foreach(var c in BlockModules.PlacementCells(part))if(state.reserved.Contains(c))throw new InvalidDataException(state.ship.name+" overlaps "+shape+" at "+cell+" ["+c+"]");
        foreach(var c in BlockModules.AllCells(part))state.reserved.Add(c);
        state.ship.parts.Add(part);return part;
    }
    static bool TryAdd(State state,BlockShape shape,Vector3Int cell,Quaternion rotation)
    {
        var part=new BlockRecord{shape=shape,cell=cell,position=(Vector3)cell+Vector3.up*.5f,rotation=rotation};
        foreach(var c in BlockModules.PlacementCells(part))if(state.reserved.Contains(c))return false;
        Add(state,shape,cell,rotation);return true;
    }

    public static void ValidateBlueprint(BlueprintData data,ModuleDatabase db)
    {
        var report=DeploymentValidator.ValidateForRange(data,db);
        if(!report.IsValid)throw new InvalidDataException(data.name+": "+string.Join(";",report.Errors.Select(e=>e.key)));
        if(!BlockConnectivity.IsConnected(data))throw new InvalidDataException(data.name+" lacks face connectivity.");
        var cells=new Dictionary<Vector3Int,BlockRecord>();
        foreach(var p in data.parts)foreach(var c in BlockModules.BodyCells(p))cells.Add(c,p);
        var clearance=new WeaponClearance();
        foreach(var p in data.parts)
        {
            if((BlockModules.IsWeapon(p.shape)||BlockModules.IsArmor(p.shape))&&!BlockConnectivity.HasCompleteMount(p,data.parts))throw new InvalidDataException(data.name+" incomplete mount "+p.shape+" "+p.cell);
            if(BlockModules.IsThruster(p.shape))foreach(var c in BlockModules.ExhaustCells(p))
                if(cells.TryGetValue(c,out var other)&&other!=p)throw new InvalidDataException(data.name+" blocked exhaust "+p.cell+" -> "+other.cell);
            if(!BlockModules.IsWeapon(p.shape))continue;
            var definition=db.Get(p.shape);
            foreach(var other in data.parts)if(other!=p&&clearance.IsBlockedBy(p,definition,other,db.Get(other.shape).mesh))
                throw new InvalidDataException(data.name+" blocked weapon "+p.shape+" "+p.cell+" -> "+other.shape+" "+other.cell);
        }
    }

    static FleetSummary Summarize(string id,string path,BlueprintData data,ModuleDatabase db,FlightTuning tuning)
    {
        var result=new FleetSummary{id=id,name=data.name,blueprint=path,parts=data.parts.Count};var states=new List<ShipPartState>();
        foreach(var p in data.parts)
        {
            var d=db.Get(p.shape);result.cost+=d.cost;result.massKg+=d.massKg;result.hp+=d.maxHp;result.shield+=d.shieldCapacity;
            result.generation+=d.generationWatts;result.storage+=d.storageJoules;if(d.weapon){result.weapons++;result.sustainedWeaponPower+=d.weapon.sustainedPowerWatts;}
            states.Add(new ShipPartState{record=p,present=true,hp=d.maxHp,maxHp=d.maxHp,thrustAvailable=true,availableExhaustMask=-1});
        }
        var performance=ShipPerformance.Calculate(states,Quaternion.identity,tuning);
        result.positiveAcceleration=new[]{performance.accelerationPositive.x,performance.accelerationPositive.y,performance.accelerationPositive.z};
        result.negativeAcceleration=new[]{performance.accelerationNegative.x,performance.accelerationNegative.y,performance.accelerationNegative.z};result.turnDegreesPerSecond=performance.angularRate;
        for(int axis=0;axis<3;axis++)if(result.positiveAcceleration[axis]<=0||result.negativeAcceleration[axis]<=0)throw new InvalidDataException(data.name+" cannot translate on every axis.");
        if(result.turnDegreesPerSecond<=0||result.generation<=0)throw new InvalidDataException(data.name+" has no usable steering/power.");
        return result;
    }
    static string Serialize(object value)
    {
        var type=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        return (string)type.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    }
}

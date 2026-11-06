using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    /// <summary>Imports the supplied expansion without changing the original ten module definitions.</summary>
    public static class ComponentLibraryInstaller
    {
        const string Root = "Assets/_Starframe/";
        const string Sources = Root + "assetsForCodex/";
        const string Output = Root + "Art/Models/Construction/Expansion/";
        const string Icons = Root + "Art/UI/Construction/Icons/Expansion/";
        const string Models = Root + "Art/Models/Modules/Expansion/";
        const string Materials = Root + "Art/Materials/Ships/Expansion/";
        const string Settings = Root + "Settings/Modules/";
        const string Evidence = "Documentation/Thruster6-Weapons-2026-10-25/";
        sealed class Part
        {
            public BlockShape shape;
            public string source, title;
            public ModuleCategory category;
            public Vector3Int size;
            public float mass, hp, recovery, thrust;
            public Part(BlockShape shape, string source, string title, ModuleCategory category, int x, int y, int z, float mass, float hp, float recovery, float thrust=0)
            { this.shape=shape;this.source=source;this.title=title;this.category=category;size=new Vector3Int(x,y,z);this.mass=mass;this.hp=hp;this.recovery=recovery;this.thrust=thrust; }
        }
        static Part[] Parts() => new[]
        {
            new Part(BlockShape.Block5,"block5","切角方块",ModuleCategory.Hull,1,1,1,230,46,6),
            new Part(BlockShape.Block6,"block6","半斜角方块",ModuleCategory.Hull,1,1,1,180,36,6),
            new Part(BlockShape.Block7,"block7","双格半高板",ModuleCategory.Hull,1,1,2,300,60,6),
            new Part(BlockShape.Block8,"block8","八格船体",ModuleCategory.Hull,2,2,2,2400,480,10),
            new Part(BlockShape.Thruster3,"thruster3","推进器 3",ModuleCategory.Propulsion,1,2,2,2500,360,10,1480000),
            new Part(BlockShape.Thruster4,"thruster4","推进器 4",ModuleCategory.Propulsion,1,2,4,4800,650,12,2880000),
            new Part(BlockShape.Thruster5,"thruster5","推进器 5",ModuleCategory.Propulsion,2,3,2,7600,950,14,4400000),
            new Part(BlockShape.Power2,"psu2","大型电源",ModuleCategory.Power,2,2,2,4800,650,12),
            new Part(BlockShape.Shield2,"shield2","大型护盾发生器",ModuleCategory.Shield,2,2,2,4200,600,16),
            new Part(BlockShape.Armor1,"armor1","装甲 1",ModuleCategory.Shield,1,1,1,420,180,7),
            new Part(BlockShape.Armor2,"armor2","装甲 2",ModuleCategory.Shield,1,1,2,800,340,9),
            new Part(BlockShape.Armor3,"armor3","装甲 3",ModuleCategory.Shield,1,1,3,1180,480,11),
            new Part(BlockShape.Armor4,"armor4","装甲 4",ModuleCategory.Shield,2,1,3,2280,900,14),
            new Part(BlockShape.Gun1,"gun1","机枪 1",ModuleCategory.Weapon,1,1,3,650,110,8),
            new Part(BlockShape.Gun2,"gun2","机枪 2",ModuleCategory.Weapon,2,1,4,1550,240,10),
            new Part(BlockShape.Gun3,"gun3","机枪 3",ModuleCategory.Weapon,2,2,4,2900,420,13),
            new Part(BlockShape.Laser2,"lazer2","激光速射炮 2",ModuleCategory.Weapon,2,1,4,1700,230,10),
            new Part(BlockShape.Laser3,"lazer3","激光速射炮 3",ModuleCategory.Weapon,2,2,4,3200,400,13),
            new Part(BlockShape.Railgun1,"railgun1","电磁狙击炮 1",ModuleCategory.Weapon,1,1,4,1000,140,10),
            new Part(BlockShape.Railgun2,"railgun2","电磁狙击炮 2",ModuleCategory.Weapon,2,1,5,2100,280,12),
            new Part(BlockShape.Railgun3,"railgun3","电磁狙击炮 3",ModuleCategory.Weapon,2,2,6,3900,470,16),
            new Part(BlockShape.Missile1,"missle1","追踪导弹 1",ModuleCategory.Weapon,1,1,3,850,120,9),
            new Part(BlockShape.Missile2,"missle2","追踪导弹 2",ModuleCategory.Weapon,2,1,3,1600,220,11),
            new Part(BlockShape.Missile3,"missle3","追踪导弹 3",ModuleCategory.Weapon,1,1,5,1800,240,13),
            new Part(BlockShape.Sword1,"sword1","光束刃 1",ModuleCategory.Weapon,1,1,3,700,130,8),
            new Part(BlockShape.Sword2,"sword2","光束刃 2",ModuleCategory.Weapon,2,1,4,1550,260,11)
        };

        [MenuItem("Starframe/Components/Install expanded component library")]
        public static void Install()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before importing components.");
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Settings+"ModuleDatabase.asset");
            if(!database)throw new InvalidOperationException("ModuleDatabase is missing.");
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Models);Directory.CreateDirectory(Icons);Directory.CreateDirectory(Materials);Directory.CreateDirectory(Evidence+"Before");
            BackupOriginalDefinitions(database);
            AssetDatabase.Refresh();
            var hull=database.Get(BlockShape.Cube).material;
            var engine=database.Get(BlockShape.Thruster1).material;
            var weapon=database.Get(BlockShape.Laser).material;
            var power=database.Get(BlockShape.Power).material;
            var shield=database.Get(BlockShape.Shield);
            var armor=CloneMaterial(hull,"ArmorIvory",new Color(.965f,.965f,.965f,1),.64f);
            var ballistic=CloneMaterial(weapon,"BallisticGraphite",new Color(.755f,.76f,.765f,1),.60f);
            var rail=CloneMaterial(weapon,"RailgunGraphite",new Color(.72f,.735f,.75f,1),.64f);
            var beam=CloneMaterial(weapon,"BeamCeramic",new Color(.81f,.83f,.85f,1),.63f);
            var report=new StringBuilder("# Expanded component library\n\nImported 26 supplied models. Original ten definitions, source FBX/PNG files and user blueprints/maps are preserved.\n\nSizes below use local X × Y × Z after model orientation; propulsion exhaust points +Y, weapons fire +Z. All meshes share one uniform scale and their bottom lies at Y=-0.5.\n\n|Component|Cells X×Y×Z|Mass kg|HP|Recovery s|Weapon damage × rate|Power W|\n|---|---:|---:|---:|---:|---:|---:|\n");
            foreach(var spec in Parts())
            {
                var definition=database.Get(spec.shape);
                if(!definition){definition=ScriptableObject.CreateInstance<ModuleDefinition>();definition.name=spec.shape.ToString();AssetDatabase.CreateAsset(definition,Settings+spec.shape+".asset");database.modules.Add(definition);}
                definition.shape=spec.shape;definition.displayName=spec.title;definition.category=spec.category;
                definition.massKg=spec.mass;definition.maxHp=spec.hp;definition.recoverySeconds=spec.recovery;definition.cost=0;
                definition.thrustNewtons=spec.thrust;definition.occupiedCells=Cells(spec.size);definition.drawOutline=false;definition.outline=null;
                definition.generationWatts=definition.storageJoules=definition.thrusterPowerWatts=definition.shieldPowerWatts=definition.shieldCapacity=0;
                definition.shieldSurfaceMaterial=null;definition.shieldSurfaceMesh=null;definition.weapon=null;
                string modelPath=Models+spec.shape+".fbx";
                File.Copy(Sources+spec.source+".fbx",modelPath,true);
                AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if(!importer)throw new FileNotFoundException("Supplied component model is missing.",Sources+spec.source+".fbx");
                importer.isReadable=true;importer.importNormals=ModelImporterNormals.Import;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var orientation=spec.category==ModuleCategory.Propulsion?Quaternion.Euler(0,0,-90):spec.shape==BlockShape.Armor4?Quaternion.Euler(0,90,0):Quaternion.identity;
                var baked=Bake(model,orientation,spec.size,out var normalization);
                baked.name=spec.shape+"BevelledVisual";definition.mesh=StoreMesh(baked,Output+spec.shape+"Mesh.asset");
                definition.material=spec.category==ModuleCategory.Propulsion?engine:spec.category==ModuleCategory.Power?power:spec.shape==BlockShape.Shield2?shield.material:
                    IsArmor(spec.shape)?armor:IsRail(spec.shape)?rail:IsGun(spec.shape)||IsMissile(spec.shape)?ballistic:IsSword(spec.shape)?beam:spec.category==ModuleCategory.Weapon?weapon:hull;
                definition.icon=ImportIcon(spec.source+".png",spec.shape+".png");
                string selected=spec.source+(spec.shape==BlockShape.Sword1||spec.shape==BlockShape.Sword2?"-1.png":"s.png");
                definition.selectedIcon=ImportIcon(selected,spec.shape+"Selected.png");
                if(spec.shape==BlockShape.Power2){definition.generationWatts=560;definition.storageJoules=4200;}
                if(spec.shape==BlockShape.Shield2){definition.shieldCapacity=2000;definition.shieldRadius=16;definition.shieldRecoveryDelay=2.5f;definition.shieldRecoveryPerSecond=260;definition.shieldPowerWatts=520;definition.shieldSurfaceMaterial=shield.shieldSurfaceMaterial;definition.shieldSurfaceMesh=shield.shieldSurfaceMesh;}
                if(spec.category==ModuleCategory.Weapon)
                {
                    var mesh=definition.mesh;var tip=TipCenter(mesh);
                    definition.muzzleLocalPosition=new Vector3(tip.x,tip.y,mesh.bounds.max.z+.035f);
                    definition.muzzleClearanceDiameter=IsMissile(spec.shape)?Mathf.Min(.6f,mesh.bounds.size.x*.55f):IsSword(spec.shape)?.3f:IsGun(spec.shape)?.18f:.22f;
                    definition.weapon=MakeWeapon(spec.shape,definition.material);
                    if(IsMissile(spec.shape)) BakeMissileParts(model,normalization,spec,definition);
                    else if(IsSword(spec.shape)) ConfigureEnergyBlade(spec,definition);
                    else if(IsGun(spec.shape)) FindBarrelMuzzles(definition);
                }
                EditorUtility.SetDirty(definition);
                var w=definition.weapon;
                report.AppendLine("|"+spec.shape+"|"+spec.size.x+"×"+spec.size.y+"×"+spec.size.z+"|"+spec.mass+"|"+spec.hp+"|"+spec.recovery+"|"+(w?w.damage+" × "+w.shotsPerSecond:"—")+"|"+(w?w.sustainedPowerWatts:spec.shape==BlockShape.Power2?-560:spec.shape==BlockShape.Shield2?520:0)+"|");
            }
            database.modules=database.modules.Where(d=>d).GroupBy(d=>d.shape).Select(g=>g.First()).OrderBy(d=>(int)d.shape).ToList();
            EditorUtility.SetDirty(database);BlockModules.Configure(database);BlockModules.InvalidateSurfaces();
            AssetDatabase.SaveAssets();
            File.WriteAllText(Evidence+"Catalog.md",report.ToString());
            File.WriteAllText(Evidence+"Catalog.json",JsonUtility.ToJson(new CatalogSnapshot{entries=database.modules.Select(d=>new CatalogEntry{shape=d.shape.ToString(),module=JsonUtility.ToJson(d),weapon=d.weapon?JsonUtility.ToJson(d.weapon):""}).ToArray()},true));
            AssetDatabase.Refresh();
            Debug.Log("Expanded component library installed: "+database.modules.Count+" modules.");
        }

        static WeaponDefinition MakeWeapon(BlockShape shape,Material material)
        {
            string asset=Settings+shape+"Weapon.asset";
            var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(asset);
            if(!w){w=ScriptableObject.CreateInstance<WeaponDefinition>();w.name=shape+"Weapon";AssetDatabase.CreateAsset(w,asset);}
            w.tracerStyle=TracerStyle.Voxel;w.tracerLingerSeconds=0;w.minimumVisualPixels=0;w.delivery=WeaponDelivery.Projectile;w.activationMode=WeaponActivationMode.Hold;w.requireTargetLock=false;
            w.effectColor=new Color(.08f,1f,.24f,1);w.visualIntensity=5;w.visualWidth=.25f;w.visualLength=8;w.tailLength=6;
            w.projectileDiameter=.18f;w.penetrationDepth=1;w.secondLayerDamageMultiplier=0;
            w.projectileMaterial=material;w.missileMesh=null;w.launcherMesh=null;w.missileLaunchOffsets=new[]{Vector3.zero};w.beamMesh=null;w.muzzleOffsets=Array.Empty<Vector3>();
            w.missileLockSeconds=2;w.reloadSeconds=4;w.missileTurnDegrees=110;w.missileAcceleration=160;w.missileLaunchSpeed=35;w.explosionRadius=3;w.beamDuration=.12f;
            if(IsGun(shape))
            {
                int tier=(int)shape-(int)BlockShape.Gun1;
                Set(w,new[]{15f,20f,28f}[tier],new[]{16f,20f,24f}[tier],new[]{1800f,2000f,2200f}[tier],new[]{1800f,2200f,2600f}[tier],new[]{20f,35f,60f}[tier]);
                w.effectColor=new Color(1f,.62f,.055f,1);w.visualLength=4+tier;w.tailLength=100;w.visualWidth=2.2f+tier*.2f;w.visualIntensity=6.5f;w.minimumVisualPixels=2.2f;w.projectileDiameter=.16f+tier*.035f;
            }
            else if(shape==BlockShape.Laser2||shape==BlockShape.Laser3)
            {
                int tier=shape==BlockShape.Laser2?0:1;Set(w,tier==0?40:65,10,tier==0?1500:1600,5000,tier==0?100:180);
                w.visualLength=tier==0?20:24;w.tailLength=tier==0?12:15;w.visualWidth=tier==0?.85f:1.05f;w.projectileDiameter=tier==0?.8f:1;
            }
            else if(IsRail(shape))
            {
                int tier=(int)shape-(int)BlockShape.Railgun1;
                Set(w,new[]{340f,650f,1150f}[tier],new[]{3f,2.5f,2f}[tier],new[]{10000f,12000f,14000f}[tier],new[]{6500f,7500f,8500f}[tier],new[]{65f,120f,200f}[tier]);
                w.effectColor=new Color(1f,.66f,.12f,1);w.penetrationDepth=new[]{3f,5f,7f}[tier];w.secondLayerDamageMultiplier=.75f;
                ApplyRailPulseVisual(w,tier);
            }
            else if(IsMissile(shape))
            {
                int tier=(int)shape-(int)BlockShape.Missile1;
                w.delivery=WeaponDelivery.GuidedMissile;w.requireTargetLock=true;
                float reload=new[]{4f,5.5f,7f}[tier];
                Set(w,new[]{400f,700f,1100f}[tier],1/reload,new[]{240f,300f,360f}[tier],new[]{3000f,3500f,4000f}[tier],new[]{90f,150f,230f}[tier]);
                w.reloadSeconds=reload;w.missileLockSeconds=2;w.missileLaunchSpeed=new[]{35f,40f,45f}[tier];w.missileAcceleration=new[]{160f,180f,200f}[tier];w.missileTurnDegrees=new[]{110f,95f,80f}[tier];w.explosionRadius=new[]{3f,4.5f,6f}[tier];
                w.effectColor=new Color(1f,.4f,.025f,1);w.visualLength=1.5f+tier;w.tailLength=7+tier*3;w.visualWidth=.32f+tier*.1f;w.projectileDiameter=.35f+tier*.1f;
            }
            else
            {
                bool large=shape==BlockShape.Sword2;w.delivery=WeaponDelivery.Beam;
                Set(w,large?80:45,12,1400,40,large?220:125);
                w.effectColor=new Color(.08f,1f,.24f,1);w.visualLength=40;w.tailLength=0;w.visualWidth=large?.7f:.45f;w.projectileDiameter=large?1.1f:.75f;w.penetrationDepth=large?2:1.5f;w.secondLayerDamageMultiplier=.65f;
            }
            EditorUtility.SetDirty(w);return w;
        }
        static void Set(WeaponDefinition w,float damage,float rate,float speed,float range,float power){w.damage=damage;w.shotsPerSecond=rate;w.projectileSpeed=speed;w.range=range;w.sustainedPowerWatts=power;}
        // 2026-10-25 follow-up: slightly slower rapid lasers, faster kinetic guns, and a long readable rail pulse.
        [MenuItem("Starframe/Components/Apply follow-up weapon tuning")]
        public static void ApplyFollowUpWeaponTuning()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before tuning production weapons.");
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Settings+"ModuleDatabase.asset");
            if(!database)throw new InvalidOperationException("ModuleDatabase is missing.");
            foreach(var shape in new[]{BlockShape.Laser2,BlockShape.Laser3,BlockShape.Gun1,BlockShape.Gun2,BlockShape.Gun3,BlockShape.Railgun1,BlockShape.Railgun2,BlockShape.Railgun3})
            {
                var definition=database.Get(shape);var w=definition?definition.weapon:null;
                if(!w)throw new InvalidOperationException("Weapon asset is missing for "+shape);
                if(IsGun(shape))
                {
                    int tier=(int)shape-(int)BlockShape.Gun1;
                    w.shotsPerSecond=new[]{16f,20f,24f}[tier];w.projectileSpeed=new[]{1800f,2000f,2200f}[tier];
                    w.visualLength=4+tier;w.tailLength=100;w.visualWidth=2.2f+tier*.2f;w.minimumVisualPixels=2.2f;
                }
                else if(shape==BlockShape.Laser2||shape==BlockShape.Laser3)w.shotsPerSecond=10;
                else {int tier=(int)shape-(int)BlockShape.Railgun1;w.shotsPerSecond=new[]{3f,2.5f,2f}[tier];w.projectileSpeed=new[]{10000f,12000f,14000f}[tier];ApplyRailPulseVisual(w,tier);}
                EditorUtility.SetDirty(w);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Follow-up weapon tuning applied: lasers 10/s, guns 16/20/24 at 1800/2000/2200, rails 10/12/14 km/s continuous pulses.");
        }
        static void ApplyRailPulseVisual(WeaponDefinition w,int tier)
        {
            w.tracerStyle=TracerStyle.Continuous;w.tracerLingerSeconds=.09f;w.firstShotImmediate=true;
            w.visualLength=new[]{70f,100f,140f}[tier];w.tailLength=new[]{200f,300f,420f}[tier];
            w.visualWidth=8+tier;w.visualIntensity=9+tier;w.minimumVisualPixels=4.5f+tier*.5f;
        }
        static bool IsArmor(BlockShape s)=>s>=BlockShape.Armor1&&s<=BlockShape.Armor4;
        static bool IsGun(BlockShape s)=>s>=BlockShape.Gun1&&s<=BlockShape.Gun3;
        static bool IsRail(BlockShape s)=>s>=BlockShape.Railgun1&&s<=BlockShape.Railgun3;
        static bool IsMissile(BlockShape s)=>s>=BlockShape.Missile1&&s<=BlockShape.Missile3;
        static bool IsSword(BlockShape s)=>s==BlockShape.Sword1||s==BlockShape.Sword2;
        static Vector3Int[] Cells(Vector3Int size){var cells=new List<Vector3Int>();for(int y=0;y<size.y;y++)for(int z=0;z<size.z;z++)for(int x=0;x<size.x;x++)cells.Add(new Vector3Int(x,y,z));return cells.ToArray();}

        static Mesh Bake(GameObject source,Quaternion rotation,Vector3Int cells,out Matrix4x4 normalization)
        {
            var raw=Combine(source,Matrix4x4.Rotate(rotation),null);
            var b=raw.bounds;
            float scale=Mathf.Min(.495f,(cells.x-.02f)/b.size.x,(cells.y-.02f)/b.size.y,(cells.z-.02f)/b.size.z);
            var centre=new Vector3((cells.x-1)*.5f,-.5f+b.extents.y*scale,(cells.z-1)*.5f);
            normalization=Matrix4x4.TRS(centre,Quaternion.identity,Vector3.one*scale)*Matrix4x4.Translate(-b.center)*Matrix4x4.Rotate(rotation);
            UnityEngine.Object.DestroyImmediate(raw);
            return Combine(source,normalization,null);
        }
        static Mesh Combine(GameObject source,Matrix4x4 normalize,Func<MeshFilter,bool> include)
        {
            var combines=new List<CombineInstance>();
            foreach(var f in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!f.sharedMesh||(include!=null&&!include(f)))continue;
                for(int i=0;i<f.sharedMesh.subMeshCount;i++)combines.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=i,transform=normalize*source.transform.worldToLocalMatrix*f.transform.localToWorldMatrix});
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combines.ToArray(),true,true);mesh.RecalculateBounds();
            if(mesh.uv.Length==mesh.vertexCount)mesh.RecalculateTangents();
            return mesh;
        }
        static void BakeMissileParts(GameObject source,Matrix4x4 normalization,Part spec,ModuleDefinition definition)
        {
            var weapon=definition.weapon;
            string baseName=spec.shape==BlockShape.Missile1?"Cube.286":spec.shape==BlockShape.Missile2?"Cube.062":"Cube.301";
            var launcher=Combine(source,normalization,f=>f.name==baseName);launcher.name=spec.shape+"Launcher";
            weapon.launcherMesh=StoreMesh(launcher,Output+spec.shape+"LauncherMesh.asset");
            var filters=source.GetComponentsInChildren<MeshFilter>(true);
            var missileFilters=filters.Where(f=>f.name!=baseName).ToArray();
            var mouths=new List<Vector3>();
            foreach(var filter in missileFilters)
            {
                var partMesh=Combine(source,normalization,f=>f==filter);
                var mouth=TipCenter(partMesh);mouth.z=partMesh.bounds.max.z+.035f;mouths.Add(mouth);UnityEngine.Object.DestroyImmediate(partMesh);
            }
            definition.muzzleLocalPosition=mouths.Aggregate(Vector3.zero,(sum,v)=>sum+v)/mouths.Count;
            weapon.missileLaunchOffsets=mouths.Select(v=>v-definition.muzzleLocalPosition).ToArray();
            var missile=missileFilters[0];
            var payload=Combine(source,normalization,f=>f==missile);
            var center=payload.bounds.center;var vertices=payload.vertices;
            for(int i=0;i<vertices.Length;i++)vertices[i]-=center;
            payload.vertices=vertices;payload.RecalculateBounds();payload.name=spec.shape+"Projectile";
            weapon.missileMesh=StoreMesh(payload,Output+spec.shape+"ProjectileMesh.asset");EditorUtility.SetDirty(weapon);
        }

        static void ConfigureEnergyBlade(Part spec,ModuleDefinition definition)
        {
            // The supplied triangular structure is a permanent physical component. The energy
            // blade is an independently authored effect emitted from its tip.
            var vertices=new List<Vector3>();var triangles=new List<int>();
            var rings=new[]{new Vector2(0,.12f),new Vector2(.025f,.5f),new Vector2(.78f,.34f),new Vector2(.96f,.2f),new Vector2(1,0)};
            foreach(var ring in rings)
            {
                vertices.Add(new Vector3(-ring.y,-ring.y,ring.x));
                vertices.Add(new Vector3(ring.y,-ring.y,ring.x));
                vertices.Add(new Vector3(ring.y,ring.y,ring.x));
                vertices.Add(new Vector3(-ring.y,ring.y,ring.x));
            }
            triangles.AddRange(new[]{0,2,1,0,3,2});
            for(int ring=0;ring<rings.Length-1;ring++)for(int side=0;side<4;side++)
            {int a=ring*4+side,b=ring*4+(side+1)%4,c=(ring+1)*4+(side+1)%4,d=(ring+1)*4+side;triangles.AddRange(new[]{a,b,c,a,c,d});}
            var beam=new Mesh{name=spec.shape+"IndependentEnergyBlade"};beam.SetVertices(vertices);beam.SetTriangles(triangles,0);beam.RecalculateNormals();beam.RecalculateBounds();
            definition.weapon.beamMesh=StoreMesh(beam,Output+spec.shape+"BeamMesh.asset");EditorUtility.SetDirty(definition.weapon);
        }
        static void FindBarrelMuzzles(ModuleDefinition definition)
        {
            var islands=MeshIslands(definition.mesh);var all=definition.mesh.bounds;
            var barrels=islands.Where(m=>m.bounds.max.z>all.max.z-.05f&&m.bounds.size.z>all.size.z*.45f).OrderBy(m=>m.bounds.center.y).ThenBy(m=>m.bounds.center.x).ToArray();
            if(barrels.Length>0)
            {
                var mouths=barrels.Select(m=>new Vector3(m.bounds.center.x,m.bounds.center.y,m.bounds.max.z+.035f)).ToArray();
                definition.muzzleLocalPosition=mouths.Aggregate(Vector3.zero,(sum,v)=>sum+v)/mouths.Length;
                definition.weapon.muzzleOffsets=mouths.Select(v=>v-definition.muzzleLocalPosition).ToArray();
                EditorUtility.SetDirty(definition.weapon);
            }
            foreach(var island in islands)UnityEngine.Object.DestroyImmediate(island);
        }
        static Mesh[] MeshIslands(Mesh source)
        {
            var vertices=source.vertices;var triangles=source.triangles;var normals=source.normals;var uv=source.uv;var tangents=source.tangents;
            var parent=Enumerable.Range(0,vertices.Length).ToArray();
            Func<int,int> find=null;find=i=>parent[i]==i?i:parent[i]=find(parent[i]);
            Action<int,int> union=(a,b)=>{a=find(a);b=find(b);if(a!=b)parent[a]=b;};
            var weld=new Dictionary<Vector3Int,int>();
            for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*10000);if(weld.TryGetValue(key,out int other))union(i,other);else weld.Add(key,i);}
            for(int i=0;i<triangles.Length;i+=3){union(triangles[i],triangles[i+1]);union(triangles[i],triangles[i+2]);}
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<triangles.Length;i+=3){int group=find(triangles[i]);if(!groups.TryGetValue(group,out var list)){list=new List<int>();groups.Add(group,list);}list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
            var result=new List<Mesh>();
            foreach(var list in groups.Values)
            {
                var indices=list.Distinct().ToArray();var remap=new Dictionary<int,int>();for(int i=0;i<indices.Length;i++)remap.Add(indices[i],i);
                var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.vertices=indices.Select(i=>vertices[i]).ToArray();mesh.triangles=list.Select(i=>remap[i]).ToArray();
                if(normals.Length==vertices.Length)mesh.normals=indices.Select(i=>normals[i]).ToArray();else mesh.RecalculateNormals();
                if(uv.Length==vertices.Length)mesh.uv=indices.Select(i=>uv[i]).ToArray();
                if(tangents.Length==vertices.Length)mesh.tangents=indices.Select(i=>tangents[i]).ToArray();
                mesh.RecalculateBounds();result.Add(mesh);
            }
            return result.ToArray();
        }

        static Vector3 TipCenter(Mesh mesh)
        {
            float end=mesh.bounds.max.z;var vertices=mesh.vertices.Where(v=>v.z>end-.04f).ToArray();
            if(vertices.Length==0)return mesh.bounds.center;
            return vertices.Aggregate(Vector3.zero,(sum,v)=>sum+v)/vertices.Length;
        }
        static Mesh StoreMesh(Mesh mesh,string path)
        {
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Material CloneMaterial(Material source,string name,Color tint,float smoothness)
        {
            string path=Materials+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(source);material.name=name;AssetDatabase.CreateAsset(material,path);}else material.CopyPropertiesFromMaterial(source);
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",tint);
            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",smoothness);
            EditorUtility.SetDirty(material);return material;
        }
        static Sprite ImportIcon(string source,string name)
        {
            string destination=Icons+name;
            File.Copy(Sources+source,destination,true);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(destination);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.spritePixelsPerUnit=100;importer.maxTextureSize=2048;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(destination);
        }
        static void BackupOriginalDefinitions(ModuleDatabase database)
        {
            string path=Evidence+"Before/OriginalModules.json";
            if(!File.Exists(path))File.WriteAllText(path,JsonUtility.ToJson(new CatalogSnapshot{entries=database.modules.Where(d=>(int)d.shape<10).Select(d=>new CatalogEntry{shape=d.shape.ToString(),module=JsonUtility.ToJson(d),weapon=d.weapon?JsonUtility.ToJson(d.weapon):""}).ToArray()},true));
            foreach(var d in database.modules.Where(d=>(int)d.shape<10))
            {string src=AssetDatabase.GetAssetPath(d),dest=Evidence+"Before/"+Path.GetFileName(src);if(!File.Exists(dest))File.Copy(src,dest);if(d.weapon){string wp=AssetDatabase.GetAssetPath(d.weapon),wd=Evidence+"Before/"+Path.GetFileName(wp);if(!File.Exists(wd))File.Copy(wp,wd);}}
            string code=Root+"Scripts/Runtime/Construction/Flight/ModuleDefinition.cs",copy=Evidence+"Before/ModuleDefinition.cs";if(!File.Exists(copy))File.Copy(code,copy);
        }
        [Serializable]sealed class CatalogSnapshot{public CatalogEntry[] entries;}
        [Serializable]sealed class CatalogEntry{public string shape,module,weapon;}
    }
}

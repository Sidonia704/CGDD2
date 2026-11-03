using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Starframe.Combat;
using Starframe.Flight;
public static class DenseSmokeAudit{
 static void Check(bool b,string s){if(!b)throw new Exception(s);}
 public static string Run(){
 var go=new GameObject("Dense smoke audit");var smoke=go.AddComponent<WeaponSmokePool>();
 try{
 smoke.Initialize();var gun=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Gun1Weapon.asset");
 var missile=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Missile1Weapon.asset");
 var laser=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Laser2Weapon.asset");
 var rng=UnityEngine.Random.state;var systems=go.GetComponentsInChildren<ParticleSystem>();Check(systems.Length==6,"Expected six shared systems, not per-shot objects");
 smoke.EmitMuzzle(Vector3.zero,Vector3.forward,gun,0);Check(smoke.MuzzleParticlesForTeam(0)==3,"Short launch plume missing");
 var ps=systems.Single(s=>s.name.StartsWith("Gun muzzle smoke 0"));var particles=new ParticleSystem.Particle[8192];ps.GetParticles(particles);var pos=particles[0].position;
 go.transform.position=Vector3.right*100;ps.GetParticles(particles);Check(Vector3.Distance(pos,particles[0].position)<.0001f,"Muzzle moved with ship");
 smoke.EmitBallisticSegment(Vector3.zero,Vector3.right*100,gun,0);
 var path=systems.Single(s=>s.name.StartsWith("Solid path smoke 0"));int n=path.GetParticles(particles);
 Check(n>0&&particles.Take(n).All(p=>p.position.x>=0&&p.position.x<=100),"Ballistic samples outside actual sweep");
 for(int i=0;i<10000;i++)smoke.EmitBallisticSegment(Vector3.zero,Vector3.right*100,gun,0);
 int own=smoke.BallisticParticlesForTeam(0);smoke.EmitBallisticSegment(Vector3.zero,Vector3.up*100,gun,1);
 Check(smoke.BallisticParticlesForTeam(1)>0,"Player saturated enemy flight smoke budget");
 smoke.EmitMissileSegment(Vector3.zero,Vector3.forward*30,missile,0);smoke.EmitMissileSegment(Vector3.zero,Vector3.forward*30,missile,1);
 Check(smoke.MissileParticlesForTeam(0)>0&&smoke.MissileParticlesForTeam(1)>0,"Solid rounds starved missile trails");
 int before=smoke.MuzzleParticleCount+smoke.MissileParticleCount+smoke.BallisticParticleCount;
 smoke.EmitMuzzle(Vector3.zero,Vector3.forward,laser);smoke.EmitBallisticSegment(Vector3.zero,Vector3.forward*100,laser);
 Check(before==smoke.MuzzleParticleCount+smoke.MissileParticleCount+smoke.BallisticParticleCount,"Energy weapon emitted smoke");
 Check(smoke.EmittedThisFrame<=WeaponSmokePool.MaximumEmissionsPerFrame,"Frame budget exceeded");
 Check(UnityEngine.Random.state.Equals(rng),"Cosmetics consumed combat RNG");
 foreach(var s in systems)s.Simulate(3,false,false);
 Check(smoke.MuzzleParticleCount>0&&smoke.MissileParticleCount>0&&smoke.BallisticParticleCount>0,"Smoke faded too soon");
 foreach(var s in systems)s.Simulate(5,false,false);
 Check(smoke.MuzzleParticleCount+smoke.MissileParticleCount+smoke.BallisticParticleCount==0,"Smoke did not expire");
 smoke.Clear();smoke.EmitBallisticSegment(Vector3.zero,Vector3.right*30,gun,1);
 Check(smoke.BallisticParticlesForTeam(1)>0,"Reused enemy pool failed");smoke.Clear();
 Check(!ShaderUtil.ShaderHasError(Shader.Find("Starframe/Combat/Weapon Smoke")),"Shader error");
 string result="PASS six shared systems; world-space muzzle residency; solid path sample bounds; own/enemy budgets isolated; solid/missile budgets isolated; energy weapons excluded; bounded emissions; combat RNG preserved; smoke alive at3s / expired at8s; clear/reuse; shader compile.";
 File.WriteAllText("Documentation/WeaponSmokeDense-2026-11-03/audit.txt",result);return result;
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}

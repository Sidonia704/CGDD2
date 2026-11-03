using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Starframe.Combat;
using Starframe.Flight;
public static class SmokeReview{
 static void Check(bool value,string note){if(!value)throw new Exception(note);}
 public static string Audit(){
 var go=new GameObject("Smoke audit");var smoke=go.AddComponent<WeaponSmokePool>();
 try{
 smoke.Initialize();var gun=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Gun1Weapon.asset");
 var missile=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Missile1Weapon.asset");
 var laser=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Laser2Weapon.asset");
 Check(gun.smokeTrail&&missile.smokeTrail&&!laser.smokeTrail,"Weapon opt-ins");
 var rng=UnityEngine.Random.state;
 smoke.EmitMuzzle(Vector3.zero,Vector3.forward,gun);Check(smoke.MuzzleParticleCount==1&&smoke.MissileParticleCount==0,"Gun must emit only at the muzzle");
 var systems=go.GetComponentsInChildren<ParticleSystem>();var muzzle=systems.Single(s=>s.name.StartsWith("Gun"));
 var particles=new ParticleSystem.Particle[WeaponSmokePool.MissileCapacity];
 int n=muzzle.GetParticles(particles);var initial=particles[0].position;
 go.transform.position=new Vector3(100,0,0);muzzle.GetParticles(particles);
 Check(Vector3.Distance(initial,particles[0].position)<.0001f&&muzzle.main.simulationSpace==ParticleSystemSimulationSpace.World,"Muzzle cloud followed ship");
 smoke.EmitMissileSegment(new Vector3(10,0,0),new Vector3(30,0,0),missile);
 var flight=systems.Single(s=>s.name.StartsWith("Missile"));n=flight.GetParticles(particles);
 Check(n>0&&particles.Take(n).All(p=>p.position.x>=9.6f&&p.position.x<=30),"Flight smoke exceeded actual segment");
 int before=smoke.MuzzleParticleCount;smoke.EmitMuzzle(Vector3.zero,Vector3.forward,laser);
 Check(smoke.MuzzleParticleCount==before,"Energy weapon emitted smoke");
 for(int i=0;i<10000;i++)smoke.EmitMuzzle(Vector3.zero,Vector3.forward,gun);
 Check(smoke.EmittedThisFrame<=WeaponSmokePool.MaximumEmissionsPerFrame&&smoke.MuzzleParticleCount<=WeaponSmokePool.MuzzleCapacity,"Emission budget");
 Check(UnityEngine.Random.state.Equals(rng),"Smoke consumed gameplay RNG");
 foreach(var ps in systems)ps.Simulate(1,false,false);
 Check(smoke.MuzzleParticleCount>0&&smoke.MissileParticleCount>0,"Smoke disappeared within one second");
 foreach(var ps in systems)ps.Simulate(2,false,false);
 Check(smoke.MuzzleParticleCount==0&&smoke.MissileParticleCount==0,"Smoke did not expire");
 smoke.Clear();smoke.EmitMuzzle(Vector3.zero,Vector3.forward,gun);
 Check(smoke.MuzzleParticleCount==1,"Clear/reuse failed");
 smoke.Clear();Check(smoke.MuzzleParticleCount+smoke.MissileParticleCount==0,"Clear left particles");
 string result="PASS: native opt-ins; muzzle stays at world firing position; flight samples stop inside real segment; lasers excluded; bounded emission; gameplay RNG preserved; smoke survives 1s and expires by 3s; clear/reuse.";
 File.WriteAllText("Documentation/WeaponSmoke-2026-11-03/audit.txt",result);return result;
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}

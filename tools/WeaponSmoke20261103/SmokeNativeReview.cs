using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;
public static class SmokeNativeReview {
 static Task running;
 public static string Start(){running=Run();return "Started native smoke firing review";}
 static async Task Frame(){int f=Time.frameCount;while(f==Time.frameCount)await Task.Yield();}
 static async Task Run(){
 var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();var flight=w.Player.flight;bool external=flight.ExternalControl;
 try{
 if(w.Bots.Count==0)w.SpawnTargets(Starframe.App.ShipWorkspace.Ship,1);w.SetBotMode(RangeBotMode.Stationary,false);var target=w.Bots.First();
 var owner=w.Player.ship;owner.InvulnerableUntil=(float)owner.CombatTime+30;target.ship.InvulnerableUntil=(float)target.ship.CombatTime+30;
 target.flight.ResetMotion(owner.CorePhysicsPosition+owner.transform.forward*700+owner.transform.right*170,owner.transform.rotation);
 var parts=owner.Parts.Where(p=>p.present&&p.view&&owner.Tuning.GetDefinition(p.record.shape)?.weapon?.smokeTrail==true).ToArray();
 var guns=parts.Where(p=>owner.Tuning.GetDefinition(p.record.shape).weapon.delivery==WeaponDelivery.Projectile).Take(4).ToArray();
 var launchers=parts.Where(p=>owner.Tuning.GetDefinition(p.record.shape).weapon.delivery==WeaponDelivery.GuidedMissile).Take(6).ToArray();
 var smoke=w.GetComponent<WeaponSmokePool>();int peakMuzzle=0,peakFlight=0,first=Time.frameCount,shots=0,missiles=0;
 flight.SetExternalInput(Vector3.right*.85f,Vector2.zero);
 float start=Time.time,nextGun=0,nextMissile=.15f;bool captured=false,lingering=false;
 while(Time.time-start<4.2f){
 float t=Time.time-start;
 if(t<2.2f&&t>=nextGun){nextGun=t+.4f;
 foreach(var p in guns){var d=owner.Tuning.GetDefinition(p.record.shape);var matrix=CylindricalHitQuery.PhysicsMatrix(p.view.GetComponent<Collider>());
 w.Projectiles.Fire(owner,d.weapon,matrix.MultiplyPoint3x4(d.muzzleLocalPosition),matrix.MultiplyVector(Vector3.forward),p.view.transform,d.muzzleLocalPosition);shots++;}}
 if(t<2.2f&&t>=nextMissile&&missiles<launchers.Length){nextMissile=t+.32f;var p=launchers[missiles];var d=owner.Tuning.GetDefinition(p.record.shape);var matrix=CylindricalHitQuery.PhysicsMatrix(p.view.GetComponent<Collider>());
 w.Projectiles.FireMissileTube(owner,d.weapon,p.view.transform,d.muzzleLocalPosition,matrix.MultiplyVector(Vector3.forward),target.ship,0);missiles++;}
 peakMuzzle=Mathf.Max(peakMuzzle,smoke.MuzzleParticleCount);peakFlight=Mathf.Max(peakFlight,smoke.MissileParticleCount);
 if(t>=2.05f&&!captured){ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/WeaponSmoke-2026-11-03/native-firing.png"));captured=true;}
 if(t>=3.0f&&!lingering){ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/WeaponSmoke-2026-11-03/native-lingering.png"));lingering=true;}
 await Frame();
 }
 flight.SetExternalInput(Vector3.zero,Vector2.zero);
 if(peakMuzzle<=0||peakFlight<=0||Time.frameCount-first<20)throw new Exception("Missing native smoke or frozen frames");
 File.WriteAllText("Documentation/WeaponSmoke-2026-11-03/native.txt",$"PASS controlled native range firing through actual mounted muzzle transforms; shots={shots}; missiles={missiles}; frames={Time.frameCount-first}; peak gun={peakMuzzle}/{WeaponSmokePool.MuzzleCapacity}; peak flight={peakFlight}/{WeaponSmokePool.MissileCapacity}. Screen captures use native camera and actual projectile simulation; temporary review control/positions are not saved. Not a player performance benchmark.");
 }catch(Exception e){File.WriteAllText("Documentation/WeaponSmoke-2026-11-03/native.txt","FAIL "+e);}
 finally{if(flight){flight.SetExternalInput(Vector3.zero,Vector2.zero);flight.ExternalControl=external;}}
 }
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;
public static class DenseSmokeNative {
 static Task running;
 public static string Start(){running=Run();return "Started native multidirectional live weapons review";}
 static async Task Frame(){int f=Time.frameCount;while(f==Time.frameCount)await Task.Yield();}
 static async Task Run(){
 var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();var flight=w.Player.flight;bool external=flight.ExternalControl;
 try{
 if(w.Bots.Count<3)w.SpawnTargets(Starframe.App.ShipWorkspace.Ship,3-w.Bots.Count);
 w.SetBotMode(RangeBotMode.Stationary,false);
 var origin=w.Player.ship.CorePhysicsPosition;var facing=w.Player.ship.transform.rotation;
 for(int i=0;i<3;i++){
 var b=w.Bots[i];var local=new Vector3((i-1)*310,(i==1?150:-50),520+i*110);
 var p=origin+facing*local;b.flight.ResetMotion(p,Quaternion.LookRotation(origin-p,facing*Vector3.up));b.ship.InvulnerableUntil=(float)b.ship.CombatTime+40;
 }
 w.Player.ship.InvulnerableUntil=(float)w.Player.ship.CombatTime+40;
 var actors=new[]{w.Player,w.Bots[0],w.Bots[1],w.Bots[2]};
 var gunParts=actors.Select(a=>a.ship.Parts.Where(p=>p.present&&p.view&&a.ship.Tuning.GetDefinition(p.record.shape)?.weapon?.delivery==WeaponDelivery.Projectile).Take(4).ToArray()).ToArray();
 var missileParts=actors.Select(a=>a.ship.Parts.Where(p=>p.present&&p.view&&a.ship.Tuning.GetDefinition(p.record.shape)?.weapon?.delivery==WeaponDelivery.GuidedMissile).Take(4).ToArray()).ToArray();
 float start=Time.time,nextGun=0,nextMissile=.1f;int wave=0,shots=0,missiles=0,first=Time.frameCount;
 var smoke=w.GetComponent<WeaponSmokePool>();int[] peaks=new int[6];bool captured=false,lingering=false;
 flight.SetExternalInput(Vector3.right*.6f,Vector2.zero);
 while(Time.time-start<9){
 float t=Time.time-start;
 if(t<6&&t>=nextGun){nextGun=t+.38f;
 for(int i=0;i<actors.Length;i++)foreach(var p in gunParts[i]){
 var a=actors[i];var d=a.ship.Tuning.GetDefinition(p.record.shape);var matrix=CylindricalHitQuery.PhysicsMatrix(p.view.GetComponent<Collider>());
 w.Projectiles.Fire(a.ship,d.weapon,matrix.MultiplyPoint3x4(d.muzzleLocalPosition),matrix.MultiplyVector(Vector3.forward),p.view.transform,d.muzzleLocalPosition);shots++;
 }}
 if(t<5.5f&&t>=nextMissile){nextMissile=t+.45f;
 for(int i=0;i<actors.Length;i++)if(missileParts[i].Length>0){
 var a=actors[i];var p=missileParts[i][wave%missileParts[i].Length];var d=a.ship.Tuning.GetDefinition(p.record.shape);var matrix=CylindricalHitQuery.PhysicsMatrix(p.view.GetComponent<Collider>());
 var target=i==0?w.Bots[wave%3].ship:w.Player.ship;
 w.Projectiles.FireMissileTube(a.ship,d.weapon,p.view.transform,d.muzzleLocalPosition,matrix.MultiplyVector(Vector3.forward),target,0);missiles++;
 }wave++;}
 peaks[0]=Mathf.Max(peaks[0],smoke.MissileParticlesForTeam(0));peaks[1]=Mathf.Max(peaks[1],smoke.MissileParticlesForTeam(1));
 peaks[2]=Mathf.Max(peaks[2],smoke.BallisticParticlesForTeam(0));peaks[3]=Mathf.Max(peaks[3],smoke.BallisticParticlesForTeam(1));
 peaks[4]=Mathf.Max(peaks[4],smoke.MuzzleParticlesForTeam(0));peaks[5]=Mathf.Max(peaks[5],smoke.MuzzleParticlesForTeam(1));
 if(t>=5.1f&&!captured){ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/WeaponSmokeDense-2026-11-03/crossfire.png"));captured=true;}
 if(t>=7&&!lingering){ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/WeaponSmokeDense-2026-11-03/lingering.png"));lingering=true;}
 await Frame();
 }
 if(peaks.Any(x=>x<=0)||Time.frameCount-first<60)throw new Exception("Missing faction / weapon smoke or frozen frames: "+string.Join(",",peaks));
 File.WriteAllText("Documentation/WeaponSmokeDense-2026-11-03/native.txt",$"PASS native range controlled4ship bidirectional review; shots={shots}; missiles={missiles}; frames={Time.frameCount-first}; peak missile own/enemy={peaks[0]}/{peaks[1]}; solid path={peaks[2]}/{peaks[3]}; muzzle={peaks[4]}/{peaks[5]}. Actual mounted muzzle transforms, projectile sweeps and seeker trajectories. Temporary invulnerability/targets/inputs are not saved; not a normal player pacing or standalone performance benchmark.");
 }catch(Exception e){File.WriteAllText("Documentation/WeaponSmokeDense-2026-11-03/native.txt","FAIL "+e);}
 finally{if(flight){flight.SetExternalInput(Vector3.zero,Vector2.zero);flight.ExternalControl=external;}}
 }
}

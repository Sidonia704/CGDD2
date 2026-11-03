using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class MissileGuidanceTests
    {
        WeaponDefinition weapon;
        [SetUp]public void Setup(){weapon=ScriptableObject.CreateInstance<WeaponDefinition>();}
        [TearDown]public void Cleanup(){Object.DestroyImmediate(weapon);}

        [Test]public void AngularSpeedAndAccelerationStayBoundedWhenTheTurnAxisChanges()
        {
            weapon.missileTurnDegrees=90;weapon.missileTurnAcceleration=180;
            weapon.missileSeekerConeDegrees=179;weapon.missileSeekerLossSeconds=100;
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            for(int i=0;i<240;i++){
                var previous=guidance.AngularVelocityDegrees;
                Vector3 target=i<80?new Vector3(100,0,100):i<160?new Vector3(0,100,100):new Vector3(-100,0,100);
                guidance.Step(weapon,.2f+i*.02f,target,Vector3.zero,100,.02f);
                Assert.That(guidance.IsTracking,Is.True);
                Assert.That(guidance.AngularVelocityDegrees.magnitude,Is.LessThanOrEqualTo(90.0001f));
                Assert.That((guidance.AngularVelocityDegrees-previous).magnitude,Is.LessThanOrEqualTo(3.6001f));
                Assert.That(guidance.Direction.magnitude,Is.EqualTo(1).Within(.00001f));
            }
        }

        [Test]public void ConeGraceMustBeContinuousAndLostLockCannotResume()
        {
            weapon.missileTurnDegrees=0;
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            guidance.Step(weapon,1,Vector3.right,Vector3.zero,100,.2f);
            Assert.That(guidance.IsTracking,Is.True);
            guidance.Step(weapon,1.1f,Vector3.forward,Vector3.zero,100,.1f);
            Assert.That(guidance.OutsideConeSeconds,Is.Zero);
            guidance.Step(weapon,1.3f,Vector3.right,Vector3.zero,100,.2f);
            guidance.Step(weapon,1.44f,Vector3.right,Vector3.zero,100,.14f);
            Assert.That(guidance.IsTracking,Is.True);
            guidance.Step(weapon,1.45f,Vector3.right,Vector3.zero,100,.01f);
            Assert.That(guidance.IsTracking,Is.False);
            guidance.Step(weapon,2,Vector3.forward,Vector3.zero,100,.5f);
            Assert.That(guidance.IsTracking,Is.False);
            Assert.That(guidance.Direction,Is.EqualTo(Vector3.forward));
        }

        [Test]public void GuidanceExpiryKeepsTheLastDirectionEvenWhenTargetReturnsToTheCone()
        {
            weapon.missileGuidanceSeconds=.5f;
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            guidance.Step(weapon,.3f,new Vector3(1,0,2),Vector3.zero,100,.1f);
            Assert.That(Vector3.Angle(Vector3.forward,guidance.Direction),Is.GreaterThan(.1f));
            var direction=guidance.Direction;
            guidance.Step(weapon,.5f,Vector3.forward,Vector3.zero,100,.2f);
            guidance.Step(weapon,1,Vector3.forward,Vector3.zero,100,.5f);
            Assert.That(guidance.IsTracking,Is.False);
            Assert.That(guidance.Direction,Is.EqualTo(direction));
            Assert.That(guidance.AngularVelocityDegrees,Is.EqualTo(Vector3.zero));
        }

        [Test]public void EjectionDelayDoesNotSpendTurnAccelerationOrConeGrace()
        {
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            guidance.Step(weapon,.1f,Vector3.right,Vector3.zero,100,.1f);
            Assert.That(guidance.Direction,Is.EqualTo(Vector3.forward));
            Assert.That(guidance.OutsideConeSeconds,Is.Zero);
            guidance.Step(weapon,.14f,Vector3.right,Vector3.zero,100,.04f);
            Assert.That(guidance.OutsideConeSeconds,Is.EqualTo(.02f).Within(.00001f));
            Assert.That(guidance.AngularVelocityDegrees,Is.EqualTo(Vector3.zero),"An unseen target cannot command a fresh turn during grace.");
            Assert.That(guidance.Direction,Is.EqualTo(Vector3.forward));
            guidance.Reset(Vector3.forward);
            guidance.Step(weapon,.14f,new Vector3(1,0,1),Vector3.zero,100,.04f);
            Assert.That(guidance.OutsideConeSeconds,Is.Zero);
            Assert.That(guidance.AngularVelocityDegrees.magnitude,Is.EqualTo(3.6f).Within(.001f));
            Assert.That(Vector3.Angle(Vector3.forward,guidance.Direction),Is.EqualTo(.036f).Within(.01f));
        }

        [TestCase(240,135,450,120,12,1)]
        [TestCase(245,115,360,110,14,-1)]
        [TestCase(250,95,280,100,16,1)]
        public void AcceleratingMissileInterceptsACrossingTargetBeforeItsSeekerExpires(float cruise,float turn,float angularAcceleration,float acceleration,float lifetime,int side)
        {
            ConfigureTracking(cruise,turn,angularAcceleration,acceleration,lifetime);
            float miss=SimulateCrossing(.02f,side,out float age);
            Assert.That(miss,Is.LessThan(1.5f),"A bounded real trajectory must reach the moving target, not only point toward a prediction.");
            Assert.That(age,Is.LessThan(lifetime));
        }

        [Test]public void SmallAlignmentErrorConvergesWithoutPersistentSideToSideHunting()
        {
            ConfigureTracking(240,135,450,120,12);
            var guidance=new MissileGuidance();guidance.Reset(Quaternion.Euler(0,25,0)*Vector3.forward);
            for(int i=0;i<200;i++)guidance.Step(weapon,.2f+i*.02f,Vector3.forward*500,Vector3.zero,240,.02f);
            Assert.That(Vector3.Angle(guidance.Direction,Vector3.forward),Is.LessThan(.05f));
            Assert.That(guidance.AngularVelocityDegrees.magnitude,Is.LessThan(.05f));
        }

        [Test]public void CrossingCaptureRemainsAccurateAtThirtyAndOneHundredTwentyUpdatesPerSecond()
        {
            ConfigureTracking(240,135,450,120,12);
            float coarse=SimulateCrossing(1f/30f,1,out float coarseAge);
            float fine=SimulateCrossing(1f/120f,1,out float fineAge);
            Assert.That(coarse,Is.LessThan(1.5f));Assert.That(fine,Is.LessThan(1.5f));
            Assert.That(Mathf.Abs(coarseAge-fineAge),Is.LessThan(.15f),"The same chase must not depend on the sampling frequency.");
        }

        [Test]public void OutrunningTargetCannotCauseTeleportationOrGuidancePastItsLifetime()
        {
            ConfigureTracking(240,135,450,120,1);
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            Vector3 missile=Vector3.zero,target=Vector3.forward*500;float speed=25;
            for(int i=0;i<100;i++)
            {
                guidance.Step(weapon,(i+1)*.02f,target-missile,Vector3.forward*400,speed,.02f);
                speed=Mathf.MoveTowards(speed,weapon.projectileSpeed,weapon.missileAcceleration*.02f);
                missile+=guidance.Direction*speed*.02f;target+=Vector3.forward*8;
            }
            Assert.That(guidance.IsTracking,Is.False);Assert.That(guidance.Direction,Is.EqualTo(Vector3.forward));
            Assert.That(target.z-missile.z,Is.GreaterThan(500));
        }

        [Test]public void UnseenTargetCannotReverseAngularMomentumDuringConeGrace()
        {
            ConfigureTracking(240,110,360,120,12);weapon.missileSeekerLossSeconds=.3f;
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            guidance.Step(weapon,.3f,new Vector3(100,0,300),Vector3.zero,240,.1f);
            var previous=guidance.AngularVelocityDegrees;
            Assert.That(previous.magnitude,Is.GreaterThan(0));
            guidance.Step(weapon,.32f,Vector3.left*100,Vector3.zero,240,.02f);
            Assert.That(guidance.IsTracking,Is.True);
            Assert.That(guidance.AngularVelocityDegrees.magnitude,Is.LessThan(previous.magnitude));
            Assert.That(Vector3.Dot(previous,guidance.AngularVelocityDegrees),Is.GreaterThanOrEqualTo(0),
                "Grace may brake the last visible turn but cannot chase a new hidden bearing.");
            for(int i=0;i<20;i++)guidance.Step(weapon,.34f+i*.02f,Vector3.left*100,Vector3.zero,240,.02f);
            Assert.That(guidance.IsTracking,Is.False);
            guidance.Step(weapon,1,guidance.Direction*100,Vector3.zero,240,.1f);
            Assert.That(guidance.IsTracking,Is.False);
        }

        [TestCase(240,110,360,60)]
        [TestCase(245,95,300,55)]
        [TestCase(250,80,240,50)]
        public void CloseLateralCrossingCanOvershootAndPermanentlyLoseTheSeeker(float cruise,float turn,float angularAcceleration,float cone)
        {
            ConfigureTracking(cruise,turn,angularAcceleration,120,12);
            weapon.missileSeekerConeDegrees=cone;weapon.missileSeekerLossSeconds=.3f;
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            Vector3 missile=Vector3.zero,target=Vector3.forward*65,velocity=Vector3.right*100;
            float closest=float.PositiveInfinity;
            // A ship already crossing at 100 m/s is below the flight tuning's 250 m/s ceiling.
            // Starting with a cruising missile tests the short turn/overshoot opportunity, not outrunning it.
            for(int i=0;i<250;i++){
                var before=target-missile;
                guidance.Step(weapon,1+i*.02f,before,velocity,cruise,.02f);
                missile+=guidance.Direction*cruise*.02f;target+=velocity*.02f;
                var segment=target-missile-before;
                float fraction=segment.sqrMagnitude>.000001f?Mathf.Clamp01(-Vector3.Dot(before,segment)/segment.sqrMagnitude):0;
                closest=Mathf.Min(closest,(before+segment*fraction).magnitude);
            }
            Assert.That(closest,Is.GreaterThan(10),"A fast lateral break has a real geometric miss, including the swept trajectory.");
            Assert.That(guidance.IsTracking,Is.False,"Passing the target must not produce endless perfect U-turns.");
        }

        [TestCase(240,110,360,60)]
        [TestCase(245,95,300,55)]
        [TestCase(250,80,240,50)]
        public void TunedSeekersStillCaptureAPredictableDistantCrossing(float cruise,float turn,float angularAcceleration,float cone)
        {
            ConfigureTracking(cruise,turn,angularAcceleration,120,12);
            weapon.missileSeekerConeDegrees=cone;weapon.missileSeekerLossSeconds=.3f;
            Assert.That(SimulateCrossing(.02f,1,out _),Is.LessThan(1.5f));
        }

        void ConfigureTracking(float cruise,float turn,float angularAcceleration,float acceleration,float lifetime)
        {
            weapon.projectileSpeed=cruise;weapon.missileLaunchSpeed=25;weapon.missileAcceleration=acceleration;
            weapon.missileTurnDegrees=turn;weapon.missileTurnAcceleration=angularAcceleration;
            weapon.missileGuidanceSeconds=lifetime;weapon.missileSeekerConeDegrees=65;weapon.missileSeekerLossSeconds=.45f;weapon.range=4000;
        }
        float SimulateCrossing(float dt,int side,out float capturedAge)
        {
            var guidance=new MissileGuidance();guidance.Reset(Vector3.forward);
            Vector3 missile=Vector3.zero,target=new Vector3(70*side,0,450),targetVelocity=Vector3.right*(60*side);
            float speed=weapon.missileLaunchSpeed,closest=float.PositiveInfinity;capturedAge=weapon.missileGuidanceSeconds;
            for(float age=dt;age<weapon.missileGuidanceSeconds;age+=dt)
            {
                Vector3 before=target-missile;
                guidance.Step(weapon,age,before,targetVelocity,speed,dt);
                speed=Mathf.MoveTowards(speed,weapon.projectileSpeed,weapon.missileAcceleration*dt);
                missile+=guidance.Direction*speed*dt;target+=targetVelocity*dt;
                Vector3 after=target-missile,segment=after-before;
                float fraction=segment.sqrMagnitude>.000001f?Mathf.Clamp01(-Vector3.Dot(before,segment)/segment.sqrMagnitude):0;
                closest=Mathf.Min(closest,(before+segment*fraction).magnitude);
                if(closest<1){capturedAge=age;break;}
            }
            return closest;
        }
    }
}

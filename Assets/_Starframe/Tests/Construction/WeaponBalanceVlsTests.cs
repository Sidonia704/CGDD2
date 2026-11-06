using NUnit.Framework;
using UnityEditor;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponBalanceVlsTests
    {
        [TestCase("Gun1Weapon",12,2700,8640,.6f)]
        [TestCase("Gun2Weapon",24,2700,15300,.8f)]
        [TestCase("Gun3Weapon",32,2700,25920,1)]
        [TestCase("LaserRapidCannon",10,6600,10080,.6f)]
        [TestCase("Laser2Weapon",8,6600,18630,.8f)]
        [TestCase("Laser3Weapon",6,6600,30780,1)]
        [TestCase("Railgun1Weapon",1.5f,10800,12960,.6f)]
        [TestCase("Railgun2Weapon",1,10800,18000,.8f)]
        [TestCase("Railgun3Weapon",.75f,10800,24960,1)]
        public void NativeCadenceSpeedAndSustainedDamageMatchApprovedProfile(string name,float rate,float speed,float dps,float gain)
        {
            var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+".asset");
            Assert.That(w,Is.Not.Null);Assert.That(w.shotsPerSecond,Is.EqualTo(rate).Within(.00001f));
            Assert.That(w.projectileSpeed,Is.EqualTo(speed));Assert.That(w.damage*w.shotsPerSecond,Is.EqualTo(dps).Within(.01f));
            Assert.That(GameAudioRuntime.ShotGain(w),Is.EqualTo(gain).Within(.00001f));
        }
        [TestCase("Gun1Weapon","Gun2Weapon","Gun3Weapon")]
        [TestCase("LaserRapidCannon","Laser2Weapon","Laser3Weapon")]
        [TestCase("Railgun1Weapon","Railgun2Weapon","Railgun3Weapon")]
        public void NativeDirectFireSizesShareLargestTierSpeed(string small,string medium,string large)
        {
            var largest=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+large+".asset");
            Assert.That(largest,Is.Not.Null);
            foreach(var name in new[]{small,medium,large})
            {
                var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+".asset");
                Assert.That(weapon,Is.Not.Null,name);
                Assert.That(weapon.projectileSpeed,Is.EqualTo(largest.projectileSpeed),name);
            }
        }
        [Test]public void AlertAndMissileLevelsUseSingleReductionAndLowerRadarPitch()
        {
            var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
            Assert.That(tuning.Get(GameSound.EnemyDetected).gain,Is.EqualTo(.24f).Within(.00001f));
            Assert.That(tuning.Get(GameSound.EnemyDetected).pitch,Is.EqualTo(.8f).Within(.00001f));
            Assert.That(tuning.Get(GameSound.MissileShot).gain,Is.EqualTo(.301f).Within(.00001f));
            Assert.That(tuning.Get(GameSound.MissileShot).minimumInterval,Is.LessThanOrEqualTo(1f/16f));
        }
    }
}

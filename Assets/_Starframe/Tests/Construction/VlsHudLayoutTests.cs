using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class VlsHudLayoutTests
    {
        [Test]public void MixedMagazineSizesPackWithoutOverlapsOrEmptyVerticalSlots()
        {
            var states=new List<WeaponHudState>();var bounds=new List<Rect>();
            var definition=ScriptableObject.CreateInstance<WeaponDefinition>();definition.family=WeaponFamily.VerticalLaunchMissile;
            try
            {
                foreach(int capacity in new[]{16,8,8,16,8,16,8})states.Add(new WeaponHudState(null,definition,1,true,false,false,0,0,1,capacity,capacity));
                CombatWeaponStatusGraphic.PackMissiles(states,bounds);
                Assert.That(bounds.Count,Is.EqualTo(states.Count));
                for(int i=0;i<bounds.Count;i++)for(int j=i+1;j<bounds.Count;j++)Assert.That(bounds[i].Overlaps(bounds[j]),Is.False);
                foreach(float x in new[]{78f,106f})
                {
                    float previousBottom=72;
                    foreach(var r in bounds)if(Mathf.Approximately(r.x,x)){Assert.That(r.y-previousBottom,Is.InRange(0,3.01f));previousBottom=r.yMax;}
                }
                Assert.That(bounds[0].height,Is.GreaterThan(bounds[1].height*2));
            }
            finally{Object.DestroyImmediate(definition);}
        }
    }
}

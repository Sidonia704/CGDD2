// @role Define the countermeasure salvo, lifetime, missile decoy chance and voxel appearance.
// @tune burstCount:int:1+:Sparks in one shared decoy burst; each burst is one lure opportunity per missile.
// @tune cooldownSeconds:float:0+:Independent cooldown per surviving launcher.
// @tune spreadHalfAngleDegrees:float:0-89:Half angle about the launcher's rotated local up direction.
// @tune emissionSpeed:float:0+:Outward speed added to the launching ship's velocity in metres per second.
// @tune lifetime:float:0.01+:Burst lifetime in seconds.
// @tune decoyChance:float:0-1:One decoy roll per eligible missile and burst.
// @tune decoyRadius:float:0+:Maximum missile distance from an eligible decoy burst in metres.
// @tune sparkSize:float:0.01+:Initial yellow voxel side length in metres before particle scaling.
// @tune glowIntensity:float:0+:Yellow voxel glow intensity.

using UnityEngine;

namespace Starframe.Flight
{
    [CreateAssetMenu(menuName="Starframe/Flare Definition")]
    public sealed class FlareDefinition:ScriptableObject
    {
        [Min(1)]public int burstCount=12;
        [Min(0)]public float cooldownSeconds=10;
        [Range(0,89)]public float spreadHalfAngleDegrees=22;
        [Min(0)]public float emissionSpeed=24;
        [Min(.01f)]public float lifetime=3;
        [Range(0,1)]public float decoyChance=.45f;
        [Min(0)]public float decoyRadius=150;
        [Min(.01f)]public float sparkSize=.22f;
        [Min(0)]public float glowIntensity=5;
    }
}

// @role Describe an editable enemy blueprint, size, score and pilot behavior for authored encounters.
// @calls BlueprintRepository, BotBehaviorProfile

using Starframe.Construction;
using UnityEngine;

namespace Starframe.Combat
{
    public enum EnemySize { Small, Large, Capital }

    [CreateAssetMenu(menuName="Starframe/Enemy Archetype")]
    public sealed class EnemyArchetypeDefinition : ScriptableObject
    {
        public string id, nameZh, nameEn;
        public EnemySize size;
        public TextAsset blueprint;
        [Tooltip("Optional second fit of the same hull, used for later waves.")]
        public TextAsset alternateBlueprint;
        [Min(0)] public int score=100;
        public BotBehaviorProfile behavior;

        public BlueprintData CreateBlueprint(bool alternate=false)
        {
            var source=alternate&&alternateBlueprint?alternateBlueprint:blueprint;
            if(!source) throw new System.InvalidOperationException("Enemy "+id+" has no blueprint.");
            var data=JsonUtility.FromJson<BlueprintData>(source.text);
            if(data==null||data.parts==null||data.parts.Count==0)
                throw new System.IO.InvalidDataException("Enemy "+id+" has an empty blueprint.");
            return BlueprintRepository.Upgrade(data);
        }
    }
}

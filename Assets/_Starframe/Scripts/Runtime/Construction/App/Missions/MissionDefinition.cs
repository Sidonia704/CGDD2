// @role Define mission identity and difficulty-dependent deployment budgets.
// @tune unpricedPartCost:float:0.01+:Optional provisional cost for an otherwise zero-cost part.
// @tune relaxedBudgetMultiplier:float:0+:Budget multiplier for relaxed difficulty.
// @tune standardBudgetMultiplier:float:0+:Budget multiplier for standard difficulty.
// @tune expertBudgetMultiplier:float:0+:Budget multiplier for expert difficulty.

using System;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.App
{
    public enum MissionDifficulty { Relaxed, Standard, Expert }

    [CreateAssetMenu(menuName = "Starframe/Mission Definition")]
    public sealed class MissionDefinition : ScriptableObject
    {
        public string id, titleKey, descriptionKey, regionKey;
        public Texture2D thumbnail;
        [Tooltip("May be empty while the level content is being built.")]
        public string sceneName;
        [Min(0)] public float baseBudget;
        [Tooltip("Price zero-cost module definitions for this mission without changing module balance assets.")]
        public bool useProvisionalPricing;
        [Min(.01f)] public float unpricedPartCost = 10;
        [Tooltip("0 disables the optional mass constraint.")]
        [Min(0)] public float maximumMassKg;
        public string[] objectivesKeys = Array.Empty<string>();
        [Min(0)] public float relaxedBudgetMultiplier = 1.25f;
        [Min(0)] public float standardBudgetMultiplier = 1;
        [Min(0)] public float expertBudgetMultiplier = .75f;

        public float CostFor(ModuleDefinition definition)
        {
            if (!definition) throw new ArgumentNullException(nameof(definition));
            return useProvisionalPricing && definition.cost == 0 ? unpricedPartCost : definition.cost;
        }

        public float BudgetFor(MissionDifficulty difficulty)
        {
            float multiplier;
            switch (difficulty)
            {
                case MissionDifficulty.Relaxed: multiplier = relaxedBudgetMultiplier; break;
                case MissionDifficulty.Standard: multiplier = standardBudgetMultiplier; break;
                case MissionDifficulty.Expert: multiplier = expertBudgetMultiplier; break;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
            if (baseBudget < 0 || multiplier < 0) return 0;
            double value = (double)baseBudget * multiplier;
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > float.MaxValue ? 0 : (float)value;
        }
    }
}

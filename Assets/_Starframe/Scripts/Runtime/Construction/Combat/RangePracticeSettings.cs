// @role Persist the selected stationary, flying or combat drill mode.

using System;
using UnityEngine;

namespace Starframe.Combat
{
    public enum RangeBotMode { Stationary, FlyOnly, StationaryFire, Combat }

    // Kept separate from the blueprint roster: changing a drill never changes its ships.
    public static class RangePracticeSettings
    {
        public const string PreferenceKey = "Starframe.RangePractice.Mode.v1";

        public static RangeBotMode Mode
        {
            get
            {
                int value = PlayerPrefs.GetInt(PreferenceKey, (int)RangeBotMode.Combat);
                return value >= (int)RangeBotMode.Stationary && value <= (int)RangeBotMode.Combat
                    ? (RangeBotMode)value : RangeBotMode.Combat;
            }
            set
            {
                if (!Enum.IsDefined(typeof(RangeBotMode), value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                PlayerPrefs.SetInt(PreferenceKey, (int)value);
                PlayerPrefs.Save();
            }
        }
    }
}

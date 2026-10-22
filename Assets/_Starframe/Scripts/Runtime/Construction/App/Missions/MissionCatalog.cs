// @role Find configured missions by stable identifier.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starframe.App
{
    [CreateAssetMenu(menuName = "Starframe/Mission Catalog")]
    public sealed class MissionCatalog : ScriptableObject
    {
        public List<MissionDefinition> missions = new List<MissionDefinition>();

        public MissionDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || missions == null) return null;
            foreach (var mission in missions)
                if (mission && string.Equals(mission.id, id, StringComparison.Ordinal)) return mission;
            return null;
        }
    }
}

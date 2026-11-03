// @role Track finite wave queues and idempotent enemy defeats independently of presentation and physics.
// @calls ArcadeMissionDefinition
using System.Collections.Generic;
namespace Starframe.Combat
{
    public sealed class ArcadeWaveProgress
    {
        readonly HashSet<int> living=new HashSet<int>();
        int pending;
        public int Pending=>pending;
        public int Alive=>living.Count;
        public bool IsCleared=>pending==0&&living.Count==0;
        public void Begin(int total){living.Clear();pending=System.Math.Max(0,total);}
        public void Append(int total){pending+=System.Math.Max(0,total);}
        public bool Spawn(int id){if(pending<=0||!living.Add(id))return false;pending--;return true;}
        public bool Defeat(int id)=>living.Remove(id);
    }
}

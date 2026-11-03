// @role Record a level-reported outcome and return to the result screen.
// @calls ShipWorkspace

using System;
using UnityEngine;
using Starframe.Construction;

namespace Starframe.App
{
    public enum MissionOutcome{Victory,Defeat,Aborted}
    [Serializable] public sealed class MissionRunSummary
    {
        public float durationSeconds,damageDealt,damageTaken;
        public int enemiesDestroyed,survivingParts;
        public MissionRunSummary Copy()=>JsonUtility.FromJson<MissionRunSummary>(JsonUtility.ToJson(this));
    }
    [Serializable] public sealed class MissionResult
    {
        public DeploymentRequest request;public MissionOutcome outcome;public MissionRunSummary summary;public string completedUtc;
        public MissionResult Copy()=>JsonUtility.FromJson<MissionResult>(JsonUtility.ToJson(this));
    }
    /// <summary>Level scripts decide victory/defeat; this records a report without touching the saved ship.</summary>
    public static class MissionResults
    {
        static MissionResult last;
        public static MissionResult Last=>last?.Copy();
        public static event Action<MissionResult> Completed;
        public static bool Finish(MissionOutcome outcome,MissionRunSummary summary)
        {
            if(!Enum.IsDefined(typeof(MissionOutcome),outcome))throw new ArgumentOutOfRangeException(nameof(outcome));
            var request=MissionSession.Current;if(request==null||last!=null)return false;
            var copy=(summary??new MissionRunSummary()).Copy();
            copy.durationSeconds=Positive(copy.durationSeconds);copy.damageDealt=Positive(copy.damageDealt);copy.damageTaken=Positive(copy.damageTaken);
            copy.enemiesDestroyed=Mathf.Max(0,copy.enemiesDestroyed);copy.survivingParts=Mathf.Clamp(copy.survivingParts,0,request.ship.parts.Count);
            last=new MissionResult{request=request,outcome=outcome,summary=copy,completedUtc=DateTime.UtcNow.ToString("O")};Completed?.Invoke(last.Copy());return true;
        }
        public static bool FinishAndReturn(MissionOutcome outcome,MissionRunSummary summary)
        {
            if(!Finish(outcome,summary))return false;
            AppNavigation.SelectedMissionId=last.request.missionId;AppNavigation.OpenHome(AppPage.Results);return true;
        }
        public static void Clear()=>last=null;
        static float Positive(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Max(0,value);
    }
}

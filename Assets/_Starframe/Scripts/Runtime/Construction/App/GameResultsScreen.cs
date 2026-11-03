// @role Present reported mission results without rewriting the saved blueprint.
// @calls GameAppBootstrap, MissionResults, ShipWorkspace, UiTheme, L10n

using UnityEngine;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    public sealed partial class GameAppBootstrap
    {
        public void ShowResults()
        {
            var result=MissionResults.Last;if(result==null){ShowHome();return;}
            Begin(AppPage.Results);preview.Show(result.request.ship);preview.ResetView();preview.SetFocus(.59f,.76f);
            Header(T("app.resultsTitle"),T("app.resultsSubtitle"));
            ui.Panel("MissionReportGlass",48,248,970,768,true);
            var definition=catalog?catalog.Find(result.request.missionId):null;
            var tint=result.outcome==MissionOutcome.Victory?UiTheme.Current.success:result.outcome==MissionOutcome.Defeat?UiTheme.Current.danger:UiTheme.Current.amber;
            ui.Label("Outcome",T("app.outcome."+result.outcome),84,282,898,52,34,tint);
            ui.Label("ResultOperation",definition?T(definition.titleKey):T("app.resultsTitle"),84,352,898,38,23);
            UserName("ResultShip",result.request.ship.name,84,403,898,42,20);
            float seconds=result.summary.durationSeconds;
            ResultMetric("Score",T("mission.arcade.resultScore"),result.summary.score.ToString("N0"),84,478);
            ResultMetric("Waves",T("mission.arcade.resultWaves"),$"{result.summary.wavesCompleted} / {result.summary.totalWaves}",548,478);
            ResultMetric("Duration",T("app.resultDuration"),$"{Mathf.FloorToInt(seconds/60):00}:{Mathf.FloorToInt(seconds%60):00}",84,585);
            ResultMetric("Kills",T("app.resultKills"),result.summary.enemiesDestroyed.ToString("N0"),548,585);
            ResultMetric("DamageDealt",T("app.resultDamageDealt"),result.summary.damageDealt.ToString("N0"),84,692);
            ResultMetric("DamageTaken",T("app.resultDamageTaken"),result.summary.damageTaken.ToString("N0"),548,692);
            ui.Label("ResultSurvivors",T("app.resultSurvivors",result.summary.survivingParts,result.request.ship.parts.Count),84,808,898,30,18,GlassUiPalette.Ink);
            ui.Label("BlueprintPreserved",T("app.resultBlueprintPreserved"),84,850,898,48,16,GlassUiPalette.Muted);
            ui.Panel("FleetTallyGlass",1080,790,744,146);
            ui.Label("ResultClassKills",T("mission.arcade.resultClassKills",result.summary.smallDestroyed,result.summary.largeDestroyed,result.summary.capitalDestroyed),1104,810,696,30,17,GlassUiPalette.Ink);
            ui.Label("ResultBestScore",T("mission.arcade.resultBestScore",result.summary.bestScore.ToString("N0")),1104,854,696,28,17,GlassUiPalette.Ink);
            ui.Label("ResultCombo",T("mission.arcade.resultCombo",Mathf.Max(1,result.summary.maxCombo)),1104,894,696,26,16,GlassUiPalette.Muted);
            ui.Button("ResultsHome",T("app.backHangar"),84,925,426,56,ShowHome);
            ui.Button("ResultsPrepare",T("app.prepareAgain"),536,925,446,56,()=>{AppNavigation.Difficulty=result.request.difficulty;SelectMission(result.request.missionId);},true);
            ui.Label("ResultPreviewHint",T("app.orbitHint"),1080,971,744,32,14,GlassUiPalette.Muted).alignment=TMPro.TextAlignmentOptions.Center;
        }
        void ResultMetric(string name,string label,string value,float x,float y)
        {
            ui.Label(name+"Label",label,x,y,410,25,15,GlassUiPalette.Muted);
            ui.Label(name+"Value",value,x,y+31,410,55,34);
        }
    }
}

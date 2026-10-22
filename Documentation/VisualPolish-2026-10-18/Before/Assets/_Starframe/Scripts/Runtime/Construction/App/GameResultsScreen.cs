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
            Begin(AppPage.Results);preview.Show(result.request.ship);preview.ResetView();preview.SetFocus(.60f,.72f);
            Header(T("app.resultsTitle"),T("app.resultsSubtitle"));
            ui.Panel("MissionReportGlass",48,226,970,728,true);
            var definition=catalog?catalog.Find(result.request.missionId):null;
            var tint=result.outcome==MissionOutcome.Victory?UiTheme.Current.success:result.outcome==MissionOutcome.Defeat?UiTheme.Current.danger:UiTheme.Current.amber;
            ui.Label("Outcome",T("app.outcome."+result.outcome),84,262,898,52,34,tint);
            ui.Label("ResultOperation",definition?T(definition.titleKey):T("app.resultsTitle"),84,335,898,38,22);
            UserName("ResultShip",result.request.ship.name,84,393,898,45,23);
            float seconds=result.summary.durationSeconds;
            ResultMetric("Duration",T("app.resultDuration"),$"{Mathf.FloorToInt(seconds/60):00}:{Mathf.FloorToInt(seconds%60):00}",84,480);
            ResultMetric("Kills",T("app.resultKills"),result.summary.enemiesDestroyed.ToString("N0"),530,480);
            ResultMetric("DamageDealt",T("app.resultDamageDealt"),result.summary.damageDealt.ToString("N0"),84,605);
            ResultMetric("DamageTaken",T("app.resultDamageTaken"),result.summary.damageTaken.ToString("N0"),530,605);
            ui.Label("ResultSurvivors",T("app.resultSurvivors",result.summary.survivingParts,result.request.ship.parts.Count),84,750,898,34,19,GlassUiPalette.Accent);
            ui.Label("BlueprintPreserved",T("app.resultBlueprintPreserved"),84,810,898,76,16,GlassUiPalette.Muted);
            ui.Button("ResultsHome",T("app.backHangar"),84,984,426,60,ShowHome);
            ui.Button("ResultsPrepare",T("app.prepareAgain"),536,984,446,60,()=>{AppNavigation.Difficulty=result.request.difficulty;SelectMission(result.request.missionId);},true);
            ui.Label("ResultPreviewHint",T("app.orbitHint"),1080,971,744,32,14,GlassUiPalette.Muted).alignment=TMPro.TextAlignmentOptions.Center;
        }
        void ResultMetric(string name,string label,string value,float x,float y)
        {
            ui.Label(name+"Label",label,x,y,410,27,14,GlassUiPalette.Muted);
            ui.Label(name+"Value",value,x,y+39,410,55,36);
        }
    }
}

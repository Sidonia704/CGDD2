using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.Tests
{
    public sealed class MissionHUDTests
    {
        [UnityTest] public IEnumerator WaveReadoutSupportsBothLocalesLargeTextAndPauseWithoutCoveringAim()
        {
            yield return L10n.EnsureReady();
            string previousLocale=LocalizationSettings.SelectedLocale?.Identifier.Code;
            var previousSettings=GameSettings.Current.Clone();
            var configuration=ScriptableObject.CreateInstance<ArcadeMissionDefinition>();
            configuration.waves=Enumerable.Range(1,5).Select(i=>new ArcadeWave{nameEn="Heavy formation "+i,nameZh="重装编队 "+i}).ToArray();
            var host=new GameObject("Mission HUD isolated fixture");
            var canvasObject=new GameObject("FlightRangeCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(host.transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            UiStyle.ApplyScaler(canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>());
            var hud=host.AddComponent<RangeHUD>();hud.enabled=false;
            Set(hud,"canvas",canvas);
            var modal=MenuSurface.Rect((RectTransform)canvas.transform,"ExitModal",0,0,1920,1080);modal.gameObject.SetActive(false);
            Set(hud,"modal",modal.gameObject);
            foreach(string name in new[]{"RangeOptions","RangeOptionsText","RangeSettings","RangeSettingsText","ExitTitle","ExitHint","ConfirmExitText"}){
                var child=MenuSurface.Rect(modal,name,0,0,200,50);
                if(name.EndsWith("Text",StringComparison.Ordinal)||name.StartsWith("Exit",StringComparison.Ordinal))child.gameObject.AddComponent<TextMeshProUGUI>();
            }
            var director=host.AddComponent<ArcadeMissionDirector>();director.enabled=false;
            Set(director,"definition",configuration);Property(director,"WaveIndex",4);Property(director,"State",ArcadeMissionState.Intermission);
            Property(director,"Score",123456);Property(director,"ComboMultiplier",3);
            var missionHud=host.AddComponent<MissionHUD>();
            try{
                var large=previousSettings.Clone();large.uiScale=1.25f;GameSettings.SetCurrent(large);GameSettings.Apply();
                hud.ConfigureMissionPresentation();missionHud.Initialize(director);
                Assert.That(modal.Find("RangeOptions").gameObject.activeSelf,Is.False);
                foreach(string locale in new[]{"en","zh-Hans"}){
                    Property(director,"WaveIndex",4);
                    L10n.SelectLocale(locale);yield return L10n.EnsureReady();yield return null;
                    Invoke(missionHud,"Refresh");Canvas.ForceUpdateCanvases();
                    foreach(string name in new[]{"MissionStage"}){
                        var text=Label(canvas,name);Assert.That(text,Is.Not.Null,name);text.ForceMeshUpdate(true,true);
                        Assert.That(text.font,Is.Not.Null,name);Assert.That(text.isTextOverflowing,Is.False,name+" must fit at large text size in "+locale);
                        Assert.That(text.isTextTruncated,Is.False,name+" must not truncate in "+locale);
                    }
                    var readout=Label(canvas,"MissionStage").rectTransform;
                    Assert.That(readout.anchorMax.x,Is.LessThan(.25f),"Wave readout belongs at the left edge.");
                    Assert.That(canvas.transform.Find("MissionHUD").GetComponentsInChildren<UnityEngine.UI.Image>(true),Is.Empty,"Wave UI has no background or border images.");
                    Assert.That(canvas.transform.Find("MissionHUD").GetComponentsInChildren<UnityEngine.UI.Button>(true),Is.Empty);
                    Assert.That(canvas.transform.Find("MissionHUD/MissionBrief"),Is.Null);
                    Assert.That(Label(canvas,"MissionWave"),Is.Null);Assert.That(Label(canvas,"MissionScore"),Is.Null);Assert.That(Label(canvas,"MissionClock"),Is.Null);
                    Assert.That(Label(canvas,"MissionContacts"),Is.Null);Assert.That(Label(canvas,"WaveCountdown"),Is.Null);
                    Assert.That(Label(canvas,"MissionStage").text,Is.EqualTo("4/5"));
                    Property(director,"WaveIndex",3);Property(director,"State",ArcadeMissionState.Combat);Invoke(missionHud,"Refresh");
                    Assert.That(Label(canvas,"MissionStage").text,Is.EqualTo("3/5"));
                    Property(director,"State",ArcadeMissionState.Victory);Invoke(missionHud,"Refresh");
                    Assert.That(canvas.transform.Find("MissionHUD/MissionOutcome").gameObject.activeSelf,Is.True);
                    Assert.That(Label(canvas,"MissionOutcomeTitle").text,Is.EqualTo(L10n.T("app.outcome.Victory")));
                    Property(director,"State",ArcadeMissionState.Intermission);
                }
                modal.gameObject.SetActive(true);Invoke(missionHud,"Update");
                var group=canvas.transform.Find("MissionHUD").GetComponent<CanvasGroup>();
                Assert.That(group.alpha,Is.Zero);Assert.That(group.blocksRaycasts,Is.False,"Wave text must not capture clicks while the pause dialog is open.");
                modal.gameObject.SetActive(false);Invoke(missionHud,"Update");Assert.That(group.alpha,Is.EqualTo(1));
            }finally{
                UnityEngine.Object.Destroy(host);UnityEngine.Object.Destroy(configuration);
                GameSettings.SetCurrent(previousSettings);GameSettings.Apply();
                if(!string.IsNullOrEmpty(previousLocale))L10n.SelectLocale(previousLocale);
            }
            yield return L10n.EnsureReady();yield return null;
        }
        static TextMeshProUGUI Label(Canvas canvas,string name)=>canvas.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(label=>label.name==name);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static void Property(object target,string name,object value)=>target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public).GetSetMethod(true).Invoke(target,new[]{value});
        static void Invoke(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    }
}

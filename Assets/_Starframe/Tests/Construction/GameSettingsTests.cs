using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Starframe.App;

namespace Starframe.Tests
{
    public sealed class GameSettingsTests
    {
        string folder,path;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"StarframeSettingsTests_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);path=Path.Combine(folder,"preferences.json");
        }
        [TearDown] public void Cleanup()
        {
            if(!Directory.Exists(folder))return;
            Assert.That(Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),Is.True);
            Assert.That(Path.GetFileName(folder),Does.StartWith("StarframeSettingsTests_"));
            Directory.Delete(folder,true);
        }

        [Test] public void AtomicReplacementRoundTripsAndKeepsPreviousSnapshot()
        {
            var first=new GameSettingsData{localeCode="zh-Hans",mouseSensitivity=1.7f};
            GameSettingsStorage.Write(path,first);
            var next=first.Clone();next.localeCode="en";next.mouseSensitivity=.6f;
            next.bindings[0].path="<Keyboard>/upArrow";
            GameSettingsStorage.Write(path,next);
            var actual=GameSettingsStorage.Read(path,new GameSettingsData());
            Assert.That(actual.localeCode,Is.EqualTo("en"));Assert.That(actual.mouseSensitivity,Is.EqualTo(.6f).Within(.0001f));
            Assert.That(actual.bindings[0].path,Is.EqualTo("<Keyboard>/upArrow"));
            var backup=GameSettingsStorage.Read(path+".bak",new GameSettingsData());
            Assert.That(backup.localeCode,Is.EqualTo("zh-Hans"));Assert.That(backup.mouseSensitivity,Is.EqualTo(1.7f).Within(.0001f));
            Assert.That(Directory.GetFiles(folder,"*.tmp"),Is.Empty);
        }
        [Test] public void MissingPreferencesReturnIndependentFallback()
        {
            var fallback=new GameSettingsData{localeCode="zh-Hans"};
            var actual=GameSettingsStorage.Read(path,fallback);actual.bindings[0].path="<Keyboard>/i";
            Assert.That(actual.localeCode,Is.EqualTo("zh-Hans"));Assert.That(fallback.bindings[0].path,Is.EqualTo("<Keyboard>/w"));
        }
        [Test] public void UnsupportedVersionsAreRejectedWithoutOverwritingTheFile()
        {
            const string source="{\"version\":99,\"localeCode\":\"en\"}";File.WriteAllText(path,source);
            Assert.Throws<InvalidDataException>(()=>GameSettingsStorage.Read(path,new GameSettingsData()));
            Assert.That(File.ReadAllText(path),Is.EqualTo(source));
        }
        [Test] public void ApplicationFallbackRecoversUnsupportedVersionWithoutChangingTheFileOrDefaults()
        {
            const string source="{\"version\":99,\"localeCode\":\"en\"}";
            File.WriteAllText(path,source);
            var fallback=new GameSettingsData{localeCode="zh-Hans",mouseSensitivity=1.4f};
            var recovered=GameSettingsStorage.ReadFallback(path,fallback,out var error);
            Assert.That(error,Is.TypeOf<InvalidDataException>());
            Assert.That(recovered.localeCode,Is.EqualTo("zh-Hans"));
            Assert.That(recovered.mouseSensitivity,Is.EqualTo(1.4f));
            recovered.bindings[0].path="<Keyboard>/i";
            Assert.That(fallback.bindings[0].path,Is.EqualTo("<Keyboard>/w"));
            Assert.That(File.ReadAllText(path),Is.EqualTo(source));
            Assert.That(File.Exists(path+".bak"),Is.False);
        }
        [Test] public void NumericValidationRejectsNonFiniteAndOutOfRangeValues()
        {
            var settings=new GameSettingsData{fieldOfView=float.NaN,bloomIntensity=float.PositiveInfinity,mouseSensitivity=-90,
                aimSmoothing=100,masterVolume=-1,sfxVolume=10,resolutionWidth=0,resolutionHeight=int.MaxValue,
                vSyncCount=90,targetFrameRate=10,localeCode="ZH-cn",fullscreenMode=999};
            settings.Validate();
            Assert.That(settings.fieldOfView,Is.EqualTo(65));Assert.That(settings.bloomIntensity,Is.EqualTo(.65f));
            Assert.That(settings.mouseSensitivity,Is.EqualTo(.2f));Assert.That(settings.aimSmoothing,Is.EqualTo(3));
            Assert.That(settings.masterVolume,Is.Zero);Assert.That(settings.sfxVolume,Is.EqualTo(1));
            Assert.That(settings.resolutionWidth,Is.EqualTo(640));Assert.That(settings.resolutionHeight,Is.EqualTo(8640));
            Assert.That(settings.vSyncCount,Is.EqualTo(2));Assert.That(settings.targetFrameRate,Is.EqualTo(30));
            Assert.That(settings.localeCode,Is.EqualTo("zh-Hans"));Assert.That(settings.fullscreenMode,Is.EqualTo(1));
        }
        [Test] public void BindingValidationRepairsMissingReservedAndUnknownControls()
        {
            var settings=new GameSettingsData{bindings=new[]{new GameBinding(GameAction.Forward,"<Keyboard>/escape"),
                new GameBinding(GameAction.Backward,"<Keyboard>/imaginaryKey"),new GameBinding(GameAction.Boost,"<Keyboard>/0")}};
            settings.Validate();
            Assert.That(settings.bindings.Length,Is.EqualTo(10));
            Assert.That(GameInput.Get(GameAction.Forward,settings).path,Is.EqualTo("<Keyboard>/w"));
            Assert.That(GameInput.Get(GameAction.Backward,settings).path,Is.EqualTo("<Keyboard>/s"));
            Assert.That(GameInput.Get(GameAction.Boost,settings).path,Is.EqualTo("<Keyboard>/0"));
            Assert.That(GameInput.IsSupported("<Keyboard>/999"),Is.False);
        }
        [Test] public void ConflictDetectionIncludesSecondaryModifierBindings()
        {
            var settings=new GameSettingsData();
            Assert.That(GameInput.Conflicts(settings,GameAction.Forward,"<Keyboard>/rightShift"),Is.EquivalentTo(new[]{GameAction.Boost}));
            Assert.That(GameInput.Conflicts(settings,GameAction.Boost,"<Keyboard>/rightShift"),Is.Empty);
            Assert.That(GameInput.Conflicts(settings,GameAction.Forward,"<Keyboard>/i"),Is.Empty);
            Assert.That(settings.bindings.Select(binding=>binding.action).Distinct().Count(),Is.EqualTo(10));
        }
        [Test] public void DraftCloneDoesNotModifyAppliedSettings()
        {
            var applied=new GameSettingsData();var draft=applied.Clone();
            draft.bindings[0].path="<Keyboard>/i";draft.bloomEnabled=false;
            Assert.That(applied.bindings[0].path,Is.EqualTo("<Keyboard>/w"));Assert.That(applied.bloomEnabled,Is.True);
        }
    }
}

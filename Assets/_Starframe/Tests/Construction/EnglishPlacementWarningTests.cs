using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;
using Starframe.Presentation;
using L10n=Starframe.Presentation.L10n;
namespace Starframe.Construction.Tests
{
    public sealed class EnglishPlacementWarningTests
    {
        [Test]public void EveryAuthoredPlacementFailureHasAnEnglishTableEntry()
        {
            var en=AssetDatabase.LoadAssetAtPath<StringTable>("Assets/_Starframe/Localization/GameUI_en.asset");
            var text=File.ReadAllText("Assets/_Starframe/Scripts/Runtime/Construction/ShipAssembly.cs");
            var messages=Regex.Matches(text,"Reject\\(\"([^\"]+)\"");Assert.That(messages.Count,Is.GreaterThan(10));
            foreach(Match m in messages)
            {
                string message=m.Groups[1].Value;
                string key=message=="镜像另一侧无法安装："?"error.mirrorOtherSide":L10n.LegacyKey(message);
                Assert.That(key,Is.Not.Null,"Untranslated placement warning: "+message);
                var entry=en.GetEntry(key);Assert.That(entry,Is.Not.Null,key);
                Assert.That(Regex.IsMatch(entry.Value,"[一-龥]"),Is.False,key+" contains Chinese in English table.");
            }
        }
        [Test]public void DynamicMirrorPrefixKeepsAPlaceForItsLocalizedReason()
        {
            var en=AssetDatabase.LoadAssetAtPath<StringTable>("Assets/_Starframe/Localization/GameUI_en.asset");
            string result=string.Format(en.GetEntry("error.mirrorOtherSide").Value,en.GetEntry("error.clearance").Value);
            StringAssert.Contains("mirrored side",result);StringAssert.Contains("occupied",result);
            Assert.That(Regex.IsMatch(result,"[一-龥]"),Is.False);
        }
        [Test]public void PlayerEnteredChineseNamesAreNotDomainWarnings()
        {Assert.That(L10n.LegacyKey("我的旗舰"),Is.Null);Assert.That(L10n.DomainMessage("我的旗舰"),Is.EqualTo("我的旗舰"));}
    }
}

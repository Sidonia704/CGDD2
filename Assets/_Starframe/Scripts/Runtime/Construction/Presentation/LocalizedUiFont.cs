// @role Apply localized fallback fonts to user-supplied UI text.
// @calls L10n

using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Starframe.Presentation
{
    /// <summary>Fonts are Addressable assets owned by Localization, never destroyed by a screen.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedUiFont : MonoBehaviour
    {
        TMP_Text target;
        [SerializeField] bool userContent;
        string lastText;
        void OnEnable() { target = GetComponent<TMP_Text>(); L10n.Changed+=ApplyForContent;ApplyForContent(); }
        void OnDisable() { L10n.Changed-=ApplyForContent; }
        public void EnableUserContent()
        {
            bool first=!userContent;userContent=true;
            if(!target)target=GetComponent<TMP_Text>();
            if(first||!target||lastText!=target.text)RefreshUserContent();
            else RefreshUserContent();
        }
        public void ApplyForContent(){if(userContent)EnableUserContent();else Apply(L10n.CurrentFont);}
        void LateUpdate(){if(userContent&&target&&target.text!=lastText)RefreshUserContent();}
        void RefreshUserContent()
        {
            if(!target)target=GetComponent<TMP_Text>();
            if(!target)return;
            lastText=target.text;
            bool cjk=false;
            foreach(char character in lastText??string.Empty)
                if((character>='\u3400'&&character<='\u9fff')||(character>='\u3000'&&character<='\u30ff')||(character>='\uff00'&&character<='\uffef')){cjk=true;break;}
            var locale=cjk?LocalizationSettings.AvailableLocales.GetLocale("zh-Hans"):null;
            Apply(L10n.FontForLocale(locale));
        }
        public void Apply(TMP_FontAsset font)
        {
            if (!target) target = GetComponent<TMP_Text>();
            if (!target || !font) return;
            target.font = font;
            // TMP's font asset setter also replaces the material with the matching atlas.
            // Do not compress Latin glyph widths to make translations fit.
            target.characterWidthAdjustment = 0;
            if (target.transform.parent is RectTransform parent)
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(parent);
        }
    }
}

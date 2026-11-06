// @role Resolve the shared Figma palette and Latin typography with a smaller Chinese fallback.
// @calls GameSettings, LocalizedUiFont
using TMPro;
using UnityEngine;
using Starframe.App;

namespace Starframe.Presentation
{
    public static class FigmaVisuals
    {
        static TMP_FontAsset body,bold,display;
        public static TMP_FontAsset BodyFont=>body?body:(body=Resources.Load<TMP_FontAsset>("UI/Fonts/SulphurPoint-Regular"));
        public static TMP_FontAsset BoldFont=>bold?bold:(bold=Resources.Load<TMP_FontAsset>("UI/Fonts/SulphurPoint-Bold"));
        public static TMP_FontAsset TitleFont=>display?display:(display=Resources.Load<TMP_FontAsset>("UI/Fonts/DolceVita"));
        public static Color Background=>GameSettings.Current.backgroundColor;
        public static readonly Color Panel=new Color32(27,31,45,255);
        public static readonly Color Accent=new Color32(185,255,3,255);
        public static readonly Color Orange=new Color32(216,80,34,255);
        public static void SetFont(TMP_Text text,bool bold=false,bool title=false)
        {
            if(!text)return;
            var style=text.GetComponent<FigmaFontStyle>();
            if(!style)style=text.gameObject.AddComponent<FigmaFontStyle>();
            style.bold=bold;style.title=title;
            var font=ResolveFont(text,text.font);
            if(font)text.font=font;
            text.fontStyle=FontStyles.Normal;
        }
        public static TMP_FontAsset ResolveFont(TMP_Text text,TMP_FontAsset fallback)
        {
            var style=text?text.GetComponent<FigmaFontStyle>():null;
            var selected=style&&style.title?TitleFont:style&&style.bold?BoldFont:BodyFont;
            return selected?selected:fallback;
        }
    }
    public sealed class FigmaFontStyle:MonoBehaviour
    {
        public bool bold,title;
    }
}

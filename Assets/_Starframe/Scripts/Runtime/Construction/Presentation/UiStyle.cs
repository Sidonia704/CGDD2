// @role Share readable typography, safe canvas sizing and control colours across game screens.
// @calls GameSettings, GlassUiPalette
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Presentation
{
    public static class UiStyle
    {
        public const float ControlHeight=48, BodySize=18, CaptionSize=14, HeadingSize=28;
        public static void ApplyScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        }
        public static void ApplyText(TMP_Text label,float size)
        {
            var binding=label.GetComponent<UiTextScale>();
            if(!binding)binding=label.gameObject.AddComponent<UiTextScale>();
            binding.Configure(label,size);
        }
        public static ColorBlock ControlColors()
        {
            var result=ColorBlock.defaultColorBlock;
            result.normalColor=Color.white;result.highlightedColor=new Color(1.22f,1.25f,1.25f,1);
            result.selectedColor=result.highlightedColor;result.pressedColor=new Color(.75f,.84f,.85f,1);
            result.disabledColor=new Color(.65f,.67f,.68f,.48f);result.fadeDuration=.10f;return result;
        }
        public static void FitSafePlane(RectTransform root)
        {
            var parent=root.parent as RectTransform;if(!parent)return;
            root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);root.pivot=new Vector2(.5f,.5f);
            root.anchoredPosition=Vector2.zero;root.sizeDelta=new Vector2(1920,1080);
            root.localScale=Vector3.one*Mathf.Min(parent.rect.width/1920,parent.rect.height/1080);
        }
    }
}

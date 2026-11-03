// @role Apply the user's readable text size with bounded fitting inside existing controls.
// @calls GameSettings
using TMPro;
using UnityEngine;
using Starframe.App;

namespace Starframe.Presentation
{
    [DisallowMultipleComponent]
    public sealed class UiTextScale:MonoBehaviour
    {
        TMP_Text label;float baseSize;
        public void Configure(TMP_Text target,float size){label=target;baseSize=size;Apply();}
        void OnEnable()=>GameSettings.Changed+=Apply;
        void OnDisable()=>GameSettings.Changed-=Apply;
        void Apply()
        {
            if(!label||baseSize<=0)return;
            float scale=GameSettings.Current.uiScale;
            label.fontSize=baseSize*scale;label.fontSizeMin=baseSize*Mathf.Min(1,scale);
            label.fontSizeMax=baseSize*scale;
            label.enableAutoSizing=Mathf.Abs(scale-1)>.001f;
        }
    }
}

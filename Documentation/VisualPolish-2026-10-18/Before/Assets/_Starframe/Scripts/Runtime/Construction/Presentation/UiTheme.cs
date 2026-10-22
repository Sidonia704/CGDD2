using UnityEngine;

namespace Starframe.Presentation
{
    [CreateAssetMenu(menuName="Starframe/UI/Design Theme")]
    public sealed class UiTheme:ScriptableObject
    {
        const string ResourcePath="UI/WorkbenchTheme";
        static UiTheme current;
        public static UiTheme Current
        {
            get
            {
                if(!current)current=Resources.Load<UiTheme>(ResourcePath);
                if(!current){current=CreateInstance<UiTheme>();current.hideFlags=HideFlags.HideAndDontSave;}
                return current;
            }
        }
        [Header("分组开关：关闭任意一项，单独恢复本轮之前的设计")]
        [InspectorName("统一玻璃与配色")]public bool refinedGlass=true;
        [InspectorName("装配布局与内容")]public bool assemblyLayout=true;
        [InspectorName("飞行布局与内容")]public bool flightLayout=true;
        [InspectorName("准星、预瞄与目标指示器")]public bool aimReticle=true;
        [InspectorName("平面战术雷达")]public bool tacticalRadar=true;
        [InspectorName("船体状态图")]public bool statusSchematic=true;
        [Header("基础色：未来首页、设置与内页共用")]
        public Color glassTint=new Color(.075f,.105f,.125f,.32f);
        public Color dialogTint=new Color(.055f,.075f,.09f,.82f);
        public Color buttonTint=new Color(.045f,.07f,.085f,.6f);
        public Color text=new Color(.92f,.955f,.96f,1);
        public Color muted=new Color(.57f,.68f,.72f,1);
        public Color accent=new Color(.56f,.86f,.84f,1);
        public Color amber=new Color(1,.76f,.43f,1);
        public Color danger=new Color(1,.35f,.30f,1);
        public Color success=new Color(.55f,.84f,.70f,1);
        [Header("磨砂玻璃")]
        [Range(4,36)]public float blurRadius=24;
        [Range(0,.03f)]public float grainStrength=.0015f;
        [Range(0,.05f)]public float sheenStrength=.018f;
        [Header("武器命中反馈：实时可调")]
        [InspectorName("启用命中反馈")]public bool hitFeedbackEnabled=true;
        [InspectorName("每秒最大反馈次数"),Range(1,20)]public float hitFeedbackMaxRate=8;
        [InspectorName("反馈时长（秒）"),Range(.12f,.6f)]public float hitFeedbackDuration=.24f;
        [InspectorName("未命中十字间距（像素）"),Range(3,24)]public float reticleIdleGap=11;
        [InspectorName("命中十字间距（像素）"),Range(0,16)]public float reticleHitGap=3;
        [InspectorName("十字臂长度（像素）"),Range(5,20)]public float reticleArmLength=10;
        [InspectorName("命中线段最大长度（像素）"),Range(5,24)]public float hitMarkerLength=12;
        [InspectorName("命中反馈颜色")]public Color hitMarkerColor=new Color(1,.27f,.19f,1);
        [TextArea(2,4)]public string usage="更改布局开关后重新进入对应页面即可生效。关闭某项只恢复对应 UI 外观；不会修改游戏参数或蓝图。";
        public void SetAll(bool enabled){refinedGlass=assemblyLayout=flightLayout=aimReticle=tacticalRadar=statusSchematic=enabled;}
        public static void Reload()=>current=null;
    }
}

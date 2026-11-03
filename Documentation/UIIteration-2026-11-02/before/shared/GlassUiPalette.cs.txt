// @role Provide shared UI colors, materials, glass effects and text setup.
// @calls UiTheme

using UnityEngine;
namespace Starframe.Flight
{
    public static class GlassUiPalette
    {
        static Starframe.Presentation.UiTheme Theme=>Starframe.Presentation.UiTheme.Current;
        public static Color Glass=>Theme.refinedGlass?Theme.glassTint:new Color(.105f,.125f,.155f,.24f);
        public static Color Dialog=>Theme.refinedGlass?Theme.dialogTint:Glass;
        public static Color Button=>Theme.refinedGlass?Theme.buttonTint:new Color(.07f,.09f,.115f,.4f);
        public static Color Ink=>Theme.refinedGlass?Theme.text:new Color(.94f,.97f,1,1);
        public static Color Muted=>Theme.refinedGlass?Theme.muted:new Color(.67f,.76f,.86f,1);
        public static Color Accent=>Theme.refinedGlass?Theme.accent:new Color(.48f,.82f,1,1);
        public static Color Action
        {
            get {if(!Theme.refinedGlass||!Theme.frostedFinish)return Accent;var color=Color.Lerp(Button,Accent,.19f);color.a=.94f;return color;}
        }
        public static Color ActionInk=>Theme.refinedGlass&&Theme.frostedFinish?Ink:new Color(.045f,.075f,.08f,1);
        public static void Apply(Material material,Color? tint=null)
        {
            material.SetColor("_Tint",tint??Glass);material.SetFloat("_BlurRadius",Theme.refinedGlass?Theme.blurRadius:18);
            material.SetFloat("_GrainStrength",Theme.refinedGlass?Theme.grainStrength:.002f);
            material.SetFloat("_SheenStrength",Theme.refinedGlass?Theme.sheenStrength:.012f);
            material.SetFloat("_FinishStrength",Theme.refinedGlass&&Theme.frostedFinish?1:0);
            material.SetFloat("_BackdropSaturation",Theme.backdropSaturation);
            material.SetFloat("_BackdropTransmission",Theme.backdropTransmission);
            material.SetFloat("_RefractionPixels",Theme.refractionPixels);
        }
    }
}

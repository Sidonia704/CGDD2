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
        public static void Apply(Material material,Color? tint=null)
        {
            material.SetColor("_Tint",tint??Glass);material.SetFloat("_BlurRadius",Theme.refinedGlass?Theme.blurRadius:18);
            material.SetFloat("_GrainStrength",Theme.refinedGlass?Theme.grainStrength:.002f);
            material.SetFloat("_SheenStrength",Theme.refinedGlass?Theme.sheenStrength:.012f);
        }
    }
}

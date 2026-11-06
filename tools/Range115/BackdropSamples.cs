using Starframe.EditorTools;
public static class BackdropSamples
{
 public static string Optimize()=>UniformBackdropBuilder.OptimizeRenderMeshes();
 public static string Lods()=>UniformBackdropBuilder.BuildDisplayLods();
 public static object Run(){return new[]{UniformBackdropBuilder.BuildOne(0),UniformBackdropBuilder.BuildOne(15)};}
}

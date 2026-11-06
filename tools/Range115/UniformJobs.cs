using Starframe.EditorTools;
public static class UniformJobs
{
 public static object Samples()=>UniformMapBuilder.BeginSamples();
 public static object Apply()=>UniformMapBuilder.BeginApply();
 public static object Status()=>UniformMapBuilder.Status();
 public static object Retry()=>UniformMapBuilder.RetryErrors();
}

using System.ComponentModel;
using Microsoft.SemanticKernel;

public sealed class LocalTimePlugin
{
  [KernelFunction, Description("Retrieves the current time in Local Time.")]
  public static String GetCurrentLocalTime()
  {
    return "The current local time now is :" + DateTime.Now.ToString("HH:mm:ss");
  }
}
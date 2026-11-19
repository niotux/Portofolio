namespace Holodeck.Web.Computers;

// What storing a sudo grant needs next: nothing while the environment is stopped, restarting the process when the
// supervisor took it, or recreating an environment whose container predates sudo.
public enum SudoChange
{
  Saved,
  Applied,
  RecreateEnvironment
}

namespace PrimordialPower.Platform;

[Flags]
public enum ViewportFlags
{
  Standard = 0 << 0,
  WithShutdownEvents = 1 << 0,

  WithBorders = 0 << 1,
  NoBorders = 1 << 1,

  Resizeable = 0 << 2,
  FixedSize = 1 << 2,

  Visible = 0 << 3,
  Hidden = 1 << 3,

  MainWindow = WithShutdownEvents,
  UtilityWindow = Hidden | FixedSize | NoBorders
}

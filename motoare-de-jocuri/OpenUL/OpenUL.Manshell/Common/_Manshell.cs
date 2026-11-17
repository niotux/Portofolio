using System.Numerics;

namespace OpenUL.Manshell.Common;

public static class ManshellLib
{
  public static IReadOnlyList<ManshellCmd> Common { get; } =
  [
    new HelpCmd(),
    new CallCmd(),
    new ClearCmd(),
    new ExitCmd(),
    new ExeCmd(),
    new CmdCmd(),

    new EqualsCmd(),

    new GetCmd(),
    new Get_AtCmd(),

    new SetCmd(),
    new Set_AtCmd(),

    new NewCmd(),
    new New_ArrayCmd(),

    new Read_InCmd(),
    new Write_OutCmd(),
  ];

  public static IReadOnlyList<ManshellCmd> Runtime { get; } =
  [
    new Get_TypeCmd(),
    new Get_BaseCmd(),

    new List_AssembliesCmd(),
    new List_MembersCmd(),
    new List_TypesCmd(),
    new List_VarsCmd(),
    new List_BasesCmd(),

    new Load_AssemblyCmd(),
    new Load_CmdCmd(),
    new Load_CmdListCmd(),

    new Is_TypeCmd(),
  ];

  public static IReadOnlyList<ManshellCmd> IO { get; } =
  [
    new Get_DirCmd(),
    new Set_DirCmd(),

    new List_DirCmd(),

    new Read_FileCmd(),
    new Write_FileCmd(),
  ];
}
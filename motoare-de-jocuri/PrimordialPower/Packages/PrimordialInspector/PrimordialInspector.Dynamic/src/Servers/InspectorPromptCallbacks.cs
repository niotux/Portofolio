using PrettyPrompt;
using PrettyPrompt.Highlighting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace PrimordialInspector.Scripting.Servers;

internal class InspectorPromptCallbacks : PromptCallbacks
{
  protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text, CancellationToken cancellationToken)
  {
    var highlights = new List<FormatSpan>();

    try
    {
      var syntaxTree = CSharpSyntaxTree.ParseText(text);
      var root = syntaxTree.GetRoot();

      foreach (var token in root.DescendantTokens())
      {
        var span = token.Span;
        var length = span.Length;
        var start = span.Start;

        if (length == 0) continue;

        var color = token.Kind() switch
        {
          // Action Keywords
          SyntaxKind.IfKeyword or
          SyntaxKind.ElseKeyword or
          SyntaxKind.ForKeyword or
          SyntaxKind.ForEachKeyword or
          SyntaxKind.WhileKeyword or
          SyntaxKind.DoKeyword or
          SyntaxKind.SwitchKeyword or
          SyntaxKind.CaseKeyword or
          SyntaxKind.ReturnKeyword or
          SyntaxKind.BreakKeyword or
          SyntaxKind.ContinueKeyword or
          SyntaxKind.GotoKeyword or
          SyntaxKind.TryKeyword or
          SyntaxKind.CatchKeyword or
          SyntaxKind.FinallyKeyword or
          SyntaxKind.ThrowKeyword or
          SyntaxKind.AwaitKeyword
            => AnsiColor.BrightMagenta,

          // Keywords
          SyntaxKind.VarKeyword or
          SyntaxKind.NewKeyword or
          SyntaxKind.ThisKeyword or
          SyntaxKind.BaseKeyword or
          SyntaxKind.UsingKeyword or
          SyntaxKind.NamespaceKeyword or
          SyntaxKind.ClassKeyword or
          SyntaxKind.StructKeyword or
          SyntaxKind.InterfaceKeyword or
          SyntaxKind.EnumKeyword or
          SyntaxKind.DelegateKeyword or
          SyntaxKind.PublicKeyword or
          SyntaxKind.PrivateKeyword or
          SyntaxKind.ProtectedKeyword or
          SyntaxKind.InternalKeyword or
          SyntaxKind.StaticKeyword or
          SyntaxKind.ReadOnlyKeyword or
          SyntaxKind.ConstKeyword or
          SyntaxKind.VirtualKeyword or
          SyntaxKind.OverrideKeyword or
          SyntaxKind.AbstractKeyword or
          SyntaxKind.SealedKeyword or
          SyntaxKind.AsyncKeyword or
          SyntaxKind.InKeyword or
          SyntaxKind.OutKeyword or
          SyntaxKind.RefKeyword or
          SyntaxKind.ParamsKeyword or
          SyntaxKind.IsKeyword or
          SyntaxKind.AsKeyword or
          SyntaxKind.TypeOfKeyword or
          SyntaxKind.SizeOfKeyword or
          SyntaxKind.DefaultKeyword or
          SyntaxKind.NullKeyword or
          SyntaxKind.TrueKeyword or
          SyntaxKind.FalseKeyword
            => AnsiColor.Blue,

          // Types (built-in)
          SyntaxKind.IntKeyword or
          SyntaxKind.LongKeyword or
          SyntaxKind.ShortKeyword or
          SyntaxKind.ByteKeyword or
          SyntaxKind.SByteKeyword or
          SyntaxKind.UIntKeyword or
          SyntaxKind.ULongKeyword or
          SyntaxKind.UShortKeyword or
          SyntaxKind.FloatKeyword or
          SyntaxKind.DoubleKeyword or
          SyntaxKind.DecimalKeyword or
          SyntaxKind.CharKeyword or
          SyntaxKind.BoolKeyword or
          SyntaxKind.StringKeyword or
          SyntaxKind.ObjectKeyword or
          SyntaxKind.VoidKeyword
            => AnsiColor.Blue,

          // Strings
          SyntaxKind.StringLiteralToken or
          SyntaxKind.CharacterLiteralToken or
          SyntaxKind.InterpolatedStringStartToken or
          SyntaxKind.InterpolatedStringEndToken or
          SyntaxKind.InterpolatedStringTextToken
            => AnsiColor.BrightRed,

          // Numbers
          SyntaxKind.NumericLiteralToken
            => AnsiColor.BrightYellow,

          // Comments
          _ when token.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                 token.IsKind(SyntaxKind.MultiLineCommentTrivia)
            => AnsiColor.BrightBlack,

          _ => (AnsiColor?)null
        };

        if (color.HasValue)
        {
          highlights.Add(new FormatSpan(start, length, new ConsoleFormat(Foreground: color.Value)));
        }
      }

      // Highlight identifiers that look like types (PascalCase)
      foreach (var token in root.DescendantTokens())
      {
        if (token.IsKind(SyntaxKind.IdentifierToken))
        {
          var text_token = token.Text;
          if (text_token.Length > 0 && char.IsUpper(text_token[0]))
          {
            highlights.Add(new FormatSpan(token.Span.Start, token.Span.Length,
              new ConsoleFormat(Foreground: AnsiColor.BrightGreen)));
          }
        }
      }
    }
    catch
    {
      // If parsing fails, return no highlights
    }

    return Task.FromResult<IReadOnlyCollection<FormatSpan>>(highlights);
  }
}

using System;

namespace PrimordialPower.Core;


public enum KeyState : byte
{
  Inactive,
  Active
}

/// <summary>
/// Keyboard map with all the key states.
/// </summary>
public class Keyboard
{
  // Letters
  private volatile bool _A = false;
  public bool A
  {
    get => _A;
    internal set => _A = value;
  }
  public volatile KeyState B = KeyState.Inactive;
  public volatile KeyState C = KeyState.Inactive;
  public volatile KeyState D = KeyState.Inactive;
  public volatile KeyState E = KeyState.Inactive;
  public volatile KeyState F = KeyState.Inactive;
  public volatile KeyState G = KeyState.Inactive;
  public volatile KeyState H = KeyState.Inactive;
  public volatile KeyState I = KeyState.Inactive;
  public volatile KeyState J = KeyState.Inactive;
  public volatile KeyState K = KeyState.Inactive;
  public volatile KeyState L = KeyState.Inactive;
  public volatile KeyState M = KeyState.Inactive;
  public volatile KeyState N = KeyState.Inactive;
  public volatile KeyState O = KeyState.Inactive;
  public volatile KeyState P = KeyState.Inactive;
  public volatile KeyState Q = KeyState.Inactive;
  public volatile KeyState R = KeyState.Inactive;
  public volatile KeyState S = KeyState.Inactive;
  public volatile KeyState T = KeyState.Inactive;
  public volatile KeyState U = KeyState.Inactive;
  public volatile KeyState V = KeyState.Inactive;
  public volatile KeyState W = KeyState.Inactive;
  public volatile KeyState X = KeyState.Inactive;
  public volatile KeyState Y = KeyState.Inactive;
  public volatile KeyState Z = KeyState.Inactive;

  // Symbols

  /// <summary>
  /// -
  /// </summary>
  public volatile KeyState Minus = KeyState.Inactive;

  /// <summary>
  /// _
  /// </summary>
  public volatile KeyState Underscore = KeyState.Inactive;

  /// <summary>
  /// =
  /// </summary>
  public volatile KeyState Equal = KeyState.Inactive;

  /// <summary>
  /// +
  /// </summary>
  public volatile KeyState Plus = KeyState.Inactive;

  /// <summary>
  /// [
  /// </summary>
  public volatile KeyState LSqrBracket = KeyState.Inactive;

  /// <summary>
  /// ]
  /// </summary>
  public volatile KeyState RSqrBracket = KeyState.Inactive;

  /// <summary>
  /// (
  /// </summary>
  public volatile KeyState LBracket = KeyState.Inactive;

  /// <summary>
  /// )
  /// </summary>
  public volatile KeyState RBracket = KeyState.Inactive;

  /// <summary>
  /// {
  /// </summary>
  public volatile KeyState LCurlyBracket = KeyState.Inactive;

  /// <summary>
  /// }
  /// </summary>
  public volatile KeyState RCurlyBracket = KeyState.Inactive;

  /// <summary>
  /// ;
  /// </summary>
  public volatile KeyState Semicolon = KeyState.Inactive;

  /// <summary>
  /// :
  /// </summary>
  public volatile KeyState Colon = KeyState.Inactive;

  /// <summary>
  /// '
  /// </summary>
  public volatile KeyState Apostrophe = KeyState.Inactive;

  /// <summary>
  /// "
  /// </summary>
  public volatile KeyState QuotMark = KeyState.Inactive;

  /// <summary>
  /// ,
  /// </summary>
  public volatile KeyState Comma = KeyState.Inactive;

  /// <summary>
  /// .
  /// </summary>
  public volatile KeyState Period = KeyState.Inactive;

  /// <summary>
  /// <
  /// </summary>
  public volatile KeyState SmallerThan = KeyState.Inactive;

  /// <summary>
  /// >
  /// </summary>
  public volatile KeyState BiggerThan = KeyState.Inactive;

  /// <summary>
  /// /
  /// </summary>
  public volatile KeyState Slash = KeyState.Inactive;

  /// <summary>
  /// ?
  /// </summary>
  public volatile KeyState QuestionMark = KeyState.Inactive;

  /// <summary>
  /// \
  /// </summary>
  public volatile KeyState Backslash = KeyState.Inactive;

  /// <summary>
  /// |
  /// </summary>
  public volatile KeyState Pipe = KeyState.Inactive;

  /// <summary>
  /// `
  /// </summary>
  public volatile KeyState Backtick = KeyState.Inactive;

  /// <summary>
  /// ~
  /// </summary>
  public volatile KeyState Tilde = KeyState.Inactive;

  /// <summary>
  /// !
  /// </summary>
  public volatile KeyState ExclMark = KeyState.Inactive;

  /// <summary>
  /// @
  /// </summary>
  public volatile KeyState At = KeyState.Inactive;

  /// <summary>
  /// #
  /// </summary>
  public volatile KeyState Hashtag = KeyState.Inactive;

  /// <summary>
  /// $
  /// </summary>
  public volatile KeyState Dollar = KeyState.Inactive;

  /// <summary>
  /// %
  /// </summary>
  public volatile KeyState Percent = KeyState.Inactive;

  /// <summary>
  /// ^
  /// </summary>
  public volatile KeyState Caret = KeyState.Inactive;

  /// <summary>
  /// &
  /// </summary>
  public volatile KeyState Ampersand = KeyState.Inactive;

  /// <summary>
  /// *
  /// </summary>
  public volatile KeyState Asterix = KeyState.Inactive;

  // Numbers
  public volatile KeyState D0 = KeyState.Inactive;
  public volatile KeyState D1 = KeyState.Inactive;
  public volatile KeyState D2 = KeyState.Inactive;
  public volatile KeyState D3 = KeyState.Inactive;
  public volatile KeyState D4 = KeyState.Inactive;
  public volatile KeyState D5 = KeyState.Inactive;
  public volatile KeyState D6 = KeyState.Inactive;
  public volatile KeyState D7 = KeyState.Inactive;
  public volatile KeyState D8 = KeyState.Inactive;
  public volatile KeyState D9 = KeyState.Inactive;

  // Function Keys
  public volatile KeyState F1 = KeyState.Inactive;
  public volatile KeyState F2 = KeyState.Inactive;
  public volatile KeyState F3 = KeyState.Inactive;
  public volatile KeyState F4 = KeyState.Inactive;
  public volatile KeyState F5 = KeyState.Inactive;
  public volatile KeyState F6 = KeyState.Inactive;
  public volatile KeyState F7 = KeyState.Inactive;
  public volatile KeyState F8 = KeyState.Inactive;
  public volatile KeyState F9 = KeyState.Inactive;
  public volatile KeyState F10 = KeyState.Inactive;
  public volatile KeyState F11 = KeyState.Inactive;
  public volatile KeyState F12 = KeyState.Inactive;

  // Special Keys
  public volatile KeyState SuperKey = KeyState.Inactive;
  public volatile KeyState WindowsKey = KeyState.Inactive;
  public volatile KeyState Space = KeyState.Inactive;
  public volatile KeyState Enter = KeyState.Inactive;
  public volatile KeyState Escape = KeyState.Inactive;
  public volatile KeyState CapsLock = KeyState.Inactive;
  public volatile KeyState ScrollLock = KeyState.Inactive;
  public volatile KeyState Tab = KeyState.Inactive;
  public volatile KeyState Backspace = KeyState.Inactive;
  public volatile KeyState Delete = KeyState.Inactive;
  public volatile KeyState Insert = KeyState.Inactive;
  public volatile KeyState Home = KeyState.Inactive;
  public volatile KeyState End = KeyState.Inactive;
  public volatile KeyState PageUp = KeyState.Inactive;
  public volatile KeyState PageDn = KeyState.Inactive;
  public volatile KeyState PrintScreen = KeyState.Inactive;
  public volatile KeyState Pause = KeyState.Inactive;

  // Arrow Keys
  public volatile KeyState Up = KeyState.Inactive;
  public volatile KeyState Down = KeyState.Inactive;
  public volatile KeyState Left = KeyState.Inactive;
  public volatile KeyState Right = KeyState.Inactive;

  // Modifiers
  public volatile KeyState LShift = KeyState.Inactive;
  public volatile KeyState RShift = KeyState.Inactive;
  public volatile KeyState LCtrl = KeyState.Inactive;
  public volatile KeyState RCtrl = KeyState.Inactive;
  public volatile KeyState LAlt = KeyState.Inactive;
  public volatile KeyState RAlt = KeyState.Inactive;
}
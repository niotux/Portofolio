using System.Collections.Concurrent;
using System.Reflection;

namespace PrimordialPower.Core.Win32;

public class Win32InputSystem : IInputSystem
{

  private readonly ConcurrentDictionary<IViewport, InputContext> _OpenContexts = new();
  public InputContext CreateInputContext(IViewport viewportContext)
  {
    var inputContext = new InputContext(viewportContext);
    _OpenContexts[viewportContext] = inputContext;
    return inputContext;
  }



  private volatile bool _ShouldClose = false;
  public Task Run(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested && !_ShouldClose)
    {
      foreach (var context in _OpenContexts)
      {
        context.Value.InputEvents();
      }
    }
    return Task.CompletedTask;
  }

  public Task SignalShutdown()
  {
    _ShouldClose = true;
    return Task.CompletedTask;
  }
}
// public class Win32InputSystem : IInputSystem
// {
//   public Win32InputSystem()
//   {

//   }


//   private bool _ShouldExit = false;
//   public Task Run(CancellationToken cancellationToken)
//   {
//     while (!_ShouldExit && !cancellationToken.IsCancellationRequested)
//     {
//       _UpdateKeyboard();
//     }
//     return Task.CompletedTask;
//   }


//   public Task SignalShutdown()
//   {
//     return Task.CompletedTask;
//   }



//   public Keyboard Keyboard { get; } = new();

//   private static byte[] _CurrentKeyState = new byte[256];

//   private void _UpdateKeyboard()
//   {
//     API.GetKeyboardState(_CurrentKeyState);

//     // Letters
//     _UpdateKey(ref this.Keyboard.A, API.VK_A);
//     _UpdateKey(ref this.Keyboard.B, API.VK_B);
//     _UpdateKey(ref this.Keyboard.C, API.VK_C);
//     _UpdateKey(ref this.Keyboard.D, API.VK_D);
//     _UpdateKey(ref this.Keyboard.E, API.VK_E);
//     _UpdateKey(ref this.Keyboard.F, API.VK_F);
//     _UpdateKey(ref this.Keyboard.G, API.VK_G);
//     _UpdateKey(ref this.Keyboard.H, API.VK_H);
//     _UpdateKey(ref this.Keyboard.I, API.VK_I);
//     _UpdateKey(ref this.Keyboard.J, API.VK_J);
//     _UpdateKey(ref this.Keyboard.K, API.VK_K);
//     _UpdateKey(ref this.Keyboard.L, API.VK_L);
//     _UpdateKey(ref this.Keyboard.M, API.VK_M);
//     _UpdateKey(ref this.Keyboard.N, API.VK_N);
//     _UpdateKey(ref this.Keyboard.O, API.VK_O);
//     _UpdateKey(ref this.Keyboard.P, API.VK_P);
//     _UpdateKey(ref this.Keyboard.Q, API.VK_Q);
//     _UpdateKey(ref this.Keyboard.R, API.VK_R);
//     _UpdateKey(ref this.Keyboard.S, API.VK_S);
//     _UpdateKey(ref this.Keyboard.T, API.VK_T);
//     _UpdateKey(ref this.Keyboard.U, API.VK_U);
//     _UpdateKey(ref this.Keyboard.V, API.VK_V);
//     _UpdateKey(ref this.Keyboard.W, API.VK_W);
//     _UpdateKey(ref this.Keyboard.X, API.VK_X);
//     _UpdateKey(ref this.Keyboard.Y, API.VK_Y);
//     _UpdateKey(ref this.Keyboard.Z, API.VK_Z);

//     // Numbers
//     _UpdateKey(ref this.Keyboard.D0, API.VK_0);
//     _UpdateKey(ref this.Keyboard.D1, API.VK_1);
//     _UpdateKey(ref this.Keyboard.D2, API.VK_2);
//     _UpdateKey(ref this.Keyboard.D3, API.VK_3);
//     _UpdateKey(ref this.Keyboard.D4, API.VK_4);
//     _UpdateKey(ref this.Keyboard.D5, API.VK_5);
//     _UpdateKey(ref this.Keyboard.D6, API.VK_6);
//     _UpdateKey(ref this.Keyboard.D7, API.VK_7);
//     _UpdateKey(ref this.Keyboard.D8, API.VK_8);
//     _UpdateKey(ref this.Keyboard.D9, API.VK_9);

//     // Function Keys
//     _UpdateKey(ref this.Keyboard.F1, API.VK_F1);
//     _UpdateKey(ref this.Keyboard.F2, API.VK_F2);
//     _UpdateKey(ref this.Keyboard.F3, API.VK_F3);
//     _UpdateKey(ref this.Keyboard.F4, API.VK_F4);
//     _UpdateKey(ref this.Keyboard.F5, API.VK_F5);
//     _UpdateKey(ref this.Keyboard.F6, API.VK_F6);
//     _UpdateKey(ref this.Keyboard.F7, API.VK_F7);
//     _UpdateKey(ref this.Keyboard.F8, API.VK_F8);
//     _UpdateKey(ref this.Keyboard.F9, API.VK_F9);
//     _UpdateKey(ref this.Keyboard.F10, API.VK_F10);
//     _UpdateKey(ref this.Keyboard.F11, API.VK_F11);
//     _UpdateKey(ref this.Keyboard.F12, API.VK_F12);

//     // Special Keys
//     _UpdateKey(ref this.Keyboard.Space, API.VK_SPACE);
//     _UpdateKey(ref this.Keyboard.Enter, API.VK_RETURN);
//     _UpdateKey(ref this.Keyboard.Escape, API.VK_ESCAPE);
//     _UpdateKey(ref this.Keyboard.Tab, API.VK_TAB);
//     _UpdateKey(ref this.Keyboard.Backspace, API.VK_BACK);
//     _UpdateKey(ref this.Keyboard.Delete, API.VK_DELETE);
//     _UpdateKey(ref this.Keyboard.Insert, API.VK_INSERT);
//     _UpdateKey(ref this.Keyboard.Home, API.VK_HOME);
//     _UpdateKey(ref this.Keyboard.End, API.VK_END);
//     _UpdateKey(ref this.Keyboard.PageUp, API.VK_PRIOR);
//     _UpdateKey(ref this.Keyboard.PageDn, API.VK_NEXT);
//     _UpdateKey(ref this.Keyboard.PrintScreen, API.VK_SNAPSHOT);
//     _UpdateKey(ref this.Keyboard.Pause, API.VK_PAUSE);
//     _UpdateKey(ref this.Keyboard.CapsLock, API.VK_CAPITAL);
//     _UpdateKey(ref this.Keyboard.ScrollLock, API.VK_SCROLL);

//     // Arrow Keys
//     _UpdateKey(ref this.Keyboard.Up, API.VK_UP);
//     _UpdateKey(ref this.Keyboard.Down, API.VK_DOWN);
//     _UpdateKey(ref this.Keyboard.Left, API.VK_LEFT);
//     _UpdateKey(ref this.Keyboard.Right, API.VK_RIGHT);

//     // Modifiers
//     _UpdateKey(ref this.Keyboard.LShift, API.VK_LSHIFT);
//     _UpdateKey(ref this.Keyboard.RShift, API.VK_RSHIFT);
//     _UpdateKey(ref this.Keyboard.LCtrl, API.VK_LCONTROL);
//     _UpdateKey(ref this.Keyboard.RCtrl, API.VK_RCONTROL);
//     _UpdateKey(ref this.Keyboard.LAlt, API.VK_LMENU);
//     _UpdateKey(ref this.Keyboard.RAlt, API.VK_RMENU);
//     _UpdateKey(ref this.Keyboard.WindowsKey, API.VK_LWIN);
//     _UpdateKey(ref this.Keyboard.SuperKey, API.VK_LWIN);

//     // Symbol Keys
//     _UpdateKey(ref this.Keyboard.Minus, API.VK_OEM_MINUS);
//     _UpdateKey(ref this.Keyboard.Equal, API.VK_OEM_PLUS);
//     _UpdateKey(ref this.Keyboard.LSqrBracket, API.VK_OEM_4);
//     _UpdateKey(ref this.Keyboard.RSqrBracket, API.VK_OEM_6);
//     _UpdateKey(ref this.Keyboard.Semicolon, API.VK_OEM_1);
//     _UpdateKey(ref this.Keyboard.Apostrophe, API.VK_OEM_7);
//     _UpdateKey(ref this.Keyboard.Comma, API.VK_OEM_COMMA);
//     _UpdateKey(ref this.Keyboard.Period, API.VK_OEM_PERIOD);
//     _UpdateKey(ref this.Keyboard.Slash, API.VK_OEM_2);
//     _UpdateKey(ref this.Keyboard.Backslash, API.VK_OEM_5);
//     _UpdateKey(ref this.Keyboard.Backtick, API.VK_OEM_3);
//   }

//   private void _UpdateKey(ref KeyState key, int keyCode)
//   {
//     if (!((_CurrentKeyState[keyCode] & 0x80) != 0))
//       key = KeyState.Inactive;
//     else
//       key = KeyState.Active;
//   }


//   public Mouse Mouse { get; } = new();
// }

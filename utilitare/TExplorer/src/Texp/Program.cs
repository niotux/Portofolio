using System;
using System.IO;
using Texp.Commands;
using Texp.Core;
using Texp.Ui;
using Terminal.Gui.App;

// texp [folder]: opens in the given folder, else the current one.
string start = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
Explorer explorer = new(Command.DiscoverAll(), start);

IApplication app = Application.Create();
app.Init();
explorer.Post = action => app.Invoke(action);
ExplorerView view = new(explorer);
app.Keyboard.KeyDown += view.OnAppKeyDown;
app.AddTimeout(TimeSpan.FromMilliseconds(500), view.Tick);
app.Run(view, null);

view.Dispose();
app.Dispose();

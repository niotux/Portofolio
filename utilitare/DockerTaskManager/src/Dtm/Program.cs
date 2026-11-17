using System;
using Dtm.Docker;
using Dtm.Ui;
using Terminal.Gui.App;

DockerCli cli = new("docker");
Fleet fleet = new(cli);
fleet.Start();

IApplication app = Application.Create();
app.Init();
DashboardView view = new(fleet);
app.Keyboard.KeyDown += view.OnAppKeyDown;
app.AddTimeout(TimeSpan.FromMilliseconds(500), view.Tick);
app.Run(view, null);

fleet.Stop();
view.Dispose();
app.Dispose();

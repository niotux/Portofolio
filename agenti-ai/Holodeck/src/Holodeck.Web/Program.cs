using Holodeck.Web;

WebApplication app = HolodeckApp.Build(args);
await HolodeckApp.InitializeAsync(app);
app.Run();

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

// Desktop toolkit fixture: one window, a text box, a button and a label the button updates.
public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var input = new TextBox { Name = "Input", PlaceholderText = "Your name" };
            var result = new TextBlock { Name = "Result", Text = "Not clicked" };
            var button = new Button { Name = "Greet", Content = "Greet" };
            var clicks = 0;
            button.Click += (_, _) => result.Text = $"Hello {input.Text} ({++clicks})";
            desktop.MainWindow = new Window
            {
                Title = "Toolkit Fixture",
                Width = 420,
                Height = 220,
                Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { input, button, result } },
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

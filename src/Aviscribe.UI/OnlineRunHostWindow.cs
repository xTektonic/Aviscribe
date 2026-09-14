using Avalonia.Controls;
using Avalonia.Platform;

namespace Aviscribe.UI;

public sealed class OnlineRunHostWindow : Window
{
    public OnlineRunHostWindow(OnlineRunView content)
    {
        Title = "Multiplayer";
        Width = 760;
        Height = 680;
        MinWidth = 650;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        using var iconStream = AssetLoader.Open(
            new Uri("avares://Aviscribe.UI/Assets/aviscribe-icon.png"));
        Icon = new WindowIcon(iconStream);
        Content = content;
        content.CloseRequested += (_, _) => Close();
        Closed += (_, _) => content.Dispose();
    }
}

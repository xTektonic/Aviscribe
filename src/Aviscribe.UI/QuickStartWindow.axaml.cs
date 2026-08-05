using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aviscribe.UI;

public partial class QuickStartWindow : Window
{
    public QuickStartWindow()
    {
        InitializeComponent();
        this.GetControl<Button>("btnClose").Click += CloseGuide;
        this.GetControl<Button>("btnOpenSettings").Click += OpenSettings;
    }

    private void CloseGuide(object? sender, RoutedEventArgs args) => Close(false);

    private void OpenSettings(object? sender, RoutedEventArgs args) => Close(true);
}

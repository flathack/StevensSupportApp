using System.Windows;

namespace StevensSupportHelper.Admin;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

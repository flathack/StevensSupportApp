using System.Windows;
using System.Windows.Controls;

namespace StevensSupportHelper.Admin.Services;

public static class PromptDialog
{
    public static string? Show(Window owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            Height = 180,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = BuildContent(prompt, initialValue, out var textBox, out var okButton)
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? textBox.Text.Trim() : null;
    }

    private static UIElement BuildContent(string prompt, string initialValue, out TextBox textBox, out Button okButton)
    {
        var panel = new Grid { Margin = new Thickness(16) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);

        textBox = new TextBox
        {
            Text = initialValue,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(textBox, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 3);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true
        };

        okButton = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);

        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(buttons);
        return panel;
    }
}
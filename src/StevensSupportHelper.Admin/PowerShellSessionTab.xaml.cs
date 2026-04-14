using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class PowerShellSessionTab : UserControl
{
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly PowerShellTemplateStore _templateStore = new();
    private readonly ObservableCollection<PowerShellTemplateDefinition> _templates = [];

    public PowerShellSessionTab(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;
        InitializeComponent();
        TemplateComboBox.ItemsSource = _templates;
        LoadTemplates();
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandTextBox.Text))
        {
            StatusTextBlock.Text = "Enter a PowerShell command first.";
            return;
        }

        try
        {
            ToggleBusy(true, "Running remote PowerShell command...");
            OutputTextBox.Text = await _remoteService.ExecuteCommandAsync(_client, CommandTextBox.Text, CancellationToken.None);
            ToggleBusy(false, $"Command completed for {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"PowerShell command failed: {exception.Message}");
        }
    }

    private void LoadTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is not PowerShellTemplateDefinition template)
        {
            StatusTextBlock.Text = "Select a PowerShell template first.";
            return;
        }

        CommandTextBox.Text = template.Script;
        StatusTextBlock.Text = $"Loaded template '{template.Name}'.";
    }

    private void SaveTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandTextBox.Text))
        {
            StatusTextBlock.Text = "Enter a PowerShell command first.";
            return;
        }

        string? name = PromptDialog.Show(Window.GetWindow(this)!, "Save PowerShell Template", "Template name:", TemplateComboBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var template = new PowerShellTemplateDefinition(name.Trim(), CommandTextBox.Text);
        var existing = _templates.FirstOrDefault(item => string.Equals(item.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _templates[_templates.IndexOf(existing)] = template;
        }
        else
        {
            _templates.Add(template);
        }

        PersistTemplates(template.Name);
        StatusTextBlock.Text = $"Saved template '{template.Name}'.";
    }

    private void DeleteTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is not PowerShellTemplateDefinition template)
        {
            StatusTextBlock.Text = "Select a PowerShell template first.";
            return;
        }

        _templates.Remove(template);
        PersistTemplates(null);
        StatusTextBlock.Text = $"Deleted template '{template.Name}'.";
    }

    private void LoadTemplates()
    {
        _templates.Clear();
        foreach (var template in _templateStore.Load().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _templates.Add(template);
        }

        if (_templates.Count > 0)
        {
            TemplateComboBox.SelectedIndex = 0;
        }
    }

    private void PersistTemplates(string? preferredName)
    {
        _templateStore.Save(_templates.ToArray());
        LoadTemplates();
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            TemplateComboBox.SelectedItem = _templates.FirstOrDefault(item => string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        TemplateComboBox.IsEnabled = !isBusy;
        LoadTemplateButton.IsEnabled = !isBusy;
        SaveTemplateButton.IsEnabled = !isBusy;
        DeleteTemplateButton.IsEnabled = !isBusy;
        CommandTextBox.IsEnabled = !isBusy;
        RunButton.IsEnabled = !isBusy;
        StatusTextBlock.Text = status;
    }
}

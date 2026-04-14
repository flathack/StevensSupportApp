using System.Windows;

namespace StevensSupportHelper.Admin;

public partial class ClientSettingsWindow : Window
{
    public ClientSettingsWindow(string rustDeskId, string rustDeskPassword, string remoteUserName, string remotePassword, string notes)
    {
        InitializeComponent();
        RustDeskIdTextBox.Text = rustDeskId;
        RustDeskPasswordBox.Password = rustDeskPassword;
        RemoteUserNameTextBox.Text = remoteUserName;
        RemotePasswordBox.Password = remotePassword;
        NotesTextBox.Text = notes;
    }

    public string RustDeskId => RustDeskIdTextBox.Text.Trim();
    public string RustDeskPassword => RustDeskPasswordBox.Password.Trim();
    public string RemoteUserName => RemoteUserNameTextBox.Text.Trim();
    public string RemotePassword => RemotePasswordBox.Password.Trim();
    public string Notes => NotesTextBox.Text.Trim();

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

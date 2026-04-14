using System.ComponentModel;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray;

internal sealed class ClientChatForm : Form
{
    private readonly ListView _messagesListView;
    private readonly TextBox _messageTextBox;
    private readonly Button _sendButton;
    private readonly Button _refreshButton;
    private readonly Label _statusLabel;

    public ClientChatForm()
    {
        Text = "StevensSupportHelper Chat";
        Width = 760;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        _messagesListView = new ListView
        {
            Left = 12,
            Top = 12,
            Width = 720,
            Height = 360,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _messagesListView.Columns.Add("Time", 160);
        _messagesListView.Columns.Add("From", 180);
        _messagesListView.Columns.Add("Message", 360);

        _messageTextBox = new TextBox
        {
            Left = 12,
            Top = 386,
            Width = 720,
            Height = 90,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        _refreshButton = new Button
        {
            Left = 502,
            Top = 488,
            Width = 110,
            Height = 32,
            Text = "Refresh"
        };
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _sendButton = new Button
        {
            Left = 622,
            Top = 488,
            Width = 110,
            Height = 32,
            Text = "Send"
        };
        _sendButton.Click += (_, _) => SendRequested?.Invoke(this, EventArgs.Empty);

        _statusLabel = new Label
        {
            Left = 12,
            Top = 492,
            Width = 470,
            Height = 24,
            Text = "Ready"
        };

        Controls.Add(_messagesListView);
        Controls.Add(_messageTextBox);
        Controls.Add(_refreshButton);
        Controls.Add(_sendButton);
        Controls.Add(_statusLabel);
    }

    public event EventHandler? SendRequested;

    public event EventHandler? RefreshRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string DraftMessage
    {
        get => _messageTextBox.Text;
        set => _messageTextBox.Text = value;
    }

    public void SetBusy(bool isBusy, string status)
    {
        _sendButton.Enabled = !isBusy;
        _refreshButton.Enabled = !isBusy;
        _messageTextBox.Enabled = !isBusy;
        _statusLabel.Text = status;
    }

    public void SetMessages(IReadOnlyList<ChatMessageDto> messages)
    {
        _messagesListView.BeginUpdate();
        _messagesListView.Items.Clear();
        foreach (var message in messages.OrderByDescending(static item => item.CreatedAtUtc))
        {
            var item = new ListViewItem(message.CreatedAtUtc.LocalDateTime.ToString("g"));
            item.SubItems.Add($"{message.SenderRole}: {message.SenderDisplayName}");
            item.SubItems.Add(message.Message);
            _messagesListView.Items.Add(item);
        }

        _messagesListView.EndUpdate();
    }
}

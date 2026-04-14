using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray;

internal sealed class SupportApprovalForm : Form
{
    public event EventHandler? ApproveRequested;
    public event EventHandler? DenyRequested;

    public SupportApprovalForm(SupportRequestDto request)
    {
        Text = "StevensSupportHelper Support-Anfrage";
        Width = 520;
        Height = 260;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        var titleLabel = new Label
        {
            Left = 20,
            Top = 20,
            Width = 460,
            Height = 28,
            Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold),
            Text = "Ein Administrator möchte eine Support-Sitzung starten."
        };

        var detailsLabel = new Label
        {
            Left = 20,
            Top = 62,
            Width = 460,
            Height = 72,
            Text = $"Admin: {request.AdminDisplayName}{Environment.NewLine}" +
                   $"Kanal: {request.PreferredChannel}{Environment.NewLine}" +
                   $"Grund: {request.Reason}"
        };

        var hintLabel = new Label
        {
            Left = 20,
            Top = 142,
            Width = 460,
            Height = 30,
            Text = "Sie sehen diese Anfrage absichtlich sichtbar. Ohne Zustimmung wird keine interaktive Sitzung gestartet."
        };

        var approveButton = new Button
        {
            Left = 230,
            Top = 185,
            Width = 120,
            Height = 34,
            Text = "Genehmigen"
        };
        approveButton.Click += (_, _) => ApproveRequested?.Invoke(this, EventArgs.Empty);

        var denyButton = new Button
        {
            Left = 360,
            Top = 185,
            Width = 120,
            Height = 34,
            Text = "Ablehnen"
        };
        denyButton.Click += (_, _) => DenyRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(titleLabel);
        Controls.Add(detailsLabel);
        Controls.Add(hintLabel);
        Controls.Add(approveButton);
        Controls.Add(denyButton);
    }
}
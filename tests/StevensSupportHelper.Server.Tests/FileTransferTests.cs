using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class FileTransferTests
{
    private static (Services.ClientRegistry Registry, RegisterClientResponse Client, Guid SessionId) SetupWithActiveSession()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(
            client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");
        var decision = registry.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));
        return (registry, client, decision.ActiveSession!.SessionId);
    }

    [Fact]
    public void QueueUpload_ActiveSession_Succeeds()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var response = registry.QueueUploadTransfer(
            client.ClientId,
            new QueueFileUploadRequest("test.txt", "subfolder/test.txt", content),
            "Admin");

        Assert.Equal("PendingClientProcessing", response.Status);
        Assert.NotEqual(Guid.Empty, response.TransferId);
    }

    [Fact]
    public void QueueUpload_NoActiveSession_Throws()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(
            client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));

        Assert.Throws<InvalidOperationException>(() =>
            registry.QueueUploadTransfer(
                client.ClientId,
                new QueueFileUploadRequest("test.txt", "test.txt", Convert.ToBase64String(new byte[] { 1 })),
                "Admin"));
    }

    [Fact]
    public void QueueUpload_PathTraversal_Throws()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var content = Convert.ToBase64String(new byte[] { 1 });

        Assert.Throws<InvalidOperationException>(() =>
            registry.QueueUploadTransfer(
                client.ClientId,
                new QueueFileUploadRequest("test.txt", "../../../etc/passwd", content),
                "Admin"));
    }

    [Fact]
    public void QueueUpload_AbsolutePath_Throws()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var content = Convert.ToBase64String(new byte[] { 1 });

        Assert.Throws<InvalidOperationException>(() =>
            registry.QueueUploadTransfer(
                client.ClientId,
                new QueueFileUploadRequest("test.txt", "C:\\Windows\\test.txt", content),
                "Admin"));
    }

    [Fact]
    public void QueueDownload_ActiveSession_Succeeds()
    {
        var (registry, client, _) = SetupWithActiveSession();

        var response = registry.QueueDownloadTransfer(
            client.ClientId,
            new QueueFileDownloadRequest("logs/app.log"),
            "Admin");

        Assert.Equal("PendingClientProcessing", response.Status);
    }

    [Fact]
    public void CompleteFileTransfer_Upload_Succeeds()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var upload = registry.QueueUploadTransfer(
            client.ClientId,
            new QueueFileUploadRequest("test.txt", "test.txt", content),
            "Admin");

        var result = registry.CompleteFileTransfer(
            upload.TransferId,
            new CompleteFileTransferRequest(client.ClientId, client.ClientSecret, true, null, null));

        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public void CompleteFileTransfer_Download_WithContent_Succeeds()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var download = registry.QueueDownloadTransfer(
            client.ClientId,
            new QueueFileDownloadRequest("test.txt"),
            "Admin");

        var fileContent = Convert.ToBase64String(new byte[] { 10, 20, 30 });
        var result = registry.CompleteFileTransfer(
            download.TransferId,
            new CompleteFileTransferRequest(client.ClientId, client.ClientSecret, true, null, fileContent));

        Assert.Equal("Completed", result.Status);

        var content = registry.GetFileTransferContent(download.TransferId);
        Assert.Equal(fileContent, content.ContentBase64);
    }

    [Fact]
    public void CompleteFileTransfer_Failure_MarkedAsFailed()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var upload = registry.QueueUploadTransfer(
            client.ClientId,
            new QueueFileUploadRequest("test.txt", "test.txt", Convert.ToBase64String(new byte[] { 1 })),
            "Admin");

        var result = registry.CompleteFileTransfer(
            upload.TransferId,
            new CompleteFileTransferRequest(client.ClientId, client.ClientSecret, false, "Disk full", null));

        Assert.Equal("Failed", result.Status);
    }

    [Fact]
    public void GetFileTransferContent_UploadDirection_Throws()
    {
        var (registry, client, _) = SetupWithActiveSession();
        var content = Convert.ToBase64String(new byte[] { 1 });
        var upload = registry.QueueUploadTransfer(
            client.ClientId,
            new QueueFileUploadRequest("test.txt", "test.txt", content),
            "Admin");
        registry.CompleteFileTransfer(upload.TransferId, new CompleteFileTransferRequest(client.ClientId, client.ClientSecret, true, null, null));

        Assert.Throws<InvalidOperationException>(() =>
            registry.GetFileTransferContent(upload.TransferId));
    }
}

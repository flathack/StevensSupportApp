using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.DTOs;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;
using StevensSupportHelper.Shared.Contracts;
using StevensSupportHelper.Shared.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var configuredRateLimiting = builder.Configuration
	.GetSection(RateLimitingOptions.SectionName)
	.Get<RateLimitingOptions>() ?? new RateLimitingOptions();
var configuredAdminAuth = builder.Configuration
	.GetSection(AdminAuthOptions.SectionName)
	.Get<AdminAuthOptions>() ?? new AdminAuthOptions();

builder.Services.Configure<ServerStorageOptions>(builder.Configuration.GetSection(ServerStorageOptions.SectionName));
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.Configure<ClientRegistrationOptions>(builder.Configuration.GetSection(ClientRegistrationOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<BootstrapUserOptions>(builder.Configuration.GetSection(BootstrapUserOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<ServerStateStore>();
builder.Services.AddSingleton<ClientRegistrationVerifier>();
builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<RequestRateLimitService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();

EnsureBootstrapUser(app);

AppDiagnostics.WriteEvent("Server", "Startup", "Server host initialized.");
app.Lifetime.ApplicationStarted.Register(() => AppDiagnostics.WriteEvent("Server", "Started", "Server application started."));
app.Lifetime.ApplicationStopping.Register(() => AppDiagnostics.WriteEvent("Server", "Stopping", "Server application stopping."));
app.Lifetime.ApplicationStopped.Register(() => AppDiagnostics.WriteEvent("Server", "Stopped", "Server application stopped."));

if (!app.Environment.IsDevelopment())
{
	app.UseHttpsRedirection();
}

app.UseRouting();
app.Use(async (httpContext, next) =>
{
	try
	{
		await next();
	}
	catch (Exception exception)
	{
		AppDiagnostics.WriteEvent(
			"Server",
			"UnhandledRequestException",
			$"Unhandled exception for {httpContext.Request.Method} {httpContext.Request.Path}.",
			exception);
		throw;
	}
});

if (configuredRateLimiting.Enabled)
{
	app.Use(async (httpContext, next) =>
	{
		var appliedPolicy = ResolveAppliedRateLimit(httpContext, configuredRateLimiting);
		if (appliedPolicy is null)
		{
			await next();
			return;
		}

		var partitionKey = appliedPolicy.PartitionKind switch
		{
			RequestRateLimitPartitionKind.Admin => ResolveAdminPartitionKey(httpContext, configuredAdminAuth.ApiKeyHeaderName),
			_ => ResolveClientPartitionKey(httpContext)
		};

		var limiter = httpContext.RequestServices.GetRequiredService<RequestRateLimitService>();
		if (limiter.TryAcquire(appliedPolicy.PolicyName, partitionKey, appliedPolicy.Options, out var retryAfter))
		{
			await next();
			return;
		}

		httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
		httpContext.Response.ContentType = "application/json";
		httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
		await httpContext.Response.WriteAsJsonAsync(new { message = "Rate limit exceeded. Please retry later." });
	});
}

var readRoles = new[] { AdminRole.Auditor, AdminRole.Operator, AdminRole.Administrator };
var manageRoles = new[] { AdminRole.Operator, AdminRole.Administrator };

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

// Auth API
app.MapPost("/api/auth/register", (HttpContext httpContext, RegisterRequest request, UserService userService, JwtTokenService jwtService, AdminAuthService authService) =>
{
    var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, manageRoles);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

    var (user, error) = userService.CreateUser(
        request.Username,
        request.Password,
        request.DisplayName ?? request.Username,
        request.Roles ?? []);

    if (user is null)
    {
        return Results.BadRequest(new { message = error });
    }

    return Results.Created($"/api/auth/users/{user.Id}", new RegisterResponse(
        user.Id,
        user.Username,
        user.DisplayName,
        user.Roles,
        "Benutzer wurde erfolgreich angelegt."));
});

app.MapPost("/api/auth/login", (LoginRequest request, UserService userService, JwtTokenService jwtService) =>
{
    if (!userService.ValidateCredentials(request.Username, request.Password))
    {
        return Results.Unauthorized();
    }

    var user = userService.GetUserByUsername(request.Username);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var accessToken = jwtService.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.Roles);
    var refreshToken = jwtService.GenerateRefreshToken();

    userService.UpdateUserLastLogin(user.Id);

    return Results.Ok(new LoginResponse(
        accessToken,
        refreshToken,
        jwtService.GetRefreshTokenExpiry(),
        new UserInfoResponse(user.Id, user.Username, user.DisplayName, user.Roles, user.IsMfaEnabled, user.LastLoginAtUtc)));
});

app.MapPost("/api/auth/refresh", (RefreshTokenRequest request, UserService userService, JwtTokenService jwtService) =>
{
    // For simplicity, validation is done via the access token in the Authorization header
    // In production, you'd validate the refresh token against stored tokens
    var authHeader = userService.GetType().Assembly;
    return Results.BadRequest(new { message = "Refresh token validation not implemented." });
});

app.MapPost("/api/auth/change-password", (HttpContext httpContext, ChangePasswordRequest request, UserService userService, JwtTokenService jwtService) =>
{
    var (isValid, userId, error) = ValidateAuthHeader(httpContext, jwtService);
    if (!isValid || userId is null)
    {
        return Results.Json(new { message = error }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var (success, errorMessage) = userService.UpdateUserPassword(userId.Value, request.OldPassword, request.NewPassword);
    if (!success)
    {
        return Results.BadRequest(new { message = errorMessage });
    }

    return Results.Ok(new { message = "Passwort wurde erfolgreich geändert." });
});

app.MapGet("/api/auth/me", (HttpContext httpContext, UserService userService, JwtTokenService jwtService) =>
{
    var (isValid, userId, error) = ValidateAuthHeader(httpContext, jwtService);
    if (!isValid || userId is null)
    {
        return Results.Json(new { message = error }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var user = userService.GetUserById(userId.Value);
    if (user is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new UserInfoResponse(user.Id, user.Username, user.DisplayName, user.Roles, user.IsMfaEnabled, user.LastLoginAtUtc));
});

// User management (Admin only)
app.MapGet("/api/auth/users", (HttpContext httpContext, UserService userService, JwtTokenService jwtService, AdminAuthService authService) =>
{
    var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

    var users = userService.GetAllUsers();
    return Results.Ok(users.Select(u => new UserInfoResponse(u.Id, u.Username, u.DisplayName, u.Roles, u.IsMfaEnabled, u.LastLoginAtUtc)));
});

app.MapPut("/api/auth/users/{userId:guid}/roles", (HttpContext httpContext, Guid userId, UpdateUserRolesRequest request, UserService userService, JwtTokenService jwtService, AdminAuthService authService) =>
{
    var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, manageRoles);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

    var (success, error) = userService.UpdateUserRoles(userId, request.Roles);
    if (!success)
    {
        return Results.NotFound(new { message = error });
    }

    return Results.Ok(new { message = "Benutzerrollen aktualisiert." });
});

app.MapPost("/api/auth/users/{userId:guid}/reset-password", (HttpContext httpContext, Guid userId, ResetPasswordRequest request, UserService userService, JwtTokenService jwtService, AdminAuthService authService) =>
{
    var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, manageRoles);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

    var (success, error) = userService.ResetUserPassword(userId, request.NewPassword);
    if (!success)
    {
        return Results.NotFound(new { message = error });
    }

    return Results.Ok(new { message = "Passwort wurde zurückgesetzt." });
});

app.MapDelete("/api/auth/users/{userId:guid}", (HttpContext httpContext, Guid userId, UserService userService, JwtTokenService jwtService, AdminAuthService authService) =>
{
    var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, manageRoles);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

    var (success, error) = userService.DeleteUser(userId);
    if (!success)
    {
        return Results.NotFound(new { message = error });
    }

    return Results.Ok(new { message = "Benutzer wurde gelöscht." });
});

app.MapPost("/api/clients/register", (RegisterClientRequest request, ClientRegistry registry) =>
{
	try
	{
		var response = registry.Register(request);
		return Results.Ok(response);
	}
	catch (InvalidOperationException exception)
	{
		return Results.BadRequest(new { message = exception.Message });
	}
});

app.MapPost("/api/clients/heartbeat", (ClientHeartbeatRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.Heartbeat(request));
	}
	catch (InvalidOperationException)
	{
		return Results.Unauthorized();
	}
});

app.MapPost("/api/clients/support-state", (GetSupportStateRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.GetSupportState(request));
	}
	catch (InvalidOperationException)
	{
		return Results.Unauthorized();
	}
});

app.MapPost("/api/clients/chat-messages", (SendClientChatMessageRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.AddClientChatMessage(request));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/clients/support-requests/{requestId:guid}/decision", (Guid requestId, SubmitSupportDecisionRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.SubmitSupportDecision(requestId, request));
	}
	catch (InvalidOperationException)
	{
		return Results.Unauthorized();
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/clients/file-transfers/{transferId:guid}/complete", (Guid transferId, CompleteFileTransferRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.CompleteFileTransfer(transferId, request));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/clients/agent-jobs/{jobId:guid}/complete", (Guid jobId, CompleteAgentJobRequest request, ClientRegistry registry) =>
{
	try
	{
		return Results.Ok(registry.CompleteAgentJob(jobId, request));
	}
	catch (InvalidOperationException)
	{
		return Results.Unauthorized();
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapGet("/api/admin/session", (HttpContext httpContext, AdminAuthService authService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService: null, userService: null, out var admin, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	return Results.Ok(new AdminSessionInfoResponse(
		admin.DisplayName,
		admin.Roles.Select(static role => role.ToString()).ToArray(),
		!string.IsNullOrWhiteSpace(admin.TotpSecret)));
});

app.MapGet("/api/admin/clients", (HttpContext httpContext, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	return Results.Ok(registry.GetClients());
});

app.MapGet("/api/admin/clients/{clientId:guid}", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	var client = registry.GetClients().FirstOrDefault(entry => entry.ClientId == clientId);
	return client is null ? Results.NotFound() : Results.Ok(client);
});

app.MapGet("/api/admin/clients/{clientId:guid}/chat-messages", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.GetChatMessages(clientId));
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/chat-messages", (HttpContext httpContext, Guid clientId, SendAdminChatMessageRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.AddAdminChatMessage(clientId, admin.DisplayName, request.Message));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapGet("/api/admin/audit", (HttpContext httpContext, int? take, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	return Results.Ok(registry.GetAuditEntries(take ?? 50));
});

app.MapGet("/api/admin/audit-entries", (HttpContext httpContext, int? limit, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	return Results.Ok(registry.GetAuditEntries(limit ?? 50));
});

app.MapGet("/api/admin/remote-actions", (HttpContext httpContext, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	return Results.Ok(new[]
	{
		new { Name = "collect_support_snapshot.ps1", Description = "Sammelt Diagnosedaten und eine Systemübersicht.", RequiresElevation = false },
		new { Name = "restart_spooler.ps1", Description = "Startet den Windows-Druckspooler neu.", RequiresElevation = true },
		new { Name = "winget_update_all.ps1", Description = "Plant Softwareupdates per winget ein.", RequiresElevation = true }
	});
});

app.MapGet("/api/admin/file-transfers/{transferId:guid}", (HttpContext httpContext, Guid transferId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.GetFileTransfer(transferId));
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapGet("/api/admin/file-transfers/{transferId:guid}/content", (HttpContext httpContext, Guid transferId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.GetFileTransferContent(transferId));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/support-requests", (HttpContext httpContext, Guid clientId, CreateSupportRequestRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.CreateSupportRequest(clientId, request, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/file-transfers/upload", (HttpContext httpContext, Guid clientId, QueueFileUploadRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueUploadTransfer(clientId, request, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/file-transfers/download", (HttpContext httpContext, Guid clientId, QueueFileDownloadRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueDownloadTransfer(clientId, request, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/active-session/end", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.EndActiveSession(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/process-snapshot", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueProcessSnapshotJob(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/windows-update-scan", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueWindowsUpdateScanJob(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/windows-update-install", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueWindowsUpdateInstallJob(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/registry-snapshot", (HttpContext httpContext, Guid clientId, AgentRegistrySnapshotRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueRegistrySnapshotJob(clientId, request.RegistryPath, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/service-snapshot", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueServiceSnapshotJob(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/service-control", (HttpContext httpContext, Guid clientId, AgentServiceControlRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueServiceControlJob(clientId, request.ServiceName, request.Action, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/script-execution", (HttpContext httpContext, Guid clientId, AgentScriptExecutionRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueueScriptExecutionJob(clientId, request.ScriptContent, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/power-plan-snapshot", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueuePowerPlanSnapshotJob(clientId, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPost("/api/admin/clients/{clientId:guid}/agent-jobs/power-plan-activate", (HttpContext httpContext, Guid clientId, AgentPowerPlanActivateRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.QueuePowerPlanActivateJob(clientId, request.Guid, admin.DisplayName));
	}
	catch (InvalidOperationException exception)
	{
		return Results.Conflict(new { message = exception.Message });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapGet("/api/admin/agent-jobs/{jobId:guid}", (HttpContext httpContext, Guid jobId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out _, readRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		return Results.Ok(registry.GetAgentJob(jobId));
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapDelete("/api/admin/clients/{clientId:guid}", (HttpContext httpContext, Guid clientId, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		registry.DeleteClient(clientId, admin.DisplayName);
		return Results.Ok(new { message = "Client deleted." });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

app.MapPut("/api/admin/clients/{clientId:guid}/metadata", (HttpContext httpContext, Guid clientId, UpdateAdminClientMetadataRequest request, ClientRegistry registry, AdminAuthService authService, JwtTokenService jwtService, UserService userService) =>
{
	var authorizationFailure = RequireAdmin(httpContext, authService, jwtService, userService, out var admin, manageRoles);
	if (authorizationFailure is not null)
	{
		return authorizationFailure;
	}

	try
	{
		registry.UpdateClientMetadata(clientId, request, admin.DisplayName);
		return Results.Ok(new { message = "Client metadata updated." });
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound();
	}
});

static IResult? RequireAdmin(HttpContext httpContext, AdminAuthService authService, JwtTokenService? jwtService, UserService? userService, out AuthenticatedAdmin admin, params AdminRole[] requiredRoles)
{
    if (TryAuthenticateAdminViaJwt(httpContext, jwtService, userService, out admin, out var jwtError))
    {
        if (requiredRoles.Length == 0 || requiredRoles.Any(admin.HasRole))
        {
            return null;
        }

        return Results.Json(
            new { message = $"Admin '{admin.DisplayName}' besitzt keine der erforderlichen Rollen: {string.Join(", ", requiredRoles)}." },
            statusCode: StatusCodes.Status403Forbidden);
    }

	if (!authService.TryAuthenticate(httpContext.Request, out admin, out var errorMessage))
	{
		return Results.Json(new { message = jwtError ?? errorMessage }, statusCode: StatusCodes.Status401Unauthorized);
	}

	if (!authService.HasAnyRole(admin, requiredRoles))
	{
		return Results.Json(
			new { message = $"Admin '{admin.DisplayName}' besitzt keine der erforderlichen Rollen: {string.Join(", ", requiredRoles)}." },
			statusCode: StatusCodes.Status403Forbidden);
	}

	return null;
}

static bool TryAuthenticateAdminViaJwt(HttpContext httpContext, JwtTokenService? jwtService, UserService? userService, out AuthenticatedAdmin admin, out string? error)
{
    admin = AuthenticatedAdmin.Empty;
    error = null;

    if (jwtService is null || userService is null)
    {
        return false;
    }

    if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        return false;
    }

    var bearerToken = authHeader.ToString();
    if (!bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        error = "Ungültiger Authorization-Header. Erwartet wird 'Bearer <token>'.";
        return false;
    }

    var token = bearerToken["Bearer ".Length..].Trim();
    var (isValid, userId, jwtError) = jwtService.ValidateAccessToken(token);
    if (!isValid || userId is null)
    {
        error = jwtError ?? "Das Zugriffstoken ist ungültig.";
        return false;
    }

    var user = userService.GetUserById(userId.Value);
    if (user is null || !user.IsActive)
    {
        error = "Benutzer wurde nicht gefunden oder ist deaktiviert.";
        return false;
    }

    var roles = user.Roles
        .Select(static role => Enum.TryParse<AdminRole>(role, ignoreCase: true, out var parsedRole) ? parsedRole : AdminRole.Auditor)
        .Distinct()
        .ToArray();

    admin = new AuthenticatedAdmin(user.DisplayName, roles, user.TotpSecret ?? string.Empty);
    return true;
}

static (bool IsValid, Guid? UserId, string? Error) ValidateAuthHeader(HttpContext httpContext, JwtTokenService jwtService)
{
	if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
	{
		return (false, null, "Missing Authorization header.");
	}

	var bearerToken = authHeader.ToString();
	if (!bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
	{
		return (false, null, "Invalid Authorization header format. Expected 'Bearer <token>'.");
	}

	var token = bearerToken["Bearer ".Length..].Trim();
	return jwtService.ValidateAccessToken(token);
}

app.Run();

static string ResolveAdminPartitionKey(HttpContext httpContext, string headerName)
{
	if (httpContext.Request.Headers.TryGetValue(headerName, out var apiKeyValues))
	{
		var apiKey = apiKeyValues.ToString().Trim();
		if (!string.IsNullOrWhiteSpace(apiKey))
		{
			return apiKey;
		}
	}

	return ResolveRemoteAddress(httpContext);
}

static string ResolveClientPartitionKey(HttpContext httpContext)
{
	return ResolveRemoteAddress(httpContext);
}

static string ResolveRemoteAddress(HttpContext httpContext)
{
	return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static AppliedRateLimit? ResolveAppliedRateLimit(HttpContext httpContext, RateLimitingOptions options)
{
	var path = httpContext.Request.Path;
	if (path.StartsWithSegments("/api/clients", StringComparison.OrdinalIgnoreCase))
	{
		return new AppliedRateLimit("client", options.ClientPolicy, RequestRateLimitPartitionKind.Client);
	}

	if (!path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
	{
		return null;
	}

	return HttpMethods.IsGet(httpContext.Request.Method)
		? new AppliedRateLimit("admin-read", options.AdminReadPolicy, RequestRateLimitPartitionKind.Admin)
		: new AppliedRateLimit("admin-write", options.AdminWritePolicy, RequestRateLimitPartitionKind.Admin);
}

static void EnsureBootstrapUser(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var options = scope.ServiceProvider
        .GetRequiredService<IOptions<BootstrapUserOptions>>()
        .Value;

    if (!options.Enabled
        || string.IsNullOrWhiteSpace(options.Username)
        || string.IsNullOrWhiteSpace(options.Password))
    {
        return;
    }

    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
    if (userService.GetUserByUsername(options.Username) is not null)
    {
        return;
    }

    var roles = options.Roles.Count > 0
        ? options.Roles
        : ["Administrator", "Operator", "Auditor"];

    var displayName = string.IsNullOrWhiteSpace(options.DisplayName)
        ? options.Username
        : options.DisplayName;

    var (_, error) = userService.CreateUser(options.Username, options.Password, displayName, roles);
    if (!string.IsNullOrWhiteSpace(error))
    {
        AppDiagnostics.WriteEvent("Server", "BootstrapUserFailed", error);
    }
    else
    {
        AppDiagnostics.WriteEvent("Server", "BootstrapUserCreated", $"Bootstrap user '{options.Username}' created.");
    }
}

public partial class Program
{
}

internal sealed record AppliedRateLimit(
	string PolicyName,
	RateLimitPolicyOptions Options,
	RequestRateLimitPartitionKind PartitionKind);

internal enum RequestRateLimitPartitionKind
{
	Client,
	Admin
}

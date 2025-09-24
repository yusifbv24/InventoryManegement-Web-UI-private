﻿using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace NotificationService.Application.Services
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(IConnectionManager connectionManager, ILogger<NotificationHub> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Store connection for this user
                await _connectionManager.AddConnection(userId, Context.ConnectionId);

                // Add to user-specific group - this is crucial for targeted notifications
                var userGroup = $"user-{userId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, userGroup);
                _logger.LogInformation($"User {userId} joined group {userGroup}");

                // Add to role groups for role-based notifications
                var roles = Context.User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
                foreach (var role in roles)
                {
                    var roleGroup = $"role-{role}";
                    await Groups.AddToGroupAsync(Context.ConnectionId, roleGroup);
                    _logger.LogInformation($"User {userId} added to role group: {roleGroup}");
                }

                // Send connection confirmation with initial data
                await Clients.Caller.SendAsync("ConnectionEstablished", new
                {
                    connectionId = Context.ConnectionId,
                    userId = userId,
                    userGroup = userGroup,
                    roleGroups = roles.Select(r => $"role-{r}").ToList(),
                    timestamp = DateTime.UtcNow,
                    message = "Connected successfully"
                });
                _logger.LogInformation($"User {userId} connected with ID {Context.ConnectionId}");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                await _connectionManager.RemoveConnection(userId, Context.ConnectionId);
                _logger.LogInformation($"User {userId} disconnected: {exception?.Message ?? "Normal disconnect"}");
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinUserGroup()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var userGroup = $"user-{userId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, userGroup);
                _logger.LogInformation($"User {userId} explicitly joined group {userGroup}");
            }
        }
    }
}
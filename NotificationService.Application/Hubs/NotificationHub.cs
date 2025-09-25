﻿using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Services;
using NotificationService.Domain.Repositories;

namespace NotificationService.Application.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(
            IConnectionManager connectionManager,
            IServiceProvider serviceProvider,
            ILogger<NotificationHub> logger)
        {
            _connectionManager = connectionManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                // Get user ID from claims - handle both possible claim types
                var userId = Context.User?.FindFirst("UserId")?.Value
                    ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var userName = Context.User?.Identity?.Name ?? "Unknown";

                if (!string.IsNullOrEmpty(userId))
                {
                    // Add to connection tracking
                    await _connectionManager.AddConnection(userId, Context.ConnectionId);

                    // Add to user-specific group for targeted notifications
                    var userGroup = $"user-{userId}";
                    await Groups.AddToGroupAsync(Context.ConnectionId, userGroup);
                    _logger.LogInformation($"User {userName} (ID: {userId}) connected with connection {Context.ConnectionId}, joined group {userGroup}");

                    // Add to role-based groups for role-specific notifications
                    var roles = Context.User?.FindAll(ClaimTypes.Role)?.Select(c => c.Value) ?? Enumerable.Empty<string>();
                    var roleGroups = new List<string>();

                    foreach (var role in roles)
                    {
                        var roleGroup = $"role-{role}";
                        await Groups.AddToGroupAsync(Context.ConnectionId, roleGroup);
                        roleGroups.Add(roleGroup);
                        _logger.LogInformation($"User {userName} added to role group: {roleGroup}");
                    }

                    // Send connection confirmation back to the client
                    await Clients.Caller.SendAsync("ConnectionEstablished", new
                    {
                        connectionId = Context.ConnectionId,
                        userId = userId,
                        userName = userName,
                        userGroup = userGroup,
                        roleGroups = roleGroups,
                        timestamp = DateTime.UtcNow,
                        message = "Connected to notification service successfully"
                    });

                    // Send any pending notifications
                    await SendPendingNotifications(userId);
                }
                else
                {
                    _logger.LogWarning($"User connected but no userId found in claims");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync");
                throw;
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                await _connectionManager.RemoveConnection(userId, Context.ConnectionId);
                _logger.LogInformation($"User {userId} disconnected: {exception?.Message ?? "Normal disconnect"}");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Method to check connection status
        public async Task<bool> Ping()
        {
            await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
            return true;
        }

        private async Task SendPendingNotifications(string userId)
        {
            try
            {
                if(!int.TryParse(userId,out int userIdInt))
                {
                    _logger.LogWarning($"Invalid userId format: {userId}");
                    return;
                }

                // Get a scoped service provider to access the repository
                using var scope= _serviceProvider.CreateScope();
                var notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

                // Fetch unread notifications for the user
                var unreadNotifications = await notificationRepository.GetByUserIdAsync(userIdInt, unreadOnly: true);

                if (!unreadNotifications.Any())
                {
                    _logger.LogInformation($"No pending notifications for user {userId}");
                    return;
                }

                _logger.LogInformation($"Sending {unreadNotifications.Count()} pending notifications to user {userId}");

                // Sort notifications by creation date (oldest first)
                var sortedNotifications = unreadNotifications.OrderBy(n => n.CreatedAt);

                // Send each notification to the user
                foreach(var notification in sortedNotifications)
                {
                    var notificationDto = new
                    {
                        id = notification.Id,
                        type = notification.Type,
                        title = notification.Title,
                        message = notification.Message,
                        createdAt = notification.CreatedAt,
                        isRead = notification.IsRead,
                        data = notification.Data
                    };

                    // Send to the specific caller (the user who just connected)
                    await Clients.Caller.SendAsync("ReceivePendingNotification", notificationDto);

                    // Small delay between notifications to avoid overwhelming the client
                    await Task.Delay(100);
                }

                // Send a summary message
                await Clients.Caller.SendAsync("PendingNotificationsComplete", new
                {
                    count = unreadNotifications.Count(),
                    timestamp= DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending pending notifications to user {userId}");
            }
        }
    }
}
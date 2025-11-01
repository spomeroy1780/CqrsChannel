using ConsoleExMediator.Domain.Services;

namespace ConsoleExMediator.Infrastructure.Services;

/// <summary>
/// Implementation of INotificationService
/// Single Responsibility: Handles all notification operations
/// Open/Closed: Can add new notification channels without modifying existing code
/// Interface Segregation: Implements focused notification interface
/// </summary>
public sealed class NotificationService : INotificationService
{
    public Task SendOrderConfirmationAsync(
        string email,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        // Simulate sending email
        // In production, this would integrate with an email service
        return Task.CompletedTask;
    }

    public Task SendShippingNotificationAsync(
        string email,
        int orderId,
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));
        
        if (string.IsNullOrWhiteSpace(trackingNumber))
            throw new ArgumentException("Tracking number is required", nameof(trackingNumber));

        // Simulate sending email
        // In production, this would integrate with an email service
        return Task.CompletedTask;
    }

    public Task SendCancellationNotificationAsync(
        string email,
        int orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        // Simulate sending email
        // In production, this would integrate with an email service
        return Task.CompletedTask;
    }
}

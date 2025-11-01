namespace ConsoleExMediator.Domain.Services;

/// <summary>
/// Domain service for sending notifications
/// Interface Segregation: Focused interface for notifications
/// Open/Closed: Can add new notification types without modifying existing code
/// </summary>
public interface INotificationService
{
    Task SendOrderConfirmationAsync(string email, int orderId, CancellationToken cancellationToken = default);
    Task SendShippingNotificationAsync(string email, int orderId, string trackingNumber, CancellationToken cancellationToken = default);
    Task SendCancellationNotificationAsync(string email, int orderId, string reason, CancellationToken cancellationToken = default);
}

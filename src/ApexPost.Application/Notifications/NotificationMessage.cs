namespace ApexPost.Application.Notifications;

/// <summary>
/// Represents a notification prepared for delivery by an Application-owned abstraction.
/// </summary>
public sealed record NotificationMessage(string Recipient, string Subject, string Body);

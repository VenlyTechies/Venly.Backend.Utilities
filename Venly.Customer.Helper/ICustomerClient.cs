namespace Venly.Customer.Helper;

/// <summary>CustomerService's internal surface, for services that need to reach a customer.</summary>
public interface ICustomerClient
{
    /// <summary>
    /// Notifies a customer on the channels named; CustomerService decides where each one goes. Throws on a
    /// non-2xx answer — a 502 means nothing was delivered and is worth retrying.
    /// </summary>
    Task<CustomerNotificationResult> NotifyAsync(
        string customerId, CustomerNotification notification, CancellationToken ct = default);
}

using System.Net;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

/// <summary>Payment audit Stage 6 (H2): which provider failures a retry can fix.</summary>
public static class StripeErrors
{
    /// <summary>
    /// A failure that says nothing about the payment: the network, Stripe's own errors (5xx), rate limiting (429),
    /// an idempotent request still in progress or a lock timeout (409), or a timeout the caller did not ask for.
    /// Stripe.net has already retried these twice by default. Anything else, such as an invalid request (400) or a
    /// bad key (401), is a defect or a misconfiguration, and retrying cannot fix it.
    /// </summary>
    public static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        StripeException stripe => (int)stripe.HttpStatusCode == 0
            || stripe.HttpStatusCode is HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
            || (int)stripe.HttpStatusCode >= 500
            || stripe.StripeError?.Type == "api_connection_error",
        HttpRequestException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };
}

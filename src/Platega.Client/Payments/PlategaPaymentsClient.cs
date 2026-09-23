using System.Globalization;
using Platega.Http;
using Platega.Serialization;

namespace Platega.Payments;

/// <summary>Payment creation, status and exports.</summary>
public interface IPlategaPaymentsClient
{
    /// <summary>
    /// Creates a payment and returns the page the payer must be sent to.
    /// Not retried automatically: the endpoint has no idempotency key.
    /// </summary>
    Task<CreatedPayment> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the current status and details of a transaction.</summary>
    Task<PlategaTransaction> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Returns the QR code or payment link of a host-to-host transaction. Requires H2H to be enabled for the merchant.</summary>
    Task<H2HPaymentData> GetH2HPaymentDataAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Builds a transaction export and returns the link to the file.</summary>
    Task<Uri> ExportTransactionsAsync(TransactionExportRequest request, TransactionExportFormat format, CancellationToken cancellationToken = default);
}

internal sealed class PlategaPaymentsClient(PlategaConnection connection) : IPlategaPaymentsClient
{
    public async Task<CreatedPayment> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        CreateTransactionWireRequest wire = new CreateTransactionWireRequest
        {
            PaymentMethod = request.Method,
            PaymentDetails = request.Amount,
            Description = request.Description,
            Return = request.ReturnUrl.AbsoluteUri,
            FailedUrl = request.FailedUrl.AbsoluteUri,
            Payload = request.Payload,
            OrderId = request.OrderId,
            Metadata = request.Metadata,
        };

        if (request.Method is null)
        {
            CreateTransactionV2WireResponse created = await connection
                .PostAsync(
                    "v2/transaction/process",
                    wire,
                    PlategaJsonContext.Relaxed.CreateTransactionWireRequest,
                    PlategaJsonContext.Relaxed.CreateTransactionV2WireResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            EnsureCreated(created.TransactionId, created.Url, "POST v2/transaction/process");
            return new CreatedPayment
            {
                TransactionId = created.TransactionId,
                Status = created.Status,
                PaymentUrl = created.Url!,
                ExpiresIn = created.ExpiresIn,
                UsdtRate = created.Rate,
            };
        }

        CreateTransactionWireResponse createdWithMethod = await connection
            .PostAsync(
                "transaction/process",
                wire,
                PlategaJsonContext.Relaxed.CreateTransactionWireRequest,
                PlategaJsonContext.Relaxed.CreateTransactionWireResponse,
                cancellationToken)
            .ConfigureAwait(false);

        EnsureCreated(createdWithMethod.TransactionId, createdWithMethod.Redirect, "POST transaction/process");
        return new CreatedPayment
        {
            TransactionId = createdWithMethod.TransactionId,
            Status = createdWithMethod.Status,
            PaymentUrl = createdWithMethod.Redirect!,
            ExpiresIn = createdWithMethod.ExpiresIn,
            UsdtRate = createdWithMethod.UsdtRate,
            PaymentMethodName = createdWithMethod.PaymentMethod,
            Amount = createdWithMethod.PaymentDetails,
            MerchantId = createdWithMethod.MerchantId,
        };
    }

    public Task<PlategaTransaction> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        connection.GetAsync(
            $"transaction/{PlategaFormat.FormatId(transactionId)}",
            PlategaJsonContext.Relaxed.PlategaTransaction,
            cancellationToken);

    public Task<H2HPaymentData> GetH2HPaymentDataAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        connection.GetAsync(
            $"h2h/{PlategaFormat.FormatId(transactionId)}",
            PlategaJsonContext.Relaxed.H2HPaymentData,
            cancellationToken);

    public async Task<Uri> ExportTransactionsAsync(
        TransactionExportRequest request,
        TransactionExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.From > request.To)
        {
            throw new ArgumentException("Export period start must not be after its end.", nameof(request));
        }

        string formatSegment = format switch
        {
            TransactionExportFormat.Csv => "csv",
            TransactionExportFormat.Excel => "excel",
            TransactionExportFormat.Json => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format."),
        };

        TransactionExportWireRequest wire = new TransactionExportWireRequest
        {
            Statuses = request.StatusCodes,
            PaymentMethods = request.PaymentMethods
                .Select(method => ((int)method).ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            From = PlategaFormat.FormatTimestamp(request.From),
            To = PlategaFormat.FormatTimestamp(request.To),
            TimeZoneId = request.TimeZoneId,
        };

        string path = $"transaction/export/{formatSegment}";
        FileUrlWireResponse response = await connection
            .PostAsync(
                path,
                wire,
                PlategaJsonContext.Relaxed.TransactionExportWireRequest,
                PlategaJsonContext.Relaxed.FileUrlWireResponse,
                cancellationToken)
            .ConfigureAwait(false);

        if (!Uri.TryCreate(response.Url, UriKind.Absolute, out Uri? fileUri))
        {
            throw new PlategaApiException(System.Net.HttpStatusCode.OK, $"POST {path}", response.Url);
        }

        return fileUri;
    }

    /// <summary>A 200 answer without an id or a link is not a created payment.</summary>
    internal static void EnsureCreated(Guid id, string? url, string endpoint)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(url))
        {
            throw new PlategaApiException(System.Net.HttpStatusCode.OK, endpoint, "Response has no transaction id or payment link.");
        }
    }

    private static void Validate(CreatePaymentRequest request)
    {
        if (request.Amount.Amount <= 0)
        {
            throw new ArgumentException("Payment amount must be positive.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Amount.Currency))
        {
            throw new ArgumentException("Payment currency is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Payment description is required.", nameof(request));
        }

        if (!request.ReturnUrl.IsAbsoluteUri || !request.FailedUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Return and failed URLs must be absolute.", nameof(request));
        }

        if (request.Method is PaymentMethod.Subscription or PaymentMethod.Unknown)
        {
            throw new ArgumentException(
                "Use IPlategaSubscriptionsClient to create subscriptions; Unknown is not a valid payment method.",
                nameof(request));
        }
    }
}

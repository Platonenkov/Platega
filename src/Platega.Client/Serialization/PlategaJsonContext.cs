using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Platega.Balances;
using Platega.Callbacks;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Platega.Serialization;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CreateTransactionWireRequest))]
[JsonSerializable(typeof(CreateTransactionV2WireResponse))]
[JsonSerializable(typeof(CreateTransactionWireResponse))]
[JsonSerializable(typeof(PlategaTransaction))]
[JsonSerializable(typeof(H2HPaymentData))]
[JsonSerializable(typeof(TransactionExportWireRequest))]
[JsonSerializable(typeof(FileUrlWireResponse))]
[JsonSerializable(typeof(List<PlategaBalance>))]
[JsonSerializable(typeof(CancelAvailability))]
[JsonSerializable(typeof(CancelResult))]
[JsonSerializable(typeof(CreateSubscriptionWireRequest))]
[JsonSerializable(typeof(CreateSubscriptionWireResponse))]
[JsonSerializable(typeof(PlategaSubscription))]
[JsonSerializable(typeof(SubscriptionPage))]
[JsonSerializable(typeof(SubscriptionCancelResult))]
[JsonSerializable(typeof(CardPayoutWireRequest))]
[JsonSerializable(typeof(CardPayoutResult))]
[JsonSerializable(typeof(List<SavedCard>))]
[JsonSerializable(typeof(CallbackWire))]
internal sealed partial class PlategaJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Context with relaxed escaping: Cyrillic descriptions are sent as-is instead of <c>\uXXXX</c> sequences.
    /// </summary>
    public static PlategaJsonContext Relaxed { get; } = new PlategaJsonContext(
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
}

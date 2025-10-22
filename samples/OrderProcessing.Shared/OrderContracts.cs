namespace OrderProcessing.Shared;

public sealed record OrderRequest(string OrderId, decimal Amount, string CustomerId);

public sealed record OrderResult(string OrderId, string Status, string? TransactionId, decimal Amount);

public sealed record PaymentReceipt(string TransactionId, decimal Amount, DateTimeOffset CapturedAt);

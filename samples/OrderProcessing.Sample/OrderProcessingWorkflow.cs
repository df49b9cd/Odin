using Odin.Sdk;

namespace OrderProcessing.Sample;

/// <summary>
/// Sample order processing workflow demonstrating Odin workflow patterns
/// </summary>
public class OrderProcessingWorkflow : IWorkflow<OrderRequest, OrderResult>
{
    public async Task<Result<OrderResult>> ExecuteAsync(
        OrderRequest input,
        CancellationToken cancellationToken)
    {
        // TODO: Implement workflow execution using Hugo primitives
        // This is a placeholder demonstrating the intended structure

        // Example structure (to be implemented):
        // return await ExecuteActivity<ValidateOrderActivity>(input)
        //     .Then(validated => ExecuteActivity<ProcessPaymentActivity>(validated))
        //     .Then(paid => ExecuteActivity<FulfillOrderActivity>(paid))
        //     .Recover(error => ExecuteActivity<CompensateOrderActivity>(error))
        //     .Ensure(() => LogCompletion())
        //     .Finally(result => PersistAuditLog(result));

        await Task.CompletedTask; // Placeholder

        return Result<OrderResult>.Ok(new OrderResult
        {
            OrderId = input.OrderId,
            Status = "Completed"
        });
    }
}

public record OrderRequest
{
    public required string OrderId { get; init; }
    public decimal Amount { get; init; }
    public required string CustomerId { get; init; }
}

public record OrderResult
{
    public required string OrderId { get; init; }
    public required string Status { get; init; }
    public string? TransactionId { get; init; }
}

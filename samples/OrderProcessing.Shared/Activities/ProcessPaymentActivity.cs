using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Sdk;
using static Hugo.Go;

namespace OrderProcessing.Shared.Activities;

public sealed class ProcessPaymentActivity(ILogger<ProcessPaymentActivity> logger)
    : ActivityBase<OrderRequest, PaymentReceipt>
{
    protected override Task<Result<PaymentReceipt>> ExecuteAsync(
        WorkflowExecutionContext context,
        OrderRequest input,
        CancellationToken cancellationToken)
    {
        return CaptureAsync(
            effectId: $"payment::{context.WorkflowId}",
            effect: async ct =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Processing payment for workflow {WorkflowId} (amount: {Amount})",
                        context.WorkflowId,
                        input.Amount);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);

                var receipt = new PaymentReceipt(
                    TransactionId: $"txn-{Guid.NewGuid():N}",
                    Amount: input.Amount,
                    CapturedAt: context.TimeProvider.GetUtcNow());

                return Ok(receipt);
            },
            cancellationToken);
    }
}

using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Sdk;
using OrderProcessing.Shared.Activities;
using static Hugo.Go;

namespace OrderProcessing.Shared.Workflows;

public sealed class OrderProcessingWorkflow(
    ProcessPaymentActivity paymentActivity,
    ILogger<OrderProcessingWorkflow> logger)
    : WorkflowBase<OrderRequest, OrderResult>
{
    protected override async Task<Result<OrderResult>> ExecuteAsync(
        WorkflowExecutionContext context,
        OrderRequest input,
        CancellationToken cancellationToken)
    {
        var decisionResult = RequireVersion("order-processing.core", 1, 2);
        if (decisionResult.IsFailure)
        {
            return Result.Fail<OrderResult>(decisionResult.Error!);
        }

        context.Tick();
        logger.LogInformation(
            "Starting order workflow {WorkflowId} (run: {RunId})",
            context.WorkflowId,
            context.RunId);

        var paymentResult = await paymentActivity.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        if (paymentResult.IsFailure)
        {
            logger.LogError(
                "Payment failed for workflow {WorkflowId}: {Error}",
                context.WorkflowId,
                paymentResult.Error?.Message);

            return Result.Fail<OrderResult>(paymentResult.Error!);
        }

        var receipt = paymentResult.Value;
        var output = new OrderResult(
            OrderId: input.OrderId,
            Status: "Completed",
            TransactionId: receipt.TransactionId,
            Amount: receipt.Amount);

        context.IncrementReplayCount();
        logger.LogInformation(
            "Completed order workflow {WorkflowId} with transaction {TransactionId}",
            context.WorkflowId,
            receipt.TransactionId);

        return Ok(output);
    }
}

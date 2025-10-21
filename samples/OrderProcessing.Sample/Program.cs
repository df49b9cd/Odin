using OrderProcessing.Sample;

Console.WriteLine("Odin - Order Processing Sample");
Console.WriteLine("================================");
Console.WriteLine();
Console.WriteLine("This sample demonstrates order processing workflows with Odin.");
Console.WriteLine();
Console.WriteLine("TODO: Implement worker registration and workflow execution");
Console.WriteLine("      once Odin SDK is fully implemented.");
Console.WriteLine();

// Example usage (to be implemented):
// var client = new OdinClient("localhost:7233");
// var execution = await client.StartWorkflowAsync<OrderProcessingWorkflow>(
//     new OrderRequest { OrderId = "12345", Amount = 99.99m, CustomerId = "cust-001" },
//     new WorkflowOptions { Namespace = "default", TaskQueue = "orders" });
// Console.WriteLine($"Started workflow: {execution.WorkflowId}");

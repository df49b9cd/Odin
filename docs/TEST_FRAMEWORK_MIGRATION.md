# Test Framework Migration Guide

## Overview

Odin uses modern test frameworks to ensure maintainable and readable tests:

- **xUnit v3**: Next-generation test framework with better async support and performance
- **Shouldly**: Readable assertion library with clear error messages
- **NSubstitute**: Clean and intuitive mocking framework

## Migration from Previous Stack

| Old Framework | New Framework | Version |
|---------------|---------------|---------|
| xUnit v2 (2.9.3) | xUnit v3 | 0.5.0-pre.27 |
| FluentAssertions (6.12.0) | Shouldly | 4.2.1 |
| Moq (4.20.70) | NSubstitute | 5.3.0 |

## xUnit v3 Changes

### Using Statements
```csharp
// Old (xUnit v2)
using Xunit;

// New (xUnit v3)
using Xunit;  // Same namespace, but different package
```

### Attributes
xUnit v3 maintains backward compatibility with v2 attributes:
- `[Fact]` - Single test method
- `[Theory]` - Parameterized test
- `[InlineData]` - Inline test data
- `[MemberData]` - External test data

### Async Tests
xUnit v3 has improved async support and better handling of `ValueTask`:
```csharp
[Fact]
public async Task TestAsync()
{
    var result = await SomeAsyncOperation();
    result.ShouldBe(expectedValue);
}
```

## Shouldly Assertion Syntax

Shouldly provides more readable assertions with clear error messages.

### Basic Assertions
```csharp
// Equality
result.ShouldBe(expected);
result.ShouldNotBe(unexpected);

// Null checks
obj.ShouldBeNull();
obj.ShouldNotBeNull();

// Boolean
condition.ShouldBeTrue();
condition.ShouldBeFalse();

// Numeric comparisons
value.ShouldBeGreaterThan(0);
value.ShouldBeLessThanOrEqualTo(100);

// String assertions
text.ShouldContain("substring");
text.ShouldStartWith("prefix");
text.ShouldEndWith("suffix");
text.ShouldBeEmpty();

// Collection assertions
collection.ShouldContain(item);
collection.ShouldNotContain(item);
collection.ShouldBeEmpty();
collection.ShouldNotBeEmpty();
list.Count.ShouldBe(5);

// Type assertions
obj.ShouldBeOfType<MyType>();
obj.ShouldBeAssignableTo<IMyInterface>();

// Exception assertions
Should.Throw<ArgumentException>(() => Method());
await Should.ThrowAsync<InvalidOperationException>(async () => await AsyncMethod());
```

### Error Messages
Shouldly provides clear, human-readable error messages:
```csharp
result.ShouldBe(5);
// Error: result should be 5 but was 3
```

## NSubstitute Mocking

NSubstitute provides a clean, intuitive API for creating mocks and stubs.

### Creating Substitutes
```csharp
// Interface substitution
var service = Substitute.For<IMyService>();

// Multiple interfaces
var substitute = Substitute.For<IInterface1, IInterface2>();

// Partial substitution (for abstract classes)
var substitute = Substitute.ForPartsOf<AbstractClass>(constructorArgs);
```

### Setting Up Return Values
```csharp
// Simple return
service.GetValue().Returns(42);

// Async returns
service.GetValueAsync().Returns(Task.FromResult(42));
service.GetValueAsync().Returns(ValueTask.FromResult(42));

// Return based on arguments
service.Calculate(Arg.Any<int>()).Returns(x => (int)x[0] * 2);

// Throw exceptions
service.DoSomething().Returns(x => throw new InvalidOperationException());
```

### Argument Matching
```csharp
// Any argument
service.Process(Arg.Any<string>());

// Specific value
service.Process(Arg.Is<string>(s => s.StartsWith("test")));

// Out/ref arguments
service.TryGet(out Arg.Any<int>()).Returns(x => {
    x[0] = 42;
    return true;
});
```

### Verifying Calls
```csharp
// Received exactly once (default)
service.Received().Process(Arg.Any<string>());

// Received specific number of times
service.Received(3).Process("test");

// Did not receive
service.DidNotReceive().Process(Arg.Any<string>());

// Received with specific arguments
service.Received().Process(Arg.Is<string>(s => s.Length > 5));
```

### Clearing Received Calls
```csharp
service.ClearReceivedCalls();
```

## Example Test

Here's a complete example using all three frameworks:

```csharp
using Xunit;
using Shouldly;
using NSubstitute;

namespace Odin.Core.Tests;

public class WorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteWorkflow_WithValidInput_ReturnsSuccessResult()
    {
        // Arrange
        var historyService = Substitute.For<IHistoryService>();
        var executor = new WorkflowExecutor(historyService);
        var request = new StartWorkflowRequest
        {
            WorkflowType = "OrderProcessing",
            Input = "{\"orderId\":123}"
        };

        historyService
            .AppendEventsAsync(Arg.Any<WorkflowId>(), Arg.Any<List<HistoryEvent>>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await executor.ExecuteAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(WorkflowStatus.Completed);
        result.WorkflowId.ShouldNotBeNullOrEmpty();

        // Verify history service was called
        await historyService.Received(1).AppendEventsAsync(
            Arg.Is<WorkflowId>(wf => wf.ToString() == result.WorkflowId),
            Arg.Is<List<HistoryEvent>>(events => events.Count > 0)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteWorkflow_WithInvalidInput_ThrowsArgumentException(string invalidInput)
    {
        // Arrange
        var historyService = Substitute.For<IHistoryService>();
        var executor = new WorkflowExecutor(historyService);
        var request = new StartWorkflowRequest
        {
            WorkflowType = "OrderProcessing",
            Input = invalidInput
        };

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(
            async () => await executor.ExecuteAsync(request)
        );
    }
}
```

## Best Practices

### Test Naming
Use descriptive test names that follow the pattern:
```
MethodName_Scenario_ExpectedBehavior
```

Example:
```csharp
[Fact]
public async Task StartWorkflow_WithValidRequest_CreatesNewExecution()
```

### Arrange-Act-Assert
Structure tests clearly:
```csharp
[Fact]
public async Task TestMethod()
{
    // Arrange
    var service = Substitute.For<IService>();
    var sut = new SystemUnderTest(service);

    // Act
    var result = await sut.ExecuteAsync();

    // Assert
    result.ShouldBe(expected);
    await service.Received().MethodAsync();
}
```

### Async Testing
Always use `async Task` for async tests, never `async void`:
```csharp
// Good
[Fact]
public async Task TestAsync() { }

// Bad
[Fact]
public async void TestAsync() { }
```

### Test Data
For complex test data, use `[MemberData]` or `[ClassData]`:
```csharp
[Theory]
[MemberData(nameof(TestCases))]
public void Test(TestCase testCase)
{
    // Test logic
}

public static IEnumerable<object[]> TestCases()
{
    yield return new object[] { new TestCase { /* ... */ } };
}
```

## Hugo-Specific Testing

### Testing Workflows
When testing Hugo-based workflows:

1. **Test Determinism**: Ensure workflows produce the same results when replayed
2. **Mock Activities**: Activities should be mocked in workflow tests
3. **Test Cancellation**: Verify proper handling of `CancellationToken`
4. **Test Error Handling**: Verify `Result<T>` error propagation

```csharp
[Fact]
public async Task Workflow_Should_Be_Deterministic()
{
    // Arrange
    var store = new DeterministicEffectStore();
    var workflow = new MyWorkflow(store);
    
    // Act - First execution
    var result1 = await workflow.ExecuteAsync(input, CancellationToken.None);
    var history1 = store.GetHistory();
    
    // Act - Replay
    store.Clear();
    var result2 = await workflow.ExecuteAsync(input, CancellationToken.None);
    var history2 = store.GetHistory();
    
    // Assert
    result1.ShouldBe(result2);
    history1.ShouldBe(history2);
}
```

### Testing with Hugo Primitives

```csharp
[Fact]
public async Task WaitGroup_Coordination_Works()
{
    // Arrange
    var wg = new WaitGroup();
    var completed = 0;
    
    // Act
    wg.Add(3);
    for (int i = 0; i < 3; i++)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            Interlocked.Increment(ref completed);
            wg.Done();
        });
    }
    
    await wg.Wait();
    
    // Assert
    completed.ShouldBe(3);
}
```

## Migration Checklist

When writing new tests:

- [ ] Use `xunit.v3` package (implicit via Directory.Build.props)
- [ ] Use Shouldly for assertions (`result.ShouldBe(expected)`)
- [ ] Use NSubstitute for mocking (`Substitute.For<IInterface>()`)
- [ ] Follow Arrange-Act-Assert pattern
- [ ] Use descriptive test names
- [ ] Test async operations with `async Task`
- [ ] Verify determinism for workflows
- [ ] Test cancellation scenarios
- [ ] Test error handling with `Result<T>`

## Additional Resources

- [xUnit v3 Documentation](https://xunit.net/docs/getting-started/v3/cmdline)
- [Shouldly Documentation](https://docs.shouldly.org/)
- [NSubstitute Documentation](https://nsubstitute.github.io/help.html)
- [Hugo Library Documentation](https://github.com/df49b9cd/Hugo)

## Package Versions

Current versions managed in `Directory.Packages.props`:

```xml
<PackageVersion Include="xunit.v3" Version="0.5.0-pre.27" />
<PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0-pre.35" />
<PackageVersion Include="Shouldly" Version="4.2.1" />
<PackageVersion Include="NSubstitute" Version="5.3.0" />
<PackageVersion Include="coverlet.collector" Version="6.0.4" />
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
```

All test projects automatically receive these packages via `Directory.Build.props` when `IsTestProject=true`.

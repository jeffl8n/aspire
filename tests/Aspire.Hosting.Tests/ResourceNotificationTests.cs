// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Aspire.Hosting.Tests;

public class ResourceNotificationTests
{
    [Fact]
    public void InitialStateCanBeSpecified()
    {
        var builder = DistributedApplication.CreateBuilder();

        var custom = builder.AddResource(new CustomResource("myResource"))
            .WithEndpoint(name: "ep", scheme: "http", port: 8080)
            .WithEnvironment("x", "1000")
            .WithInitialState(new()
            {
                ResourceType = "MyResource",
                Properties = [new("A", "B")],
            });

        var annotation = custom.Resource.Annotations.OfType<ResourceSnapshotAnnotation>().SingleOrDefault();

        Assert.NotNull(annotation);

        var state = annotation.InitialSnapshot;

        Assert.Equal("MyResource", state.ResourceType);
        Assert.Empty(state.EnvironmentVariables);
        Assert.Collection(state.Properties, c =>
        {
            Assert.Equal("A", c.Name);
            Assert.Equal("B", c.Value);
        });
    }

    [Fact]
    public async Task ResourceUpdatesAreQueued()
    {
        var resource = new CustomResource("myResource");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        async Task<List<ResourceEvent>> GetValuesAsync(CancellationToken cancellationToken)
        {
            var values = new List<ResourceEvent>();

            await foreach (var item in notificationService.WatchAsync(cancellationToken))
            {
                values.Add(item);

                if (values.Count == 2)
                {
                    break;
                }
            }

            return values;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var enumerableTask = GetValuesAsync(cts.Token);

        await notificationService.PublishUpdateAsync(resource, state => state with { Properties = state.Properties.Add(new("A", "value")) });

        await notificationService.PublishUpdateAsync(resource, state => state with { Properties = state.Properties.Add(new("B", "value")) });

        var values = await enumerableTask;

        Assert.Collection(values,
            c =>
            {
                Assert.Equal(resource, c.Resource);
                Assert.Equal("myResource", c.ResourceId);
                Assert.Equal("CustomResource", c.Snapshot.ResourceType);
                Assert.Equal("value", c.Snapshot.Properties.Single(p => p.Name == "A").Value);
            },
            c =>
            {
                Assert.Equal(resource, c.Resource);
                Assert.Equal("myResource", c.ResourceId);
                Assert.Equal("CustomResource", c.Snapshot.ResourceType);
                Assert.Equal("value", c.Snapshot.Properties.Single(p => p.Name == "B").Value);
            });
    }

    [Fact]
    public async Task WatchingAllResourcesNotifiesOfAnyResourceChange()
    {
        var resource1 = new CustomResource("myResource1");
        var resource2 = new CustomResource("myResource2");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        async Task<List<ResourceEvent>> GetValuesAsync(CancellationToken cancellation)
        {
            var values = new List<ResourceEvent>();

            await foreach (var item in notificationService.WatchAsync(cancellation))
            {
                values.Add(item);

                if (values.Count == 3)
                {
                    break;
                }
            }

            return values;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var enumerableTask = GetValuesAsync(cts.Token);

        await notificationService.PublishUpdateAsync(resource1, state => state with { Properties = state.Properties.Add(new("A", "value")) });

        await notificationService.PublishUpdateAsync(resource2, state => state with { Properties = state.Properties.Add(new("B", "value")) });

        await notificationService.PublishUpdateAsync(resource1, "replica1", state => state with { Properties = state.Properties.Add(new("C", "value")) });

        var values = await enumerableTask;

        Assert.Collection(values,
            c =>
            {
                Assert.Equal(resource1, c.Resource);
                Assert.Equal("myResource1", c.ResourceId);
                Assert.Equal("CustomResource", c.Snapshot.ResourceType);
                Assert.Equal("value", c.Snapshot.Properties.Single(p => p.Name == "A").Value);
            },
            c =>
            {
                Assert.Equal(resource2, c.Resource);
                Assert.Equal("myResource2", c.ResourceId);
                Assert.Equal("CustomResource", c.Snapshot.ResourceType);
                Assert.Equal("value", c.Snapshot.Properties.Single(p => p.Name == "B").Value);
            },
            c =>
            {
                Assert.Equal(resource1, c.Resource);
                Assert.Equal("replica1", c.ResourceId);
                Assert.Equal("CustomResource", c.Snapshot.ResourceType);
                Assert.Equal("value", c.Snapshot.Properties.Single(p => p.Name == "C").Value);
            });
    }

    [Fact]
    public async Task WaitingOnResourceReturnsWhenResourceReachesTargetState()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = notificationService.WaitForResourceAsync("myResource1", "SomeState", cts.Token);

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });
        await waitTask;

        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitingOnResourceReturnsWhenResourceReachesTargetStateWithDifferentCasing()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = notificationService.WaitForResourceAsync("MYreSouRCe1", "sOmeSTAtE", cts.Token);

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });
        await waitTask;

        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitingOnResourceReturnsImmediatelyWhenResourceIsInTargetStateAlready()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        // Publish the state update first
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = notificationService.WaitForResourceAsync("myResource1", "SomeState", cts.Token);

        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitingOnResourceReturnsWhenResourceReachesRunningStateIfNoTargetStateSupplied()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = notificationService.WaitForResourceAsync("myResource1", targetState: null, cancellationToken: cts.Token);

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = KnownResourceStates.Running });
        await waitTask;

        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitingOnResourceReturnsCorrectStateWhenResourceReachesOneOfTargetStatesBeforeCancellation()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = notificationService.WaitForResourceAsync("myResource1", ["SomeState", "SomeOtherState"], cts.Token);

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeOtherState" });
        var reachedState = await waitTask;

        Assert.Equal("SomeOtherState", reachedState);
    }

    [Fact]
    public async Task WaitingOnResourceReturnsCorrectStateWhenResourceReachesOneOfTargetStates()
    {
        var resource1 = new CustomResource("myResource1");

        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        var waitTask = notificationService.WaitForResourceAsync("myResource1", ["SomeState", "SomeOtherState"], default);

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeOtherState" });
        var reachedState = await waitTask;

        Assert.Equal("SomeOtherState", reachedState);
    }

    [Fact]
    public async Task WaitingOnResourceThrowsOperationCanceledExceptionIfResourceDoesntReachStateBeforeCancellationTokenSignaled()
    {
        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), new TestHostApplicationLifetime());

        using var cts = new CancellationTokenSource();
        var waitTask = notificationService.WaitForResourceAsync("myResource1", "SomeState", cts.Token);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await waitTask;
        });
    }

    [Fact]
    public async Task WaitingOnResourceThrowsOperationCanceledExceptionIfResourceDoesntReachStateBeforeApplicationStoppingCancellationTokenSignaled()
    {
        using var hostApplicationLifetime = new TestHostApplicationLifetime();
        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), hostApplicationLifetime);

        var waitTask = notificationService.WaitForResourceAsync("myResource1", "SomeState");
        hostApplicationLifetime.StopApplication();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await waitTask;
        });
    }

    [Fact]
    public async Task WaitingOnResourceThrowsOperationCanceledExceptionIfResourceDoesntReachStateBeforeCancellationTokenSignalledWhenApplicationStoppingTokenExists()
    {
        using var hostApplicationLifetime = new TestHostApplicationLifetime();
        var notificationService = new ResourceNotificationService(new NullLogger<ResourceNotificationService>(), hostApplicationLifetime);

        using var cts = new CancellationTokenSource();
        var waitTask = notificationService.WaitForResourceAsync("myResource1", "SomeState", cts.Token);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await waitTask;
        });
    }

    [Fact]
    public async Task PublishLogsStateTextChangesCorrectly()
    {
        var resource1 = new CustomResource("resource1");
        var logger = new FakeLogger<ResourceNotificationService>();
        var notificationService = new ResourceNotificationService(logger, new TestHostApplicationLifetime());

        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });

        var logs = logger.Collector.GetSnapshot();

        // Initial state text, log just the new state
        Assert.Single(logs.Where(l => l.Level == LogLevel.Debug));
        Assert.Contains(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state: SomeState"));

        logger.Collector.Clear();

        // Same state text as previous state, no log
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });

        logs = logger.Collector.GetSnapshot();

        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug);
        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state: SomeState"));

        logger.Collector.Clear();

        // Different state text, log the transition from the previous state to the new state
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "NewState" });

        logs = logger.Collector.GetSnapshot();

        Assert.Single(logs.Where(l => l.Level == LogLevel.Debug));
        Assert.Contains(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state: SomeState -> NewState"));

        logger.Collector.Clear();

        // Null state text, no log
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = null });

        logs = logger.Collector.GetSnapshot();

        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug);
        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state:"));

        logger.Collector.Clear();

        // Empty state text, no log
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "" });

        logs = logger.Collector.GetSnapshot();

        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug);
        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state:"));

        logger.Collector.Clear();

        // White space state text, no log
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = " " });

        logs = logger.Collector.GetSnapshot();

        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug);
        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Debug && l.Message.Contains("Resource resource1/resource1 changed state:"));

        logger.Collector.Clear();
    }

    [Fact]
    public async Task PublishLogsTraceStateDetailsCorrectly()
    {
        var resource1 = new CustomResource("resource1");
        var logger = new FakeLogger<ResourceNotificationService>();
        var notificationService = new ResourceNotificationService(logger, new TestHostApplicationLifetime());

        var createdDate = DateTime.Now;
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { CreationTimeStamp = createdDate });
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { State = "SomeState" });
        await notificationService.PublishUpdateAsync(resource1, snapshot => snapshot with { ExitCode = 0 });

        var logs = logger.Collector.GetSnapshot();

        Assert.Single(logs.Where(l => l.Level == LogLevel.Debug));
        Assert.Equal(3, logs.Where(l => l.Level == LogLevel.Trace).Count());
        Assert.Contains(logs, l => l.Level == LogLevel.Trace && l.Message.Contains("Resource resource1/resource1 update published:") && l.Message.Contains($"CreationTimeStamp = {createdDate:s}"));
        Assert.Contains(logs, l => l.Level == LogLevel.Trace && l.Message.Contains("Resource resource1/resource1 update published:") && l.Message.Contains("State = { Text = SomeState"));
        Assert.Contains(logs, l => l.Level == LogLevel.Trace && l.Message.Contains("Resource resource1/resource1 update published:") && l.Message.Contains("ExitCode = 0"));
    }

    private sealed class CustomResource(string name) : Resource(name),
        IResourceWithEnvironment,
        IResourceWithConnectionString,
        IResourceWithEndpoints
    {
        public ReferenceExpression ConnectionStringExpression =>
            ReferenceExpression.Create($"CustomConnectionString");
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stoppingCts = new();

        public TestHostApplicationLifetime()
        {
            ApplicationStopping = _stoppingCts.Token;
        }

        public CancellationToken ApplicationStarted { get; }
        public CancellationToken ApplicationStopped { get; }
        public CancellationToken ApplicationStopping { get; }

        public void StopApplication()
        {
            _stoppingCts.Cancel();
        }

        public void Dispose()
        {
            _stoppingCts.Dispose();
        }
    }
}

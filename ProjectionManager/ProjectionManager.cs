using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using PatientManagement.Framework.Helpers;
namespace ProjectionManager;

class ProjectionManager
{
    readonly EventStoreClient _eventStore;

    readonly List<IProjection> _projections;

    readonly ConnectionFactory _connectionFactory;

    public ProjectionManager(
        EventStoreClient eventStore,
        ConnectionFactory connectionFactory,
        List<IProjection> projections)
    {
        _projections = projections;
        _eventStore = eventStore;
        _connectionFactory = connectionFactory;
    }
    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var projection in _projections)
        {
            await StartProjectionAsync(projection, ct);
        }
    }

    async Task StartProjectionAsync(IProjection projection, CancellationToken ct)
    {
        var checkpoint = GetPosition(projection.GetType());

        if (checkpoint.HasValue)
        {
            await _eventStore.SubscribeToAllAsync(
                checkpoint.Value,
                EventAppeared(projection),
                false,
                ConnectionDropped(projection, ct)!,
                cancellationToken: ct
            );
        }
        else
        {
            await _eventStore.SubscribeToAllAsync(
                EventAppeared(projection),
                false,
                ConnectionDropped(projection, ct)!,
                cancellationToken: ct
            );
        }
    }

    private Action<StreamSubscription, SubscriptionDroppedReason, Exception> ConnectionDropped(
        IProjection projection,
        CancellationToken ct)
    {
        return (_, reason, exc) =>
        {
            Console.WriteLine(
                $"Projection {projection.GetType().Name} dropped with {reason}, Exception: {exc.Message} {exc.StackTrace}");
            
            Resubscribe(projection, ct);
        };
    }
    
    private readonly object resubscribeLock = new();
    private void Resubscribe(IProjection projection, CancellationToken ct)
    {
        while (true)
        {
            var resubscribed = false;
            try
            {
                Monitor.Enter(resubscribeLock);

                using (NoSynchronizationContextScope.Enter())
                {
                    StartProjectionAsync(projection, ct).Wait(ct);
                }

                resubscribed = true;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Failed to resubscribe with '{exception.Message}{exception.StackTrace}'");
            }
            finally
            {
                Monitor.Exit(resubscribeLock);
            }

            if (resubscribed)
                break;

            Thread.Sleep(1000);
        }
    }
    
    public static class NoSynchronizationContextScope
    {
        public static Disposable Enter()
        {
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            return new Disposable(context);
        }

        public struct Disposable: IDisposable
        {
            private readonly SynchronizationContext? synchronizationContext;

            public Disposable(SynchronizationContext? synchronizationContext)
            {
                this.synchronizationContext = synchronizationContext;
            }

            public void Dispose() =>
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        }
    }

    Func<StreamSubscription, ResolvedEvent, CancellationToken, Task> EventAppeared(IProjection projection)
    {
        return (s, e, ct) =>
        {
            Console.WriteLine(
                $"Handling Projection {projection.GetType().Name} event {e.Event.EventType} id: {e.Event.EventId}");

            if (!projection.CanHandle(e.Event.EventType))
            {
                return Task.CompletedTask;
            }

            var deserializedEvent = e.Deserialize();
            projection.Handle(e.Event.EventType, deserializedEvent);

            UpdatePosition(projection.GetType(), e.OriginalPosition!.Value);
            return Task.CompletedTask;
        };
    }

    Position? GetPosition(Type projection)
    {
        using (var session = _connectionFactory.Connect())
        {
            var state = session.Load<ProjectionState>(projection.Name);

            if (state == null)
            {
                return null;
            }

            return new Position(state.CommitPosition, state.PreparePosition);
        }
    }

    void UpdatePosition(Type projection, Position position)
    {
        using (var session = _connectionFactory.Connect())
        {
            var state = session.Load<ProjectionState>(projection.Name);

            if (state == null)
            {
                session.Store(new ProjectionState
                {
                    Id = projection.Name,
                    CommitPosition = position.CommitPosition,
                    PreparePosition = position.PreparePosition
                });
            }
            else
            {
                state.CommitPosition = position.CommitPosition;
                state.PreparePosition = position.PreparePosition;
            }

            session.SaveChanges();
        }
    }
}

public class ProjectionState
{
    public string Id { get; set; } = default!;

    public ulong CommitPosition { get; set; }

    public ulong PreparePosition { get; set; }
}
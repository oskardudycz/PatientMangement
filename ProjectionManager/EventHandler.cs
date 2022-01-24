using System;

namespace ProjectionManager
{
    public record EventHandler(
        string EventType,
        Action<object> Handler
    );
}
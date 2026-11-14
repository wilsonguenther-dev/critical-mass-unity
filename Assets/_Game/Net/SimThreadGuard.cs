using System;
using System.Threading;

namespace CriticalMass.Net
{
    /// <summary>
    /// online-rooms-3: the Sim keeps static scratch buffers (Combat, Collide, BotDirector and others), so two worlds must
    /// never be stepped by two threads at the same moment; the first game-to-game test did exactly that (an online peer on
    /// a worker thread beside the offline match on the main thread) and drifted at tick 30, then threw an index error.
    /// Every caller that steps a world (the lockstep session, the game's SimDriver) wraps the step in Enter(); a second
    /// thread stepping while the first is inside throws at once with a plain message instead of silently corrupting.
    /// (It lives here, not in the Sim, because the Sim may not use System.Threading.) No allocation.
    /// </summary>
    public static class SimThreadGuard
    {
        private static int _owner, _depth;

        public static Scope Enter()
        {
            int me = Thread.CurrentThread.ManagedThreadId;
            int prev = Interlocked.CompareExchange(ref _owner, me, 0);
            if (prev != 0 && prev != me)
                throw new InvalidOperationException("Two threads stepped the Sim at once (thread " + me + " while " + prev +
                                                    " was inside). The Sim is single-threaded: step every world from one thread.");
            _depth++;
            return new Scope(true);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly bool _held;
            public Scope(bool held) { _held = held; }
            public void Dispose()
            {
                if (!_held) return;
                if (--_depth == 0) Volatile.Write(ref _owner, 0);
            }
        }
    }
}

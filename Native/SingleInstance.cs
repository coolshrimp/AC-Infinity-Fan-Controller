using System;
using System.Threading;

namespace ACInfinityDesktop;

// Kernel objects keep the lock shared across executable copies in this session.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }

    public SingleInstance(string name = "ACInfinityDesktop.Native")
    {
        // Create the event first: an early second launch can signal before the UI exists.
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + name + ".Activate");
        mutex = new Mutex(false, "Local\\" + name);
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary) activation.Set();
    }

    public void Listen(Action showWindow)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen.");
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => showWindow(), null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        activation.Dispose();
    }
}

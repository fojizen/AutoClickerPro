using System.Threading;

namespace AutoClickerPro.Helpers;

/// <summary>
/// Enforces a single running instance of the application using a named system Mutex.
/// Running two instances would try to register the same global hotkeys and low-level mouse
/// hooks twice, causing silent failures or conflicting click behavior, so we prevent it outright.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "AutoClickerPro_SingleInstance_Mutex_9F3E7B2C";
    private readonly Mutex _mutex;
    private readonly bool _isOwned;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out _isOwned);
    }

    /// <summary>True if this process is the first (and only) instance.</summary>
    public bool IsFirstInstance => _isOwned;

    public void Dispose()
    {
        if (_isOwned)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned, ignore */ }
        }
        _mutex.Dispose();
    }
}

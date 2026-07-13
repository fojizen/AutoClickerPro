using System.Diagnostics;
using AutoClickerPro.Models;

namespace AutoClickerPro.Services;

/// <summary>
/// Runs the actual click loops. Each mouse button gets its own independent, cancellable
/// background loop so left/right clicking can be started, paused, or stopped separately
/// without affecting the other.
///
/// Design notes for low CPU usage:
/// - Uses Task.Delay (not a spin-wait or Sleep-based tight loop), which is non-blocking and
///   lets the thread pool stay idle between clicks.
/// - Only one Task per active button is alive at a time.
/// - Real-time CPS is measured with a lightweight rolling counter, not per-click allocations.
/// </summary>
public sealed class ClickEngine : IDisposable
{
    private readonly Random _rng = new();
    private readonly Dictionary<MouseButtonTarget, ButtonRuntime> _runtimes = new();

    public event Action<MouseButtonTarget, double>? RealTimeCpsUpdated;
    public event Action<MouseButtonTarget, MacroState>? StateChanged;

    public ClickEngine()
    {
        _runtimes[MouseButtonTarget.Left] = new ButtonRuntime();
        _runtimes[MouseButtonTarget.Right] = new ButtonRuntime();
    }

    public MacroState GetState(MouseButtonTarget target) => _runtimes[target].State;

    /// <summary>Starts (or resumes) continuous clicking for the given button using the supplied settings.</summary>
    public void Start(ClickSettings settings)
    {
        var rt = _runtimes[settings.Target];

        if (rt.State == MacroState.Running)
            return;

        if (rt.State == MacroState.Paused)
        {
            // Resume: flip the pause flag back and wake the loop immediately via the signal -
            // it may currently be blocked in ResumeSignal.WaitAsync() below, and Release() here
            // unblocks it the instant this hotkey fires rather than after some polling interval.
            rt.IsPaused = false;
            SetState(settings.Target, MacroState.Running);
            rt.ResumeSignal.Release();
            return;
        }

        // Fresh start: spin up a new cancellable loop.
        rt.Cts?.Cancel();
        rt.Cts?.Dispose();
        rt.Cts = new CancellationTokenSource();
        rt.IsPaused = false;

        SetState(settings.Target, MacroState.Running);

        _ = RunLoopAsync(settings, rt, rt.Cts.Token);
    }

    /// <summary>Pauses clicking without discarding the running loop (cheap resume).</summary>
    public void Pause(MouseButtonTarget target)
    {
        var rt = _runtimes[target];
        if (rt.State != MacroState.Running) return;

        rt.IsPaused = true;
        SetState(target, MacroState.Paused);
    }

    /// <summary>Fully stops clicking and tears down the loop.</summary>
    public void Stop(MouseButtonTarget target)
    {
        var rt = _runtimes[target];
        rt.Cts?.Cancel();
        rt.IsPaused = false;
        SetState(target, MacroState.Stopped);
        RealTimeCpsUpdated?.Invoke(target, 0);
    }

    public void StopAll()
    {
        Stop(MouseButtonTarget.Left);
        Stop(MouseButtonTarget.Right);
    }

    private void SetState(MouseButtonTarget target, MacroState state)
    {
        _runtimes[target].State = state;
        StateChanged?.Invoke(target, state);
    }

    private async Task RunLoopAsync(ClickSettings settings, ButtonRuntime rt, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        int clicksInWindow = 0;
        double lastReport = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (rt.IsPaused)
                {
                    // Block until Start() calls ResumeSignal.Release() or Stop() cancels the
                    // token - no fixed polling interval, so a Hold-Mode release-then-press cycle
                    // (which goes through Pause()/this Resume path) resumes clicking the instant
                    // the hotkey is pressed again instead of waiting up to the old 50ms poll tick.
                    await rt.ResumeSignal.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    NativeMethods.SendClick(settings.Target == MouseButtonTarget.Left);
                }
                catch (Exception ex)
                {
                    // A single failed native call shouldn't kill the whole loop or crash the app -
                    // log via Debug output and keep going; if it's systemic, Stop() will still work.
                    System.Diagnostics.Debug.WriteLine($"SendClick failed: {ex.Message}");
                }

                clicksInWindow++;

                // Report real-time CPS about 4x/second so the UI feels live without excessive dispatcher churn.
                if (sw.Elapsed.TotalSeconds - lastReport >= 0.25)
                {
                    double actualCps = clicksInWindow / (sw.Elapsed.TotalSeconds - lastReport);
                    RealTimeCpsUpdated?.Invoke(settings.Target, actualCps);
                    clicksInWindow = 0;
                    lastReport = sw.Elapsed.TotalSeconds;
                }

                double effectiveCps = settings.Cps;
                if (settings.UseRandomVariation && settings.RandomVariation > 0)
                {
                    // Jitter within +/- RandomVariation, clamped so we never hit <=0 CPS (which would mean infinite delay).
                    double jitter = (_rng.NextDouble() * 2 - 1) * settings.RandomVariation;
                    effectiveCps = Math.Max(0.5, settings.Cps + jitter);
                }

                // Manual delay is a fixed extra wait added on top of the CPS-derived interval,
                // applied after random CPS variation: total = (1000 / effectiveCps) + DelayMs.
                // DelayMs == 0 reduces exactly to the pre-existing behaviour.
                double cpsIntervalMs = 1000.0 / effectiveCps;
                int delayMs = (int)Math.Max(1, Math.Round(cpsIntervalMs + settings.DelayMs));
                await Task.Delay(delayMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop() - covers both Task.Delay's TaskCanceledException and the
            // plain OperationCanceledException that ResumeSignal.WaitAsync(token) throws.
        }
    }

    public void Dispose()
    {
        foreach (var rt in _runtimes.Values)
        {
            rt.Cts?.Cancel();
            rt.Cts?.Dispose();
            rt.ResumeSignal.Dispose();
        }
    }

    /// <summary>Per-button mutable runtime state, kept out of the public API surface.</summary>
    private sealed class ButtonRuntime
    {
        public CancellationTokenSource? Cts;
        public bool IsPaused;
        public MacroState State = MacroState.Stopped;

        // Starts at 0 (nothing to wait for). Pause->Resume is strictly 1:1 (both Pause() and
        // the Resume branch of Start() are guarded by State checks), so exactly one Release()
        // ever matches exactly one WaitAsync() - no risk of the count exceeding the max of 1.
        public readonly SemaphoreSlim ResumeSignal = new(0, 1);
    }
}

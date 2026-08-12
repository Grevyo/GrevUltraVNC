using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using NAudio.Wave;

namespace GrevUltraVNC.Services;

public sealed class RemoteAudioPlaybackService : IDisposable
{
    private readonly Machine _machine;
    private readonly string _controllerId;
    private readonly GrevAgentClient _agent = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _buffer;
    private long _lastSequence;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public bool IsRunning => _cancellation is not null;

    public RemoteAudioPlaybackService(Machine machine, string controllerId)
    {
        _machine = machine;
        _controllerId = controllerId;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_cancellation is not null) return;

        var start = await _agent.RunAudioAsync(
            _machine,
            new AgentAudioRequest("start", _controllerId, _lastSequence),
            cancellationToken);

        if (!start.Success)
            throw new InvalidOperationException(start.Message);

        if (start.SampleRate > 0 && start.Channels > 0)
            EnsurePlayback(start.SampleRate, start.Channels);

        _cancellation = new CancellationTokenSource();
        _loop = RunLoopAsync(_cancellation.Token);
        StatusChanged?.Invoke(start.Message);
    }

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        _cancellation = null;

        if (cancellation is not null)
        {
            cancellation.Cancel();
            try
            {
                if (_loop is not null)
                    await _loop;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
                _loop = null;
            }
        }

        try
        {
            await _agent.RunAudioAsync(
                _machine,
                new AgentAudioRequest("stop", _controllerId, _lastSequence),
                CancellationToken.None);
        }
        catch
        {
            // Listener presence expires automatically if the target goes away.
        }

        StopPlayback();
        StatusChanged?.Invoke("Computer audio off.");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _agent.RunAudioAsync(
                    _machine,
                    new AgentAudioRequest("read", _controllerId, _lastSequence),
                    cancellationToken);

                if (!response.Success)
                    throw new InvalidOperationException(response.Message);

                if (response.SampleRate > 0 && response.Channels > 0)
                    EnsurePlayback(response.SampleRate, response.Channels);

                if (!string.IsNullOrWhiteSpace(response.DataBase64) && _buffer is not null)
                {
                    var audio = Convert.FromBase64String(response.DataBase64);
                    if (audio.Length > 0)
                        _buffer.AddSamples(audio, 0, audio.Length);
                }

                _lastSequence = Math.Max(_lastSequence, response.LastSequence);
                StatusChanged?.Invoke(response.Message);
                await Task.Delay(55, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Audio reconnecting · {ex.Message}");
                await Task.Delay(800, cancellationToken);
            }
        }
    }

    private void EnsurePlayback(int sampleRate, int channels)
    {
        if (_buffer is not null &&
            _buffer.WaveFormat.SampleRate == sampleRate &&
            _buffer.WaveFormat.Channels == channels)
            return;

        StopPlayback();

        var format = new WaveFormat(sampleRate, 16, Math.Clamp(channels, 1, 2));
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

        _output = new WaveOutEvent
        {
            DesiredLatency = 120,
            NumberOfBuffers = 3,
            Volume = 0.85f
        };
        _output.Init(_buffer);
        _output.Play();
    }

    private void StopPlayback()
    {
        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _output = null;
        _buffer = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RemoteAudioPlaybackService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        _agent.Dispose();
    }
}

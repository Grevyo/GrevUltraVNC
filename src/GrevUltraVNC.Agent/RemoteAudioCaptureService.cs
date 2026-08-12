using GrevUltraVNC.Contracts;
using NAudio.Wave;

namespace GrevUltraVNC.Agent;

public sealed class RemoteAudioCaptureService : IDisposable
{
    private static readonly TimeSpan ListenerTimeout = TimeSpan.FromSeconds(8);
    private const int MaxBufferedBytes = 2 * 1024 * 1024;
    private const int MaxResponseBytes = 192 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AudioChunk> _chunks = [];
    private readonly Timer _cleanupTimer;

    private WasapiLoopbackCapture? _capture;
    private long _nextSequence;
    private int _bufferedBytes;
    private int _sampleRate;
    private int _channels;
    private string? _lastError;

    public RemoteAudioCaptureService()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public AgentAudioResponse Execute(AgentAudioRequest request)
    {
        lock (_gate)
        {
            var controllerId = request.ControllerId?.Trim() ?? string.Empty;
            if (controllerId.Length is < 1 or > 100)
                return Response(false, "Controller identity is invalid.", request.LastSequence, []);

            CleanupLocked();
            var operation = request.Operation?.Trim().ToLowerInvariant() ?? string.Empty;

            if (operation == "stop")
            {
                _listeners.Remove(controllerId);
                StopIfUnusedLocked();
                return Response(true, "Computer audio stopped.", request.LastSequence, []);
            }

            if (operation is not ("start" or "read"))
                return Response(false, "Unknown audio operation.", request.LastSequence, []);

            _listeners[controllerId] = DateTimeOffset.UtcNow;
            if (!EnsureCaptureLocked(out var error))
                return Response(false, error, request.LastSequence, []);

            if (operation == "start")
                return Response(true, "Computer audio connected.", request.LastSequence, []);

            var selected = new List<AudioChunk>();
            var bytes = 0;
            foreach (var chunk in _chunks.Where(item => item.Sequence > request.LastSequence))
            {
                if (bytes > 0 && bytes + chunk.Data.Length > MaxResponseBytes) break;
                selected.Add(chunk);
                bytes += chunk.Data.Length;
                if (bytes >= MaxResponseBytes) break;
            }

            if (selected.Count == 0)
                return Response(true, "Waiting for computer audio…", request.LastSequence, []);

            var combined = new byte[selected.Sum(item => item.Data.Length)];
            var offset = 0;
            foreach (var chunk in selected)
            {
                Buffer.BlockCopy(chunk.Data, 0, combined, offset, chunk.Data.Length);
                offset += chunk.Data.Length;
            }

            return Response(true, "Computer audio streaming.", selected[^1].Sequence, combined);
        }
    }

    private bool EnsureCaptureLocked(out string error)
    {
        error = string.Empty;
        if (_capture is not null) return true;

        try
        {
            _lastError = null;
            var capture = new WasapiLoopbackCapture();
            _sampleRate = capture.WaveFormat.SampleRate;
            _channels = Math.Clamp(capture.WaveFormat.Channels, 1, 2);
            capture.DataAvailable += Capture_DataAvailable;
            capture.RecordingStopped += Capture_RecordingStopped;
            capture.StartRecording();
            _capture = capture;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            error = $"Windows audio capture could not start: {ex.Message}";
            return false;
        }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded <= 0) return;

        try
        {
            var pcm = ConvertToPcm16(capture.WaveFormat, e.Buffer, e.BytesRecorded, _channels);
            if (pcm.Length == 0) return;

            lock (_gate)
            {
                var chunk = new AudioChunk(Interlocked.Increment(ref _nextSequence), pcm);
                _chunks.Add(chunk);
                _bufferedBytes += pcm.Length;

                while (_chunks.Count > 0 && _bufferedBytes > MaxBufferedBytes)
                {
                    _bufferedBytes -= _chunks[0].Data.Length;
                    _chunks.RemoveAt(0);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _lastError = ex.Message;
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            if (e.Exception is not null)
                _lastError = e.Exception.Message;

            if (ReferenceEquals(_capture, sender))
            {
                _capture?.Dispose();
                _capture = null;
            }
        }
    }

    private static byte[] ConvertToPcm16(WaveFormat format, byte[] buffer, int bytesRecorded, int outputChannels)
    {
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frameBytes = bytesPerSample * Math.Max(1, format.Channels);
        var frameCount = bytesRecorded / frameBytes;
        if (frameCount <= 0) return [];

        var output = new byte[frameCount * outputChannels * 2];
        var outputOffset = 0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * frameBytes;
            for (var channel = 0; channel < outputChannels; channel++)
            {
                var sourceOffset = frameOffset + channel * bytesPerSample;
                var sample = ReadSample(format, buffer, sourceOffset);
                var pcm = (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
                output[outputOffset++] = (byte)(pcm & 0xFF);
                output[outputOffset++] = (byte)((pcm >> 8) & 0xFF);
            }
        }

        return output;
    }

    private static double ReadSample(WaveFormat format, byte[] buffer, int offset)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            return Math.Clamp(BitConverter.ToSingle(buffer, offset), -1f, 1f);

        if (format.Encoding == WaveFormatEncoding.Pcm)
        {
            return format.BitsPerSample switch
            {
                16 => BitConverter.ToInt16(buffer, offset) / 32768d,
                24 => ReadPcm24(buffer, offset) / 8388608d,
                32 => BitConverter.ToInt32(buffer, offset) / 2147483648d,
                _ => 0d
            };
        }

        return 0d;
    }

    private static int ReadPcm24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value;
    }

    private AgentAudioResponse Response(bool success, string message, long lastSequence, byte[] data)
    {
        var active = _capture is not null && _listeners.Count > 0;
        if (!success && !string.IsNullOrWhiteSpace(_lastError))
            message = $"{message} ({_lastError})";

        return new AgentAudioResponse(
            success,
            message,
            active,
            _sampleRate,
            _channels,
            16,
            data.Length == 0 ? string.Empty : Convert.ToBase64String(data),
            lastSequence);
    }

    private void Cleanup()
    {
        lock (_gate)
        {
            CleanupLocked();
            StopIfUnusedLocked();
        }
    }

    private void CleanupLocked()
    {
        var cutoff = DateTimeOffset.UtcNow - ListenerTimeout;
        foreach (var id in _listeners.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
            _listeners.Remove(id);
    }

    private void StopIfUnusedLocked()
    {
        if (_listeners.Count != 0 || _capture is null) return;

        var capture = _capture;
        _capture = null;
        try
        {
            capture.DataAvailable -= Capture_DataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            capture.Dispose();
            _chunks.Clear();
            _bufferedBytes = 0;
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        lock (_gate)
        {
            _listeners.Clear();
            StopIfUnusedLocked();
        }
    }

    private sealed record AudioChunk(long Sequence, byte[] Data);
}

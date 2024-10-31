using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tizen;

public class AudioPlayer
{
    private int accLength = 0;
    private int streamIndex = 0;
    private bool isStreaming = false;

    private readonly SynchronizationContext _syncContext = SynchronizationContext.Current;
    private MemoryStream audioStream;
    private List<MemoryStream> streamList;

    private AudioDucking audioDucking;
    private AudioPlayback audioPlayback;
    private AudioStreamPolicy audioStreamPolicy;

    private Timer bufferChecker;

    private AudioPlayerState currentAudioPlayerState = AudioPlayerState.Unavailable;
    internal event EventHandler<AudioPlayerChangedEventArgs> AudioPlayerStateChanged;

    public AudioPlayer(AudioOptions audioOptions = null)
    {
        CurrentAudioOptions = audioOptions ?? DefaultAudioOptions;
        Initialize();
        ConfigureAudioDucking();
        AttachEventHandlers();
    }

    private void Initialize()
    {
        audioStream = new MemoryStream();
        streamList = new List<MemoryStream>();
        audioStreamPolicy = new AudioStreamPolicy(CurrentAudioOptions.StreamType);
        InitAudio(CurrentAudioOptions.SampleRate);
        bufferChecker = new Timer(100);
        bufferChecker.Tick += OnBufferChecker;
    }

    private void ConfigureAudioDucking()
    {
        audioDucking = new AudioDucking(CurrentAudioOptions.DuckingTargetStreamType);
        audioDucking.DuckingStateChanged += (sender, arg) =>
        {
            if (arg.IsDucked)
                CurrentAudioPlayerState = AudioPlayerState.Playing;
        };
    }

    private void AttachEventHandlers()
    {
        AudioPlayerStateChanged += OnStateChanged;
    }

    public void InitializeStream()
    {
        streamIndex = 0;
        streamList.Clear();
    }

    public void AddStream(byte[] buffer)
    {
        streamList.Add(new MemoryStream(buffer));
    }

    public bool IsPrepared => streamList.Count > 0;

    public void PlayStreamAudio(int sampleRate = 0)
    {
        isStreaming = true;
        EnsureAudioPlaybackInitialized(sampleRate);
        ActivateAudioDucking();
    }

    public void Play(byte[] audioBytes, int sampleRate = 0)
    {
        if (audioBytes == null)
        {
            Log.Error(LogTag, "Audio data is null.");
            return;
        }

        isStreaming = false;
        InitializeStream();
        streamList.Add(new MemoryStream(audioBytes));
        EnsureAudioPlaybackInitialized(sampleRate);
        ActivateAudioDucking();
    }

    public void Pause() => CurrentAudioPlayerState = AudioPlayerState.Paused;

    public void Stop() => CurrentAudioPlayerState = AudioPlayerState.Stopped;

    public void Destroy()
    {
        DestroyAudioPlayback();
        streamList.Clear();
    }

    public AudioPlayerState CurrentAudioPlayerState
    {
        get => currentAudioPlayerState;
        private set
        {
            if (currentAudioPlayerState == value) return;
            var previousState = currentAudioPlayerState;
            currentAudioPlayerState = value;
            AudioPlayerStateChanged?.Invoke(this, new AudioPlayerChangedEventArgs(previousState, currentAudioPlayerState));
        }
    }

    private bool OnBufferChecker(object source, Timer.TickEventArgs e)
    {
        if (isStreaming && streamList.Count == 0)
            return true;

        if (audioStream != null && audioStream.Position == audioStream.Length && streamIndex >= streamList.Count)
        {
            FinishPlayback();
            return false;
        }

        return true;
    }

    private void OnStateChanged(object sender, AudioPlayerChangedEventArgs stateArgs)
    {
        switch (stateArgs.Current)
        {
            case AudioPlayerState.Playing:
                StartAudioPlayback();
                break;
            case AudioPlayerState.Paused:
                PauseAudioPlayback();
                break;
            case AudioPlayerState.Stopped:
            case AudioPlayerState.Finished:
                StopAudioPlayback();
                break;
        }
    }

    private void OnBufferAvailable(object sender, AudioPlaybackBufferAvailableEventArgs args)
    {
        if (audioStream.Position == audioStream.Length && streamIndex >= streamList.Count)
        {
            _syncContext.Post(_ => FinishPlayback(), null);
            return;
        }

        if (args.Length > 1024)
        {
            int length = Math.Min(args.Length, accLength);
            accLength -= length;
            byte[] buffer = new byte[length];
            audioStream.Read(buffer, 0, length);
            audioPlayback.Write(buffer);
        }
    }

    private void FinishPlayback()
    {
        CurrentAudioPlayerState = AudioPlayerState.Finished;
        Log.Debug(LogTag, "Audio playback finished.");
        bufferChecker?.Stop();
    }

    private void StartAudioPlayback()
    {
        Log.Debug(LogTag, "Audio is playing.");
        bufferChecker?.Start();
        audioPlayback?.Prepare();
    }

    private void PauseAudioPlayback()
    {
        Log.Debug(LogTag, "Audio is paused.");
        bufferChecker?.Stop();
        audioPlayback?.Pause();
        audioPlayback?.Unprepare();
        audioDucking?.DeactivateIfNeeded();
    }

    private void StopAudioPlayback()
    {
        Log.Debug(LogTag, "Audio is stopped.");
        bufferChecker?.Stop();
        audioPlayback?.Pause();
        audioPlayback?.Unprepare();
        audioDucking?.DeactivateIfNeeded();
        ResetPlayback();
    }

    private void ResetPlayback()
    {
        streamIndex = 0;
        audioStream = new MemoryStream();
    }

    private void EnsureAudioPlaybackInitialized(int sampleRate)
    {
        if (audioPlayback == null || audioPlayback.SampleRate != sampleRate)
            InitAudio(sampleRate);
    }

    private void ActivateAudioDucking()
    {
        try
        {
            audioDucking.Activate(CurrentAudioOptions.DuckingDuration, CurrentAudioOptions.DuckingRatio);
        }
        catch (Exception e)
        {
            Log.Error(LogTag, $"Audio ducking activation failed: {e.Message}");
            CurrentAudioPlayerState = AudioPlayerState.Playing;
        }
    }

    private void InitAudio(int sampleRate)
    {
        DestroyAudioPlayback();
        sampleRate = sampleRate == 0 ? CurrentAudioOptions.SampleRate : sampleRate;

        try
        {
            audioPlayback = new AudioPlayback(sampleRate, CurrentAudioOptions.Channel, CurrentAudioOptions.SampleType);
            audioPlayback.ApplyStreamPolicy(audioStreamPolicy);
            audioPlayback.BufferAvailable += OnBufferAvailable;
            audioStream = new MemoryStream();
        }
        catch (Exception e)
        {
            Log.Error(LogTag, $"Failed to initialize audio playback: {e.Message}");
        }
    }

    private void DestroyAudioPlayback()
    {
        if (audioPlayback != null)
        {
            Stop();
            audioPlayback.BufferAvailable -= OnBufferAvailable;
            audioPlayback.Dispose();
        }
        audioPlayback = null;
    }
}

// Extension for AudioDucking class to make code cleaner
public static class AudioDuckingExtensions
{
    public static void DeactivateIfNeeded(this AudioDucking audioDucking)
    {
        if (audioDucking.IsDucked)
            audioDucking.Deactivate();
    }
}

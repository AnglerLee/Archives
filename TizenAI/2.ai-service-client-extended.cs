// 스트리밍 데이터를 위한 이벤트 아규먼트
public class AudioDataReceivedEventArgs : EventArgs
{
    public byte[] AudioData { get; }
    public AudioDataReceivedEventArgs(byte[] audioData)
    {
        AudioData = audioData;
    }
}

// 스트리밍 인터페이스
public interface IAudioStreamingClient : IDisposable
{
    event EventHandler<AudioDataReceivedEventArgs> AudioDataReceived;
    Task StartStreamingAsync();
    Task StopStreamingAsync();
    bool IsStreaming { get; }
}

// Google Cloud 요청 모델들
public class GoogleTTSRequest
{
    public Input Input { get; set; }
    public Voice Voice { get; set; }
    public AudioConfig AudioConfig { get; set; }
}

public class Input
{
    public string Text { get; set; }
}

public class Voice
{
    public string LanguageCode { get; set; } = "en-US";
    public string Name { get; set; } = "en-US-Standard-A";
}

public class AudioConfig
{
    public string AudioEncoding { get; set; } = "MP3";
    public double SpeakingRate { get; set; } = 1.0;
    public double Pitch { get; set; } = 0.0;
}

public class GoogleSTTRequest
{
    public Config Config { get; set; }
    public Audio Audio { get; set; }
}

public class Config
{
    public string LanguageCode { get; set; } = "en-US";
    public bool EnableAutomaticPunctuation { get; set; } = true;
    public string Model { get; set; } = "default";
}

public class Audio
{
    public string Content { get; set; }  // Base64 encoded audio
}

// Google Cloud 서비스 클라이언트
public class GoogleCloudServiceClient : IAIServiceClient
{
    private readonly RestClient _restClient;
    private readonly string _apiKey;
    private const string TTS_ENDPOINT = "https://texttospeech.googleapis.com/v1/";
    private const string STT_ENDPOINT = "https://speech.googleapis.com/v1/";

    public GoogleCloudServiceClient(string apiKey)
    {
        _apiKey = apiKey;
        _restClient = new RestClient(new HttpClient());
    }

    public async Task<AIServiceResponse<Stream>> TextToSpeechAsync(GoogleTTSRequest request)
    {
        try
        {
            var jsonRequest = JsonSerializer.Serialize(request);
            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                $"{TTS_ENDPOINT}text:synthesize?key={_apiKey}",
                null,
                jsonRequest
            );

            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(response);
            var audioContent = Convert.FromBase64String(result["audioContent"]);
            return new AIServiceResponse<Stream>
            {
                Success = true,
                Data = new MemoryStream(audioContent)
            };
        }
        catch (Exception ex)
        {
            return new AIServiceResponse<Stream>
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<AIServiceResponse<string>> SpeechToTextAsync(GoogleSTTRequest request)
    {
        try
        {
            var jsonRequest = JsonSerializer.Serialize(request);
            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                $"{STT_ENDPOINT}speech:recognize?key={_apiKey}",
                null,
                jsonRequest
            );

            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            return new AIServiceResponse<string>
            {
                Success = true,
                Data = result["results"].ToString()
            };
        }
        catch (Exception ex)
        {
            return new AIServiceResponse<string>
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // IAIServiceClient 구현
    public Task<string> SendRequestAsync(HttpMethod method, string endpoint, string bearerToken = null, string jsonData = null)
    {
        return _restClient.SendRequestAsync(method, endpoint, bearerToken, jsonData);
    }

    public void Dispose()
    {
        _restClient?.Dispose();
    }
}

// 스트리밍 TTS 클라이언트 구현
public class StreamingTTSClient : IAudioStreamingClient
{
    private readonly string _apiKey;
    private readonly GoogleTTSRequest _request;
    private bool _isStreaming;
    private CancellationTokenSource _cancellationTokenSource;

    public event EventHandler<AudioDataReceivedEventArgs> AudioDataReceived;
    public bool IsStreaming => _isStreaming;

    public StreamingTTSClient(string apiKey, GoogleTTSRequest request)
    {
        _apiKey = apiKey;
        _request = request;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task StartStreamingAsync()
    {
        if (_isStreaming) return;

        _isStreaming = true;
        _cancellationTokenSource = new CancellationTokenSource();

        await Task.Run(async () =>
        {
            try
            {
                using var client = new GoogleCloudServiceClient(_apiKey);
                var response = await client.TextToSpeechAsync(_request);

                if (response.Success && response.Data != null)
                {
                    const int bufferSize = 4096;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;

                    while ((bytesRead = await response.Data.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        var audioData = new byte[bytesRead];
                        Array.Copy(buffer, audioData, bytesRead);
                        AudioDataReceived?.Invoke(this, new AudioDataReceivedEventArgs(audioData));

                        // 스트리밍 시뮬레이션을 위한 딜레이
                        await Task.Delay(50);
                    }
                }
            }
            finally
            {
                _isStreaming = false;
            }
        });
    }

    public Task StopStreamingAsync()
    {
        _cancellationTokenSource.Cancel();
        _isStreaming = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
    }
}

// 사용 예제
public class AIServiceExample
{
    public async Task RunExample()
    {
        var googleApiKey = "your-google-api-key";

        // Google TTS 사용 예제
        var ttsRequest = new GoogleTTSRequest
        {
            Input = new Input { Text = "Hello, this is a test of Google Text to Speech." },
            Voice = new Voice { LanguageCode = "en-US", Name = "en-US-Standard-A" },
            AudioConfig = new AudioConfig { AudioEncoding = "MP3" }
        };

        // 일반 TTS
        using (var googleService = new GoogleCloudServiceClient(googleApiKey))
        {
            var ttsResponse = await googleService.TextToSpeechAsync(ttsRequest);
            if (ttsResponse.Success)
            {
                using (var fileStream = File.Create("google_tts_output.mp3"))
                {
                    await ttsResponse.Data.CopyToAsync(fileStream);
                }
            }
        }

        // 스트리밍 TTS
        using (var streamingClient = new StreamingTTSClient(googleApiKey, ttsRequest))
        {
            streamingClient.AudioDataReceived += (sender, e) =>
            {
                // 실시간으로 오디오 데이터 처리
                Console.WriteLine($"Received {e.AudioData.Length} bytes of audio data");
            };

            await streamingClient.StartStreamingAsync();
            await Task.Delay(5000); // 5초 동안 스트리밍
            await streamingClient.StopStreamingAsync();
        }

        // Google STT 사용 예제
        var audioBytes = File.ReadAllBytes("audio_file.wav");
        var sttRequest = new GoogleSTTRequest
        {
            Config = new Config
            {
                LanguageCode = "en-US",
                EnableAutomaticPunctuation = true
            },
            Audio = new Audio
            {
                Content = Convert.ToBase64String(audioBytes)
            }
        };

        using (var googleService = new GoogleCloudServiceClient(googleApiKey))
        {
            var sttResponse = await googleService.SpeechToTextAsync(sttRequest);
            if (sttResponse.Success)
            {
                Console.WriteLine($"Transcription: {sttResponse.Data}");
            }
        }
    }
}

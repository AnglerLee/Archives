// BaseAIService 공통 클래스 추가
public abstract class BaseAIService : IAIService
{
    public abstract string ServiceName { get; }
    public abstract ServiceCapabilities Capabilities { get; }
    protected IRestClient RestClient { get; }

    protected BaseAIService(IRestClient restClient)
    {
        RestClient = restClient;
    }

    public void Dispose()
    {
        RestClient?.Dispose();
    }
}

// OpenAIService 및 GoogleCloudService 리팩토링
public class OpenAIService : BaseAIService, ITextToSpeechService, ILLMService
{
    private readonly OpenAIConfiguration _config;

    public override string ServiceName => "OpenAI";
    public override ServiceCapabilities Capabilities => ServiceCapabilities.TextToSpeech | ServiceCapabilities.LLM;

    public OpenAIService(OpenAIConfiguration config, IRestClient restClient) : base(restClient)
    {
        _config = config;
    }

    // 기존 구현
}

public class GoogleCloudService : BaseAIService, ITextToSpeechService, ISpeechToTextService
{
    private readonly AIServiceConfiguration _config;

    public override string ServiceName => "Google Cloud";
    public override ServiceCapabilities Capabilities => ServiceCapabilities.TextToSpeech | ServiceCapabilities.SpeechToText;

    public GoogleCloudService(AIServiceConfiguration config, IRestClient restClient) : base(restClient)
    {
        _config = config;
    }

    // 기존 구현
}

// Factory에서 리플렉션을 활용한 서비스 생성
public class AIServiceFactory : IAIServiceFactory
{
    private readonly Dictionary<Type, Func<AIServiceConfiguration, IRestClient, IAIService>> _serviceCreators;

    public AIServiceFactory()
    {
        _serviceCreators = new Dictionary<Type, Func<AIServiceConfiguration, IRestClient, IAIService>>
        {
            { typeof(OpenAIConfiguration), (config, client) => new OpenAIService((OpenAIConfiguration)config, client) },
            { typeof(GoogleCloudService), (config, client) => new GoogleCloudService(config, client) }
        };
    }

    public IAIService CreateService(AIServiceConfiguration config)
    {
        if (_serviceCreators.TryGetValue(config.GetType(), out var creator))
        {
            var restClient = new RestClient(new HttpClient { BaseAddress = new Uri(config.BaseUrl) });
            return creator(config, restClient);
        }
        throw new ArgumentException("Unsupported configuration type");
    }
}


public class TtsStreamingEventArgs : EventArgs
{
    public byte[] Data { get; set; }
    public string Status { get; set; }
}

public delegate void TtsStartHandler(object sender, TtsStreamingEventArgs e);
public delegate void TtsReceivingHandler(object sender, TtsStreamingEventArgs e);
public delegate void TtsFinishHandler(object sender, TtsStreamingEventArgs e);

public class OpenAIService : BaseAIService, ITextToSpeechService, ILLMService
{
    public event TtsStartHandler OnTtsStart;
    public event TtsReceivingHandler OnTtsReceiving;
    public event TtsFinishHandler OnTtsFinish;

    // 기존 필드와 생성자 유지

    public async IAsyncEnumerable<byte[]> TextToSpeechStreamAsync(string text, string voice = null, Dictionary<string, object> options = null)
    {
        var audioData = await TextToSpeechAsync(text, voice, options);

        // 시작 이벤트 호출
        OnTtsStart?.Invoke(this, new TtsStreamingEventArgs { Status = "Start" });

        const int chunkDurationMs = 160;
        const int sampleRate = 16000; // 16kHz 샘플링 예시
        const int bytesPerMs = sampleRate * 2 / 1000; // 2 bytes per sample (16-bit audio)
        int chunkSize = bytesPerMs * chunkDurationMs;

        for (int i = 0; i < audioData.Length; i += chunkSize)
        {
            var chunk = new byte[Math.Min(chunkSize, audioData.Length - i)];
            Array.Copy(audioData, i, chunk, 0, chunk.Length);

            // 데이터 수신 이벤트 호출
            OnTtsReceiving?.Invoke(this, new TtsStreamingEventArgs { Data = chunk, Status = "Receiving" });
            yield return chunk;

            await Task.Delay(chunkDurationMs); // 160ms 단위로 지연
        }

        // 종료 이벤트 호출
        OnTtsFinish?.Invoke(this, new TtsStreamingEventArgs { Status = "Finish" });
    }
}

public class Example
{
    public async Task RunExample()
    {
        var openAIConfig = new OpenAIConfiguration
        {
            ApiKey = "your-api-key",
            BaseUrl = "https://api.openai.com/v1/",
            Model = "gpt-3.5-turbo"
        };

        var factory = new AIServiceFactory();
        using var service = factory.CreateService(openAIConfig) as OpenAIService;

        if (service != null)
        {
            service.OnTtsStart += (s, e) => Console.WriteLine("TTS 시작");
            service.OnTtsReceiving += (s, e) => Console.WriteLine($"TTS 수신 중: {e.Data.Length} bytes");
            service.OnTtsFinish += (s, e) => Console.WriteLine("TTS 종료");

            await foreach (var chunk in service.TextToSpeechStreamAsync("Hello world"))
            {
                // 수신된 160ms 단위의 음성 데이터를 처리
            }
        }
    }
}



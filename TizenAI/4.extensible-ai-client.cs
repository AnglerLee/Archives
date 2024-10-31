namespace AIServices.Core
{
    [Flags]
    public enum ServiceCapabilities
    {
        None = 0,
        TextToSpeech = 1,
        SpeechToText = 2,
        LLM = 4
    }

    public interface IAIService : IDisposable
    {
        string ServiceName { get; }
        ServiceCapabilities Capabilities { get; }
        IRestClient RestClient { get; }
    }

    public interface IRestClient : IDisposable
    {
        Task<string> SendRequestAsync(HttpMethod method, string endpoint, string bearerToken = null, string jsonData = null);
    }

    // 기존 RestClient 구현체
    internal class RestClient : IRestClient
    {
        private readonly HttpClient client;
        
        internal RestClient(HttpClient httpClient)
        {
            client = httpClient;
        }

        public async Task<string> SendRequestAsync(HttpMethod method, string endpoint, string bearerToken = null, string jsonData = null)
        {
            AddBearerToken(bearerToken);
            using var request = new HttpRequestMessage(method, endpoint);
            
            if (jsonData != null)
            {
                request.Content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            }
            
            using var response = await client.SendAsync(request);
            return await HandleResponse(response);
        }

        private void AddBearerToken(string bearerToken)
        {
            if (!string.IsNullOrEmpty(bearerToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }
        }

        private async Task<string> HandleResponse(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}");
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}

namespace AIServices.Features
{
    // TTS 인터페이스
    public interface ITextToSpeechService
    {
        Task<byte[]> TextToSpeechAsync(string text, string voice = null, Dictionary<string, object> options = null);
        IAsyncEnumerable<byte[]> TextToSpeechStreamAsync(string text, string voice = null, Dictionary<string, object> options = null);
    }

    // STT 인터페이스
    public interface ISpeechToTextService
    {
        Task<string> SpeechToTextAsync(byte[] audioData, string languageCode = null, Dictionary<string, object> options = null);
        IAsyncEnumerable<string> SpeechToTextStreamAsync(Stream audioStream, string languageCode = null, Dictionary<string, object> options = null);
    }

    // LLM 인터페이스
    public interface ILLMService
    {
        Task<string> CompletionAsync(string prompt, Dictionary<string, object> options = null);
        IAsyncEnumerable<string> CompletionStreamAsync(string prompt, Dictionary<string, object> options = null);
    }
}

namespace AIServices.Models
{
    public class AIServiceResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public abstract class AIServiceConfiguration
    {
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; }
        public Dictionary<string, object> AdditionalSettings { get; set; } = new();
    }
}

namespace AIServices.Providers
{
    // OpenAI 구현 예시
    public class OpenAIConfiguration : AIServiceConfiguration
    {
        public string Organization { get; set; }
        public string Model { get; set; } = "gpt-3.5-turbo";
    }

    public class OpenAIService : IAIService, ITextToSpeechService, ILLMService
    {
        private readonly IRestClient _restClient;
        private readonly OpenAIConfiguration _config;

        public string ServiceName => "OpenAI";
        public ServiceCapabilities Capabilities => ServiceCapabilities.TextToSpeech | ServiceCapabilities.LLM;
        public IRestClient RestClient => _restClient;

        public OpenAIService(OpenAIConfiguration config)
        {
            _config = config;
            _restClient = new RestClient(new HttpClient { BaseAddress = new Uri(config.BaseUrl) });
        }

        public async Task<byte[]> TextToSpeechAsync(string text, string voice = null, Dictionary<string, object> options = null)
        {
            var request = new
            {
                input = text,
                voice = voice ?? "alloy",
                model = "tts-1",
            };

            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                "audio/speech",
                _config.ApiKey,
                JsonSerializer.Serialize(request)
            );

            return Convert.FromBase64String(response);
        }

        public async Task<string> CompletionAsync(string prompt, Dictionary<string, object> options = null)
        {
            var request = new
            {
                model = _config.Model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                "chat/completions",
                _config.ApiKey,
                JsonSerializer.Serialize(request)
            );

            // Parse response and extract completion text
            return response;
        }

        public async IAsyncEnumerable<byte[]> TextToSpeechStreamAsync(string text, string voice = null, Dictionary<string, object> options = null)
        {
            var audioData = await TextToSpeechAsync(text, voice, options);
            const int chunkSize = 4096;
            
            for (int i = 0; i < audioData.Length; i += chunkSize)
            {
                var chunk = new byte[Math.Min(chunkSize, audioData.Length - i)];
                Array.Copy(audioData, i, chunk, 0, chunk.Length);
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<string> CompletionStreamAsync(string prompt, Dictionary<string, object> options = null)
        {
            // Streaming implementation
            yield return await CompletionAsync(prompt, options);
        }

        public void Dispose()
        {
            _restClient?.Dispose();
        }
    }

    // Google Cloud 구현 예시
    public class GoogleCloudService : IAIService, ITextToSpeechService, ISpeechToTextService
    {
        public string ServiceName => "Google Cloud";
        public ServiceCapabilities Capabilities => ServiceCapabilities.TextToSpeech | ServiceCapabilities.SpeechToText;
        public IRestClient RestClient => _restClient;

        private readonly IRestClient _restClient;
        private readonly AIServiceConfiguration _config;

        // Implementation details...
    }
}

namespace AIServices.Factory
{
    public interface IAIServiceFactory
    {
        IAIService CreateService(AIServiceConfiguration config);
    }

    public class AIServiceFactory : IAIServiceFactory
    {
        public IAIService CreateService(AIServiceConfiguration config)
        {
            return config switch
            {
                OpenAIConfiguration openAIConfig => new OpenAIService(openAIConfig),
                _ => throw new ArgumentException("Unsupported configuration type")
            };
        }
    }
}

// 사용 예제
public class Example
{
    public async Task RunExample()
    {
        // OpenAI 서비스 사용
        var openAIConfig = new OpenAIConfiguration
        {
            ApiKey = "your-api-key",
            BaseUrl = "https://api.openai.com/v1/",
            Model = "gpt-3.5-turbo"
        };

        var factory = new AIServiceFactory();
        using var service = factory.CreateService(openAIConfig);

        // TTS 기능 사용
        if (service is ITextToSpeechService tts)
        {
            var audioData = await tts.TextToSpeechAsync(
                "Hello world",
                voice: "alloy",
                options: new Dictionary<string, object>
                {
                    ["speed"] = 1.0
                }
            );
        }

        // LLM 기능 사용
        if (service is ILLMService llm)
        {
            var completion = await llm.CompletionAsync(
                "Explain quantum computing",
                options: new Dictionary<string, object>
                {
                    ["temperature"] = 0.7
                }
            );
        }
    }
}

// 사용자 정의 서비스 구현 예시
public class CustomAIConfiguration : AIServiceConfiguration
{
    public string CustomParameter { get; set; }
}

public class CustomAIService : IAIService, ITextToSpeechService, ISpeechToTextService, ILLMService
{
    public string ServiceName => "Custom AI Service";
    public ServiceCapabilities Capabilities => 
        ServiceCapabilities.TextToSpeech | 
        ServiceCapabilities.SpeechToText | 
        ServiceCapabilities.LLM;
    public IRestClient RestClient => _restClient;

    private readonly IRestClient _restClient;
    private readonly CustomAIConfiguration _config;

    public CustomAIService(CustomAIConfiguration config)
    {
        _config = config;
        _restClient = new RestClient(new HttpClient { BaseAddress = new Uri(config.BaseUrl) });
    }

    // Implement interface methods...
}

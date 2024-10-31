// Core interfaces
namespace AIServices.Core
{
    public interface IAIService : IDisposable
    {
        string ServiceName { get; }
        ServiceCapabilities Capabilities { get; }
    }

    [Flags]
    public enum ServiceCapabilities
    {
        None = 0,
        TextToSpeech = 1,
        SpeechToText = 2,
        ChatCompletion = 4,
        ImageGeneration = 8
    }

    public interface IAIServiceFactory
    {
        IAIService CreateService(AIServiceConfiguration config);
    }

    public abstract class AIServiceConfiguration
    {
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; }
        public Dictionary<string, string> AdditionalSettings { get; set; } = new();
    }
}

// Audio interfaces
namespace AIServices.Audio
{
    public interface ITTSService
    {
        Task<AudioResponse> TextToSpeechAsync(TTSRequest request);
        IAsyncEnumerable<AudioChunk> TextToSpeechStreamAsync(TTSRequest request);
    }

    public interface ISTTService
    {
        Task<TranscriptionResponse> SpeechToTextAsync(STTRequest request);
        IAsyncEnumerable<TranscriptionChunk> SpeechToTextStreamAsync(Stream audioStream);
    }

    public class AudioChunk
    {
        public byte[] Data { get; set; }
        public TimeSpan Duration { get; set; }
        public int SequenceNumber { get; set; }
    }

    public class TranscriptionChunk
    {
        public string Text { get; set; }
        public bool IsFinal { get; set; }
        public float Confidence { get; set; }
    }
}

// Base models
namespace AIServices.Models
{
    public class AIServiceResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class TTSRequest
    {
        public string Text { get; set; }
        public string VoiceId { get; set; }
        public string LanguageCode { get; set; }
        public AudioFormat OutputFormat { get; set; }
        public Dictionary<string, object> AdditionalParameters { get; set; }
    }

    public class STTRequest
    {
        public Stream AudioStream { get; set; }
        public string LanguageCode { get; set; }
        public bool EnableAutomaticPunctuation { get; set; }
        public Dictionary<string, object> AdditionalParameters { get; set; }
    }

    public class AudioResponse : AIServiceResponse<Stream> { }
    public class TranscriptionResponse : AIServiceResponse<string> { }
}

// Example implementation for Google Cloud
namespace AIServices.Providers.Google
{
    public class GoogleCloudConfiguration : AIServiceConfiguration
    {
        public string ProjectId { get; set; }
        public string Location { get; set; }
    }

    public class GoogleCloudService : IAIService, ITTSService, ISTTService
    {
        private readonly RestClient _restClient;
        private readonly GoogleCloudConfiguration _config;

        public string ServiceName => "Google Cloud";
        public ServiceCapabilities Capabilities => 
            ServiceCapabilities.TextToSpeech | ServiceCapabilities.SpeechToText;

        public GoogleCloudService(GoogleCloudConfiguration config)
        {
            _config = config;
            _restClient = new RestClient(new HttpClient());
        }

        public async Task<AudioResponse> TextToSpeechAsync(TTSRequest request)
        {
            // Google Cloud TTS implementation
            var googleRequest = MapToGoogleTTSRequest(request);
            // Implementation details...
            return new AudioResponse();
        }

        public async IAsyncEnumerable<AudioChunk> TextToSpeechStreamAsync(TTSRequest request)
        {
            int sequence = 0;
            using var response = await TextToSpeechAsync(request);
            
            if (response.Success && response.Data != null)
            {
                const int bufferSize = 4096;
                byte[] buffer = new byte[bufferSize];
                int bytesRead;

                while ((bytesRead = await response.Data.ReadAsync(buffer, 0, bufferSize)) > 0)
                {
                    var chunk = new AudioChunk
                    {
                        Data = buffer[..bytesRead],
                        SequenceNumber = sequence++,
                        Duration = TimeSpan.FromMilliseconds(50) // Approximate
                    };
                    yield return chunk;
                }
            }
        }

        // Other interface implementations...
        public void Dispose() => _restClient?.Dispose();
    }

    public class GoogleCloudServiceFactory : IAIServiceFactory
    {
        public IAIService CreateService(AIServiceConfiguration config)
        {
            if (config is not GoogleCloudConfiguration googleConfig)
                throw new ArgumentException("Invalid configuration type");

            return new GoogleCloudService(googleConfig);
        }
    }
}

// Example implementation for OpenAI
namespace AIServices.Providers.OpenAI
{
    public class OpenAIConfiguration : AIServiceConfiguration
    {
        public string OrganizationId { get; set; }
        public string Model { get; set; }
    }

    public class OpenAIService : IAIService, ITTSService
    {
        public string ServiceName => "OpenAI";
        public ServiceCapabilities Capabilities => 
            ServiceCapabilities.TextToSpeech | ServiceCapabilities.ChatCompletion;

        // Implementation details...
    }
}

// Example of how to implement a custom AI service
namespace AIServices.Providers.Custom
{
    public class CustomAIConfiguration : AIServiceConfiguration
    {
        public string CustomParameter { get; set; }
    }

    public class CustomAIService : IAIService, ITTSService, ISTTService
    {
        private readonly RestClient _restClient;
        private readonly CustomAIConfiguration _config;

        public string ServiceName => "Custom AI Service";
        public ServiceCapabilities Capabilities =>
            ServiceCapabilities.TextToSpeech | ServiceCapabilities.SpeechToText;

        public CustomAIService(CustomAIConfiguration config)
        {
            _config = config;
            _restClient = new RestClient(new HttpClient());
        }

        public async Task<AudioResponse> TextToSpeechAsync(TTSRequest request)
        {
            // Custom implementation
            return new AudioResponse();
        }

        public async IAsyncEnumerable<AudioChunk> TextToSpeechStreamAsync(TTSRequest request)
        {
            // Custom streaming implementation
            yield break;
        }

        // Implement other interface members...
    }
}

// Usage example
public class AIServiceExample
{
    public async Task RunExample()
    {
        // Google Cloud 서비스 사용
        var googleConfig = new GoogleCloudConfiguration
        {
            ApiKey = "your-api-key",
            ProjectId = "your-project-id"
        };

        var factory = new GoogleCloudServiceFactory();
        using var googleService = factory.CreateService(googleConfig);

        if (googleService is ITTSService ttsService)
        {
            var request = new TTSRequest
            {
                Text = "Hello, world!",
                LanguageCode = "en-US"
            };

            // 일반 TTS 사용
            var response = await ttsService.TextToSpeechAsync(request);

            // 스트리밍 TTS 사용
            await foreach (var chunk in ttsService.TextToSpeechStreamAsync(request))
            {
                // Process audio chunk
                Console.WriteLine($"Received chunk {chunk.SequenceNumber}");
            }
        }

        // 커스텀 AI 서비스 구현 예제
        var customConfig = new CustomAIConfiguration
        {
            ApiKey = "your-api-key",
            CustomParameter = "custom-value"
        };

        using var customService = new CustomAIService(customConfig);
        if (customService is ITTSService customTTS)
        {
            // 커스텀 서비스 사용
            var request = new TTSRequest
            {
                Text = "Hello from custom service!"
            };

            var response = await customTTS.TextToSpeechAsync(request);
        }
    }
}

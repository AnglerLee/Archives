// 기본 인터페이스와 모델들
public interface IAIServiceClient : IDisposable
{
    Task<string> SendRequestAsync(HttpMethod method, string endpoint, string bearerToken = null, string jsonData = null);
}

public class AIServiceResponse<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string ErrorMessage { get; set; }
}

// GPT 관련 모델들
public class ChatGPTRequest
{
    public string Model { get; set; } = "gpt-3.5-turbo";
    public List<ChatMessage> Messages { get; set; }
    public double Temperature { get; set; } = 0.7;
}

public class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

public class ChatGPTResponse
{
    public string Id { get; set; }
    public string Model { get; set; }
    public List<ChatChoice> Choices { get; set; }
}

public class ChatChoice
{
    public ChatMessage Message { get; set; }
    public int Index { get; set; }
}

// TTS 관련 모델들
public class TTSRequest
{
    public string Model { get; set; } = "tts-1";
    public string Input { get; set; }
    public string Voice { get; set; } = "alloy";
}

// AI 서비스 구현체들
public class OpenAIServiceClient : IAIServiceClient
{
    private readonly RestClient _restClient;
    private readonly string _apiKey;
    
    public OpenAIServiceClient(string apiKey)
    {
        _apiKey = apiKey;
        _restClient = new RestClient(new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/") });
    }

    public async Task<AIServiceResponse<ChatGPTResponse>> ChatCompletionAsync(ChatGPTRequest request)
    {
        try
        {
            var jsonRequest = JsonSerializer.Serialize(request);
            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                "chat/completions",
                _apiKey,
                jsonRequest
            );

            var result = JsonSerializer.Deserialize<ChatGPTResponse>(response);
            return new AIServiceResponse<ChatGPTResponse> 
            { 
                Success = true, 
                Data = result 
            };
        }
        catch (Exception ex)
        {
            return new AIServiceResponse<ChatGPTResponse> 
            { 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    public async Task<AIServiceResponse<Stream>> TextToSpeechAsync(TTSRequest request)
    {
        try
        {
            var jsonRequest = JsonSerializer.Serialize(request);
            var response = await _restClient.SendRequestAsync(
                HttpMethod.Post,
                "audio/speech",
                _apiKey,
                jsonRequest
            );

            // TTS 응답은 바이너리 스트림으로 처리
            var byteArray = Convert.FromBase64String(response);
            var stream = new MemoryStream(byteArray);
            
            return new AIServiceResponse<Stream> 
            { 
                Success = true, 
                Data = stream 
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

    public void Dispose()
    {
        _restClient?.Dispose();
    }

    public Task<string> SendRequestAsync(HttpMethod method, string endpoint, string bearerToken = null, string jsonData = null)
    {
        return _restClient.SendRequestAsync(method, endpoint, bearerToken, jsonData);
    }
}

// 사용 예제
public class AIServiceExample
{
    public async Task RunExample()
    {
        var apiKey = "your-api-key";
        using var aiService = new OpenAIServiceClient(apiKey);

        // ChatGPT 사용 예제
        var chatRequest = new ChatGPTRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Hello, how are you?" }
            }
        };

        var chatResponse = await aiService.ChatCompletionAsync(chatRequest);
        if (chatResponse.Success)
        {
            Console.WriteLine($"GPT Response: {chatResponse.Data.Choices[0].Message.Content}");
        }

        // TTS 사용 예제
        var ttsRequest = new TTSRequest
        {
            Input = "Hello, this is a test of text to speech.",
            Voice = "alloy"
        };

        var ttsResponse = await aiService.TextToSpeechAsync(ttsRequest);
        if (ttsResponse.Success)
        {
            // 오디오 스트림 저장
            using (var fileStream = File.Create("output.mp3"))
            {
                await ttsResponse.Data.CopyToAsync(fileStream);
            }
        }
    }
}

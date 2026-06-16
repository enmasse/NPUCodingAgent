using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NPUCodingAgent.Tests")]

namespace NPUCodingAgent.Services;

public class LocalModelService(string? modelAlias = null) : IDisposable, IAsyncDisposable
{
    public const string SystemPrompt = "You are a helpful coding assistant. Provide clear, concise answers about code and programming.";
    
    private const string DefaultModelAlias = "phi-3-mini-4k";

    private readonly string _modelAlias = string.IsNullOrWhiteSpace(modelAlias)
            ? Environment.GetEnvironmentVariable("FOUNDRY_LOCAL_MODEL")?.Trim() ?? DefaultModelAlias
            : modelAlias.Trim();

    private ICatalog? _catalog;
    private IModel? _model;
    private OpenAIChatClient? _chatClient;
    private string? _modelId;
    private Uri? _endpoint;
    private ModelRuntimeInfo? _runtimeInfo;
    private bool _initialized;
    private FoundryLocalManager? foundryLocalManager;

    public string Endpoint => _endpoint?.ToString() ?? "In-process SDK client";

    public string RequestedModelSelection => _modelAlias;

    public string ModelName => _modelId ?? _modelAlias;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        var config = new Configuration
        {
            AppName = "NPUCodingAgent",
            LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information,
        };

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<Program>();

        if (foundryLocalManager is null)
        {
            try
            {
                var instance = FoundryLocalManager.Instance;
            }
            catch
            {
                await FoundryLocalManager.CreateAsync(config, logger);
            }
        }

        foundryLocalManager = FoundryLocalManager.Instance;

        var eps = foundryLocalManager.DiscoverEps();
        foreach (var ep in eps)
        {
            Console.WriteLine($"{ep.Name} — registered: {ep.IsRegistered}");
        }

        // Download and register OpenVINOExecutionProvider
        Console.WriteLine($"Registering execution providers... ");
        var result = await foundryLocalManager.DownloadAndRegisterEpsAsync();
        Console.WriteLine($"Status: {result.Status}");

        _catalog = await foundryLocalManager.GetCatalogAsync() ?? throw new InvalidOperationException("Foundry Local did not provide a model catalog.");
        var cachedModels = await _catalog.GetCachedModelsAsync();

        var models = await _catalog.ListModelsAsync();
        _model = await _catalog.GetModelAsync(_modelAlias) ?? throw new InvalidOperationException($"Model selection '{_modelAlias}' was not found in the Foundry Local catalog.");

        _modelId = _model?.Id;

        await _model.DownloadAsync((p) => Console.WriteLine($"Download progress: {p}%"));
        try
        {
            Console.WriteLine("Loading model...");
            await _model.LoadAsync();
            Console.WriteLine("Model loaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

         _chatClient = await _model.GetChatClientAsync() ?? throw new InvalidOperationException("Foundry Local did not provide a chat client for the selected model.");

        _runtimeInfo = ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);

        try
        {
            await foundryLocalManager.StartWebServiceAsync();
            _endpoint = GetManagerEndpoint(foundryLocalManager);
        }
        catch
        {
            _endpoint = null;
        }

        _runtimeInfo = ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);

        _initialized = true;
    }

    public async Task<string> GetResponseAsync(List<ChatMessage> chatHistory)
    {
        if (!FoundryLocalManager.IsInitialized)
        {
            throw new InvalidOperationException("Foundry Local manager is not initialized.");
        }

        if (_chatClient is null)
        {
            throw new InvalidOperationException("Foundry Local chat client is not initialized.");
        }

        var completion = await _chatClient.CompleteChatAsync(chatHistory)
            ?? throw new InvalidOperationException("Foundry Local returned no completion response.");

        var response = ReadCompletionText(completion);
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("Foundry Local returned an empty response.");
        }

        return response.Trim();
    }

    public async Task<string> GetStatusAsync()
    {
        if (!FoundryLocalManager.IsInitialized)
        {
            throw new InvalidOperationException("Foundry Local manager is not initialized.");
        }

        var runtime = await GetRuntimeInfoAsync();
        return $"Provider: Foundry Local SDK\nEndpoint: {runtime.Endpoint}\nModel alias: {runtime.ModelAlias}\nModel: {runtime.ModelId}\nAccelerator: {runtime.AcceleratorSummary}\nExecution provider: {runtime.ExecutionProvider}\nNPU: {(runtime.IsNpu ? "Detected" : "Not detected")}\nStatus: Ready";
    }

    public async Task<ModelRuntimeInfo> GetRuntimeInfoAsync()
    {
        if (!FoundryLocalManager.IsInitialized)
        {
            throw new InvalidOperationException("Foundry Local manager is not initialized.");
        }

        return _runtimeInfo ?? ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);
    }

    public async Task<string> PingAsync()
    {
        var response = await GetResponseAsync(
        [
            new ChatMessage("user", "Reply with exactly: pong")
        ]);

        return response;
    }

    public async Task<IReadOnlyList<string>> ListAvailableModelsAsync()
    {
        if (!FoundryLocalManager.IsInitialized)
        {
            throw new InvalidOperationException("Foundry Local manager is not initialized.");
        }

        var models = await _catalog.ListModelsAsync();
        return models is null
            ? []
            : CollectSelectableModelNames(models.Cast<object>());
    }

    internal static bool MatchesModelSelection(object model, string modelSelection)
    {
        return MatchesModelSelection(model, modelSelection, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    internal static IReadOnlyList<string> CollectSelectableModelNames(IEnumerable<object> models)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var model in models)
        {
            CollectSelectableModelNames(names, model, visited);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ReadCompletionText(ChatCompletionCreateResponse completion)
    {
        if (completion.Choices.Count == 0)
        {
            return string.Empty;
        }

        foreach (var choice in completion.Choices)
        {
            var message = choice.Message;
            var content = message?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static ModelRuntimeInfo ReadRuntimeInfo(IModel? model, string modelAlias, string? modelId, Uri? endpoint)
    {
        ArgumentNullException.ThrowIfNull(model);
        var deviceType = model.Info?.Runtime?.DeviceType;
        var executionProvider = model.Info?.Runtime?.ExecutionProvider;
        var modelDisplayName = model.Info?.DisplayName;
        var providerType = model.Info?.ProviderType;

        var endpointText = endpoint?.ToString() ?? "In-process SDK client";
        var isNpu = deviceType == DeviceType.NPU;

        var acceleratorSummary = isNpu
            ? $"NPU ({executionProvider})"
            : string.Equals(deviceType.ToString(), "Unknown", StringComparison.OrdinalIgnoreCase)
                ? executionProvider
                : $"{deviceType} ({executionProvider})";

        return new ModelRuntimeInfo(
            modelAlias,
            modelId ?? modelAlias,
            modelDisplayName ?? modelId ?? modelAlias,
            endpointText,
            deviceType,
            executionProvider,
            providerType ?? "Unknown",
            isNpu,
            acceleratorSummary);
    }

    private static Uri? GetManagerEndpoint(FoundryLocalManager manager)
    {
        var firstUrl = manager.Urls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        if (Uri.TryCreate(firstUrl, UriKind.Absolute, out var endpoint))
        {
            return endpoint;
        }

        return null;
    }

    private static bool MatchesModelSelection(object? candidate, string modelSelection, ISet<object> visited)
    {
        if (candidate is null)
        {
            return false;
        }

        if (candidate is string value)
        {
            return string.Equals(value, modelSelection, StringComparison.OrdinalIgnoreCase);
        }

        if (!visited.Add(candidate))
        {
            return false;
        }

        if (string.Equals(GetMemberValue(candidate, "Alias")?.ToString(), modelSelection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetMemberValue(candidate, "Id")?.ToString(), modelSelection, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (GetMemberValue(candidate, "Variants") is not IEnumerable variants)
        {
            return false;
        }

        return variants.Cast<object?>().Any(variant => MatchesModelSelection(variant, modelSelection, visited));
    }

    private static void CollectSelectableModelNames(ISet<string> names, object? candidate, ISet<object> visited)
    {
        if (candidate is null)
        {
            return;
        }

        if (candidate is string value)
        {
            AddName(names, value);
            return;
        }

        if (!visited.Add(candidate))
        {
            return;
        }

        AddName(names, GetMemberValue(candidate, "Alias")?.ToString());
        AddName(names, GetMemberValue(candidate, "Id")?.ToString());

        if (GetMemberValue(candidate, "Variants") is not IEnumerable variants)
        {
            return;
        }

        foreach (var variant in variants.Cast<object?>())
        {
            CollectSelectableModelNames(names, variant, visited);
        }
    }

    private static void AddName(ISet<string> names, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            names.Add(value.Trim());
        }
    }

    private static object? GetMemberValue(object? instance, string memberName)
    {
        if (instance is null)
        {
            return null;
        }

        var type = instance.GetType();
        return type.GetProperty(memberName)?.GetValue(instance)
            ?? type.GetField(memberName)?.GetValue(instance);
    }

    public sealed record ModelRuntimeInfo(
        string ModelAlias,
        string ModelId,
        string DisplayName,
        string Endpoint,
        DeviceType? DeviceType,
        string ExecutionProvider,
        string ProviderType,
        bool IsNpu,
        string AcceleratorSummary);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing)
    {
        FoundryLocalManager.Instance.Dispose();

        if (disposing)
        {
            if (_model is IDisposable disposable)
            {
                disposable.Dispose();
                _model = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_model is not null)
            {
                await _model.UnloadAsync();
            }

            if (FoundryLocalManager.Instance is not null)
            {
                await FoundryLocalManager.Instance.StopWebServiceAsync();

                if (FoundryLocalManager.Instance is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (FoundryLocalManager.Instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_model is not null)
        {
            await _model.UnloadAsync();
        }

        if (_model is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
        }

        _model = null;
    }
}

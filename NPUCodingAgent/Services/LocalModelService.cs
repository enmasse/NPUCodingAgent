using System.ClientModel;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: InternalsVisibleTo("NPUCodingAgent.Tests")]

namespace NPUCodingAgent.Services;

public sealed class LocalModelService : IDisposable
{
    private const string DefaultModelAlias = "phi-3-mini-128k-instruct-qnn-npu:4";
    private const string SystemPrompt = "You are a helpful coding assistant. Provide clear, concise answers about code and programming.";
    private const string FoundryAssemblyName = "Microsoft.AI.Foundry.Local.WinML";
    private const string FoundryManagerTypeName = "Microsoft.AI.Foundry.Local.FoundryLocalManager";
    private const string FoundryConfigurationTypeName = "Microsoft.AI.Foundry.Local.Configuration";
    private const string ChatMessageTypeName = "Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage, Betalgo.Ranul.OpenAI";

    private readonly string _modelAlias;
    private object? _manager;
    private object? _catalog;
    private object? _model;
    private object? _chatClient;
    private string? _modelId;
    private Uri? _endpoint;
    private ModelRuntimeInfo? _runtimeInfo;
    private bool _initialized;

    public LocalModelService(string? modelAlias = null)
    {
        _modelAlias = string.IsNullOrWhiteSpace(modelAlias)
            ? Environment.GetEnvironmentVariable("FOUNDRY_LOCAL_MODEL")?.Trim() ?? DefaultModelAlias
            : modelAlias.Trim();
    }

    public string Endpoint => _endpoint?.ToString() ?? "In-process SDK client";

    public string RequestedModelSelection => _modelAlias;

    public string ModelName => _modelId ?? _modelAlias;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _manager = await EnsureManagerAsync();
        await EnsureExecutionProvidersAsync(_manager);

        _catalog ??= await InvokeTaskResultAsync(_manager, "GetCatalogAsync")
            ?? throw new InvalidOperationException("Foundry Local did not provide a model catalog.");

        _model = await ResolveModelAsync(_catalog, _modelAlias)
            ?? throw new InvalidOperationException($"Model selection '{_modelAlias}' was not found in the Foundry Local catalog.");

        _modelId = GetMemberValue(_model, "Id")?.ToString() ?? _modelAlias;

        await InvokeTaskAsync(_model, "DownloadAsync");
        await InvokeTaskAsync(_model, "LoadAsync");

        _chatClient = await InvokeTaskResultAsync(_model, "GetChatClientAsync")
            ?? throw new InvalidOperationException("Foundry Local did not provide a chat client for the selected model.");

        _runtimeInfo = ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);

        try
        {
            await InvokeTaskAsync(_manager, "StartWebServiceAsync");
            _endpoint = GetManagerEndpoint(_manager);
        }
        catch
        {
            _endpoint = null;
        }

        _runtimeInfo = ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);

        _initialized = true;
    }

    public async Task<string> GetResponseAsync(List<(string role, string content)> chatHistory)
    {
        await InitializeAsync();

        if (_chatClient is null)
        {
            throw new InvalidOperationException("Foundry Local chat client is not initialized.");
        }

        var completion = await InvokeTaskResultAsync(_chatClient, "CompleteChatAsync", CreateChatMessages(chatHistory))
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
        await InitializeAsync();
        var runtime = await GetRuntimeInfoAsync();
        return $"Provider: Foundry Local SDK\nEndpoint: {runtime.Endpoint}\nModel alias: {runtime.ModelAlias}\nModel: {runtime.ModelId}\nAccelerator: {runtime.AcceleratorSummary}\nExecution provider: {runtime.ExecutionProvider}\nNPU: {(runtime.IsNpu ? "Detected" : "Not detected") }\nStatus: Ready";
    }

    public async Task<ModelRuntimeInfo> GetRuntimeInfoAsync()
    {
        await InitializeAsync();
        return _runtimeInfo ?? ReadRuntimeInfo(_model, _modelAlias, _modelId, _endpoint);
    }

    public async Task<string> PingAsync()
    {
        var response = await GetResponseAsync(
        [
            ("user", "Reply with exactly: pong")
        ]);

        return response;
    }

    public async Task<IReadOnlyList<string>> ListAvailableModelsAsync()
    {
        var manager = await EnsureManagerAsync();
        _catalog ??= await InvokeTaskResultAsync(manager, "GetCatalogAsync")
            ?? throw new InvalidOperationException("Foundry Local did not provide a model catalog.");

        var models = await InvokeTaskResultAsync(_catalog, "ListModelsAsync") as IEnumerable;
        return models is null
            ? Array.Empty<string>()
            : CollectSelectableModelNames(models.Cast<object>());
    }

    public void Dispose()
    {
        try
        {
            if (_model is not null)
            {
                InvokeTaskAsync(_model, "UnloadAsync").GetAwaiter().GetResult();
            }

            if (_manager is not null)
            {
                InvokeTaskAsync(_manager, "StopWebServiceAsync").GetAwaiter().GetResult();

                if (_manager is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    private async Task<object> EnsureManagerAsync()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        var assembly = LoadFoundryAssembly();
        var managerType = assembly.GetType(FoundryManagerTypeName)
            ?? throw new InvalidOperationException("Foundry Local manager type was not found in the installed SDK package.");

        var isInitialized = managerType.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as bool?;
        if (isInitialized == true)
        {
            _manager = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException("Foundry Local reported initialization but did not expose a manager instance.");

            return _manager;
        }

        var createMethod = managerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "CreateAsync")
            ?? throw new InvalidOperationException("Foundry Local CreateAsync was not found.");

        var configuration = CreateFoundryConfiguration(assembly);
        var createTask = createMethod.Invoke(null, CreateMethodArguments(createMethod, configuration, NullLogger.Instance, null))
            ?? throw new InvalidOperationException("Foundry Local did not return an initialization task.");

        await AwaitTaskResultAsync(createTask);

        _manager = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Foundry Local did not expose an initialized manager instance.");

        return _manager;
    }

    private static Assembly LoadFoundryAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == FoundryAssemblyName)
            ?? Assembly.Load(FoundryAssemblyName);
    }

    private static object CreateFoundryConfiguration(Assembly assembly)
    {
        var configurationType = assembly.GetType(FoundryConfigurationTypeName)
            ?? throw new InvalidOperationException("Foundry Local configuration type was not found.");

        var configuration = Activator.CreateInstance(configurationType)
            ?? throw new InvalidOperationException("Foundry Local configuration could not be created.");

        configurationType.GetProperty("AppName")?.SetValue(configuration, "NPUCodingAgent");
        return configuration;
    }

    private static object?[] CreateMethodArguments(MethodInfo method, params object?[] preferredValues)
    {
        preferredValues ??= Array.Empty<object?>();

        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i < preferredValues.Length)
            {
                arguments[i] = preferredValues[i];
                continue;
            }

            arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : GetDefaultValue(parameters[i].ParameterType);
        }

        return arguments;
    }

    private static object? GetDefaultValue(Type parameterType)
    {
        if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null)
        {
            return null;
        }

        return Activator.CreateInstance(parameterType);
    }

    private static async Task EnsureExecutionProvidersAsync(object manager)
    {
        if (await TryInvokeTaskAsync(manager, "DownloadAndRegisterEpsAsync"))
        {
            return;
        }

        await TryInvokeTaskAsync(manager, "EnsureEpsDownloadedAsync");
    }

    private static async Task<object?> ResolveModelAsync(object catalog, string modelSelection)
    {
        var model = await TryInvokeTaskResultAsync(catalog, "GetModelAsync", modelSelection);
        if (model is not null)
        {
            return model;
        }

        model = await TryInvokeTaskResultAsync(catalog, "GetModelVariantAsync", modelSelection);
        if (model is not null)
        {
            return model;
        }

        if (await TryInvokeTaskResultAsync(catalog, "ListModelsAsync") is not IEnumerable models)
        {
            return null;
        }

        foreach (var candidate in models.Cast<object>())
        {
            if (MatchesModelSelection(candidate, modelSelection))
            {
                return candidate;
            }
        }

        return null;
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

    private static async Task<bool> TryInvokeTaskAsync(object target, string methodName, params object?[] preferredValues)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == methodName)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        if (method is null)
        {
            return false;
        }

        var taskLike = method.Invoke(target, CreateMethodArguments(method, preferredValues))
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' returned no task.");

        await AwaitTaskResultAsync(taskLike);
        return true;
    }

    private static async Task<object?> TryInvokeTaskResultAsync(object target, string methodName, params object?[] preferredValues)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == methodName)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        if (method is null)
        {
            return null;
        }

        var taskLike = method.Invoke(target, CreateMethodArguments(method, preferredValues))
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' returned no task.");

        return await AwaitTaskResultAsync(taskLike);
    }

    private static async Task InvokeTaskAsync(object target, string methodName, params object?[] preferredValues)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == methodName)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' was not found on {target.GetType().FullName}.");

        var taskLike = method.Invoke(target, CreateMethodArguments(method, preferredValues))
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' returned no task.");

        await AwaitTaskResultAsync(taskLike);
    }

    private static async Task<object?> InvokeTaskResultAsync(object target, string methodName, params object?[] preferredValues)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == methodName)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' was not found on {target.GetType().FullName}.");

        var taskLike = method.Invoke(target, CreateMethodArguments(method, preferredValues))
            ?? throw new InvalidOperationException($"Foundry Local method '{methodName}' returned no task.");

        return await AwaitTaskResultAsync(taskLike);
    }

    private static async Task<object?> AwaitTaskResultAsync(object taskLike)
    {
        if (taskLike is Task task)
        {
            await task;
            return taskLike.GetType().GetProperty("Result")?.GetValue(taskLike);
        }

        throw new InvalidOperationException("Foundry Local returned an unexpected async result.");
    }

    private static object CreateChatMessages(List<(string role, string content)> chatHistory)
    {
        var messageType = Type.GetType(ChatMessageTypeName, throwOnError: true)!;
        var listType = typeof(List<>).MakeGenericType(messageType);
        var list = Activator.CreateInstance(listType)
            ?? throw new InvalidOperationException("Could not create Foundry Local chat message list.");
        var addMethod = listType.GetMethod("Add")!;

        addMethod.Invoke(list, new[] { CreateChatMessage(messageType, "system", SystemPrompt) });

        foreach (var (role, content) in chatHistory)
        {
            var normalizedRole = role is "assistant" or "system" ? role : "user";
            addMethod.Invoke(list, new[] { CreateChatMessage(messageType, normalizedRole, content) });
        }

        return list;
    }

    private static object CreateChatMessage(Type messageType, string role, string content)
    {
        var message = Activator.CreateInstance(messageType)
            ?? throw new InvalidOperationException("Could not create Foundry Local chat message.");

        messageType.GetProperty("Role")?.SetValue(message, role);
        messageType.GetProperty("Content")?.SetValue(message, content);
        return message;
    }

    private static string ReadCompletionText(object completion)
    {
        if (GetMemberValue(completion, "Choices") is not IEnumerable choices)
        {
            return string.Empty;
        }

        foreach (var choice in choices.Cast<object>())
        {
            var message = GetMemberValue(choice, "Message");
            var content = GetMemberValue(message, "Content")?.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static ModelRuntimeInfo ReadRuntimeInfo(object? model, string modelAlias, string? modelId, Uri? endpoint)
    {
        var selectedVariant = GetMemberValue(model, "SelectedVariant");
        var variantInfo = GetMemberValue(selectedVariant, "Info");
        var runtime = GetMemberValue(variantInfo, "Runtime");

        var deviceType = GetMemberValue(runtime, "DeviceType")?.ToString() ?? "Unknown";
        var executionProvider = GetMemberValue(runtime, "ExecutionProvider")?.ToString() ?? "Unknown";
        var modelDisplayName = GetMemberValue(variantInfo, "DisplayName")?.ToString();
        var providerType = GetMemberValue(variantInfo, "ProviderType")?.ToString();

        var endpointText = endpoint?.ToString() ?? "In-process SDK client";
        var isNpu = string.Equals(deviceType, "NPU", StringComparison.OrdinalIgnoreCase)
            || executionProvider.Contains("npu", StringComparison.OrdinalIgnoreCase)
            || executionProvider.Contains("qnn", StringComparison.OrdinalIgnoreCase);

        var acceleratorSummary = isNpu
            ? $"NPU ({executionProvider})"
            : string.Equals(deviceType, "Unknown", StringComparison.OrdinalIgnoreCase)
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

    private static Uri? GetManagerEndpoint(object manager)
    {
        if (GetMemberValue(manager, "Urls") is string[] urls)
        {
            var firstUrl = urls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
            if (Uri.TryCreate(firstUrl, UriKind.Absolute, out var endpoint))
            {
                return endpoint;
            }
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
        string DeviceType,
        string ExecutionProvider,
        string ProviderType,
        bool IsNpu,
        string AcceleratorSummary);
}

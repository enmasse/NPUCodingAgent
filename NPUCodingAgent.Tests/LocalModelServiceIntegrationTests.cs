using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using NPUCodingAgent.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NPUCodingAgent.Tests;

public sealed class LocalModelServiceIntegrationTests
{
    [Fact]
    public async Task ListAvailableModelsAsync_ReturnsModels()
    {
        await using var service = new LocalModelService();
        await service.InitializeAsync();

        var models = await service.ListAvailableModelsAsync();

        Assert.NotEmpty(models);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsReadyStatus()
    {
        await using var service = new LocalModelService();
        await service.InitializeAsync();

        var status = await service.GetStatusAsync();

        Assert.Contains("Status: Ready", status);
        Assert.Contains("Model:", status);
        Assert.Contains("Endpoint:", status);
        Assert.False(string.IsNullOrWhiteSpace(service.Endpoint));
    }

    [Fact]
    public async Task GetResponseAsync_ReturnsNonEmptyResponse()
    {
        await using var service = new LocalModelService();
        await service.InitializeAsync();

        var response = await service.GetResponseAsync(
        [
            new ChatMessage("user", "Reply with a short sentence confirming the model connection is working.")
        ]);

        Assert.False(string.IsNullOrWhiteSpace(response));
    }
}

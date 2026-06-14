using NPUCodingAgent.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NPUCodingAgent.Tests;

public sealed class LocalModelServiceIntegrationTests
{
    [Fact]
    public async Task ListAvailableModelsAsync_ReturnsModels()
    {
        using var service = new LocalModelService();

        var models = await service.ListAvailableModelsAsync();

        Assert.NotEmpty(models);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsReadyStatus()
    {
        using var service = new LocalModelService();

        var status = await service.GetStatusAsync();

        Assert.Contains("Status: Ready", status);
        Assert.Contains("Model:", status);
        Assert.Contains("Endpoint:", status);
        Assert.False(string.IsNullOrWhiteSpace(service.Endpoint));
    }

    [Fact]
    public async Task GetResponseAsync_ReturnsNonEmptyResponse()
    {
        using var service = new LocalModelService();

        var response = await service.GetResponseAsync(
        [
            ("user", "Reply with a short sentence confirming the model connection is working.")
        ]);

        Assert.False(string.IsNullOrWhiteSpace(response));
    }
}

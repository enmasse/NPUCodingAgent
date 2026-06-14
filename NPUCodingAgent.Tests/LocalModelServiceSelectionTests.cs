using NPUCodingAgent.Services;
using Xunit;

namespace NPUCodingAgent.Tests;

public sealed class LocalModelServiceSelectionTests
{
    [Fact]
    public void CollectSelectableModelNames_IncludesVariantAliasesAndIds()
    {
        var models = new object[]
        {
            new FakeModel
            {
                Alias = "phi-4-mini",
                Id = "phi-4-mini",
                Variants =
                [
                    new FakeModel
                    {
                        Alias = "phi-4-mini-instruct-qnn-npu",
                        Id = "phi-4-mini-instruct-qnn-npu:4"
                    },
                    new FakeModel
                    {
                        Alias = "PHI-4-MINI-INSTRUCT-QNN-NPU",
                        Id = "phi-4-mini-instruct-qnn-npu:4"
                    }
                ]
            },
            new FakeModel
            {
                Alias = "qwen2.5-coder",
                Id = "qwen2.5-coder",
                Variants =
                [
                    new FakeModel
                    {
                        Alias = "qwen2.5-coder-0.5b-instruct-qnn-npu",
                        Id = "qwen2.5-coder-0.5b-instruct-qnn-npu:4"
                    }
                ]
            }
        };

        var names = LocalModelService.CollectSelectableModelNames(models);

        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("phi-4-mini", names);
        Assert.Contains("phi-4-mini-instruct-qnn-npu", names);
        Assert.Contains("phi-4-mini-instruct-qnn-npu:4", names);
        Assert.Contains("qwen2.5-coder-0.5b-instruct-qnn-npu", names);
        Assert.Contains("qwen2.5-coder-0.5b-instruct-qnn-npu:4", names);
    }

    [Fact]
    public void MatchesModelSelection_MatchesNestedVariantAliasesAndIds()
    {
        var model = new FakeModel
        {
            Alias = "phi-4-mini",
            Id = "phi-4-mini",
            Variants =
            [
                new FakeModel
                {
                    Alias = "phi-4-mini-instruct-qnn-npu",
                    Id = "phi-4-mini-instruct-qnn-npu:4"
                }
            ]
        };

        Assert.True(LocalModelService.MatchesModelSelection(model, "phi-4-mini-instruct-qnn-npu"));
        Assert.True(LocalModelService.MatchesModelSelection(model, "phi-4-mini-instruct-qnn-npu:4"));
        Assert.False(LocalModelService.MatchesModelSelection(model, "non-existent-model"));
    }

    [Fact]
    public void CollectSelectableModelNames_IgnoresCycles()
    {
        var parent = new MutableFakeModel
        {
            Alias = "cycle-parent",
            Id = "cycle-parent"
        };

        var child = new MutableFakeModel
        {
            Alias = "cycle-child-qnn-npu",
            Id = "cycle-child-qnn-npu:4"
        };

        parent.Variants = [child];
        child.Variants = [parent];

        var names = LocalModelService.CollectSelectableModelNames([parent]);

        Assert.Contains("cycle-parent", names);
        Assert.Contains("cycle-child-qnn-npu", names);
        Assert.Contains("cycle-child-qnn-npu:4", names);
    }

    private sealed class FakeModel
    {
        public string? Alias { get; init; }

        public string? Id { get; init; }

        public object[]? Variants { get; init; }
    }

    private sealed class MutableFakeModel
    {
        public string? Alias { get; init; }

        public string? Id { get; init; }

        public object[]? Variants { get; set; }
    }
}

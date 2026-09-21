using Microsoft.Build.Framework;
using Scarlet.Sass.MSBuild.Tests.Mock;

namespace Scarlet.Sass.MSBuild.Tests;

/// <summary>
/// Covers <see cref="SassRuntimePackFactory"/>, the only part of the pack contract that speaks MSBuild.
/// </summary>
public class SassRuntimePackFactoryTests
{
    private static FakeTaskItem Pack(
        string id,
        string? rid = "osx-arm64",
        string? runtimesPath = "/packs/osx-arm64/runtimes",
        string? variant = null,
        string? priority = null)
    {
        var metadata = new Dictionary<string, string?>();

        if (rid is not null)
        {
            metadata[SassRuntimePack.RidMetadataName] = rid;
        }

        if (runtimesPath is not null)
        {
            metadata[SassRuntimePack.RuntimesPathMetadataName] = runtimesPath;
        }

        if (variant is not null)
        {
            metadata[SassRuntimePack.VariantMetadataName] = variant;
        }

        if (priority is not null)
        {
            metadata[SassRuntimePack.PriorityMetadataName] = priority;
        }

        return new FakeTaskItem(id, metadata);
    }

    [Fact]
    public void FromTaskItems_WithNull_ShouldReturnEmpty()
    {
        // Act
        var packs = SassRuntimePackFactory.FromTaskItems(null);

        // Assert
        Assert.Empty(packs);
    }

    [Fact]
    public void FromTaskItems_ShouldReadAllMetadata()
    {
        // Arrange
        var items = new ITaskItem[]
        {
            Pack("Scarlet.Sass.Runtime.darwin-arm64", variant: "default", priority: "7")
        };

        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(items));

        // Assert
        Assert.Equal("Scarlet.Sass.Runtime.darwin-arm64", pack.Id);
        Assert.Equal("osx-arm64", pack.Rid);
        Assert.Equal("/packs/osx-arm64/runtimes", pack.RuntimesPath);
        Assert.Equal("default", pack.Variant);
        Assert.Equal(7, pack.Priority);
    }

    [Fact]
    public void FromTaskItems_WithoutPriority_ShouldDefaultToZero()
    {
        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(new ITaskItem[] { Pack("pack") }));

        // Assert
        Assert.Equal(0, pack.Priority);
        Assert.Null(pack.Variant);
    }

    [Theory]
    [InlineData(null, "/packs/runtimes", SassRuntimePack.RidMetadataName)]
    [InlineData("osx-arm64", null, SassRuntimePack.RuntimesPathMetadataName)]
    public void FromTaskItems_WithMissingRequiredMetadata_ShouldSkipAndReport(string? rid, string? runtimesPath, string expectedMetadataName)
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { Pack("Incomplete.Pack", rid, runtimesPath) };

        // Act
        var packs = SassRuntimePackFactory.FromTaskItems(items, reported.Add);

        // Assert
        Assert.Empty(packs);
        var message = Assert.Single(reported);
        Assert.Contains("Incomplete.Pack", message);
        Assert.Contains(expectedMetadataName, message);
    }

    [Fact]
    public void FromTaskItems_WithNonNumericPriority_ShouldFallBackToZeroAndReport()
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { Pack("Odd.Pack", priority: "highest") };

        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(items, reported.Add));

        // Assert
        Assert.Equal(0, pack.Priority);
        Assert.Contains(reported, message => message.Contains("Odd.Pack") && message.Contains("highest"));
    }

    [Fact]
    public void FromTaskItems_WithSamePackTwice_ShouldKeepOne()
    {
        // Arrange - a package's props can be imported more than once, and items are additive
        var items = new ITaskItem[]
        {
            Pack("Scarlet.Sass.Runtime.darwin-arm64", runtimesPath: "/packs/osx-arm64/runtimes"),
            Pack("Scarlet.Sass.Runtime.darwin-arm64", runtimesPath: "/packs/osx-arm64/other/../runtimes")
        };

        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(items));

        // Assert
        Assert.Equal("/packs/osx-arm64/runtimes", pack.RuntimesPath);
    }

    [Fact]
    public void FromTaskItems_WithNullEntry_ShouldSkipItSilently()
    {
        // Arrange - not something MSBuild produces, but FromTaskItems is public
        var items = new ITaskItem?[] { null, Pack("Good.Pack") };
        var reported = new List<string>();

        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(items, reported.Add));

        // Assert
        Assert.Equal("Good.Pack", pack.Id);
        Assert.Empty(reported);
    }

    [Fact]
    public void FromTaskItems_WithEmptyIdentity_ShouldSkipAndReport()
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { Pack(string.Empty) };

        // Act
        var packs = SassRuntimePackFactory.FromTaskItems(items, reported.Add);

        // Assert
        Assert.Empty(packs);
        Assert.Contains("without an identity", Assert.Single(reported));
    }

    [Theory]
    [InlineData(SassRuntimePack.RidMetadataName)]
    [InlineData(SassRuntimePack.RuntimesPathMetadataName)]
    public void FromTaskItems_WithNullRequiredMetadata_ShouldTreatItAsMissing(string metadataName)
    {
        // Arrange - ITaskItem.GetMetadata is meant to return an empty string for absent metadata, but the
        // interface cannot enforce it, so a null must not take the parser down
        var reported = new List<string>();
        var metadata = new Dictionary<string, string?>
        {
            [SassRuntimePack.RidMetadataName] = "osx-arm64",
            [SassRuntimePack.RuntimesPathMetadataName] = "/packs/runtimes",
            [metadataName] = null
        };

        // Act
        var packs = SassRuntimePackFactory.FromTaskItems(new ITaskItem[] { new FakeTaskItem("Null.Pack", metadata) }, reported.Add);

        // Assert
        Assert.Empty(packs);
        Assert.Contains(metadataName, Assert.Single(reported));
    }

    [Fact]
    public void FromTaskItems_WithNullOptionalMetadata_ShouldFallBackToDefaults()
    {
        // Arrange
        var reported = new List<string>();
        var metadata = new Dictionary<string, string?>
        {
            [SassRuntimePack.RidMetadataName] = "osx-arm64",
            [SassRuntimePack.RuntimesPathMetadataName] = "/packs/runtimes",
            [SassRuntimePack.VariantMetadataName] = null,
            [SassRuntimePack.PriorityMetadataName] = null
        };

        // Act
        var pack = Assert.Single(SassRuntimePackFactory.FromTaskItems(new ITaskItem[] { new FakeTaskItem("Null.Pack", metadata) }, reported.Add));

        // Assert
        Assert.Null(pack.Variant);
        Assert.Equal(0, pack.Priority);
        Assert.Empty(reported);
    }

    [Fact]
    public void FromTaskItems_WithNullIdentity_ShouldSkipAndReport()
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { new FakeTaskItem(itemSpec: null) };

        // Act
        var packs = SassRuntimePackFactory.FromTaskItems(items, reported.Add);

        // Assert
        Assert.Empty(packs);
        Assert.Contains("without an identity", Assert.Single(reported));
    }

}

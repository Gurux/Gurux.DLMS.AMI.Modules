using Gurux.AMI.Module.Metadata;
using System.Text.Json;
using Xunit;

namespace Gurux.AMI.Module.Metadata.Tests;

public sealed class MetadataTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "gurux-metadata-" + Guid.NewGuid().ToString("N"));
    private const string Id = "Gurux.AMI.Module.Metadata.Tests";

    public MetadataTests()
    {
        Directory.CreateDirectory(directory);
        File.Copy(typeof(MetadataTests).Assembly.Location, Path.Combine(directory, Id + ".dll"));
    }

    [Fact]
    public void ReadsActualAssemblyVersionAndFrameworkWithoutLoadingThePackage()
    {
        var result = CompatibilityMetadata.Create(directory, Id, "1.2.3");
        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(Id, result.Id);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("net10.0", result.TargetFramework);
        Assert.Contains(result.References, r => r.Name == "System.Runtime.dll" && r.Version == "10.0.0.0");
        Assert.Contains(result.References, r => r.Name == "Gurux.AMI.Module.Metadata.dll");
        Assert.DoesNotContain(result.References, r => r.Name == Id + ".dll");
        Assert.Equal(result.References.Count, result.References.Select(r => r.Name).Distinct().Count());
    }

    [Fact]
    public void BundledDependenciesAreTraversedInsteadOfRequiredFromTheHost()
    {
        File.Copy(typeof(CompatibilityMetadata).Assembly.Location, Path.Combine(directory, "Gurux.AMI.Module.Metadata.dll"));
        var result = CompatibilityMetadata.Create(directory, Id, "1.2.3");
        Assert.DoesNotContain(result.References, r => r.Name == "Gurux.AMI.Module.Metadata.dll");
        Assert.Contains(result.References, r => r.Name == "System.Reflection.Metadata.dll");
    }

    [Theory]
    [InlineData("Wrong.Module", "1.2.3")]
    [InlineData(Id, "9.9.9")]
    [InlineData("../outside", "1.2.3")]
    public void RejectsMismatchedOrUnsafeReleaseIdentity(string id, string version)
        => Assert.ThrowsAny<Exception>(() => CompatibilityMetadata.Create(directory, id, version));

    [Fact]
    public void OlderFrameworkReferencesInBundledDependenciesUseTheHighestHostRequirement()
    {
        File.Copy(typeof(Assert).Assembly.Location, Path.Combine(directory, Path.GetFileName(typeof(Assert).Assembly.Location)));
        var result = CompatibilityMetadata.Create(directory, Id, "1.2.3");
        Assert.Single(result.References, r => r.Name == "System.Runtime.dll");
        Assert.Equal("10.0.0.0", result.References.Single(r => r.Name == "System.Runtime.dll").Version);
    }

    [Fact]
    public void RejectsInvalidMainDll()
    {
        File.WriteAllText(Path.Combine(directory, Id + ".dll"), "not an assembly");
        Assert.ThrowsAny<Exception>(() => CompatibilityMetadata.Create(directory, Id, "1.2.3"));
    }

    [Fact]
    public void WritesCatalogCompatibleJson()
    {
        string path = CompatibilityMetadata.Write(directory, Id, "1.2.3");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("module.compatibility.json", Path.GetFileName(path));
        Assert.Equal(Id, json.RootElement.GetProperty("id").GetString());
        var reference = json.RootElement.GetProperty("references")[0];
        Assert.True(reference.TryGetProperty("name", out _));
        Assert.True(reference.TryGetProperty("version", out _));
    }

    public void Dispose() => Directory.Delete(directory, true);
}

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gurux.AMI.Module.Metadata;

/// <summary>Exact host assembly requirement for a module package.</summary>
public sealed record AssemblyRequirement(string Name, string Version);

/// <summary>Compatibility requirements read from compiled package metadata without executing it.</summary>
public sealed record ModuleCompatibility(int SchemaVersion, string Id, string Version,
    string TargetFramework, IReadOnlyList<AssemblyRequirement> References);

/// <summary>Reads the deployed dependency graph and emits requirements not bundled in the package.</summary>
public static class CompatibilityMetadata
{
    private sealed record AssemblyInfo(string Name, Version Version, string? ProductVersion,
        string? Framework, IReadOnlyList<AssemblyRequirement> References);

    /// <summary>Creates a manifest from neutral managed assemblies in a publish directory.</summary>
    public static ModuleCompatibility Create(string directory, string id, string version)
    {
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z") ||
            !Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z"))
            throw new InvalidDataException("Invalid module release identity.");
        var main = ReadAssembly(Path.Combine(directory, id + ".dll"))
            ?? throw new InvalidDataException("Main module is not a managed assembly.");
        if (main.Name != id || main.ProductVersion?.Split('+')[0] != version.Split('+')[0])
            throw new InvalidDataException("Module assembly identity/version does not match the release.");
        if (main.Framework != ".NETCoreApp,Version=v10.0")
            throw new InvalidDataException("Expected a net10.0 module assembly.");

        var bundled = new Dictionary<string, AssemblyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            var info = ReadAssembly(path);
            if (info is null) continue; // Native libraries and resource satellites aren't host contracts.
            if (bundled.TryGetValue(info.Name, out var existing) && existing.Version != info.Version)
                throw new InvalidDataException($"Conflicting bundled versions of '{info.Name}'.");
            bundled[info.Name] = info;
        }
        var requirements = new Dictionary<string, AssemblyRequirement>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<AssemblyInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(main);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current.Name)) continue;
            foreach (var reference in current.References)
            {
                string name = reference.Name[..^4];
                if (bundled.TryGetValue(name, out var included))
                {
                    // The CLR can satisfy an older reference with a newer bundled DLL.
                    if (included.Version < Version.Parse(reference.Version))
                        throw new InvalidDataException($"Bundled dependency version mismatch for '{reference.Name}'.");
                    pending.Enqueue(included);
                }
                else
                {
                    if (requirements.TryGetValue(reference.Name, out var existing))
                    {
                        if (existing.Version != reference.Version && Regex.IsMatch(reference.Name,
                            @"\AGurux\.(DLMS\.AMI\.(Module|Shared|Services|Components)|Service(\.Common)?|UI\.Components)\.dll\z"))
                            throw new InvalidDataException($"Conflicting shared host requirements for '{reference.Name}'.");
                        if (Version.Parse(existing.Version) >= Version.Parse(reference.Version)) continue;
                    }
                    requirements[reference.Name] = reference;
                }
            }
        }
        if (requirements.Count == 0) throw new InvalidDataException("Module has no host assembly requirements.");
        // Older transitive libraries can reference netstandard 2.0 while .NET 10
        // supplies netstandard 2.1. Catalog's existing check uses exact offered DLL
        // versions, so use the target runtime's actual framework identities.
        foreach (string path in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            string name = Path.GetFileName(path);
            if (!requirements.TryGetValue(name, out var required)) continue;
            try
            {
                var offered = AssemblyName.GetAssemblyName(path).Version!;
                if (offered >= Version.Parse(required.Version))
                    requirements[name] = new(name, offered.ToString());
            }
            catch (BadImageFormatException) { }
        }
        return new(1, id, version, "net10.0", requirements.Values
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>Writes the package's module.compatibility.json after successful validation.</summary>
    public static string Write(string directory, string id, string version)
    {
        var metadata = Create(directory, id, version);
        string output = Path.Combine(directory, "module.compatibility.json");
        File.WriteAllText(output, JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
        return output;
    }

    private static AssemblyInfo? ReadAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return null;
        var assembly = reader.GetAssemblyDefinition();
        if (!assembly.Culture.IsNil && reader.GetString(assembly.Culture).Length != 0) return null;
        string? productVersion = null, framework = null;
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            string name = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
            if (name is not ("System.Reflection.AssemblyInformationalVersionAttribute" or
                             "System.Runtime.Versioning.TargetFrameworkAttribute")) continue;
            var value = reader.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) throw new InvalidDataException("Invalid assembly attribute.");
            if (name.EndsWith("AssemblyInformationalVersionAttribute")) productVersion = value.ReadSerializedString();
            else framework = value.ReadSerializedString();
        }
        return new(reader.GetString(assembly.Name), assembly.Version, productVersion, framework,
            reader.AssemblyReferences.Select(handle =>
            {
                var reference = reader.GetAssemblyReference(handle);
                return new AssemblyRequirement(reader.GetString(reference.Name) + ".dll", reference.Version.ToString());
            }).ToArray());
    }
}

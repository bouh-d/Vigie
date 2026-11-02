using System.Reflection;
using System.Runtime.InteropServices;
using Vigie.Core.Interop;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

/// <summary>
/// An import that names the wrong library builds and loads, then throws at its
/// first call. Every declaration is checked against the library it names.
/// </summary>
public class NativeImportTests
{
    public static TheoryData<string, string> Imports()
    {
        var imports = new TheoryData<string, string>();
        foreach (MethodInfo method in typeof(NativeMethods).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<LibraryImportAttribute>() is { } import)
            {
                imports.Add(import.LibraryName, import.EntryPoint ?? method.Name);
            }
        }

        return imports;
    }

    [Fact]
    public void ReflectionFindsTheImports()
        => Assert.Contains(Imports(), row => (string)row[1] == nameof(NativeMethods.PowerCreateRequest));

    [WindowsTheory]
    [MemberData(nameof(Imports))]
    public void TheNamedLibraryExportsTheFunction(string library, string entryPoint)
    {
        nint handle = NativeLibrary.Load(library);
        Assert.True(NativeLibrary.TryGetExport(handle, entryPoint, out _), $"{library} does not export {entryPoint}.");
    }
}

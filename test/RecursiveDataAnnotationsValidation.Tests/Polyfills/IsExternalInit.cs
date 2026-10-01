#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// The C# compiler marks `init` accessors, which records use, with this type. .NET 5 and
    /// later define it. .NET Framework does not, so the compiler reports CS0518 unless the
    /// project defines it. The compiler only needs the name, so an empty internal class works.
    /// NETFRAMEWORK is a preprocessor symbol the SDK defines for every .NET Framework target.
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/init
    /// See: https://learn.microsoft.com/dotnet/standard/frameworks#preprocessor-symbols
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif

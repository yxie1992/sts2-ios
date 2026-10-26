// The upstream harness is deliberately allowed to use game test/bootstrap APIs.
// This applies to this ordinary .NET validation executable, never to iOS export.
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("sts2")]

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}

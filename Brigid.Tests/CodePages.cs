using System.Runtime.CompilerServices;
using System.Text;

namespace Brigid.Tests;

// The metafiles and the repositories use code page 949. A type that reads it when it first loads fails for the whole run
// when the provider is not registered yet, and the order of the tests would then decide which test fails. The client
// registers it at its start (GlobalSettings); the test assembly registers it when it loads.
internal static class CodePages
{
    [ModuleInitializer]
    internal static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}

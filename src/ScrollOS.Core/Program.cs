using ScrollOS.Core;
using ScrollOS.Core.Lifecycle;
using ScrollOS.Core.Terminal;

string? home = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--home" && i + 1 < args.Length) home = args[++i];
    else if (args[i] is "-h" or "--help")
    {
        Console.WriteLine("Usage: scrollos [--home <dir>]");
        Console.WriteLine("  --home  where the timeline is stored (default: %USERPROFILE%\\.scrollos)");
        return 0;
    }
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("ScrollOS currently runs on Windows only.");
    return 1;
}

var terminal = WindowsConsole.TryOpen(out var error);
if (terminal is null)
{
    Console.Error.WriteLine(error);
    return 1;
}

var paths = ScrollPaths.Resolve(home);
try
{
    return await new ScrollOSApp(terminal, paths).RunAsync();
}
catch (Exception ex)
{
    terminal.Dispose();
    Console.Error.WriteLine($"ScrollOS crashed: {ex}");
    return 1;
}
finally
{
    terminal.Dispose();
}

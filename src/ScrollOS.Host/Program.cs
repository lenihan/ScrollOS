using ScrollOS.Host;

// The protocol owns stdout; anything else that writes to Console.Out goes to stderr (the host log).
var protocolOut = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

var host = new ScrollHost(
    protocolOut,
    Environment.GetEnvironmentVariable("SCROLLOS_SDK") ?? "",
    Environment.GetEnvironmentVariable("SCROLLOS_APPS") ?? "");
return await host.RunAsync(Console.OpenStandardInput());

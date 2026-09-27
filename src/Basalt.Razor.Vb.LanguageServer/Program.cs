using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Server;

// A language server speaks over standard input and output, so nothing else
// may be written there: a stray Console.WriteLine corrupts the protocol and
// the editor reports the server as crashed.

var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
    ServerSetup.Configure(options, Console.OpenStandardInput(), Console.OpenStandardOutput()));

await server.WaitForExit;

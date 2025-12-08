using Lilysharp.Lsp;

// The LSP server communicates via stdin/stdout
var server = new LilysharpLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
await server.RunAsync();
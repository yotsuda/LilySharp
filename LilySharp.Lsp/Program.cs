using LilySharp.Lsp;

// The LSP server communicates via stdin/stdout
var server = new LilySharpLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
await server.RunAsync();
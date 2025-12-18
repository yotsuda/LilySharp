using LilySharp.Lsp;

try
{
    // Log to stderr for debugging (won't interfere with LSP protocol on stdout)
    Console.Error.WriteLine("LSP Server starting...");
    
    // The LSP server communicates via stdin/stdout
    var server = new LilySharpLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
    
    Console.Error.WriteLine("LSP Server initialized, starting to listen...");
    
    await server.RunAsync();
    
    Console.Error.WriteLine("LSP Server RunAsync completed");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"LSP Server fatal error: {ex}");
    throw;
}

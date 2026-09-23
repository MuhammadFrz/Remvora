using Remvora.Core.Policies;
using Remvora.Elevation;

string? pipeName = null;
string? nonce = null;
int? parentPid = null;

for (var i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        pipeName = args[++i];
    }
    else if (string.Equals(args[i], "--nonce", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        nonce = args[++i];
    }
    else if (string.Equals(args[i], "--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out var parsedPid))
    {
        parentPid = parsedPid;
    }
}

if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(nonce))
{
    Console.Error.WriteLine("Error: Remvora.ElevatedWorker requires --pipe <pipeName> and --nonce <nonce> arguments.");
    return 1;
}

try
{
    var policy = ProtectedPathsPolicy.Default;
    var executor = new ElevatedOperationExecutor(policy);
    var server = new WorkerServer(pipeName, nonce, executor, parentPid);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await server.RunAsync(cts.Token).ConfigureAwait(false);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal worker server error: {ex.Message}");
    return 2;
}

namespace Sharp.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        return await SharpCliApp.RunAsync(args, cts.Token);
    }
}

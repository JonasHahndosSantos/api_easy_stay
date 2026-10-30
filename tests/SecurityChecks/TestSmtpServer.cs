using System.Net;
using System.Net.Sockets;
using System.Text;

sealed class TestSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(15));
    private readonly Task _connection;
    public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public TestSmtpServer()
    {
        _listener.Start();
        _connection = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 localhost test SMTP");
            while (await reader.ReadLineAsync(_timeout.Token) is { } command)
            {
                if (command.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 AUTH LOGIN");
                }
                else if (command.StartsWith("AUTH LOGIN", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                    Username = Encoding.UTF8.GetString(Convert.FromBase64String(parts.Length >= 3 ? parts[2] : (await reader.ReadLineAsync(_timeout.Token))!));
                    await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                    Password = Encoding.UTF8.GetString(Convert.FromBase64String((await reader.ReadLineAsync(_timeout.Token))!));
                    await writer.WriteLineAsync("235 authenticated");
                }
                else if (command.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 end with a dot");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync(_timeout.Token) is { } line && line != ".") data.AppendLine(line);
                    Message.TrySetResult(data.ToString());
                    await writer.WriteLineAsync("250 queued");
                }
                else if (command.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 closing");
                    break;
                }
                else await writer.WriteLineAsync("250 OK");
            }
        }
        catch (Exception error) { Message.TrySetException(error); }
    }

    public async ValueTask DisposeAsync()
    {
        await _timeout.CancelAsync();
        _listener.Stop();
        await _connection;
        _timeout.Dispose();
    }
}

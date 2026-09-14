using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EShop.Notification.UnitTests.TestDoubles;

/// <summary>
/// Just enough SMTP for MailKit on a plaintext loopback connection: greeting, EHLO, MAIL, RCPT, DATA, QUIT. It
/// advertises no STARTTLS and no AUTH, so a client that tries to log in fails. It records every command line and every
/// recipient. Used by <c>EmailServiceSmtpTests</c> and <c>SmtpHealthCheckTests</c> (Notification audit S5, S6).
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly bool _dropOnQuit;
    private readonly Task _acceptLoop;

    private FakeSmtpServer(bool dropOnQuit)
    {
        _dropOnQuit = dropOnQuit;
        _listener.Start();
        _acceptLoop = Task.Run(AcceptAsync);
    }

    /// <summary>Every command line received, outside message data.</summary>
    public ConcurrentQueue<string> Commands { get; } = new();

    public ConcurrentQueue<string> Recipients { get; } = new();

    /// <summary>Every message received after DATA, as sent (dot-stuffing undone).</summary>
    public ConcurrentQueue<string> Messages { get; } = new();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <param name="dropOnQuit">Close the connection on QUIT without answering it.</param>
    public static FakeSmtpServer Start(bool dropOnQuit = false) => new(dropOnQuit);

    /// <summary>A loopback port with nothing listening on it.</summary>
    public static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            await writer.WriteLineAsync("220 fake.smtp ESMTP");
            var inData = false;
            var data = new StringBuilder();

            while (await reader.ReadLineAsync() is { } line)
            {
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        Messages.Enqueue(data.ToString());
                        await writer.WriteLineAsync("250 2.0.0 Ok: queued");
                    }
                    else
                    {
                        data.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
                    }

                    continue;
                }

                Commands.Enqueue(line);

                switch (line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant())
                {
                    case "EHLO":
                    case "HELO":
                        await writer.WriteLineAsync("250 fake.smtp");
                        break;
                    case "RCPT":
                        var start = line.IndexOf('<') + 1;
                        Recipients.Enqueue(line[start..line.IndexOf('>')]);
                        await writer.WriteLineAsync("250 2.1.5 Ok");
                        break;
                    case "DATA":
                        inData = true;
                        data.Clear();
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        break;
                    case "QUIT":
                        if (!_dropOnQuit)
                        {
                            await writer.WriteLineAsync("221 2.0.0 Bye");
                        }

                        return;
                    default:
                        await writer.WriteLineAsync("250 Ok");
                        break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _acceptLoop;
        _stop.Dispose();
    }
}

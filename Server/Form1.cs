using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace Server
{
    public partial class Form1 : Form
    {
        private const int DEFAULT_BUFLEN = 512;
        private const int DEFAULT_PORT = 27015;
        private static ConcurrentQueue<(TcpClient, byte[])> messageQueue = new ConcurrentQueue<(TcpClient, byte[])>();
        private static TcpListener? listener;
        private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private static ConcurrentDictionary<int, (TcpClient client, string ip, int port)> clients = new ConcurrentDictionary<int, (TcpClient, string, int)>();
        private static int clientCounter = 0;
        private TextBox? txtLog;

        public Form1()
        {
            InitializeComponent();
            SetupUI();
            StartServerAsync();
        }

        private void SetupUI()
        {
            Text = "SERVER SIDE";
            Size = new Size(600, 700);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Left = 15;
            Top = 15;
            BackColor = Color.FromArgb(240, 240, 240);

            txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Location = new Point(10, 10),
                Size = new Size(560, 640),
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtLog);
        }

        private async void StartServerAsync()
        {
            AppendLog("Запуск сервера...");
            try
            {
                listener = new TcpListener(IPAddress.Any, DEFAULT_PORT);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();
                AppendLog("Сервер запущен. Ожидание клиентов...");

                _ = ProcessMessages();

                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        int clientId = Interlocked.Increment(ref clientCounter);
                        var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                        clients.TryAdd(clientId, (client, clientEndPoint.Address.ToString(), clientEndPoint.Port));
                        AppendLog($"Клиент #{clientId} подключился: IP {clientEndPoint.Address}, Порт {clientEndPoint.Port}");
                        _ = HandleClientAsync(client, clientId);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.OperationAborted)
                    {
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                AppendLog("Порт 27015 уже используется.");
            }
            catch (Exception ex)
            {
                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    AppendLog($"Ошибка: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, int clientId)
        {
            NetworkStream? stream = null;
            try
            {
                stream = client.GetStream();
                while (!cancellationTokenSource.Token.IsCancellationRequested && client.Connected)
                {
                    var buffer = new byte[DEFAULT_BUFLEN];
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationTokenSource.Token).ConfigureAwait(false);

                    if (bytesReceived > 0)
                    {
                        messageQueue.Enqueue((client, buffer[..bytesReceived]));
                        AppendLog($"Клиент #{clientId}: Добавлено сообщение в очередь.");
                    }
                    else
                    {
                        break; // клиент отключился
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка с клиентом #{clientId}: {ex.Message}");
            }
            finally
            {
                stream?.Dispose();
                client.Close();
                clients.TryRemove(clientId, out _);
                AppendLog($"Клиент #{clientId} отключился.");
            }
        }

        private async Task ProcessMessages()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (messageQueue.TryDequeue(out var item))
                    {
                        var (client, buffer) = item;
                        if (!client.Connected) continue;

                        string message = Encoding.UTF8.GetString(buffer);
                        var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                        int clientId = clients.FirstOrDefault(x => x.Value.ip == clientEndPoint.Address.ToString() && x.Value.port == clientEndPoint.Port).Key;
                        AppendLog($"Клиент #{clientId} отправил сообщение: {message}");

                        await Task.Delay(100, cancellationTokenSource.Token).ConfigureAwait(false);

                        var response = new string(message.Reverse().ToArray());
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);

                        try
                        {
                            var stream = client.GetStream();
                            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationTokenSource.Token).ConfigureAwait(false);
                            AppendLog($"Ответ клиенту #{clientId}: {response}");
                        }
                        catch
                        {
                            AppendLog($"Не удалось отправить сообщение клиенту #{clientId}.");
                        }
                    }
                    await Task.Delay(15, cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppendLog($"Ошибка в ProcessMessages: {ex.Message}");
                }
            }
        }

        private async Task StopServerAsync()
        {
            try
            {
                cancellationTokenSource.Cancel();

                foreach (var clientInfo in clients.Values)
                {
                    try
                    {
                        clientInfo.client.Close();
                        clientInfo.client.Dispose();
                        AppendLog($"Клиент с IP {clientInfo.ip}:{clientInfo.port} закрыт.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Ошибка при закрытии клиента {clientInfo.ip}:{clientInfo.port}: {ex.Message}");
                    }
                }
                clients.Clear();

                listener?.Stop();
                listener = null;

                AppendLog("Сервер полностью остановлен.");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при остановке сервера: {ex.Message}");
            }
            finally
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private void AppendLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => txtLog.AppendText($"{DateTime.Now}: {message}\r\n")));
            }
            else
            {
                txtLog.AppendText($"{DateTime.Now}: {message}\r\n");
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            AppendLog("Закрытие сервера...");
            await StopServerAsync();
            cancellationTokenSource.Dispose();
        }
    }
}

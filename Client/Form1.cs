using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    public partial class Form1 : Form
    {
        private const int DEFAULT_BUFLEN = 512;
        private const int DEFAULT_PORT = 27015;
        private TcpClient? client;
        private NetworkStream? stream;
        private bool isConnected = false;
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
        private TextBox? txtMessage;
        private TextBox? txtLog;
        private Button? btnSend;

        public Form1()
        {
            InitializeComponent();
            SetupUI();
            _ = MaintainConnectionAsync();
        }

        private void SetupUI()
        {
            Text = "CLIENT SIDE";
            Size = new Size(600, 300);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(240, 240, 240);
            StartPosition = FormStartPosition.Manual;
            Left = 620;
            Top = 15 + new Random().Next(40) * 10;

            txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Location = new Point(10, 10),
                Size = new Size(560, 200),
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtLog);

            txtMessage = new TextBox
            {
                Location = new Point(10, 220),
                Size = new Size(450, 35),
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 15),
                BorderStyle = BorderStyle.Fixed3D
            };
            Controls.Add(txtMessage);

            btnSend = new Button
            {
                Text = "Отправить",
                Location = new Point(470, 220),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Enabled = true
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += BtnSend_Click;
            Controls.Add(btnSend);
        }

        private async Task MaintainConnectionAsync()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (!isConnected)
                {
                    await TryConnectAsync();
                }
                await Task.Delay(3000, cancellationTokenSource.Token);
            }
        }

        private async Task TryConnectAsync()
        {
            AppendLog("Попытка подключения к серверу...");
            client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, DEFAULT_PORT);
                stream = client.GetStream();
                isConnected = true;
                AppendLog("Подключение к серверу установлено.");

                _ = SendQueuedMessagesAsync();
                _ = ReceiveMessagesAsync();
            }
            catch
            {
                AppendLog("Сервер недоступен. Ожидание следующей попытки...");
                client?.Close();
                client = null;
                stream = null;
            }
        }

        private async void BtnSend_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtMessage.Text))
            {
                AppendLog("Сообщение не может быть пустым.");
                return;
            }

            string message = txtMessage.Text;
            messageQueue.Enqueue(message);
            AppendLog($"Сообщение добавлено в очередь: {message}");
            txtMessage.Clear();

            if (isConnected)
            {
                await SendQueuedMessagesAsync();
            }
            else
            {
                AppendLog("Сервер недоступен, сообщение будет отправлено при подключении.");
            }
        }

        private async Task SendQueuedMessagesAsync()
        {
            while (isConnected && messageQueue.TryDequeue(out string? message))
            {
                try
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationTokenSource.Token);
                    AppendLog($"Сообщение отправлено: {message}");
                }
                catch
                {
                    AppendLog($"Ошибка при отправке сообщения: {message}. Возвращаем в очередь.");
                    messageQueue.Enqueue(message);
                    await DisconnectAsync();
                    break;
                }
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while (isConnected && client.Connected)
                {
                    var buffer = new byte[DEFAULT_BUFLEN];
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationTokenSource.Token);

                    if (bytesReceived > 0)
                    {
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
                        AppendLog($"Ответ от сервера: {response}");
                    }
                    else
                    {
                        AppendLog("Соединение с сервером прервано.");
                        await DisconnectAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch
            {
                AppendLog("Ошибка при получении данных от сервера.");
                await DisconnectAsync();
            }
        }

        private async Task DisconnectAsync()
        {
            if (!isConnected) return;

            AppendLog("Отключение от сервера...");
            isConnected = false;
            stream?.Dispose();
            client?.Close();
            stream = null;
            client = null;
            AppendLog("Отключено. Ожидание восстановления соединения...");
            await Task.CompletedTask;
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
            cancellationTokenSource.Cancel();
            await DisconnectAsync();
            cancellationTokenSource.Dispose();
        }
    }
}

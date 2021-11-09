using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AuroraDropCompanion
{
    public class WorkerTcpServer
    {

        private Task task;
        private int serverInstance;
        private int serverPort;

        public WorkerTcpServer()
        {
            task = new Task(() => processData());
        }

        public void Initialize(int _instance, int _port)
        {
            serverInstance = _instance;
            serverPort = _port;
            task.Start();
        }

        public void Stop()
        {
            //task.Dispose();
        }

        private void processData()
        {
            byte[] receiveBuffer = new byte[512];
            int clientsMax = 5;
            while (true)
            {
                try
                {
                    string localIp = GetLocalIPAddress();
                    if (Uri.TryCreate($"http://{localIp}:{serverPort}", UriKind.Absolute, out Uri url) && IPAddress.TryParse(url.Host, out IPAddress ipAddress))
                    {
                        // ---- start server ----
                        Debug.WriteLine($"Starting server on {localIp}:{serverPort}");
                        App.tcpConnectStatus[serverInstance] = $"Starting server on {localIp}:{serverPort}";
                        IPEndPoint localEndpoint = new IPEndPoint(ipAddress, url.Port);
                        Socket sockServer = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        sockServer.SendBufferSize = 512;
                        sockServer.ReceiveBufferSize = 512;   // 64x64 led matrix = 8192 bytes
                        try
                        {
                            sockServer.Bind(localEndpoint);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to bind to local end point");
                            App.tcpConnectStatus[serverInstance] = $"Failed to bind to local end point";
                            sockServer.Close();
                            return;
                        }
                        sockServer.Listen(clientsMax);   //  listen for up to 5 connections

                        // ---- wait for client to connect ----
                        while (true)
                        {
                            Debug.WriteLine($"Waiting for client at {localIp}:{serverPort}");
                            App.tcpConnectStatus[serverInstance] = $"Waiting for client at {localIp}:{serverPort}";
                            Socket tcpSocket = sockServer.Accept();
                            // we expect connection info, name and version
                            IPEndPoint remoteIpEndPoint = tcpSocket.RemoteEndPoint as IPEndPoint;
                            Debug.WriteLine($"Client connected from: {remoteIpEndPoint.Address}:{remoteIpEndPoint.Port}");
                            App.tcpConnectStatus[serverInstance] = $"Client connected from: {remoteIpEndPoint.Address}:{remoteIpEndPoint.Port}";

                            int bufLen = tcpSocket.Receive(receiveBuffer);
                            Debug.Write(System.Text.Encoding.Default.GetString(receiveBuffer));
                            Debug.WriteLine(String.Empty);
                            // respond with fft datca
                            string name = "AuroraDrop;V1";
                            switch (name)
                            {
                                case "AuroraDrop;V1":
                                    tcpSocket.Send(App.fftDataMono128);
                                    break;
                                case "LedBar;V1":
                                    // todo
                                    break;
                                default:
                                    tcpSocket.Send(App.fftDataMono128);
                                    break;
                            }

                            bool socketConnected = true;
                            DateTime timeout = DateTime.Now.AddSeconds(1);

                            // ---- process client requests ----
                            while (tcpSocket.Connected && socketConnected)
                            {
                                // check if we've timed out
                                if (DateTime.Now < timeout)
                                {
                                    // wait for a request for data
                                    if (tcpSocket.Available > 0)
                                    {
                                        bufLen = tcpSocket.Receive(receiveBuffer);
                                        try
                                        {
                                            tcpSocket.Send(App.fftDataMono128);
                                            timeout = DateTime.Now.AddSeconds(1);
                                        }
                                        catch (Exception ex)
                                        {

                                        }
                                    }
                                }
                                else
                                {
                                    socketConnected = false;
                                }
                            }
                            tcpSocket.Close();
                        }
                    }
                    else
                    {

                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }


    }
}

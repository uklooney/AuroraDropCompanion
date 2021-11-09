using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO.Ports;

namespace AuroraDropCompanion
{
    public class OldWorkerComServer
    {

        private Task task;
        private int comInstance;
        private string comPort;

        private SerialPort port;

        public OldWorkerComServer()
        {
            task = new Task(() => processData());
        }

        public void Initialize(int _instance, string _port)
        {
            comInstance = _instance;
            comPort = _port;
            task.Start();
        }

        public void Stop()
        {
            //task.Dispose();
        }



        string buffer = String.Empty;

        private void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (port.IsOpen)
            {
                string data = port.ReadExisting();
                buffer = buffer + data;
                // look for newline in buffer
                int rrr = buffer.IndexOf((char)10);
                //Debug.WriteLine($"rrr={rrr}");
                if (rrr >=0)
                {
                    string msg = buffer.Substring(0, rrr - 1);
                    if (buffer.Length > rrr + 1)
                    {
                        buffer = buffer.Substring(rrr + 1, buffer.Length - rrr - 1);
                        //buffer = String.Empty;
                    }
                    else
                    {
                        buffer = String.Empty;
                    }

                    //Debug.Write($"msg={msg}");
                    //Debug.WriteLine($"buffer={buffer}");

                    switch (msg)
                    {
                        case "AuroraDrop;V1":
                            // todo
                            //Debug.WriteLine($"Sending FFT to {comPort}");
                            App.comConnectStatus[comInstance] = $"Sending FFT to {comPort}";
                            port.Write(App.fftDataMono128, 0, App.fftDataMono128.Length);
                            break;
                        case "LedBar;V1":
                            // todo
                            break;
                        default:
                            Debug.WriteLine(msg);
                            // todo
                            break;
                    }
                }
            }
        }

        private void processData()
        {
            byte[] receiveBuffer = new byte[16384];
            bool portOpen = false;
            while (true)
            {
                if (!portOpen)
                {
                    try
                    {
                        Debug.WriteLine($"Starting server on {comPort}");
                        App.comConnectStatus[comInstance] = $"Starting server on {comPort}";
                        port = new SerialPort(comPort, 115200, Parity.None, 8, StopBits.One);
                        port.Encoding = System.Text.Encoding.UTF8;
                        port.ReadTimeout = 10;
                        port.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);
                        port.Open();
                        portOpen = true;
                        Debug.WriteLine($"Waiting for client on {comPort}");
                    }
                    catch (Exception ex)
                    {
                        portOpen = false;
                        // retryCount = xxx;
                        System.Threading.Thread.Sleep(5000);
                    }
                }
                System.Threading.Thread.Sleep(5000);
            }
        }



    }
}

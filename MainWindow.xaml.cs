using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraDropCompanion
{

    // https://raw.githubusercontent.com/espressif/arduino-esp32/gh-pages/package_esp32_index.json

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // CHANGE THIS TO MATCH COM PORT FOR ESP32 ON YOUR PC

        private const string COM_PORT = "COM12";        // set here com port of your esp
        private const string ESP32_IP = "192.168.1.112";    // or change this to ip address of your esp32 (slow at the moment?)
        private const int ESP32_PORT = 1234;

        // THIS IS SOME BUTCHERED TOGETER CODE TO GET US STARTED

        // leave all these alone for now
        //private const int NUM_COLS = 8 * 12;  // either 4 or 12 8x8 panels 
        //private const int NUM_ROWS = 8;
        private const int NUM_COLS = 8 * 16;  // either 4 or 12 or 16 8x8 panels  (use 8 * 16 = 128, for 64/128 wide matrices) 
        private const int NUM_ROWS = 8;



        private const int FTT_SIZE = 2048;


        private const int SAMPLE_SIZE_BYTES_32BIT_STEREO = FTT_SIZE * 8;  // 32bit stereo = 8 bytes per sample
        private const int SAMPLE_SIZE_FLOAT_STEREO = FTT_SIZE * 2;  // 4096
        private const int SAMPLE_SIZE_FLOAT_MONO = FTT_SIZE;    // 2048
        private const int BUFFER_SIZE_BYTES_32BIT_STEREO = SAMPLE_SIZE_BYTES_32BIT_STEREO * 12;    // TODO: should be longer, and skip frames
        private const int TIMER_MANAGEMENT = 500;

        private const int SERIAL_MSG_AUDIO_SPECTRUM = 65;
        private const int SERIAL_MSG_BITMAP = 66;

        private int currentBufferSize = BUFFER_SIZE_BYTES_32BIT_STEREO;
        private int currentSampleSize = SAMPLE_SIZE_BYTES_32BIT_STEREO;

        //private int bufferedBytes;
        private int audioBufferEmptyCount = 0;
        private byte[] audioBytes32Bit = new byte[SAMPLE_SIZE_BYTES_32BIT_STEREO];

        private float[] audioLeft = new float[SAMPLE_SIZE_FLOAT_MONO];
        private float[] audioRight = new float[SAMPLE_SIZE_FLOAT_MONO];
        private float[] audioMono = new float[SAMPLE_SIZE_FLOAT_MONO];

        private float[] fftMono = new float[SAMPLE_SIZE_FLOAT_MONO];
        private float[] fftLeft = new float[SAMPLE_SIZE_FLOAT_MONO];
        private float[] fftRight = new float[SAMPLE_SIZE_FLOAT_MONO];


        private Ellipse [,] ellipseLed = new Ellipse[NUM_COLS, NUM_ROWS];


        private double[] barData = new double[NUM_COLS];
        private byte[] barDataNew = new byte[NUM_COLS];


        private DispatcherTimer timerSample = new DispatcherTimer();
        private DispatcherTimer timerManagement = new DispatcherTimer();
        private DispatcherTimer timerLedMatrix = new DispatcherTimer();
        private WasapiLoopbackCapture audioCaptureLoopback = new WasapiLoopbackCapture();
        private BufferedWaveProvider bufferedWaveProvider;
        private int retryCount = 0;

        private double overallPeak = 0;

        private bool portOpen = false;

        private SerialPort port;

        SolidColorBrush colorGrey = new SolidColorBrush(Color.FromArgb(255, (byte)50, (byte)50, (byte)50));
        SolidColorBrush colorOutline = new SolidColorBrush(Color.FromArgb(255, (byte)200, (byte)0, (byte)0));
        SolidColorBrush colorCenter = Brushes.Red;

        PerformanceCounter[] pc = new PerformanceCounter[32];
        PerformanceCounter pc1 = new PerformanceCounter();
        PerformanceCounter pf1 = new PerformanceCounter();

        Socket clientSocket;
        IPEndPoint endPoint;
        int UdpSendCount = 0;


        public MainWindow()
        {
            InitializeComponent();
            SystemEvents.SessionEnding += new SessionEndingEventHandler(SystemEvents_SessionEnding);
        }



        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            // Get a list of serial port names, skip for now
            if (null != null)
            {
                string[] ports = SerialPort.GetPortNames();
                Debug.WriteLine("The following serial ports were found:");
                // Display each port name to the console.
                foreach (string port in ports)
                {
                    Debug.WriteLine(port);
                }
            }
            textboxComPort.Text = COM_PORT;
            textboxIpAddress.Text = ESP32_IP;
            textboxPort.Text = ESP32_PORT.ToString();

            CreateLeds();

            // dispatch timers. a fast 5ms sample/gui timer and a slow 500ms management timer
            timerSample = new DispatcherTimer(DispatcherPriority.Send);  // highest priority
            timerSample.Tick += new EventHandler(SampleTimer_Tick);
            timerSample.Interval = new TimeSpan(0, 0, 0, 0, (1000/25));  // 25fps
            timerSample.Start();

            timerLedMatrix = new DispatcherTimer(DispatcherPriority.Send);  // highest priority
            timerLedMatrix.Tick += new EventHandler(CommsTimer_Tick);
            timerLedMatrix.Interval = new TimeSpan(0, 0, 0, 0, (1000 / 25));  // 25
            timerLedMatrix.Start();

            // setup
            if (ESP32_IP != "127.0.0.1")
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IPAddress clientAddr = IPAddress.Parse(ESP32_IP);
                endPoint = new IPEndPoint(clientAddr, ESP32_PORT);
            }


        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // close open serial port
            if (portOpen)
            {
                //port.Close();
            }

            // close open network socket
            if (ESP32_IP != "127.0.0.1")
            {
                clientSocket.Close();
                clientSocket.Dispose();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //if (mutex != null) mutex.ReleaseMutex();
            timerSample.Stop();
            timerLedMatrix.Stop();
            timerManagement.Stop();
            if (audioCaptureLoopback.CaptureState == CaptureState.Capturing) audioCaptureLoopback.StopRecording();
            System.Windows.Application.Current.Shutdown();    // closes settings too, if open
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            //if (mutex != null) mutex.ReleaseMutex();
            timerSample.Stop();
            timerLedMatrix.Stop();
            timerManagement.Stop();
            if (audioCaptureLoopback.CaptureState == CaptureState.Capturing) audioCaptureLoopback.StopRecording();
            System.Windows.Application.Current.Shutdown();    // closes settings too, if open
        }

        private void CommsTimer_Tick(object sender, EventArgs e)
        {
            if (!portOpen) OpenPort();
            if (!portOpen) return;

            try
            {
                byte[] by = new byte[NUM_COLS];
                for (int i = 0; i < NUM_COLS; i++)
                {
                    by[i] = barDataNew[i];
                }
                portOpen = SendDataBlock(SERIAL_MSG_AUDIO_SPECTRUM, by);

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error");
                Debug.WriteLine(ex.StackTrace);
                portOpen = false;
            }
        }

        private bool OpenPort()
        {
            if (retryCount == 0)
            {
                try
                {
                    // open com port using standard ASCII protocol (characters 0-127 only)
                    port = new SerialPort(textboxComPort.Text, 115200, Parity.None, 8, StopBits.One);
                    port.Encoding = System.Text.Encoding.UTF8;  // was ASCII
                    port.ReadTimeout = 10;
                    port.Open();
                    port.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);
                    portOpen = true;
                    Debug.WriteLine("Port Open");
                    return true;
                }
                catch (Exception ex)
                {
                    portOpen = false;
                    retryCount = 20;
                    return false;
                }

            } else
            {
                retryCount--;
                return false;
            }
        }

        private void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // just print all the incoming data in the port's buffer
            // nothing being sent from arduino as of now
            if (port.IsOpen)
            {
                Debug.WriteLine(port.ReadExisting());
            }
        }

        private void SampleTimer_Tick(object sender, EventArgs e)
        {
            // ** START/STOP LOCAL LOOPBACK AUDIO SAMPLING/RECORDING
            // if loopback is the source but not recording, then start recording 
            if (audioCaptureLoopback.CaptureState == CaptureState.Stopped)
            {
                // setup WASAPI loopback capture
                audioCaptureLoopback = new WasapiLoopbackCapture();
                audioCaptureLoopback.DataAvailable += new EventHandler<WaveInEventArgs>(AudioDataAvailable);
                audioCaptureLoopback.RecordingStopped += new EventHandler<StoppedEventArgs>(StoppedRecordingLoopbackAudio);
                bufferedWaveProvider = new BufferedWaveProvider(audioCaptureLoopback.WaveFormat)
                {
                    BufferLength = currentBufferSize,
                    DiscardOnBufferOverflow = true
                };
                Console.WriteLine("=================================");
                Console.WriteLine("Starting WASAPI Loopback Recording");
                Console.WriteLine("WASAPI Loopback SampleRate: {0}", audioCaptureLoopback.WaveFormat.SampleRate);
                Console.WriteLine("WASAPI Loopback Channels: {0}", audioCaptureLoopback.WaveFormat.Channels);
                Console.WriteLine("WASAPI Loopback BitsPerSample: {0}", audioCaptureLoopback.WaveFormat.BitsPerSample);
                Console.WriteLine("WASAPI Loopback Encoding: {0}", audioCaptureLoopback.WaveFormat.Encoding);
                audioCaptureLoopback.StartRecording();
                return;
            }

            Console.WriteLine("========================================================");
            Console.WriteLine("START: Bytes in the buffer: {0}", bufferedWaveProvider.BufferedBytes);
            var audioStereoFloatInterlaced = new float[SAMPLE_SIZE_FLOAT_STEREO];  // // this is initialised every time to provide silence if required
            SavedVar.bufferSize = bufferedWaveProvider.BufferedBytes;
            if ((bufferedWaveProvider.BufferedBytes >= currentSampleSize) || (audioBufferEmptyCount > 4))
            {
                audioBufferEmptyCount = 0;
                if (bufferedWaveProvider.BufferedBytes >= currentSampleSize)
                // copy and process the sample if we have enough data in the buffer
                {
                    // if the buffer is getiing to large, dump the older sample data
                    do
                    {
                        Console.WriteLine("LOOP: Bytes in the buffer: {0}", bufferedWaveProvider.BufferedBytes);
                        bufferedWaveProvider.Read(audioBytes32Bit, 0, currentSampleSize);
                    } while (bufferedWaveProvider.BufferedBytes > SAMPLE_SIZE_FLOAT_STEREO * 2);

                    SavedVar.frameCount = SavedVar.frameCount + (1000 / TIMER_MANAGEMENT);


                    // block copy byte data to float array, we default at IeeeFloat
                    Buffer.BlockCopy(audioBytes32Bit, 0, audioStereoFloatInterlaced, 0, currentSampleSize);
                }
                else
                // we have seen the audio buffer empty more than 4 times in a row, pass the empty array (silence) to be analysed
                {
                    if (bufferedWaveProvider.BufferedBytes > 0) bufferedWaveProvider.ClearBuffer();
                    //Console.WriteLine("SAMPLE TIMER: Passing Silence: Got {0} bytes, need {1} bytes.", bufferedBytes, currentSampleSize);   
                }

                // split stereo float data into two and also combine for a third mono channel
                for (int i = 0; i < SAMPLE_SIZE_FLOAT_MONO; i++)
                {
                    audioLeft[i] = audioStereoFloatInterlaced[i * 2];
                    audioRight[i] = audioStereoFloatInterlaced[(i * 2) + 1];
                    // create third mono channel
                    audioMono[i] = (audioLeft[i] + audioRight[i]) / 2.0f;
                }

                // create FFT data from audio sample
                fftMono = FFT_NAudio(audioMono);
                // old style
                barData = GenerateBarDataMono(fftMono);
                // populate array of bytes, one for each column, holding a value between 0 -> 127/8
                double rampRate = 1;
                barDataNew = GenerateBarDataMonoNew(barData, sliderGain.Value, sliderHiRise.Value);   // gain = 50



                // generate led matrix bars
                for (int i = 0; i < NUM_COLS; i++)
                {
                    // transpose 0.0 -> 1.0 to 0 -> 8
                    double val = barData[i] * 80;
                    for (int j = 0; j < 8; j++)
                    {
                        ellipseLed[i, 7 - j].Stroke = (int)val > j ? colorOutline : colorGrey;
                        ellipseLed[i, 7 - j].Fill = (int)val > j ? colorCenter : colorGrey;
                    }

                }

            }
            else
            {
                // don't do anything if we don't have enough data to process
                audioBufferEmptyCount++;
                //Console.WriteLine("SAMPLE TIMER: Not enough sample data to process: Got {0} bytes, need {1} bytes.", bufferedBytes, SAMPLE_SIZE);   
            }


        }

        // event handlers for 'Audio Data Available' and 'Recording Stopped'
        private void AudioDataAvailable(object sender, WaveInEventArgs e)
        {
            bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            //Console.WriteLine("Audio Data Available: {0}", e.BytesRecorded);
        }

        private void StoppedRecordingLoopbackAudio(object sender, StoppedEventArgs e)
        {
            audioCaptureLoopback.Dispose();
        }

        private float[] FFT_NAudio(float[] data)
        {
            Complex[] _fftComplex = new Complex[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                _fftComplex[i].X = (float)(data[i] * FastFourierTransform.HammingWindow(i, data.Length));
                _fftComplex[i].Y = 0;
            }
            int m = (int)Math.Log(data.Length, 2.0);
            FastFourierTransform.FFT(true, m, _fftComplex);
            float[] _fft = new float[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                // magnitude = 20 * Log(sqr(Re^2 + Im^2))
                _fft[i] = ((float)Math.Sqrt((_fftComplex[i].X * _fftComplex[i].X) + (_fftComplex[i].Y * _fftComplex[i].Y)));
            }
            return _fft;
        }


        private double[] GenerateBarDataMono(float[] data)
        {
            double[] _output = new double[NUM_COLS];
            double y = 0;
            int b0 = 0;
            // parse the fft data into visible mono bars
            for (int x = 0; x < (NUM_COLS); x++)
            {
                double _peak = 0.0;
                int b1 = (int)Math.Pow(2.0, (x * 9.3 / (NUM_COLS - 1)));
                //int b1 = (int)Math.Pow(2.0, (x * 10.0 / (visibleBarCount - 1)));
                if (b1 > SAMPLE_SIZE_FLOAT_MONO - 1) b1 = SAMPLE_SIZE_FLOAT_MONO - 1;
                if (b1 <= b0) b1 = b0 + 1;
                while (b0 < b1)
                {
                    if (_peak < data[b0 + 1]) _peak = data[b0 + 1];
                    b0 = b0 + 1;
                }
                y = Math.Sqrt(_peak);
                if (overallPeak < y) overallPeak = y;
                if (y < 0.00001) y = 0;

                // add the data into the bar array
                //barData[x] = y;
                _output[x] = y;
            }
            return _output;
            //Console.WriteLine("Overall Peak: {0}", overallPeak);
        }

        private byte[] GenerateBarDataMonoNew(double[] _data, double _gain, double _ramp)
        {

            // transpose data in range of 0.0 -> 1.0 to 0.0 to 8.0
            byte[] _output = new byte[_data.Length];
            for (int x = 0; x < _data.Length; x++)
            {
                double rate = (x * (_ramp / _data.Length)) + 1;     // ramp as we go up the freq range to make it look nicer
                _output[x] = (byte)((_data[x] * _gain) * rate);
            }
            return _output;
        }

        private void CreateLeds()
        {
            Brush _barCol = Brushes.Gray;
            Brush _colourLedOn = Brushes.Red;
            for (int x = 0; x < NUM_COLS; x++)
            {
                for (int y = 0; y < NUM_ROWS; y++)
                {

                    ellipseLed[x, y] = new Ellipse() { Width = 8, Height = 8, StrokeThickness = 0, IsHitTestVisible = false, Visibility = Visibility.Visible };
                    ellipseLed[x, y].Stroke = new SolidColorBrush(Color.FromArgb(255, (byte)255, (byte)0, (byte)0));
                    ellipseLed[x, y].Fill = _barCol;
                    ellipseLed[x, y].Width = 8;
                    ellipseLed[x, y].Height = 8;
                    ellipseLed[x, y].StrokeThickness = 2;
                    Canvas.SetTop(ellipseLed[x, y], (y * 10) + 10);
                    Canvas.SetLeft(ellipseLed[x, y], (x * 10) + 10);
                    AuroraDropCanvas.Children.Add(ellipseLed[x, y]);
                }


            }
        }


        private bool SendDataStr(string _str)
        {
            if (portOpen)
            {
                try
                {
                    port.WriteLine(_str + (char)0);
                    return true;
                }
                catch (Exception ex)
                {
                    port.Close();
                    return false;
                }
            }
            port.Close();
            return false;
        }


        private bool SendDataBlock(byte _type, byte[] _bytes)
        {
            if (_bytes.Length > 255) return true;  // ignore if too much data
            if (portOpen)
            {
                try
                {
                    byte[] dataBlock = new byte[_bytes.Length + 7];  // message length + 7 preamble bytes
                    dataBlock[0] = 69;   // preamble
                    dataBlock[1] = 96;
                    dataBlock[2] = 69;
                    dataBlock[3] = 96;
                    dataBlock[4] = 0;    // always 0
                    dataBlock[5] = _type;   // char A (65) indicates audio spectrum data?
                    dataBlock[6] = (byte)_bytes.Length;
                    for (int i=0; i < _bytes.Length; i++)
                    {
                        dataBlock[i+7] = _bytes[i];
                    }
                    port.Write(dataBlock, 0, dataBlock.Length);

                    // send network data too, but slower 10 packets per second. bugs out if any faster as packets arrive from ESP libray in chunks instead of individually.
                    if (ESP32_IP != "127.0.0.1")
                    {
                        if (UdpSendCount == 0) clientSocket.SendTo(dataBlock, endPoint);
                        UdpSendCount++;
                        if (UdpSendCount > 2) UdpSendCount = 0;
                    }


                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.StackTrace);
                    port.Close();
                    return false;
                }
            }
            port.Close();
            return false;
        }




        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            SessionEndReasons reason = e.Reason;
            switch (reason)
            {
                case SessionEndReasons.Logoff:
                    //MessageBox.Show("The user is logging out. The operating system continues to run, but the user who started this application is logging out.");
                    break;
                case SessionEndReasons.SystemShutdown:
                    //MessageBox.Show("The operating system is shutting down.");
                    if (portOpen)
                    {
                        // portOpen = SendDataBlock(SERIAL_MSG_SHUTTING_DOWN, new byte[] { 0 });

                    }
                    break;
                default:
                    if (portOpen)
                    {
                        //portOpen = SendDataBlock(SERIAL_MSG_SHUTTING_DOWN, new byte[] { 0 });
                    }
                    break;
            }
        }

    }
}

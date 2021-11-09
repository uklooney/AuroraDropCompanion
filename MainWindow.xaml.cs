using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Diagnostics;
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


        private const int NUM_BINS_AURORA = 128;  // either 4 or 12 or 16 8x8 panels  (use 8 * 16 = 128, for 64/128 wide matrices) 
        private const int NUM_BINS_LEDBAR = 96;
        private const int NUM_ROWS = 8;

        private const int FTT_SIZE = 2048;


        private const int SAMPLE_SIZE_BYTES_32BIT_STEREO = FTT_SIZE * 8;  // 32bit stereo = 8 bytes per sample
        private const int SAMPLE_SIZE_FLOAT_STEREO = FTT_SIZE * 2;  // 4096
        private const int SAMPLE_SIZE_FLOAT_MONO = FTT_SIZE;    // 2048
        private const int BUFFER_SIZE_BYTES_32BIT_STEREO = SAMPLE_SIZE_BYTES_32BIT_STEREO * 12;    // TODO: should be longer, and skip frames

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


        private Ellipse [,] ellipseLed = new Ellipse[NUM_BINS_AURORA, NUM_ROWS];


        private double[] barData = new double[NUM_BINS_AURORA];
        private byte[] tcpData = new byte[NUM_BINS_AURORA];


        private DispatcherTimer timerSample = new DispatcherTimer();
        private WasapiLoopbackCapture audioCaptureLoopback = new WasapiLoopbackCapture();
        private BufferedWaveProvider bufferedWaveProvider;
        private int retryCount = 0;

        private double overallPeak = 0;

        SolidColorBrush colorGrey = new SolidColorBrush(Color.FromArgb(255, (byte)50, (byte)50, (byte)50));
        SolidColorBrush colorOutline = new SolidColorBrush(Color.FromArgb(255, (byte)200, (byte)0, (byte)0));
        SolidColorBrush colorCenter = Brushes.Red;

        public MainWindow()
        {
            InitializeComponent();
            SystemEvents.SessionEnding += new SessionEndingEventHandler(SystemEvents_SessionEnding);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {


            CreateLeds();

            // dispatch timers. a fast 5ms sample/gui timer and a slow 500ms management timer
            timerSample = new DispatcherTimer(DispatcherPriority.Send);  // highest priority
            timerSample.Tick += new EventHandler(SampleTimer_Tick);
            timerSample.Interval = new TimeSpan(0, 0, 0, 0, (1000/25));  // 25fps
            timerSample.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // close open network socket


        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //if (mutex != null) mutex.ReleaseMutex();
            timerSample.Stop();
            if (audioCaptureLoopback.CaptureState == CaptureState.Capturing) audioCaptureLoopback.StopRecording();
            System.Windows.Application.Current.Shutdown();    // closes settings too, if open
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            //if (mutex != null) mutex.ReleaseMutex();
            timerSample.Stop();
            if (audioCaptureLoopback.CaptureState == CaptureState.Capturing) audioCaptureLoopback.StopRecording();
            System.Windows.Application.Current.Shutdown();    // closes settings too, if open
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
                Debug.WriteLine("=================================");
                Debug.WriteLine("Starting WASAPI Loopback Recording");
                Debug.WriteLine("WASAPI Loopback SampleRate: {0}", audioCaptureLoopback.WaveFormat.SampleRate);
                Debug.WriteLine("WASAPI Loopback Channels: {0}", audioCaptureLoopback.WaveFormat.Channels);
                Debug.WriteLine("WASAPI Loopback BitsPerSample: {0}", audioCaptureLoopback.WaveFormat.BitsPerSample);
                Debug.WriteLine("WASAPI Loopback Encoding: {0}", audioCaptureLoopback.WaveFormat.Encoding);
                audioCaptureLoopback.StartRecording();
                return;
            }

            //Debug.WriteLine("========================================================");
            //Debug.WriteLine("START: Bytes in the buffer: {0}", bufferedWaveProvider.BufferedBytes);
            var audioStereoFloatInterlaced = new float[SAMPLE_SIZE_FLOAT_STEREO];  // // this is initialised every time to provide silence if required
            if ((bufferedWaveProvider.BufferedBytes >= currentSampleSize) || (audioBufferEmptyCount > 4))
            {
                audioBufferEmptyCount = 0;
                if (bufferedWaveProvider.BufferedBytes >= currentSampleSize)
                // copy and process the sample if we have enough data in the buffer
                {
                    // if the buffer is getting too large, dump the older sample data
                    do
                    {
                        //Debug.WriteLine("LOOP: Bytes in the buffer: {0}", bufferedWaveProvider.BufferedBytes);
                        bufferedWaveProvider.Read(audioBytes32Bit, 0, currentSampleSize);
                    } while (bufferedWaveProvider.BufferedBytes > SAMPLE_SIZE_FLOAT_STEREO * 2);

                    // block copy byte data to float array, we default at IeeeFloat
                    Buffer.BlockCopy(audioBytes32Bit, 0, audioStereoFloatInterlaced, 0, currentSampleSize);
                }
                else
                // we have seen the audio buffer empty more than 4 times in a row, pass the empty array (silence) to be analysed
                {
                    if (bufferedWaveProvider.BufferedBytes > 0) bufferedWaveProvider.ClearBuffer();
                    //Debug.WriteLine("SAMPLE TIMER: Passing Silence: Got {0} bytes, need {1} bytes.", bufferedBytes, currentSampleSize);   
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


                tcpData = GenerateBarDataMonoNew(barData, sliderGain.Value, sliderHiRise.Value);   // gain = 50

                // testing tcp server comms
                //App.newFftDataXXX = GenerateBarDataMonoNew(barData, sliderGain.Value, sliderHiRise.Value);   // gain = 50
                App.fftDataMono128 = tcpData;
                App.newFftData = true;


                // generate led matrix bars
                for (int i = 0; i < NUM_BINS_AURORA; i++)
                {
                    // transpose 0.0 -> 1.0 to 0 -> 8
                    double val = barData[i] * 80;
                    for (int j = 0; j < 8; j++)
                    {
                        ellipseLed[i, 7 - j].Stroke = (int)val > j ? colorOutline : colorGrey;
                        ellipseLed[i, 7 - j].Fill = (int)val > j ? colorCenter : colorGrey;
                    }

                }

                labelStatus0.Content = App.tcpConnectStatus[0];
                labelStatus1.Content = App.tcpConnectStatus[1];

                labelStatus2.Content = App.comConnectStatus[0];
                labelStatus3.Content = App.comConnectStatus[1];

            }
            else
            {
                // don't do anything if we don't have enough data to process
                audioBufferEmptyCount++;
                //Debug.WriteLine("SAMPLE TIMER: Not enough sample data to process: Got {0} bytes, need {1} bytes.", bufferedBytes, SAMPLE_SIZE);   
            }


        }

        // event handlers for 'Audio Data Available' and 'Recording Stopped'
        private void AudioDataAvailable(object sender, WaveInEventArgs e)
        {
            bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            //Debug.WriteLine("Audio Data Available: {0}", e.BytesRecorded);
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
            double[] _output = new double[NUM_BINS_AURORA];
            double y = 0;
            int b0 = 0;
            // parse the fft data into visible mono bars
            for (int x = 0; x < (NUM_BINS_AURORA); x++)
            {
                double _peak = 0.0;
                int b1 = (int)Math.Pow(2.0, (x * 9.3 / (NUM_BINS_AURORA - 1)));
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
            //Debug.WriteLine("Overall Peak: {0}", overallPeak);
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
            for (int x = 0; x < NUM_BINS_AURORA; x++)
            {
                for (int y = 0; y < NUM_ROWS; y++)
                {

                    ellipseLed[x, y] = new Ellipse() { Width = 4, Height = 4, StrokeThickness = 0, IsHitTestVisible = false, Visibility = Visibility.Visible };
                    ellipseLed[x, y].Stroke = new SolidColorBrush(Color.FromArgb(255, (byte)255, (byte)0, (byte)0));
                    ellipseLed[x, y].Fill = _barCol;
                    ellipseLed[x, y].Width = 4;
                    ellipseLed[x, y].Height = 4;
                    ellipseLed[x, y].StrokeThickness = 2;
                    Canvas.SetTop(ellipseLed[x, y], (y * 6) + 6);
                    Canvas.SetLeft(ellipseLed[x, y], (x * 6) + 6);
                    AuroraDropCanvas.Children.Add(ellipseLed[x, y]);
                }


            }
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
                    break;
                default:
                    break;
            }
        }

    }
}

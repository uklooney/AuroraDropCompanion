using System;
using System.Windows;

namespace AuroraDropCompanion
{

    public partial class App : System.Windows.Application
    {
        public WorkerTcpServer taskWorkerTest1;
        public WorkerTcpServer taskWorkerTest2;

        public WorkerComServer comWorkerTest1;
        public WorkerComServer comWorkerTest2;

        public static byte[] fftDataMono128 = new byte[128];
        public static bool newFftData = false;
        public static string[] tcpConnectStatus = new string[2];        // ip to 2 servers
        public static string[] comConnectStatus = new string[2];        // up to 2 com ports

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // CHANGE THIS TO MATCH PORT CONFIGURED IN ESP32
            // TODO: doesn't scale very well, should use single server with async clients!!!
            taskWorkerTest1 = new WorkerTcpServer();
            taskWorkerTest1.Initialize(0,81);
            taskWorkerTest2 = new WorkerTcpServer();
            taskWorkerTest2.Initialize(1,82);

            // CHANGE THIS TO MATCH COM PORT FOR ESP32 ON YOUR PC
            comWorkerTest1 = new WorkerComServer();
            comWorkerTest1.Initialize(0, "COM8");
            comWorkerTest2 = new WorkerComServer();
            comWorkerTest2.Initialize(1, "COM25");

        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            //taskWorkerTest1.Stop();
        }
    }


}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Signal_Generator
{
    public static class Transmission
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        static NamedPipeServerStream PipeCommunication;
        public static Thread soapyProcThread;
        public static Process soapyPowerPROC;
        public static bool keepStream = false;
        #region Cntrl C
        internal const int CTRL_C_EVENT = 0;

        [DllImport("kernel32.dll")]
        internal static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        internal static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);
        // Delegate type to be used as the Handler Routine for SCCH
        delegate Boolean ConsoleCtrlDelegate(uint CtrlType);
        #endregion
        private static void sendCntrlC()
        {

            //https://stackoverflow.com/questions/283128/how-do-i-send-ctrlc-to-a-process-in-c
            if (AttachConsole((uint)soapyPowerPROC.Id))
            {
                SetConsoleCtrlHandler(null, true);
                try
                {
                    if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
                        soapyPowerPROC.WaitForExit();
                }
                finally
                {
                    SetConsoleCtrlHandler(null, false);
                    FreeConsole();
                }

            }
        }
        private static void KillProcessAndChildren(int pid)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
       static Thread transmissionPIPE = new Thread(() => { });
        static bool stopPipeThread;
        public static void endTask()
        {
            stopPipeThread = true;
            while (transmissionPIPE.IsAlive)
            {
                Thread.Sleep(1);
            }
            sendPIPE(930e6, 1, false); //stop transmitting
        }
        private static void sendPIPE(double frequency,double gain,bool transmitting)
        {
            if (UI.hasCalibration)
            {
                gain += UI.caliData.MinBy(x => Math.Abs(x.Key - frequency)).Value;
            }
            var cmd = $"{frequency}@{gain}@{((transmitting) ? 1:0)}";
            if (PipeCommunication == null) return;
#if DEBUG
            Logger.Info($"Sending to python binding over pipe --> {cmd}");
#endif
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write($"\"{cmd}\"");
                PipeCommunication.Write(stream.ToArray(), 0, stream.ToArray().Length);
                byte[] bytes_to_read = new byte[1];
                string message = "";
                do
                {
                    PipeCommunication.Read(bytes_to_read, 0, 1);
                    message += Encoding.UTF8.GetString(bytes_to_read);
                } while (!PipeCommunication.IsMessageComplete);

                if (message == "ack")
                {
#if DEBUG
                    Logger.Debug("Received ack from python binding");
#endif
                } else
                {
                    Logger.Error($"Unknown PIPE MESSAGE {message}");
                }
            }
        }
        public struct sweepBurst
        {
            public sweepBurst()
            {
                level_txt = string.Empty;
                frequency_txt = string.Empty;
                sleep_txt = string.Empty;
            }
            public string level_txt, frequency_txt, sleep_txt;
            public double Frequency;
            public double level;
            public int sleep;
        }
        public static void beginList(Dictionary<int,sweepBurst> list)
        {
            endTask();
            transmissionPIPE = new Thread(() =>
            {
                stopPipeThread = false;
               var sortedDic = list.OrderBy(obj => obj.Key).ToDictionary(obj => obj.Key, obj => obj.Value);
                while (!stopPipeThread)
                {
                   foreach(KeyValuePair<int,sweepBurst> item in sortedDic)
                    {
                        sendPIPE(item.Value.Frequency, item.Value.level, true);
                        Thread.Sleep(item.Value.sleep);
                    }
                }
            })
            { Priority = ThreadPriority.AboveNormal };
            transmissionPIPE.Start();
        }
        public static void beginSpecific(double frequency, double gain) => sendPIPE(frequency, gain, true);
        public static void beginSweep(double freqStart,double freqStop,double gainStart,double gainStop,double stepDivider)
        {
            transmissionPIPE = new Thread(() =>
            {
                double stepFreq = (freqStop - freqStart) / stepDivider;
                double stepGain = (gainStop - gainStart) / stepDivider;
                stepDivider = 0;
                stopPipeThread = false;
                while (!stopPipeThread)
                {
                    double currentFreq = freqStart + (stepFreq * stepDivider);
                    double currentGain = gainStart + (stepGain * stepDivider);
                    
                    if (currentFreq > freqStop)
                    {
                        stepDivider = 0;
                        continue;
                    }
                    stepDivider++;
                    sendPIPE(currentFreq, currentGain, true);
                    Thread.Sleep(20);
                }
            }
            )
            { Priority = ThreadPriority.AboveNormal };
            transmissionPIPE.Start();
        }
        public static void beginStream()
        {
            Logger.Debug("Starting transmission");
            Logger.Debug($"Restarting PIPE");
            if (PipeCommunication is null)
            {

                PipeCommunication = new NamedPipeServerStream("RFtransmissionPipe", PipeDirection.InOut, 1, PipeTransmissionMode.Message);
                Logger.Debug($"Created PIPE to communicate with Soapy Python Binding");
            }
            Logger.Debug($"Restarting transmission");
            soapyProcThread = new Thread(() =>
            {
                soapyPowerPROC = new Process();
                soapyPowerPROC.StartInfo.FileName = "python.exe";
                soapyPowerPROC.StartInfo.Arguments = $"\"{Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "transmission.py")}\"";
                Logger.Debug($"executing --> {soapyPowerPROC.StartInfo.Arguments}");
                soapyPowerPROC.StartInfo.UseShellExecute = false;
                soapyPowerPROC.StartInfo.RedirectStandardOutput = true;
                soapyPowerPROC.StartInfo.RedirectStandardError = true;
                //* Set your output and error (asynchronous) handlers
                soapyPowerPROC.OutputDataReceived += new DataReceivedEventHandler(processSoapyData);
                soapyPowerPROC.ErrorDataReceived += new DataReceivedEventHandler(processSoapyError);
                //* Start process and handlers
                soapyPowerPROC.Start();
                soapyPowerPROC.BeginOutputReadLine();
                soapyPowerPROC.BeginErrorReadLine();
                keepStream = true;
                PipeCommunication.WaitForConnection();//waiting for soapySpectrum to connect to pipe server
                Logger.Debug("Python Binding Connected to PIPE");
                while (keepStream)
                {
                    Thread.Sleep(1000);
                }
                if (!PipeCommunication.IsConnected)
                    PipeCommunication.Dispose();

                sendCntrlC();
                KillProcessAndChildren(soapyPowerPROC.Id);
                soapyPowerPROC.WaitForExit();
            })
            { Priority = ThreadPriority.Highest };
            soapyProcThread.Start();
        }
        private static void processSoapyData(object sendingProcess, DataReceivedEventArgs outLine)
        {
#if DEBUG
            Logger.Debug($"[Transmission-DATA] {outLine.Data}");
#endif
        }
        public static void processSoapyError(object sendingProcess, DataReceivedEventArgs outLine)
        {
#if DEBUG
            Logger.Error($"[Transmission-ERROR] {outLine.Data}");
#endif
        }
    }
}

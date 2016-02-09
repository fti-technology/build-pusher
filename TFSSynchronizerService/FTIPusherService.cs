using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using FTIPusher.Util;
using NLog;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace FTIPusher
{
    public partial class FtiPusherService : ServiceBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private const string LogSource = "FTIPusher";
        private ServiceOptionsRoot _readJsonConfigOptions;

        private ServiceCoreLogic serviceCoreLogic = null;

        public FtiPusherService()
        {
            InitializeComponent();
            eventLog1 = new EventLog();
            if (!EventLog.SourceExists(LogSource))
            {
                EventLog.CreateEventSource(LogSource, LogSource);
            }
            eventLog1.Source = LogSource;
            eventLog1.Log = LogSource;
            eventLog1.WriteEntry("Service starting");
            Logger.Info("{0} Service starting", LogSource);
        }

        protected override async void OnStart(string[] args)
        {
            eventLog1.WriteEntry("Service OnStart");
//#if DEBUG
//     //System.Diagnostics.Debugger.Launch();
//#endif
            Logger.Error("OnStart logic called");
            var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            _readJsonConfigOptions = ServiceOptions.ReadJsonConfigOptions(Logger);
            if (_readJsonConfigOptions == null)
            {
                Logger.Error("Json options file could not be loaded");
                throw new TaskCanceledException("Invalid JSon data options");
            }

            try
            {
                Logger.Info("Starting service Polling");
                if (_readJsonConfigOptions.ExternalMirror != null)
                {
                    Logger.Info("Starting service Polling in Dual Mode");
                    DoWork(true);
                }
                else
                {
                    Logger.Info("Starting service Polling in Single Mode");
                    DoWork();
                }
            }
            catch (TaskCanceledException exception) 
            {
                Logger.InfoException("Exception - serviced stopped", exception);
            }
        }

        /// <summary>
        /// Entry point to launch the polling activities
        /// </summary>
        public async void DoWork(bool runSecondPoll=false)
        {
            Task t1 = PollMain();
            List<Task> taskList = new List<Task>() {t1};
            if (runSecondPoll)
            {
                Task t2 = PollMirror();
                taskList.Add(t2);
            }

            Task[] tasksList = taskList.ToArray();

            await Task.WhenAll(tasksList);
        }


        protected override void OnPause()
        {
            eventLog1.WriteEntry("In OnPause");
            serviceCoreLogic.StopUpdates = true;
            while (!serviceCoreLogic.HasStopped)
            {
                RequestAdditionalTime(200000);
                Thread.Sleep(120000);
            }
        }

        protected override void OnStop()
        {
            eventLog1.WriteEntry("In OnStop");
            Logger.Info("{0} serviced stopped", LogSource);
        }

        /// <summary>
        /// Run the secondary polling loop
        /// </summary>
        /// <returns></returns>
        private async Task PollMirror()
        {
            eventLog1.WriteEntry("Service Poll logic Mirror");
            serviceCoreLogic = new ServiceCoreLogic(Logger, _readJsonConfigOptions);
            Logger.Info("Core Mirror Logic Created");
            CancellationToken cancellation = _cts.Token;

            int pollingFreq = _readJsonConfigOptions.ExternalMirror.UpdateFrequencyInMinutes;
            eventLog1.WriteEntry("Mirror Polling Frequency Set" + pollingFreq);
            Logger.Info("Mirror Polling Frequency Set: {0}", pollingFreq);
            TimeSpan[] intervals =
            {
                TimeSpan.FromMinutes(0),
                TimeSpan.FromMinutes(pollingFreq)
            };

            var index = 0;
            Logger.Info("Mirror logic loop started...");
            while (true)
            {
                await Task.Delay(intervals[index], cancellation);
                Logger.Info("Interval fired");
                try
                {
                    Logger.Info("Running Mirror Loop Logic");
                    bool ret = await Task.Run(() => serviceCoreLogic.RunExternalMirrorLogic(_readJsonConfigOptions));
                    if (serviceCoreLogic.HasStopped)
                    {
                        Logger.Info("Running Mirror loop Complete - Stop requested");
                        break;
                    }

                    if (index == 0)
                        index = 1;

                    Logger.Info("Running Mirror loop Complete");
                }
                catch
                {
                    // rerun on exception
                    index = 0;
                }

                if (cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Run the polling loop for the main logic branch
        /// </summary>
        /// <returns></returns>
        private async Task PollMain()
        {
            eventLog1.WriteEntry("Service Poll logic Main");
            serviceCoreLogic = new ServiceCoreLogic(Logger, _readJsonConfigOptions);
            Logger.Info("Core Service Logic Created");
            CancellationToken cancellation = _cts.Token;

            int pollingFreq = _readJsonConfigOptions.UpdateFrequencyInMinutes;
            eventLog1.WriteEntry("Main Polling Frequency Set" + pollingFreq);
            Logger.Info("Main Polling Frequency Set: {0}", pollingFreq);
            TimeSpan[] intervals =
            {
                TimeSpan.FromMinutes(0),
                TimeSpan.FromMinutes(pollingFreq)
            };
            var index = 0;
            Logger.Info("Main logic loop started...");
            while (true)
            {
                await Task.Delay(intervals[index], cancellation);
                Logger.Info("Interval fired");
                try
                {
                    Logger.Info("Running Main Loop Logic");
                    bool ret = await Task.Run(() => serviceCoreLogic.RunPusherLogic(_readJsonConfigOptions));
                    if (serviceCoreLogic.HasStopped)
                    {
                        Logger.Info("Running Main Loop Complete - Stop requested");
                        break;
                    }

                    if (index == 0)
                        index = 1;

                    Logger.Info("Running Main loop Complete");
                }
                catch
                {
                    // rerun on exception
                    index = 0;
                }

                if (cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}

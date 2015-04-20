using System;
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
                await Poll();
            }
            catch (TaskCanceledException exception) 
            {
                Logger.InfoException("Exception - serviced stopped", exception);
            }
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

        private async Task Poll()
        {
            eventLog1.WriteEntry("Service Poll logic");
            serviceCoreLogic = new ServiceCoreLogic(Logger);
            Logger.Info("Core Service Logic Created");
            CancellationToken cancellation = _cts.Token;

            eventLog1.WriteEntry("Polling Frequency Set" + _readJsonConfigOptions.UpdateFrequencyInMinutes);
            Logger.Info("Polling Frequency Set: {0}", _readJsonConfigOptions.UpdateFrequencyInMinutes);
            TimeSpan[] intervals =
            {
                TimeSpan.FromMinutes(0),
                TimeSpan.FromMinutes(_readJsonConfigOptions.UpdateFrequencyInMinutes)
            };
            var index = 0;
            Logger.Info("Core logic loop started...");
            while (true)
            {
                await Task.Delay(intervals[index], cancellation);
                Logger.Info("Interval fired");
                try
                {
                    Logger.Info("Running Core Logic");
                    bool ret = await Task.Run(() => serviceCoreLogic.RunPusherLogic(_readJsonConfigOptions));
                    if (serviceCoreLogic.HasStopped)
                    {
                        Logger.Info("Running Core Complete - Stop requested");
                        break;
                    }

                    if (index == 0)
                        index = 1;

                    Logger.Info("Running Core Complete");
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

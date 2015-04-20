using System.ServiceProcess;
using System.Threading;
using ServiceProcess.Helpers;
using TFSSynchronizerService;

namespace FTIPusher
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            ServiceBase[] servicesToRun;
            servicesToRun = new ServiceBase[] 
            { 
                new FtiPusherService() 
            };
            servicesToRun.LoadServices();
        }
    }
}

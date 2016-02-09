using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FTIPusher.Util;
using NLog;
using System.IO;

namespace BuildDataDriverTest
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]       
        public void verify_options_parse_success()
        {
            string optionsFile = @"FTIPusherServiceOptions.json";

            Assert.IsTrue(File.Exists(optionsFile));
            ILogger logger = LogManager.GetCurrentClassLogger();
            var ret = ServiceOptions.ReadJsonConfigOptions(logger);

            Assert.IsNotNull(ret);
            Assert.IsNotNull(ret.BuildServer);
            Assert.IsNotNull(ret.BuildsToWatch);
            Assert.IsNotNull(ret.HTTPShares);
            Assert.IsNotNull(ret.RunBuildUpdate);
            Assert.IsNotNull(ret.ExternalMirror);
        }
    }
}
                              
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{
    public interface IFtpLocation
    {
        string FtpUrl { get; set; }
        string FtpUser { get; set; }
        string FtpPassWord { get; set; }
        string FtpProxy { get; set; }
        string FtpPort { get; set; }
        string FTPDirectory { get; set; }
    }

}

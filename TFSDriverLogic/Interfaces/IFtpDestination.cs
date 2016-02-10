namespace BuildDataDriver.Interfaces
{
    public interface IFtpDestination
    {
        string FtpId { get; set; }
        string FTPDirectory { get; set; }
    }
}
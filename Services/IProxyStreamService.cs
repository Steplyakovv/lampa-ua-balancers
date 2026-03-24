using AngleSharp.Dom;

namespace UafixApiNew.Services
{
    public interface IProxyStreamService
    {
        Task<string?> GetProxyM3u8Result( string url );
	}
}

using AngleSharp.Dom;

namespace UafixApiNew.Services
{
    public interface IProxyService
    {
        Task<string?> GetProxyM3u8Result( string url );
	}
}

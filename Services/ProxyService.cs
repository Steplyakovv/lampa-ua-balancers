using System.Text.RegularExpressions;

namespace UafixApiNew.Services
{
	public class ProxyService : IProxyService
	{
		private readonly IHttpClientFactory _clientFactory;
		private readonly ILogger<ProxyService> _logger;
		private readonly IHttpContextAccessor _httpContextAccessor;

		private readonly string[] _needProxySource = new[] { "https://ashdi.vip" };

		private const string WorkerProxy = "https://proxy-worker.s-teplyakovv.workers.dev/?url=";

		private HttpClient Client => _clientFactory.CreateClient( "UafixClient" );

		public ProxyService(
			IHttpClientFactory clientFactory,
			IHttpContextAccessor httpContextAccessor,
			ILogger<ProxyService> logger
		)
		{
			_clientFactory = clientFactory;
			_httpContextAccessor = httpContextAccessor;
			_logger = logger;
		}

		public async Task<string?> GetProxyM3u8Result( string url ) {
			var referrer = _needProxySource.FirstOrDefault( p => url.Contains( p ) );

			if ( string.IsNullOrWhiteSpace( referrer ) )
				return null;

			return await ConvertToProxyM3u8( url, referrer );
		}

		private async Task<string> ConvertToProxyM3u8( string url, string referrer ) {
			var request = new HttpRequestMessage( HttpMethod.Get, url );
			request.Headers.Referrer = new Uri( referrer );

			var response = await Client.SendAsync( request );

			var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

			_logger.LogInformation( "Original URL: {Url}", url );
			_logger.LogInformation( "Final URL: {FinalUrl}", finalUrl );

			var content = await response.Content.ReadAsStringAsync();

			var baseUrl = finalUrl.Substring( 0, finalUrl.LastIndexOf( '/' ) + 1 );

			var myHost = GetMyHost();

			var lines = content.Split( '\n' );

			for ( int i = 0; i < lines.Length; i++ ) {
				var line = lines[ i ].Trim();

				if ( string.IsNullOrWhiteSpace( line ) )
					continue;

				if ( line.StartsWith( "#EXT-X-KEY" ) ) {
					lines[ i ] = RewriteKey( line, baseUrl );
					continue;
				}

				if ( line.StartsWith( "#" ) )
					continue;

				var absoluteUrl = BuildAbsoluteUrl( line, baseUrl );

				lines[ i ] = absoluteUrl.EndsWith( ".m3u8" )
					? $"{myHost}/proxy-m3u8?url={Uri.EscapeDataString( absoluteUrl )}"
					: WorkerProxy + Uri.EscapeDataString( absoluteUrl );
			}

			return string.Join( "\n", lines );
		}

		private string RewriteKey( string line, string baseUrl ) {
			var match = Regex.Match( line, @"URI=""(?<url>[^""]+)""" );

			if ( !match.Success )
				return line;

			var keyUrl = match.Groups[ "url" ].Value;

			var absoluteKeyUrl = BuildAbsoluteUrl( keyUrl, baseUrl );

			var proxiedKey = WorkerProxy + Uri.EscapeDataString( absoluteKeyUrl );

			return line.Replace( keyUrl, proxiedKey );
		}

		private string BuildAbsoluteUrl( string url, string baseUrl ) {
			if ( url.StartsWith( "http" ) )
				return url;

			return new Uri( new Uri( baseUrl ), url ).ToString();
		}

		private string GetMyHost() {
			var request = _httpContextAccessor.HttpContext?.Request;

			if ( request == null )
				return "";

			return $"{request.Scheme}://{request.Host}";
		}
	}
}
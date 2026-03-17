
namespace UafixApiNew.Services
{
	public class ProxyService : IProxyService
	{
		private readonly IHttpClientFactory _clientFactory;
		private readonly ILogger<UafixService> _logger;

		private readonly HttpClient _client;
		private readonly string _myHost;

		private const string _proxy = "https://proxy-worker.s-teplyakovv.workers.dev/?url=";
		private readonly string[] _needProxySource;


		public ProxyService( 
			IHttpClientFactory clientFactory,
			IHttpContextAccessor httpContextAccessor,
			ILogger<UafixService> logger
		) {
			_clientFactory = clientFactory;
			_logger = logger;

			_needProxySource = new[] { "https://ashdi.vip" };

			_client = _clientFactory.CreateClient( "DefaultClient" );

			var httpContext = httpContextAccessor.HttpContext;
			_myHost = $"{httpContext?.Request.Scheme}://{httpContext?.Request.Host}";
		}

		public async Task<string?> GetProxyM3u8Result( string url ) {
			var referrer = _needProxySource.FirstOrDefault( p => url.Contains( p ) );

			var proxyUrl = string.IsNullOrWhiteSpace( referrer )
				? null
				: await ConvertToProxyM3u8( url, referrer );

			_logger.LogInformation( "Прокси ссылка: {Query}", proxyUrl );

			return proxyUrl;
		}

		private async Task<string> ConvertToProxyM3u8( string url, string referrer ) {
			var request = new HttpRequestMessage( HttpMethod.Get, url );
			request.Headers.Referrer = new Uri( referrer ); 

			var response = await _client.SendAsync( request );
			var content = await response.Content.ReadAsStringAsync();

			var baseUrl = url.Substring( 0, url.LastIndexOf( '/' ) + 1 );

			var lines = content.Split( '\n' );

			for ( int i = 0; i < lines.Length; i++ ) {
				string line = lines[ i ].Trim();

				if ( string.IsNullOrWhiteSpace( line ) || line.StartsWith( "#" ) )
					continue;

				string absoluteUrl = line.StartsWith( "http" ) ? line : ( baseUrl + line );

				//lines[ i ] = absoluteUrl.Contains( ".m3u8" )
				//	? $"{_myHost}/proxy-m3u8?url={Uri.EscapeDataString( absoluteUrl )}"
				//	: _proxy + Uri.EscapeDataString( absoluteUrl );

				lines[ i ] = _proxy + Uri.EscapeDataString( absoluteUrl );
			}

			return string.Join( "\n", lines );
		}
	}
}

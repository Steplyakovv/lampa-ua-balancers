using HtmlAgilityPack;
using System.Net;
using UafixApiNew.Settings;

namespace UafixApiNew.Managers;

public class ProxyManager
{
	private readonly IHttpClientFactory _clientFactory;

	private HttpClient Сlient => _clientFactory.CreateClient( "DefaultClient" );

	public ProxyManager( IHttpClientFactory clientFactory ) {
		_clientFactory = clientFactory;
	}

	public async Task<HtmlDocument?> GetFirstValidHtml( string targetUrl, string errorMessage ) {
		var proxyData = await Сlient.GetStringAsync( ProxySettings.ProxySourceIps );
		var proxies = proxyData.Split( '\n', StringSplitOptions.RemoveEmptyEntries )
							   .Select( p => p.Trim() ).ToList();

		using var cts = new CancellationTokenSource();
		var activeTasks = new List<Task<string?>>();

		int proxyIndex = 0;

		for ( ; proxyIndex < Math.Min( ProxySettings.BatchSize, proxies.Count ); proxyIndex++ ) {
			activeTasks.Add( TryGetHtml( targetUrl, proxies[ proxyIndex ], cts.Token ) );
		}

		while ( activeTasks.Any() ) {
			var completedTask = await Task.WhenAny( activeTasks );
			activeTasks.Remove( completedTask );

			try {
				var result = await completedTask;

				if ( !string.IsNullOrEmpty( result ) && !result.Contains( errorMessage, StringComparison.OrdinalIgnoreCase ) ) {
					cts.Cancel();

					var doc = new HtmlDocument();
					doc.LoadHtml( result );

					return doc;
				}
			}
			catch { }

			if ( proxyIndex < proxies.Count && !cts.IsCancellationRequested ) {
				activeTasks.Add( TryGetHtml( targetUrl, proxies[ proxyIndex ], cts.Token ) );
				proxyIndex++;
			}
		}

		return null;
	}

	private async Task<string?> TryGetHtml( string url, string proxyAddr, CancellationToken token ) {
		try {
			var handler = new HttpClientHandler
			{
				Proxy = new WebProxy( $"http://{proxyAddr}" ),
				UseProxy = true,
			};

			using var client = new HttpClient( handler );

			client.Timeout = TimeSpan.FromSeconds( 30 );

			client.DefaultRequestHeaders.Add( "User-Agent", HeadersProperty.UserAgent );
			client.DefaultRequestHeaders.Add( "Accept-Language", HeadersProperty.AcceptLanguage );
			client.DefaultRequestHeaders.Add( "Referer", GetBaseUrl( url ) );

			var response = await client.GetAsync( url, token );

			if ( response.IsSuccessStatusCode )
				return await response.Content.ReadAsStringAsync( token );
				
		}
		catch {}

		return null;
	}

	private string GetBaseUrl( string url ) {
		var uri = new Uri( url );

		return uri.GetLeftPart( UriPartial.Authority );
	}

}

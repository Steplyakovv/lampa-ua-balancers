using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using UafixApiNew.Models;

namespace UafixApiNew.Services;

public class UafixService : IMovieSource
{
	private static class XPath
	{
		public const string PlayerIframe =
			"//div[contains(@class, 'video-box')]//iframe";

		public const string SearchItem = "//a[contains(@class, 'sres-wrap')]";

		public const string SearchNavigation = "//div[@class='navigation']//a[contains(@onclick, 'list_submit')]";

		public const string SearchError = "//div[@class='berrors']";

		public const string Seasons = "//div[contains(@class, 'sez-wr')]/a[@class='sect-link']";
		public const string EpisodeItems = "//div[@id='sers-wr']//div[contains(@class, 'video-item')]";
		public const string PaginationLinks = "//div[@id='bottom-nav']//ul[@class='pagination']//a";

		public const string RawTitle = ".//div[@class='vi-title']";
		public const string EpisodeSubtitle = ".//div[@class='vi-rate']";
		public const string EpisodeUrl = ".//a[contains(@class, 'vi-img')]";
	}

	private readonly IHttpClientFactory _clientFactory;
	private readonly IMemoryCache _cache;
	private readonly ILogger<UafixService> _logger;

	private const int _lengthShortwords = 4;
	private const int _limitParallelRequest = 5;
	private const int _limitSearchedPage = 5;

	private HttpClient Сlient => _clientFactory.CreateClient( "UafixClient" );

	public UafixService(
		IHttpClientFactory clientFactory,
		IMemoryCache cache,
		ILogger<UafixService> logger
	) {
		_clientFactory = clientFactory;
		_cache = cache;
		_logger = logger;
	}

	public string Name => "Uafix";

	public async Task<StreamResponse?> GetStreamByUrlAsync( string filmUrl, VideoType videoType ) {
		if ( string.IsNullOrWhiteSpace( filmUrl ) )
			return new StreamResponse( "URL пуст", false, HttpStatusCode.BadRequest );

		string cacheKey = $"uafix_url_{filmUrl.GetHashCode()}";

		return await _cache.GetOrCreateAsync( cacheKey, async entry => {
			entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours( 12 );
			entry.SlidingExpiration = TimeSpan.FromHours( 2 );

			try {
				_logger.LogInformation( "Извлечение видео для: {Url} (Тип: {Type})", filmUrl, videoType );

				var result = await ExtractVideo( filmUrl, string.Empty, videoType );

				if ( result != null )
					return result;

				return new StreamResponse( "Контент не знайдено", false, HttpStatusCode.NotFound );
			}
			catch ( HttpRequestException ex ) {
				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes( 2 );
				return new StreamResponse( $"Помилка мережі", false, ex.StatusCode.Value );
			}
			catch ( Exception ex ) {
				_logger.LogError( ex, "Критическая ошибка парсинга {Url}", filmUrl );

				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes( 1 );
				return new StreamResponse( ex.Message, false, HttpStatusCode.InternalServerError );
			}
		} );
	}

	public async Task<StreamResponse?> FindStreamAsync( string[] titles, VideoType videoType )
		=> await GetStreamByTitlesAsync( titles, videoType );

	private async Task<StreamResponse?> GetStreamByTitlesAsync( string[] titles, VideoType videoType ) {
		var distinctTitles = titles
			.Where( t => !string.IsNullOrWhiteSpace( t ) )
			.Select( t => t.Trim() )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var title in distinctTitles ) {
			_logger.LogInformation( "Uafix: Пробую поиск по названию: {Title}", title );

			var result = await GetStreamByTitleAsync( title, videoType );

			if ( result is { Success: true } )
				return result;

			if ( result?.Status == HttpStatusCode.TooManyRequests )
				return result;
		}

		return await FindStreamBySplitTitlesAsync( distinctTitles, videoType );
	}

	private async Task<StreamResponse?> FindStreamBySplitTitlesAsync( string[] titles, VideoType videoType ) {
		var searchedQueries = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		int maxIteration = titles.Length - 1;

		for ( int t = 0; t <= maxIteration; t++ ) {
			var words = titles[ t ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );

			if ( words.Length == 1 && t == maxIteration )
				return await GetStreamByTitleAsync( titles[ t ], videoType );

			for ( int i = words.Length - 1; i >= 1; i-- ) {
				var reducedTitle = string.Join( " ", words.Take( i ) );

				if ( reducedTitle.Length < _lengthShortwords || !searchedQueries.Add( reducedTitle ) )
					continue;

				_logger.LogInformation( "Сокращенный поиск: {Query}", reducedTitle );

				var result = await GetStreamByTitleAsync( reducedTitle, videoType );

				if ( result is { Success: true } ) {
					_logger.LogInformation( "Успешно найдено по: {Title}", reducedTitle );
					return result;
				}

				if ( result is not null && i == 1 && t == maxIteration )
					return result;
			}

			if ( t == maxIteration )
				return await GetStreamByTitleAsync( titles[ t ], videoType );
		}

		return null;
	}

	private async Task<StreamResponse?> GetStreamByTitleAsync( string title, VideoType videoType ) {
		if ( string.IsNullOrWhiteSpace( title ) )
			return new StreamResponse( "Заголовок не может быть пустым", false, HttpStatusCode.InternalServerError );

		string cacheKey = $"uafix_search_{title.ToLower().Trim()}";

		return await _cache.GetOrCreateAsync( cacheKey, async entry => {
			entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours( 6 );
			entry.SlidingExpiration = TimeSpan.FromHours( 1 );

			try {
				var baseUrl = $"/index.php?do=search&subaction=search&story={Uri.EscapeDataString( title )}";
				var firstPageDoc = await GetHtmlDocument( baseUrl );

				var firstNodes = firstPageDoc?.DocumentNode.SelectNodes( XPath.SearchItem );
				if ( firstNodes is null )
					return GetErrorInformation( firstPageDoc );

				var allSearchNodes = new List<HtmlNode>( firstNodes );

				var navNodes = firstPageDoc?.DocumentNode.SelectNodes( XPath.SearchNavigation );
				if ( navNodes is not null ) {
					int maxPages = navNodes
						.Select( n => Regex.Match( n.GetAttributeValue( "onclick", "" ), @"\d+", RegexOptions.Compiled ).Value )
						.Where( v => !string.IsNullOrEmpty( v ) )
						.Select( int.Parse )
						.DefaultIfEmpty( 1 )
						.Max();

					int pagesToFetch = Math.Min( maxPages, _limitSearchedPage );

					if ( pagesToFetch > 1 ) {
						var pageTasks = Enumerable.Range( 2, pagesToFetch - 1 )
							.Select( p => GetHtmlDocument( $"{baseUrl}&search_start={p}" ) );

						var docs = await Task.WhenAll( pageTasks );

						foreach ( var doc in docs.Where( d => d is not null ) ) {
							var extraNodes = doc!.DocumentNode.SelectNodes( XPath.SearchItem );
							if ( extraNodes is not null )
								allSearchNodes.AddRange( extraNodes );
						}
					}
				}

				var searchResults = GetSearchResults( allSearchNodes, videoType );

				if ( searchResults.Length == 0 )
					return new StreamResponse( "Нічого не знайдено за цим типом", false, HttpStatusCode.NotFound );

				if ( searchResults.Length == 1 ) {
					_logger.LogInformation( "Uafix: Найден точный матч для {Title}, перехожу к извлечению.", title );

					var directResult = await ExtractVideo( searchResults[ 0 ].Url, title, videoType );
					if ( directResult is not null )
						return directResult;
				}

				return new StreamResponse( title, searchResults );
			}
			catch ( HttpRequestException ex ) {
				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes( 1 );
				return new StreamResponse( ex.Message, false, ex.StatusCode.Value );
			}
			catch ( Exception ex ) {
				_logger.LogError( ex, "Uafix: Ошибка поиска для {Title}", title );

				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes( 1 );
				return new StreamResponse( ex.Message, false, HttpStatusCode.InternalServerError );
			}
		} );
	}

	private StreamResponse? GetErrorInformation( HtmlDocument searchDoc ) {
		var errorDiv = searchDoc.DocumentNode.SelectSingleNode( XPath.SearchError );

		return errorDiv is not null
			? new StreamResponse( errorDiv.InnerText.Trim(), false )
			: null;
	}

	private SearchResult[] GetSearchResults( IEnumerable<HtmlNode> nodes, VideoType videoType ) {
		var searchResults = new List<SearchResult>();

		string[] urlMarkers = videoType switch {
			VideoType.Serial => [ "/serials/" ],
			VideoType.Film => [ "/films/", "/cartoons/", "/anime/" ],
			_ => Array.Empty<string>()
		};

		foreach ( var node in nodes ) {
			string url = node.GetAttributeValue( "href", "" );
			bool isExistsUrlMarker = urlMarkers.Any( marker => url.Contains( marker, StringComparison.OrdinalIgnoreCase ) );

			if ( urlMarkers.Length > 0 && !isExistsUrlMarker )
				continue;

			var h2Node = node.SelectSingleNode( ".//h2" );
			if ( h2Node == null )
				continue;

			string title = CleanTitle( h2Node.InnerText );

			if ( !string.IsNullOrEmpty( url ) )
				searchResults.Add( new SearchResult( title, url ) );
		}

		return searchResults.ToArray();
	}


	private string CleanTitle( string rawTitle ) {
		if ( string.IsNullOrWhiteSpace( rawTitle ) )
			return "Без названия";

		var firstPart = rawTitle.Split( '/', StringSplitOptions.TrimEntries ).FirstOrDefault();

		return firstPart?.Replace( "&amp;", "&" ).Trim() ?? rawTitle;
	}

	private async Task<StreamResponse?> ExtractVideo( string filmPageUrl, string title, VideoType videoType )
		=> videoType switch {
			VideoType.Film => await ExtractFilm( filmPageUrl, title ),
			VideoType.Serial => await ExtractSerial( filmPageUrl, title ),
			VideoType.Episode => await ExtractEpisodes( filmPageUrl, title ),
			_ => await ExtractFilm( filmPageUrl, title )
		};

	private async Task<StreamResponse?> ExtractSerial( string filmPageUrl, string title ) {
		var filmDoc = await GetHtmlDocument( filmPageUrl );
		if ( filmDoc is null )
			return null;

		string? iframeUrl = GetIframeUrl( filmDoc, filmPageUrl );

		if ( !string.IsNullOrEmpty( iframeUrl ) ) {
			string playerHtml = await GetPlayerHtml( iframeUrl );
			var match = Regex.Match( playerHtml, @"file\s*:\s*'(.*?)'", RegexOptions.Singleline | RegexOptions.Compiled );

			if ( match.Success ) {
				var serialModels = GetSerialModelsFromJson( match.Groups[ 1 ].Value );

				if ( serialModels?.Length > 0 )
					return new StreamResponse( title, serialModels );
			}
		}

		return ExtractSerialFromUafix( filmDoc, filmPageUrl, title );
	}

	private StreamResponse? ExtractSerialFromUafix( HtmlDocument filmDoc, string filmPageUrl, string title ) {
		var seasonNodes = filmDoc.DocumentNode.SelectNodes( XPath.Seasons );

		if ( seasonNodes is null )
			return null;

		var seasons = seasonNodes
			.Select( a => new {
				Text = a.InnerText.Trim(),
				Href = a.GetAttributeValue( "href", "" )
			} )
			.Where( x => x.Text.Contains( "Сезон", StringComparison.OrdinalIgnoreCase ) )
			.Select( x => new SerialModel {
				Type = FolderType.Season,
				Title = x.Text,
				AuxiliaryLink = new Uri( new Uri( filmPageUrl ), x.Href ).ToString()
			} )
			.ToArray();

		return seasons.Length > 0 
			? new StreamResponse( title, seasons ) 
			: null;
	}

	private async Task<StreamResponse?> ExtractEpisodes( string filmPageUrl, string title ) {
		var firstPageDoc = await GetHtmlDocument( filmPageUrl );
		if ( firstPageDoc == null )
			return null;

		var paginationNodes = firstPageDoc.DocumentNode.SelectNodes( XPath.PaginationLinks );
		var otherPageUrls = paginationNodes
			?.Select( link => new Uri( new Uri( filmPageUrl ), link.GetAttributeValue( "href", "" ) ).ToString() )
			.Distinct()
			.Where( url => url != filmPageUrl )
			.ToList() ?? new List<string>();

		var otherPagesTasks = otherPageUrls.Select( GetHtmlDocument );
		var otherDocs = await Task.WhenAll( otherPagesTasks );

		var allDocs = new List<HtmlDocument> { firstPageDoc };
		allDocs.AddRange( 
			otherDocs.Where( d => d != null )! 
		);

		var allEpisodesRaw = new List<(int Number, string Title, string Subtitle, string Url)>();

		foreach ( var doc in allDocs ) {
			var nodes = doc.DocumentNode.SelectNodes( XPath.EpisodeItems );
			if ( nodes == null )
				continue;

			foreach ( var node in nodes ) {
				string rawTitle = node.SelectSingleNode( XPath.RawTitle )?.InnerText.Trim() ?? "";
				string episodeSubtitle = node.SelectSingleNode( XPath.EpisodeSubtitle )?.InnerText.Trim() ?? "";
				string episodeUrl = node.SelectSingleNode( XPath.EpisodeUrl )?.GetAttributeValue( "href", "" ) ?? "";

				if ( string.IsNullOrEmpty( episodeUrl ) )
					continue;

				var match = Regex.Match( rawTitle, @"Серія\s+(\d+)", RegexOptions.Compiled );
				int number = match.Success 
					? int.Parse( match.Groups[ 1 ].Value ) 
					: 0;

				string cleanTitle = match.Success 
					? $"Серія {match.Groups[ 1 ].Value}" 
					: rawTitle;

				allEpisodesRaw.Add( (number, cleanTitle, episodeSubtitle, episodeUrl) );
			}
		}

		var sortedEpisodes = allEpisodesRaw
			.GroupBy( e => e.Url )
			.Select( g => g.First() )
			.OrderBy( e => e.Number )
			.ToList();

		if ( !sortedEpisodes.Any() )
			return null;

		var semaphore = new SemaphoreSlim( _limitParallelRequest );
		var episodeTasks = sortedEpisodes.Select( async item => {
			await semaphore.WaitAsync();

			try {
				string? streamUrl = await GetStreamVideoUrl( item.Url );
				if ( string.IsNullOrEmpty( streamUrl ) )
					return null;

				return new SerialModel {
					Type = FolderType.Episode,
					File = streamUrl,
					Title = $"{item.Title} {item.Subtitle}".Trim()
				};
			}
			catch ( Exception ex ) {
				_logger.LogError( ex, "Uafix: Ошибка обработки серии {Num}", item.Number );
				return null;
			}
			finally {
				semaphore.Release();
			}
		} );

		var episodeResults = await Task.WhenAll( episodeTasks );

		var finalEpisodes = episodeResults.Where( e => e != null ).ToArray();

		return finalEpisodes.Length > 0
			? new StreamResponse( title, finalEpisodes )
			: null;
	}

	private SerialModel[]? GetSerialModelsFromJson( string serialJson ) {
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var serialModels = JsonSerializer.Deserialize<List<SerialModel>>( serialJson, options );

		if ( serialModels == null )
			return null;

		foreach ( var voice in serialModels ) {
			voice.Type = FolderType.VoiceActing;

			voice.Folder?.ForEach( season => {
				season.Type = FolderType.Season;

				season.Folder?.ForEach( episode => {
					episode.Type = FolderType.Episode;
				} );
			} );
		}

		return serialModels.ToArray();
	}

	private async Task<StreamResponse?> ExtractFilm( string filmPageUrl, string title ) {
		string? streamUrl = await GetStreamVideoUrl( filmPageUrl );

		return streamUrl is not null
			? new StreamResponse( streamUrl, title )
			: null;
	}

	private async Task<string?> GetStreamVideoUrl( string filmPageUrl ) {
		var filmDoc = await GetHtmlDocument( filmPageUrl );
		if ( filmDoc is null )
			return null;

		string? iframeUrl = GetIframeUrl( filmDoc, filmPageUrl );
		if ( string.IsNullOrEmpty( iframeUrl ) )
			return null;

		return iframeUrl switch {
			var url when url.Contains( "youtube" ) => await GetYoutubeStreamUrl( url ),
			var url when url.Contains( "zetvideo" ) => await GetBalancerStreamUrl( url ),
			_ => await GetBalancerStreamUrl( iframeUrl )
		};
	}

	private async Task<string?> GetBalancerStreamUrl( string url ) {
		string playerHtml = await GetPlayerHtml( url );
		if ( string.IsNullOrEmpty( playerHtml ) )
			return null;

		var match = Regex.Match( playerHtml, @"https?://[^\s""']+\.m3u8", RegexOptions.IgnoreCase | RegexOptions.Compiled );

		return match.Success 
			? match.Value 
			: null;
	}

	private async Task<string?> GetYoutubeStreamUrl( string url ) {
		var match = Regex.Match( url, @"(?:embed\/|v=)([\w-]{11})", RegexOptions.Compiled );

		return await Task.FromResult( 
			match.Success 
			? $"youtube:{match.Groups[ 1 ].Value}" 
			: null 
		); 
	}

	private async Task<string> GetPlayerHtml( string url ) {
		var request = new HttpRequestMessage( HttpMethod.Get, url );
		//request.Headers.Referrer = new Uri( refUrl );

		request.Headers.Referrer = new Uri( url );

		var origin = new Uri( url ).GetLeftPart( UriPartial.Authority );
		request.Headers.Add( "Origin", origin );

		var playerResponse = await Сlient.SendAsync( request );

		return await playerResponse.Content.ReadAsStringAsync();
	}

	private string? GetIframeUrl( HtmlDocument document, string baseUrl ) {
		var iframeNodes = document.DocumentNode.SelectNodes( XPath.PlayerIframe );

		if ( iframeNodes is null )
			return null;

		string? youtubeFallback = null;

		foreach ( var iframe in iframeNodes ) {
			var src = iframe.GetAttributeValue( "src", "" );

			if ( string.IsNullOrEmpty( src ) )
				continue;

			var fullUrl = NormalizeUrl( src, baseUrl );

			if ( fullUrl.Contains( "youtube" ) || fullUrl.Contains( "youtu.be" ) ) {
				youtubeFallback = fullUrl;
				continue;
			}

			return fullUrl;
		}

		return youtubeFallback;
	}

	private string NormalizeUrl( string src, string baseUrl ) {
		if ( src.StartsWith( "//" ) )
			return $"{new Uri( baseUrl ).Scheme}:{src}";

		if ( src.StartsWith( "/" ) ) {
			var uri = new Uri( baseUrl );
			return $"{uri.Scheme}://{uri.Host}{src}";
		}

		return src;
	}

	private async Task<HtmlDocument?> GetHtmlDocument( string url ) {
		var response = await Сlient.GetAsync( url );

		if ( !response.IsSuccessStatusCode )
			throw new HttpRequestException( response.ReasonPhrase, null, response.StatusCode );
			
		var html = await response.Content.ReadAsStringAsync();

		var doc = new HtmlDocument();
		doc.LoadHtml( html );

		return doc;
	}
}

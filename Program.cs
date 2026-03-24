using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using UafixApiNew.Managers;
using UafixApiNew.Models;
using UafixApiNew.Services;
using UafixApiNew.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient( "DefaultClient", client => {
	client.DefaultRequestHeaders.Add( "User-Agent", HeadersProperty.UserAgent );
	client.DefaultRequestHeaders.Add( "Accept-Language", HeadersProperty.AcceptLanguage );

	client.Timeout = TimeSpan.FromSeconds( 20 );
} );

builder.Services.AddHttpClient( "UafixClient", client => {
	client.BaseAddress = new Uri( UafixConstants.BaseUrl );

	client.DefaultRequestHeaders.Add( "User-Agent", HeadersProperty.UserAgent );
	client.DefaultRequestHeaders.Add( "Accept-Language", HeadersProperty.AcceptLanguage );

	client.Timeout = TimeSpan.FromSeconds( 20 );
} ).ConfigurePrimaryHttpMessageHandler( () => new HttpClientHandler {
	AllowAutoRedirect = true
} );

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<ProxyManager>();
builder.Services.AddScoped<IMovieSource, UafixService>();
builder.Services.AddScoped<IProxyStreamService, ProxyStreamService>();

builder.Services.AddCors( options =>
{
	options.AddDefaultPolicy( policy =>
	{
		policy
			.AllowAnyOrigin()
			.AllowAnyHeader()
			.AllowAnyMethod();
	} );
} );

builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

var forwardOptions = new ForwardedHeadersOptions {
	ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
};

forwardOptions.KnownNetworks.Clear();
forwardOptions.KnownProxies.Clear();

app.UseForwardedHeaders( forwardOptions );

app.UseStaticFiles();

app.MapGet( "/api/status", () => {
	return Results.Ok( new {
		status = "ok",
		service = "tmdb-stream",
		time = DateTime.UtcNow
	} );
} );

app.MapGet( "/debug-html", async ( string url, ProxyManager proxyHtmlService ) => {
	try {
		var result = await proxyHtmlService.GetFirstValidHtml( url, UafixConstants.ValidationMessage );

		return Results.Content( result.ToString() );
	} catch ( Exception ex ) {
		return Results.Problem( ex.Message );
	}
} );

app.MapGet( "/proxy-m3u8", async ( 
	string url, 
	IProxyStreamService proxyService, 
	HttpContext context 
) => {
	if ( string.IsNullOrWhiteSpace( url ) )
		return Results.BadRequest( "Url required" );

	var result = await proxyService.GetProxyM3u8Result( url );

	if ( result is null )
		return Results.Redirect( url );

	return Results.Content( result, "application/vnd.apple.mpegurl" );
} );

app.MapGet( "/find-stream", 
	async ( 
		[FromQuery] string[] titles, 
		[FromQuery] VideoType videoType, 
		IEnumerable<IMovieSource> sources 
	) => {
		if ( titles is null || titles.Length == 0 )
			return Results.BadRequest( new BaseResponse( "Titles are required", false ) );

		foreach ( var source in sources ) {
			var result = await source.FindStreamAsync( titles, videoType );
			if ( result is not null )
				return Results.Ok( result );  
		}

		return Results.NotFound( new BaseResponse( "Фильм не найден или поток недоступен", false ) );
} );

app.MapGet( "/extract", 
	async ( 
		[FromQuery] string url, 
		[FromQuery] VideoType videoType, 
		IEnumerable <IMovieSource> sources 
	) => {
		if ( string.IsNullOrEmpty( url ) )
			return Results.BadRequest( new BaseResponse( "Titles are required", false ) );

		foreach ( var source in sources ) {
			var result = await source.GetStreamByUrlAsync( url, videoType );
			if ( result is not null )
				return Results.Ok( result );
		}

		return Results.NotFound( new BaseResponse( "Фильм не найден или поток недоступен", false )  );
} );

app.Run();

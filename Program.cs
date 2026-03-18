using Microsoft.AspNetCore.Mvc;
using UafixApiNew.Models;
using UafixApiNew.Services;

const string User_Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

//string port = Environment.GetEnvironmentVariable( "PORT" ) ?? "5000";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient( "DefaultClient", client => {
	client.DefaultRequestHeaders.Add( "User-Agent", User_Agent );
	client.Timeout = TimeSpan.FromSeconds( 20 );
} );

builder.Services.AddHttpClient( "UafixClient", client => {
	client.BaseAddress = new Uri( "https://uafix.net" );

	client.DefaultRequestHeaders.Add( "User-Agent", User_Agent );
	client.DefaultRequestHeaders.Add( "Accept-Language", "en-US,en;q=0.9" );

	client.Timeout = TimeSpan.FromSeconds( 20 );
} );

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IMovieSource, UafixService>();
builder.Services.AddScoped<IProxyService, ProxyService>();

builder.Services.AddCors( options => {
	options.AddPolicy( "AllowAll", policy => {
		policy
			.AllowAnyOrigin()
			.AllowAnyMethod()
			.AllowAnyHeader();
	} );
} );

builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors( "AllowAll" );

//app.UseHttpsRedirection();

app.UseStaticFiles();

app.MapGet( "/api/status", () => {
	return Results.Ok( new {
		status = "ok",
		service = "tmdb-stream",
		time = DateTime.UtcNow
	} );
} );

app.MapGet( "/debug-html", async ( 
	string url, 
	string clientNmae,
	IHttpClientFactory factory 
) => {
	var client = factory.CreateClient( clientNmae );

	var request = new HttpRequestMessage( HttpMethod.Get, url );

	request.Headers.Add( "Referer", client.BaseAddress.ToString() );
	request.Headers.Add( "Origin", client.BaseAddress.ToString() );

	var response = await client.SendAsync( request );
	var html = await response.Content.ReadAsStringAsync();

	return Results.Content( html, "text/html" );
} );

app.MapGet( "/proxy-m3u8", async ( string url, IProxyService proxyService ) => {
	if ( string.IsNullOrWhiteSpace( url ) )
		return Results.BadRequest( new BaseResponse( "Url are required", false ) );

	try {
		var proxyUrl = await proxyService.GetProxyM3u8Result( url );

		return proxyUrl is null
				? Results.Redirect( url )
				: Results.Content( proxyUrl, "application/vnd.apple.mpegurl" );
		
	}
	catch ( Exception ex ) { 
		return Results.BadRequest( ex.Message ); 
	}
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


//app.Urls.Add( $"http://0.0.0.0:{port}" );
app.Run();

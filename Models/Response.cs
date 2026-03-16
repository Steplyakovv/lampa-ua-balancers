using System.Net;
using System.Text.Json.Serialization;

namespace UafixApiNew.Models;

public class BaseResponse {
	[JsonPropertyName( "success" )]
	public bool Success { get; }

	[JsonPropertyName( "message" )]
	public string? Message { get; }

	[JsonPropertyName( "status" )]
	public HttpStatusCode? Status { get; }

	public BaseResponse() {
		Success = true;
	}

	public BaseResponse( string message, bool sucess ) {
		Success = sucess;
		Message = message;
	}

	public BaseResponse( string message, bool sucess, HttpStatusCode statusCode ) {
		Status = statusCode;
	}
}

public class StreamResponse : BaseResponse
{
	[JsonPropertyName( "url" )]
	public string? Url { get; }

	[JsonPropertyName( "title" )]
	public string? Title { get; }

	[JsonPropertyName( "searchResults" )]
	public SearchResult[]? SearchResults { get; }

	[JsonPropertyName( "serial" )]
	public SerialModel[]? Serial { get; }

	public StreamResponse( string message, bool sucess ) 
		: base( message, sucess ) {}
	public StreamResponse( string message, bool sucess, HttpStatusCode statusCode ) 
		: base( message, sucess, statusCode ) {}

	public StreamResponse( string url, string title ) {
		Url = url;
		Title = title;
	}

	public StreamResponse( string url ) {
		Url = url;
	}

	public StreamResponse( string title, SearchResult[] searchResults ) {
		Title = title;
		SearchResults = searchResults;
	}

	public StreamResponse( string title, SerialModel[] serial ) {
		Title = title;
		Serial = serial;
	}
}

public record SearchResult(
	[property: JsonPropertyName( "title" )]  string Title,
	[property: JsonPropertyName( "url" )]  string Url 
);

public enum VideoType 
{ 
	Film,
	Serial,
	Episode
}

public enum FolderType 
{ 
	VoiceActing, 
	Season, 
	Episode 
}

public class SerialModel
{
	[JsonPropertyName( "title" )]
	public string? Title { get; set; }

	[JsonPropertyName( "type" )]
	public FolderType Type { get; set; }

	[JsonPropertyName( "file" )]
	public string? File { get; set; }

	[JsonPropertyName( "folder" )]
	public List<SerialModel>? Folder { get; set; }

	[JsonPropertyName( "auxiliaryLink" )]
	public string? AuxiliaryLink { get; set; }
}


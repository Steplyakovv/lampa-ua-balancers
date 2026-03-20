using UafixApiNew.Models;

namespace UafixApiNew.Services;
public interface IMovieSource
{
	string Name { get; }
	Task<StreamResponse?> FindStreamAsync( string[] titles, VideoType videoType );
	Task<StreamResponse?> GetStreamByUrlAsync( string filmUrl, VideoType videoType );
}

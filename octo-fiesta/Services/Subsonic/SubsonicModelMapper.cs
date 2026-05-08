using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;

namespace octo_fiesta.Services.Subsonic;

/// <summary>
/// Handles parsing Subsonic API responses and merging local with external search results.
/// </summary>
public class SubsonicModelMapper
{
    private readonly SubsonicResponseBuilder _responseBuilder;
    private readonly ILogger<SubsonicModelMapper> _logger;

    public SubsonicModelMapper(
        SubsonicResponseBuilder responseBuilder,
        ILogger<SubsonicModelMapper> logger)
    {
        _responseBuilder = responseBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Parses a Subsonic search response and extracts songs, albums, and artists.
    /// </summary>
    public (List<object> Songs, List<object> Albums, List<object> Artists) ParseSearchResponse(
        byte[] responseBody,
        string? contentType)
    {
        var songs = new List<object>();
        var albums = new List<object>();
        var artists = new List<object>();

        try
        {
            var content = Encoding.UTF8.GetString(responseBody);
            
            if (contentType?.Contains("json") == true)
            {
                var jsonDoc = JsonDocument.Parse(content);
                if (jsonDoc.RootElement.TryGetProperty("subsonic-response", out var response) &&
                    response.TryGetProperty("searchResult3", out var searchResult))
                {
                    if (searchResult.TryGetProperty("song", out var songElements))
                    {
                        foreach (var song in songElements.EnumerateArray())
                        {
                            songs.Add(_responseBuilder.ConvertSubsonicJsonElement(song, true));
                        }
                    }
                    if (searchResult.TryGetProperty("album", out var albumElements))
                    {
                        foreach (var album in albumElements.EnumerateArray())
                        {
                            albums.Add(_responseBuilder.ConvertSubsonicJsonElement(album, true));
                        }
                    }
                    if (searchResult.TryGetProperty("artist", out var artistElements))
                    {
                        foreach (var artist in artistElements.EnumerateArray())
                        {
                            artists.Add(_responseBuilder.ConvertSubsonicJsonElement(artist, true));
                        }
                    }
                }
            }
            else
            {
                var xmlDoc = XDocument.Parse(content);
                var ns = xmlDoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var searchResult = xmlDoc.Descendants(ns + "searchResult3").FirstOrDefault();
                
                if (searchResult != null)
                {
                    foreach (var song in searchResult.Elements(ns + "song"))
                    {
                        songs.Add(_responseBuilder.ConvertSubsonicXmlElement(song, "song"));
                    }
                    foreach (var album in searchResult.Elements(ns + "album"))
                    {
                        albums.Add(_responseBuilder.ConvertSubsonicXmlElement(album, "album"));
                    }
                    foreach (var artist in searchResult.Elements(ns + "artist"))
                    {
                        artists.Add(_responseBuilder.ConvertSubsonicXmlElement(artist, "artist"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing Subsonic search response");
        }

        return (songs, albums, artists);
    }

    /// <summary>
    /// Merges local and external search results (songs, albums, artists, playlists)
    /// without consulting the local download mappings. External entries are deduped
    /// against locals by normalized metadata only.
    /// </summary>
    public (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResults(
        List<object> localSongs,
        List<object> localAlbums,
        List<object> localArtists,
        SearchResult externalResult,
        List<ExternalPlaylist> externalPlaylists,
        bool isJson)
    {
        return MergeSearchResults(
            localSongs,
            localAlbums,
            localArtists,
            externalResult,
            externalPlaylists,
            mappings: null,
            isJson);
    }

    /// <summary>
    /// Merges local and external search results, additionally consulting the
    /// downloaded-songs <paramref name="mappings"/> snapshot to drop external entries
    /// that already have a local equivalent in the user's library.
    /// </summary>
    public (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResults(
        List<object> localSongs,
        List<object> localAlbums,
        List<object> localArtists,
        SearchResult externalResult,
        List<ExternalPlaylist> externalPlaylists,
        IReadOnlyDictionary<string, LocalSongMapping>? mappings,
        bool isJson)
    {
        if (isJson)
        {
            return MergeSearchResultsJson(localSongs, localAlbums, localArtists, externalResult, externalPlaylists, mappings);
        }
        else
        {
            return MergeSearchResultsXml(localSongs, localAlbums, localArtists, externalResult, externalPlaylists, mappings);
        }
    }

    private (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResultsJson(
        List<object> localSongs,
        List<object> localAlbums,
        List<object> localArtists,
        SearchResult externalResult,
        List<ExternalPlaylist> externalPlaylists,
        IReadOnlyDictionary<string, LocalSongMapping>? mappings)
    {
        // Build local indexes from the JSON dictionaries returned by Navidrome's search3.
        var localSongIds = new HashSet<string>(StringComparer.Ordinal);
        var localSongKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var song in localSongs)
        {
            if (song is not Dictionary<string, object> dict)
            {
                continue;
            }
            if (dict.TryGetValue("id", out var idObj) && idObj?.ToString() is { Length: > 0 } id)
            {
                localSongIds.Add(id);
            }
            var artist = dict.TryGetValue("artist", out var a) ? a?.ToString() : null;
            var title = dict.TryGetValue("title", out var t) ? t?.ToString() : null;
            var key = BuildSongKey(artist, title);
            if (key != null)
            {
                localSongKeys.Add(key);
            }
        }

        var localAlbumKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var album in localAlbums)
        {
            if (album is not Dictionary<string, object> dict)
            {
                continue;
            }
            var artist = dict.TryGetValue("artist", out var a) ? a?.ToString() : null;
            // Subsonic uses "name" for album titles; some clients expose "title" too.
            var title = dict.TryGetValue("name", out var n) ? n?.ToString()
                       : dict.TryGetValue("title", out var t) ? t?.ToString()
                       : null;
            var key = BuildAlbumKey(artist, title);
            if (key != null)
            {
                localAlbumKeys.Add(key);
            }
        }

        var mergedSongs = new List<object>(localSongs);
        foreach (var song in externalResult.Songs)
        {
            if (ShouldDropExternalSong(song, mappings, localSongIds, localSongKeys))
            {
                continue;
            }
            mergedSongs.Add(_responseBuilder.ConvertSongToJson(song));
        }

        var mergedAlbums = new List<object>(localAlbums);
        foreach (var album in externalResult.Albums)
        {
            if (ShouldDropExternalAlbum(album, localAlbumKeys))
            {
                continue;
            }
            mergedAlbums.Add(_responseBuilder.ConvertAlbumToJson(album));
        }
        // Playlists surfaced as albums; never deduped against local content.
        foreach (var playlist in externalPlaylists)
        {
            mergedAlbums.Add(ConvertPlaylistToAlbumJson(playlist));
        }

        // Deduplicate artists by name - prefer local artists over external ones
        var localArtistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artist in localArtists)
        {
            if (artist is Dictionary<string, object> dict && dict.TryGetValue("name", out var nameObj))
            {
                localArtistNames.Add(nameObj?.ToString() ?? "");
            }
        }

        var mergedArtists = localArtists.ToList();
        foreach (var externalArtist in externalResult.Artists)
        {
            if (!localArtistNames.Contains(externalArtist.Name))
            {
                mergedArtists.Add(_responseBuilder.ConvertArtistToJson(externalArtist));
            }
        }

        return (mergedSongs, mergedAlbums, mergedArtists);
    }

    private (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResultsXml(
        List<object> localSongs,
        List<object> localAlbums,
        List<object> localArtists,
        SearchResult externalResult,
        List<ExternalPlaylist> externalPlaylists,
        IReadOnlyDictionary<string, LocalSongMapping>? mappings)
    {
        var ns = XNamespace.Get("http://subsonic.org/restapi");

        var localSongIds = new HashSet<string>(StringComparer.Ordinal);
        var localSongKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var song in localSongs.Cast<XElement>())
        {
            var id = song.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                localSongIds.Add(id);
            }
            var key = BuildSongKey(song.Attribute("artist")?.Value, song.Attribute("title")?.Value);
            if (key != null)
            {
                localSongKeys.Add(key);
            }
        }

        var localAlbumKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var album in localAlbums.Cast<XElement>())
        {
            var artist = album.Attribute("artist")?.Value;
            // Album titles in Subsonic XML live on the "name" attribute (sometimes "title").
            var title = album.Attribute("name")?.Value ?? album.Attribute("title")?.Value;
            var key = BuildAlbumKey(artist, title);
            if (key != null)
            {
                localAlbumKeys.Add(key);
            }
        }

        // Deduplicate artists by name - prefer local artists over external ones
        var localArtistNamesXml = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedArtists = new List<object>();

        foreach (var artist in localArtists.Cast<XElement>())
        {
            var name = artist.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                localArtistNamesXml.Add(name);
            }
            artist.Name = ns + "artist";
            mergedArtists.Add(artist);
        }

        foreach (var artist in externalResult.Artists)
        {
            if (!localArtistNamesXml.Contains(artist.Name))
            {
                mergedArtists.Add(_responseBuilder.ConvertArtistToXml(artist, ns));
            }
        }

        // Albums
        var mergedAlbums = new List<object>();
        foreach (var album in localAlbums.Cast<XElement>())
        {
            album.Name = ns + "album";
            mergedAlbums.Add(album);
        }
        foreach (var album in externalResult.Albums)
        {
            if (ShouldDropExternalAlbum(album, localAlbumKeys))
            {
                continue;
            }
            mergedAlbums.Add(_responseBuilder.ConvertAlbumToXml(album, ns));
        }
        foreach (var playlist in externalPlaylists)
        {
            mergedAlbums.Add(ConvertPlaylistToAlbumXml(playlist, ns));
        }

        // Songs
        var mergedSongs = new List<object>();
        foreach (var song in localSongs.Cast<XElement>())
        {
            song.Name = ns + "song";
            mergedSongs.Add(song);
        }
        foreach (var song in externalResult.Songs)
        {
            if (ShouldDropExternalSong(song, mappings, localSongIds, localSongKeys))
            {
                continue;
            }
            mergedSongs.Add(_responseBuilder.ConvertSongToXml(song, ns));
        }

        return (mergedSongs, mergedAlbums, mergedArtists);
    }

    /// <summary>
    /// Decides whether an external <paramref name="song"/> already has a local
    /// equivalent in the search response and should be hidden.
    /// </summary>
    private static bool ShouldDropExternalSong(
        Song song,
        IReadOnlyDictionary<string, LocalSongMapping>? mappings,
        HashSet<string> localSongIds,
        HashSet<string> localSongKeys)
    {
        // Tier 1: precise mapping check. The user has actually downloaded this exact
        // ext id via Octo Fiesta and Navidrome returned the resulting local song in
        // this same response — drop the duplicate.
        if (mappings != null
            && !string.IsNullOrEmpty(song.ExternalProvider)
            && !string.IsNullOrEmpty(song.ExternalId)
            && mappings.TryGetValue($"{song.ExternalProvider}:{song.ExternalId}", out var mapping)
            && !string.IsNullOrEmpty(mapping.LocalSubsonicId)
            && localSongIds.Contains(mapping.LocalSubsonicId))
        {
            return true;
        }

        // Tier 2: metadata fallback. Catches pre-existing library songs (no mapping row)
        // that Navidrome already surfaced for the same query.
        var key = BuildSongKey(song.Artist, song.Title);
        return key != null && localSongKeys.Contains(key);
    }

    /// <summary>
    /// Drops external albums whose normalized (artist, title) matches an album returned
    /// by the local Navidrome search. There is no album-level mapping store, so this
    /// relies on metadata alone.
    /// </summary>
    private static bool ShouldDropExternalAlbum(Album album, HashSet<string> localAlbumKeys)
    {
        var key = BuildAlbumKey(album.Artist, album.Title);
        return key != null && localAlbumKeys.Contains(key);
    }

    private static string? BuildSongKey(string? artist, string? title)
    {
        var artistKey = StringNormalizer.CreateComparisonKey(artist);
        var titleKey = StringNormalizer.CreateComparisonKey(title);
        if (artistKey.Length == 0 || titleKey.Length == 0)
        {
            return null;
        }
        return artistKey + "\u0001" + titleKey;
    }

    private static string? BuildAlbumKey(string? artist, string? title)
    {
        var artistKey = StringNormalizer.CreateComparisonKey(artist);
        var titleKey = StringNormalizer.CreateComparisonKey(title);
        if (artistKey.Length == 0 || titleKey.Length == 0)
        {
            return null;
        }
        return artistKey + "\u0001" + titleKey;
    }
    
    /// <summary>
    /// Converts an ExternalPlaylist to a JSON object representing an album.
    /// Playlists are represented as albums with genre "Playlist" and artist "🎵 {Provider} {Curator}".
    /// </summary>
    private Dictionary<string, object> ConvertPlaylistToAlbumJson(ExternalPlaylist playlist)
    {
        var artistName = $"🎵 {char.ToUpper(playlist.Provider[0])}{playlist.Provider.Substring(1)}";
        if (!string.IsNullOrEmpty(playlist.CuratorName))
        {
            artistName += $" {playlist.CuratorName}";
        }
        
        var artistId = $"curator-{playlist.Provider}-{playlist.CuratorName?.ToLowerInvariant().Replace(" ", "-") ?? "unknown"}";
        
        var album = new Dictionary<string, object>
        {
            ["id"] = playlist.Id,
            ["name"] = playlist.Name,
            ["artist"] = artistName,
            ["artistId"] = artistId,
            ["genre"] = "Playlist",
            ["songCount"] = playlist.TrackCount,
            ["duration"] = playlist.Duration,
            ["created"] = playlist.CreatedDate.HasValue ? playlist.CreatedDate.Value.ToUniversalTime().ToString("o") : System.DateTime.UtcNow.ToString("o")
        };
        
        if (playlist.CreatedDate.HasValue)
        {
            album["year"] = playlist.CreatedDate.Value.Year;
        }
        
        if (!string.IsNullOrEmpty(playlist.CoverUrl))
        {
            album["coverArt"] = playlist.Id;
        }
        
        return album;
    }
    
    /// <summary>
    /// Converts an ExternalPlaylist to an XML element representing an album.
    /// Playlists are represented as albums with genre "Playlist" and artist "🎵 {Provider} {Curator}".
    /// </summary>
    private XElement ConvertPlaylistToAlbumXml(ExternalPlaylist playlist, XNamespace ns)
    {
        var artistName = $"🎵 {char.ToUpper(playlist.Provider[0])}{playlist.Provider.Substring(1)}";
        if (!string.IsNullOrEmpty(playlist.CuratorName))
        {
            artistName += $" {playlist.CuratorName}";
        }
        
        var artistId = $"curator-{playlist.Provider}-{playlist.CuratorName?.ToLowerInvariant().Replace(" ", "-") ?? "unknown"}";
        
        var album = new XElement(ns + "album",
            new XAttribute("id", playlist.Id),
            new XAttribute("name", playlist.Name),
            new XAttribute("artist", artistName),
            new XAttribute("artistId", artistId),
            new XAttribute("genre", "Playlist"),
            new XAttribute("songCount", playlist.TrackCount),
            new XAttribute("duration", playlist.Duration),
            new XAttribute("created", playlist.CreatedDate.HasValue ? playlist.CreatedDate.Value.ToUniversalTime().ToString("o") : System.DateTime.UtcNow.ToString("o"))
        );
        
        if (playlist.CreatedDate.HasValue)
        {
            album.Add(new XAttribute("year", playlist.CreatedDate.Value.Year));
        }
        
        if (!string.IsNullOrEmpty(playlist.CoverUrl))
        {
            album.Add(new XAttribute("coverArt", playlist.Id));
        }
        
        return album;
    }
}

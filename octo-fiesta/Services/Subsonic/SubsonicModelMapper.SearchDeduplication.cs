using System.Text;
using System.Xml.Linq;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;

namespace octo_fiesta.Services.Subsonic;

public partial class SubsonicModelMapper
{
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
            return MergeSearchResultsJsonWithMappings(localSongs, localAlbums, localArtists, externalResult, externalPlaylists, mappings);
        }
        else
        {
            return MergeSearchResultsXmlWithMappings(localSongs, localAlbums, localArtists, externalResult, externalPlaylists, mappings);
        }
    }

    private (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResultsJsonWithMappings(
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
        // Playlists surfaced as albums. Providers (notably Qobuz) sometimes return several
        // near-duplicate yearly snapshots of the same curated list, all sharing the same
        // (provider, name, curator) tuple. Collapse those before emitting.
        foreach (var playlist in DeduplicateExternalPlaylists(externalPlaylists))
        {
            mergedAlbums.Add(ConvertPlaylistToAlbumJson(playlist));
        }

        var localArtistNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artist in localArtists)
        {
            if (artist is Dictionary<string, object> dict && dict.TryGetValue("name", out var nameObj))
            {
                localArtistNames.Add(StringNormalizer.CreateArtistComparisonKey(nameObj?.ToString()));
            }
        }

        var mergedArtists = localArtists.ToList();
        foreach (var externalArtist in externalResult.Artists)
        {
            if (!localArtistNames.Contains(StringNormalizer.CreateArtistComparisonKey(externalArtist.Name)))
            {
                mergedArtists.Add(_responseBuilder.ConvertArtistToJson(externalArtist));
            }
        }

        return (mergedSongs, mergedAlbums, mergedArtists);
    }

    private (List<object> MergedSongs, List<object> MergedAlbums, List<object> MergedArtists) MergeSearchResultsXmlWithMappings(
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

        var localArtistNamesXml = new HashSet<string>(StringComparer.Ordinal);
        var mergedArtists = new List<object>();

        foreach (var artist in localArtists.Cast<XElement>())
        {
            var name = artist.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                localArtistNamesXml.Add(StringNormalizer.CreateArtistComparisonKey(name));
            }
            artist.Name = ns + "artist";
            mergedArtists.Add(artist);
        }

        foreach (var artist in externalResult.Artists)
        {
            if (!localArtistNamesXml.Contains(StringNormalizer.CreateArtistComparisonKey(artist.Name)))
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
        foreach (var playlist in DeduplicateExternalPlaylists(externalPlaylists))
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
        var titleKey = StringNormalizer.CreateSongTitleDedupeKey(title);
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
    /// Collapses external playlists that share the same normalized
    /// (provider, name, curator) tuple. Preserves insertion order and keeps the
    /// first occurrence. Entries with an empty/whitespace <see cref="ExternalPlaylist.Name"/>
    /// are passed through untouched so we never silently merge unidentified rows.
    /// </summary>
    private static IEnumerable<ExternalPlaylist> DeduplicateExternalPlaylists(
        IEnumerable<ExternalPlaylist> playlists)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var playlist in playlists)
        {
            if (string.IsNullOrWhiteSpace(playlist.Name))
            {
                yield return playlist;
                continue;
            }

            var providerKey = NormalizePlaylistKeyPart(playlist.Provider);
            var nameKey = NormalizePlaylistKeyPart(playlist.Name);
            var curatorKey = NormalizePlaylistKeyPart(playlist.CuratorName ?? string.Empty);
            var key = providerKey + "\u0001" + nameKey + "\u0001" + curatorKey;

            if (seen.Add(key))
            {
                yield return playlist;
            }
        }
    }

    /// <summary>
    /// Normalizes a single component of the playlist dedupe key. Builds on top of
    /// <see cref="StringNormalizer.CreateComparisonKey"/> but additionally trims and
    /// collapses runs of internal whitespace so trivial padding differences in the
    /// provider's response don't keep otherwise identical playlists separate.
    /// </summary>
    private static string NormalizePlaylistKeyPart(string? input)
    {
        var normalized = StringNormalizer.CreateComparisonKey(input);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length);
        var prevSpace = true;
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }

        if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
        {
            sb.Length -= 1;
        }
        return sb.ToString();
    }
    
}

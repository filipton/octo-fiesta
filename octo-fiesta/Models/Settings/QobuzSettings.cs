namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the Qobuz downloader and metadata service
/// </summary>
public class QobuzSettings
{
    /// <summary>
    /// Qobuz user authentication token
    /// Obtained from browser's localStorage after logging into play.qobuz.com
    /// </summary>
    public string? UserAuthToken { get; set; }
    
    /// <summary>
    /// Qobuz user ID
    /// Obtained from browser's localStorage after logging into play.qobuz.com
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// Preferred audio quality: FLAC, MP3_320, MP3_128
    /// If not specified or unavailable, the highest available quality will be used.
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Optional Qobuz App ID. When set together with <see cref="AppSecret"/>,
    /// the service will skip dynamically scraping play.qobuz.com's bundle.js
    /// and use these values directly. If left empty, the bundle scraper is 
    /// used as a fallback.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Optional Qobuz App Secret. Used together with <see cref="AppId"/> to
    /// sign requests (e.g. track/getFileUrl). When provided, bundle scraping
    /// is bypassed entirely.
    /// </summary>
    public string? AppSecret { get; set; }
}

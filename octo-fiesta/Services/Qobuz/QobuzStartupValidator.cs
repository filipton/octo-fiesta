using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Validation;

namespace octo_fiesta.Services.Qobuz;

/// <summary>
/// Validates Qobuz credentials at startup
/// </summary>
public class QobuzStartupValidator : BaseStartupValidator
{
    private readonly IOptions<QobuzSettings> _qobuzSettings;

    public override string ServiceName => "Qobuz";

    public QobuzStartupValidator(IOptions<QobuzSettings> qobuzSettings, HttpClient httpClient)
        : base(httpClient)
    {
        _qobuzSettings = qobuzSettings;
    }

    public override async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var userAuthToken = _qobuzSettings.Value.UserAuthToken;
        var userId = _qobuzSettings.Value.UserId;
        var quality = _qobuzSettings.Value.Quality;

        if (string.IsNullOrWhiteSpace(userAuthToken))
        {
            WriteStatus("Qobuz UserAuthToken", "NOT CONFIGURED", ConsoleColor.Red);
            WriteDetail("Set the Qobuz__UserAuthToken environment variable");
            return ValidationResult.NotConfigured("Qobuz UserAuthToken not configured");
        }

        WriteStatus("Qobuz UserAuthToken", MaskSecret(userAuthToken), ConsoleColor.Cyan);
        if (string.IsNullOrWhiteSpace(userId))
        {
            WriteStatus("Qobuz UserId", "not set (optional)", ConsoleColor.DarkGray);
        }
        else
        {
            WriteStatus("Qobuz UserId", userId, ConsoleColor.Cyan);
        }
        WriteStatus("Qobuz Quality", quality ?? "auto (highest available)", ConsoleColor.Cyan);

        var configuredAppId = _qobuzSettings.Value.AppId;
        var configuredAppSecret = _qobuzSettings.Value.AppSecret;
        var hasAppId = !string.IsNullOrWhiteSpace(configuredAppId);
        var hasAppSecret = !string.IsNullOrWhiteSpace(configuredAppSecret);

        if (hasAppId && hasAppSecret)
        {
            WriteStatus("Qobuz AppId", configuredAppId!, ConsoleColor.Cyan);
            WriteStatus("Qobuz AppSecret", MaskSecret(configuredAppSecret!), ConsoleColor.Cyan);
            WriteDetail("Bundle scraping disabled (using configured credentials)");
        }
        else if (hasAppId ^ hasAppSecret)
        {
            WriteStatus("Qobuz AppId/AppSecret", "PARTIAL", ConsoleColor.Yellow);
            WriteDetail("Set BOTH Qobuz__AppId and Qobuz__AppSecret to skip bundle scraping");
        }
        else
        {
            WriteStatus("Qobuz AppId/AppSecret", "auto (extracted from bundle.js)", ConsoleColor.Cyan);
            WriteDetail("Set Qobuz__AppId and Qobuz__AppSecret");
        }

        // Validate token by calling Qobuz API
        await ValidateQobuzTokenAsync(userAuthToken, userId, configuredAppId, cancellationToken);

        return ValidationResult.Success("Qobuz validation completed");
    }

    private async Task ValidateQobuzTokenAsync(string userAuthToken, string? userId, string? configuredAppId, CancellationToken cancellationToken)
    {
        const string fieldName = "Qobuz credentials";

        // Use the configured AppId if supplied; otherwise fall back to the long-standing
        // public Qobuz web player AppId. Using the wrong AppId will cause the API to
        // return 401 even for a perfectly valid user_auth_token.
        var appId = string.IsNullOrWhiteSpace(configuredAppId) ? "798273057" : configuredAppId!.Trim();

        try
        {
            // Probe endpoint: user/get works without needing user_id (the token identifies
            // the user). This is what QobuzDownloaderX-style clients rely on.
            var apiUrl = $"https://www.qobuz.com/api.json/0.2/user/get?app_id={appId}";

            // Try to validate with a simple API call
            // We'll use the user favorites endpoint which requires authentication.
            // Honour a configured App ID so tokens issued by a non-web-player app validate.
            var appId = _qobuzSettings.Value.AppId;
            if (string.IsNullOrWhiteSpace(appId)) appId = "798273057"; // Fallback app ID
            var apiUrl = $"https://www.qobuz.com/api.json/0.2/favorite/getUserFavorites?user_id={userId}&app_id={appId}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("X-App-Id", appId);
            request.Headers.Add("X-User-Auth-Token", userAuthToken);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:83.0) Gecko/20100101 Firefox/83.0");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    WriteStatus(fieldName, "INVALID", ConsoleColor.Red);
                    WriteDetail($"Token rejected by Qobuz (AppId={appId}). Token may be expired, or AppId/AppSecret pair is wrong.");
                }
                else
                {
                    WriteStatus(fieldName, $"HTTP {(int)response.StatusCode}", ConsoleColor.Yellow);
                    WriteDetail("Unable to verify credentials");
                }
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!string.IsNullOrEmpty(json) && !json.Contains("\"error\""))
            {
                WriteStatus(fieldName, "VALID", ConsoleColor.Green);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    WriteDetail($"User ID: {userId}");
                }
            }
            else
            {
                WriteStatus(fieldName, "INVALID", ConsoleColor.Red);
                WriteDetail("Unexpected response from Qobuz");
            }
        }
        catch (TaskCanceledException)
        {
            WriteStatus(fieldName, "TIMEOUT", ConsoleColor.Yellow);
            WriteDetail("Could not reach Qobuz within 10 seconds");
        }
        catch (HttpRequestException ex)
        {
            WriteStatus(fieldName, "UNREACHABLE", ConsoleColor.Yellow);
            WriteDetail(ex.Message);
        }
        catch (Exception ex)
        {
            WriteStatus(fieldName, "ERROR", ConsoleColor.Red);
            WriteDetail(ex.Message);
        }
    }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
using User = Examen2.Data.User;

namespace Examen2.Services;

public class EmailService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string BaseFunctionsUrl = "https://ebndevmaperaoshfrblu.supabase.co/functions/v1";
    private const string PublishableKey = "sb_publishable_JrSXPoMHeeJYEe3jgJQ5ZQ_OGWoE-A1";

    public async Task<(bool Success, bool Requires2FA, string Token, User? User, string ErrorDetail)> LoginAsync(string identifier, string password)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseFunctionsUrl}/auth-login");
            request.Headers.Add("apikey", PublishableKey);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { identifier, password }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string error = root.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            if (!success) return (false, false, string.Empty, null, error);

            bool requires2FA = root.TryGetProperty("requires2FA", out var r) && r.GetBoolean();
            string token = root.TryGetProperty("token", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            User? user = root.TryGetProperty("user", out var u)
                ? JsonConvert.DeserializeObject<User>(u.GetRawText())
                : null;

            return (true, requires2FA, token, user, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, false, string.Empty, null, ex.Message);
        }
    }

    public async Task<(bool Success, string ErrorDetail)> Send2FACodeAsync(string toEmail)
    {
        var (success, _, error) = await Post2FAActionAsync(new { action = "send", email = toEmail });
        return (success, error);
    }

    public async Task<(bool Success, string Token, string ErrorDetail)> Verify2FACodeAsync(string toEmail, string code)
    {
        return await Post2FAActionAsync(new { action = "verify", email = toEmail, code });
    }

    private async Task<(bool Success, string Token, string ErrorDetail)> Post2FAActionAsync(object payload)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseFunctionsUrl}/auth-2fa");
            request.Headers.Add("apikey", PublishableKey);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string token = root.TryGetProperty("token", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            string error = root.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;

            return (success, token, error);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
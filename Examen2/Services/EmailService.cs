using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Examen2.Services;

public class EmailService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string FunctionUrl = "https://ebndevmaperaoshfrblu.supabase.co/functions/v1/auth-2fa";
    private const string PublishableKey = "sb_publishable_JrSXPoMHeeJYEe3jgJQ5ZQ_OGWoE-A1";

    public async Task<(bool Success, string ErrorDetail)> Send2FACodeAsync(string toEmail)
    {
        return await PostActionAsync(new { action = "send", email = toEmail });
    }

    public async Task<(bool Success, string ErrorDetail)> Verify2FACodeAsync(string toEmail, string code)
    {
        return await PostActionAsync(new { action = "verify", email = toEmail, code });
    }

    private async Task<(bool Success, string ErrorDetail)> PostActionAsync(object payload)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, FunctionUrl);
            request.Headers.Add("apikey", PublishableKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            bool success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            string error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : string.Empty;

            return (success, error ?? string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PCSetupCopier;

// Public GitHub Releases over HTTPS; no GitHub CLI or credentials required.
internal sealed class GitHubReleaseClient : IDisposable
{
    private const string ApiRoot = "https://api.github.com/repos/chgao0o0/PCSetupCopier/";
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    internal GitHubReleaseClient()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCSetupCopier-Updater/1.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    internal async Task<JsonDocument> GetJsonAsync(string endpoint, CancellationToken ct)
    {
        using var response = await http.GetAsync(ApiRoot + endpoint, ct);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    internal async Task DownloadAssetAsync(long assetId, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + "releases/assets/" + assetId);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureSuccess(response);
        string? mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is "application/json" or "application/vnd.github+json" or "text/html")
            throw new InvalidDataException("GitHubが更新ZIP以外のデータを返しました。Releaseを確認してください。");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, ct);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("GitHubのリポジトリまたはReleaseが見つかりません。");
        if (response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHubのアクセス制限に達した可能性があります。少し時間をおいて再試行してください。");
        throw new HttpRequestException("GitHubからの取得に失敗しました (HTTP " +
            (int)response.StatusCode + " " + response.ReasonPhrase + ")。");
    }

    public void Dispose() => http.Dispose();
}

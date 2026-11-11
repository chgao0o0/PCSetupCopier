using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PCSetupCopier;

// Fetches private release assets using GitHub's REST API; no gh.exe required.
internal sealed class GitHubReleaseClient : IDisposable
{
    private const string ApiRoot = "https://api.github.com/repos/chgao0o0/PCSetupCopier/";
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    internal GitHubReleaseClient()
    {
        string? token = GitHubTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = RequestToken();
            if (string.IsNullOrWhiteSpace(token))
                throw new OperationCanceledException("GitHubの認証がキャンセルされました。");
            // The API request validates this credential before it is persisted.
            pendingToken = token;
        }
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCSetupCopier-Updater/1.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private string? pendingToken;
    private bool validated;

    internal async Task<JsonDocument> GetJsonAsync(string endpoint, CancellationToken ct)
    {
        using var response = await http.GetAsync(ApiRoot + endpoint, ct);
        await EnsureSuccessAsync(response, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    internal async Task DownloadAssetAsync(long assetId, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + "releases/assets/" + assetId);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        var mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is "application/json" or "application/vnd.github+json")
            throw new InvalidDataException("GitHubがZIPではなくJSONを返しました。Releaseの配布ファイルを確認してください。");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            if (!validated && pendingToken != null)
            {
                GitHubTokenStore.Write(pendingToken);
                pendingToken = null;
            }
            validated = true;
            return;
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            GitHubTokenStore.Delete();
            throw new InvalidOperationException(
                "GitHubの認証に失敗しました。トークンを削除しました。更新を再実行して、リポジトリのContents: Read権限があるトークンを登録してください。");
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("GitHubのリポジトリまたはReleaseにアクセスできません。アカウントの閲覧権限と公開済みReleaseを確認してください。");
        string reason = response.ReasonPhrase ?? "";
        // Never include response body or secrets in error messages.
        throw new HttpRequestException("GitHubからの取得に失敗しました (HTTP " + (int)response.StatusCode + " " + reason + ")。");
    }

    private static string? RequestToken()
    {
        using var dialog = new Form
        {
            Text = "GitHub 認証（初回のみ）",
            Width = 490, Height = 245,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false
        };
        var help = new Label
        {
            Text = "非公開GitHubリポジトリから更新します。\n" +
                   "初回のみGitHubのアクセストークンが必要です。\n" +
                   "対象: PCSetupCopier / 権限: Contents (読み取り専用)\n" +
                   "入力内容はWindows資格情報に保存されます。",
            AutoSize = false, Left = 18, Top = 16, Width = 445, Height = 100
        };
        var field = new TextBox
        {
            Left = 18, Top = 119, Width = 440,
            UseSystemPasswordChar = true,
            PlaceholderText = "github_pat_...",
            MaxLength = 2048
        };
        var accept = new Button { Text = "保存して続ける", Left = 301, Top = 158, Width = 157, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "キャンセル", Left = 185, Top = 158, Width = 108, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([help, field, accept, cancel]);
        dialog.AcceptButton = accept;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => field.Focus();
        if (dialog.ShowDialog() != DialogResult.OK) return null;
        return field.Text.Trim();
    }

    public void Dispose() => http.Dispose();
}

// Generic credentials are protected by Windows and scoped to the signed-in user.
internal static class GitHubTokenStore
{
    private const string Target = "PCSetupCopier:GitHubReleaseRead";
    private const uint Generic = 1;
    private const uint LocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    internal static string? Read()
    {
        if (!CredRead(Target, Generic, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    internal static void Write(string token)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(token);
        if (bytes.Length > 2560)
            throw new InvalidOperationException("GitHubトークンが長すぎます。");
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = Generic, TargetName = Target, UserName = "GitHub",
                CredentialBlob = blob, CredentialBlobSize = (uint)bytes.Length,
                Persist = LocalMachine
            };
            if (!CredWrite(ref credential, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows資格情報にトークンを保存できません。");
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
            Array.Clear(bytes);
        }
    }

    internal static void Delete() => CredDelete(Target, Generic, 0);
}

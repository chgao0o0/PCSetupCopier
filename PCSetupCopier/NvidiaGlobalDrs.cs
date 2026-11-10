using System.Runtime.InteropServices;
using System.Text.Json;

namespace PCSetupCopier;

// Native NVIDIA NVAPI DRS V1. All IDs, packing and values are from NVIDIA public NVAPI headers.
// Only the BASE (global) profile is written. Never modify per-game profiles or registry directly.
internal static class NvidiaGlobalDrs
{
    internal sealed record Option(string Label, uint Value);
    internal sealed record Setting(string Key, string Title, uint Id, IReadOnlyList<Option> Options);
    internal sealed record State(string Key, uint? Value, bool IsExplicit, bool Available = true);
    internal sealed record Change(string Key, uint? Before, uint After, bool BeforeExplicit);
    private sealed record Backup(DateTimeOffset Timestamp, List<Change> Changes);

    internal static readonly IReadOnlyList<Setting> Settings =
    [
        new("power", "電源管理モード", 0x1057EB71, [
            new("適応（Adaptive）", 0), new("最大パフォーマンス", 1),
            new("ドライバー管理", 2), new("一貫した性能を優先", 3),
            new("省電力優先", 4), new("最適電力", 5)]),
        new("vsync", "垂直同期", 0x00A879CF, [
            new("アプリに任せる", 0x60925292), new("オフを強制", 0x08416747),
            new("オンを強制", 0x47814940)]),
        new("texture", "テクスチャフィルタリング品質", 0x00CE2691, [
            new("高画質", 0xfffffff6), new("画質", 0),
            new("パフォーマンス", 10), new("ハイパフォーマンス", 20)]),
        new("fps", "最大フレームレート", 0x10835002, [
            new("制限なし", 0), new("60 FPS", 60), new("120 FPS", 120),
            new("141 FPS", 141), new("143 FPS", 143), new("144 FPS", 144),
            new("160 FPS", 160), new("162 FPS", 162),
            new("164 FPS", 164), new("165 FPS", 165), new("180 FPS", 180),
            new("240 FPS", 240), new("300 FPS", 300), new("360 FPS", 360),
            new("500 FPS", 500)]),
        new("anisotropic_mode", "異方性フィルタリング：制御方法", 0x10D2BB16, [
            new("アプリケーションに任せる", 0), new("ドライバー側で指定", 1),
            new("条件付き", 2)]),
        new("anisotropic_level", "異方性フィルタリング：倍率", 0x101E61A9, [
            new("1倍（標準）", 1), new("2倍", 2), new("4倍", 4),
            new("8倍", 8), new("16倍", 16)]),
        new("aa_mode", "アンチエイリアシング：モード", 0x107EFC5B, [
            new("アプリに任せる", 0), new("ドライバーで上書き", 1),
            new("ゲームの設定を強化", 2)]),
        new("aa_gamma", "アンチエイリアシング：ガンマ補正", 0x107D639D, [
            new("オフ", 0), new("対応時にオン", 1), new("常にオン", 2)]),
        new("aa_transparency", "アンチエイリアシング：透過MSAA", 0x10FC2D9C, [
            new("オフ", 0), new("オン", 4)]),
        new("fxaa", "アンチエイリアシング：FXAA", 0x1074C972, [
            new("オフ", 0), new("オン", 1)]),
        new("mfaa", "アンチエイリアシング：MFAA", 0x0098C1AC, [
            new("オフ", 0), new("オン", 1)]),
        new("ogl_threads", "OpenGL：スレッド最適化", 0x20C1221E, [
            new("自動", 0), new("オン", 1), new("オフ", 2)]),
        new("ogl_triple", "OpenGL：トリプルバッファリング", 0x20FDD1F9, [
            new("オフ", 0), new("オン", 1)]),
        new("shader_cache", "シェーダーキャッシュ", 0x00198FFF, [
            new("オフ", 0), new("オン", 1)]),
        new("aniso_sample_opt", "テクスチャ：異方性サンプル最適化", 0x00E73211, [
            new("オフ", 0), new("オン", 1)]),
        new("aniso_filter_opt", "テクスチャ：異方性フィルター最適化", 0x0084CD70, [
            new("オフ", 0), new("オン", 1)]),
        new("trilinear_opt", "テクスチャ：トリリニア最適化", 0x002ECAF2, [
            new("オフ", 0), new("オン", 1)]),
        new("negative_lod", "テクスチャ：ネガティブLODバイアス制限", 0x0019BB68, [
            new("許可（オフ）", 0), new("制限（オン）", 1)]),
        new("prerender", "最大プリレンダーフレーム（旧方式）", 0x007BA09E, [
            new("アプリに任せる", 0), new("1", 1), new("2", 2), new("3", 3), new("4", 4)]),
        new("vr_prerender", "VR：プリレンダーフレーム", 0x10111133, [
            new("アプリに任せる", 0), new("1", 1), new("2", 2), new("3", 3), new("4", 4)]),
        new("preferred_refresh", "優先リフレッシュレート", 0x0064B541, [
            new("アプリに任せる", 0), new("使用可能な最高値", 1)]),
        new("ambient_occlusion", "アンビエントオクルージョン", 0x00667329, [
            new("オフ", 0), new("低", 1), new("中", 2), new("高", 3)]),
        new("vsync_tear", "垂直同期：ティアリング制御", 0x005A375C, [
            new("無効", 0x96861077), new("有効", 0x99941284)]),
        new("ogl_present", "Vulkan / OpenGL表示方法", 0x20D690F8, [
            new("無効を優先", 0), new("有効を優先", 1), new("自動", 2)]),
        new("ogl_quality", "OpenGL：レンダリング品質", 0x20797D6C, [
            new("高画質", 0xfffffff6), new("画質", 0),
            new("パフォーマンス", 10), new("ハイパフォーマンス", 20)]),
        new("aa_samples", "アンチエイリアシング：サンプル数", 0x10D773D2, [
            new("なし", 0), new("2x MSAA", 0xE), new("4x MSAA", 0x10),
            new("8x MSAA", 0x25), new("16x MSAA", 0x1C)]),
        new("ogl_line_gamma", "OpenGL：ラインのガンマ補正", 0x2089BF6C, [
            new("無効", 0x10), new("有効", 0x23)]),
        new("ogl_gdi", "OpenGL：GDI互換性", 0x2072C5A3, [
            new("無効を優先", 0), new("有効を優先", 1), new("自動", 2)]),
        new("ogl_deep_color", "OpenGL：3Dアプリのディープカラー", 0x2097C2F6, [
            new("無効", 0), new("有効", 1)]),
        new("ogl_buffer_flip", "OpenGL：バッファフリッピング", 0x201F619F, [
            new("無効", 0), new("有効", 1)])
    ];

    internal static string BackupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private static string LatestBackup => Path.Combine(BackupFolder, "nvidia-global-latest.json");
    internal static bool CanRestore => File.Exists(LatestBackup);

    // NVDRS_SETTING_V1: DWORD version, 2048 UTF16 chars, five DWORD fields,
    // two 4100-byte unions (NVDRS_BINARY_SETTING valueLength + 4096 bytes); pack(4).
    private const int StructSize = 4 + 4096 + 20 + 4100 * 2;
    private const int IdOffset = 4100, TypeOffset = 4104, CurrentValueOffset = 8220;
    private const int IsExplicitOffset = 4112; // isCurrentPredefined: zero means explicit
    private const int LocationOffset = 4108;
    private const int Version = (1 << 16) | StructSize;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryDelegate(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StatusDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SessionDelegate(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleDelegate(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetProfileDelegate(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetSettingDelegate(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetSettingDelegate(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeleteSettingDelegate(IntPtr session, IntPtr profile, uint id);

    private sealed class Connection : IDisposable
    {
        private readonly IntPtr dll;
        private readonly StatusDelegate unload;
        private readonly HandleDelegate destroy;
        private readonly GetSettingDelegate get;
        private readonly SetSettingDelegate set;
        private readonly DeleteSettingDelegate delete;
        private readonly HandleDelegate save;
        private readonly IntPtr session;
        private readonly IntPtr profile;
        private bool initialized;

        private T Func<T>(QueryDelegate query, uint id) where T : Delegate
        {
            var ptr = query(id);
            if (ptr == IntPtr.Zero)
                throw new NotSupportedException("NVAPI DRS関数に非対応: 0x" + id.ToString("X8"));
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        internal Connection()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("64bit WindowsのNVIDIAドライバーが必要です。");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "nvapi64.dll");
            if (!File.Exists(path))
                throw new NotSupportedException("NVIDIA NVAPIドライバーが見つかりません。");
            dll = NativeLibrary.Load(path);
            try
            {
                IntPtr addr = NativeLibrary.GetExport(dll, "nvapi_QueryInterface");
                var query = Marshal.GetDelegateForFunctionPointer<QueryDelegate>(addr);
                var init = Func<StatusDelegate>(query, 0x0150E828);
                unload = Func<StatusDelegate>(query, 0xD22BDD7E);
                destroy = Func<HandleDelegate>(query, 0xDAD9CFF8);
                get = Func<GetSettingDelegate>(query, 0x73BF8338);
                set = Func<SetSettingDelegate>(query, 0x577DD202);
                delete = Func<DeleteSettingDelegate>(query, 0xE4A26362);
                save = Func<HandleDelegate>(query, 0xFCBC7E14);
                Check(init(), "NvAPI_Initialize");
                initialized = true;
                var create = Func<SessionDelegate>(query, 0x0694D52E);
                Check(create(out session), "DRS_CreateSession");
                Check(Func<HandleDelegate>(query, 0x375DBD6B)(session), "DRS_LoadSettings");
                Check(Func<GetProfileDelegate>(query, 0xDA8466A0)(session, out profile),
                    "DRS_GetBaseProfile");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal State Read(Setting item)
        {
            var data = Marshal.AllocHGlobal(StructSize);
            try
            {
                for (int i = 0; i < StructSize; i++) Marshal.WriteByte(data, i, 0);
                Marshal.WriteInt32(data, Version);
                int result = get(session, profile, item.Id, data);
                if (result == -160) return new State(item.Key, null, false, false);
                Check(result, "DRS_GetSetting " + item.Title);
                if (Marshal.ReadInt32(data, TypeOffset) != 0)
                    throw new InvalidDataException("NVIDIA設定がDWORD形式ではありません: " + item.Title);
                uint value = unchecked((uint)Marshal.ReadInt32(data, CurrentValueOffset));
                // A base-profile override may be reported with BASE_PROFILE_LOCATION.
                // The predefined flag, not the location enum, distinguishes its origin.
                // Incorrectly marking a custom value as inherited would delete it on restore.
                bool explicitValue = Marshal.ReadInt32(data, IsExplicitOffset) == 0;
                return new State(item.Key, value, explicitValue);
            }
            finally { Marshal.FreeHGlobal(data); }
        }

        internal void Set(Setting item, uint value)
        {
            if (!item.Options.Any(o => o.Value == value))
                throw new ArgumentOutOfRangeException(nameof(value));
            var data = Marshal.AllocHGlobal(StructSize);
            try
            {
                for (int i = 0; i < StructSize; i++) Marshal.WriteByte(data, i, 0);
                Marshal.WriteInt32(data, Version);
                Marshal.WriteInt32(data, IdOffset, unchecked((int)item.Id));
                Marshal.WriteInt32(data, TypeOffset, 0);
                Marshal.WriteInt32(data, CurrentValueOffset, unchecked((int)value));
                Check(set(session, profile, data), "DRS_SetSetting " + item.Title);
            }
            finally { Marshal.FreeHGlobal(data); }
        }

        internal void Delete(Setting item) =>
            Check(delete(session, profile, item.Id), "DRS_DeleteProfileSetting " + item.Title);

        internal void Save() => Check(save(session), "DRS_SaveSettings");

        public void Dispose()
        {
            try { if (session != IntPtr.Zero) destroy?.Invoke(session); } catch { }
            try { if (initialized) unload?.Invoke(); } catch { }
            if (dll != IntPtr.Zero) NativeLibrary.Free(dll);
        }
    }

    private static void Check(int status, string operation)
    {
        if (status != 0)
            throw new InvalidOperationException(operation + "が失敗しました（NVAPI " + status + "）。");
    }

    internal static IReadOnlyList<State> ReadAll()
    {
        using var connection = new Connection();
        var states = new List<State>();
        foreach (var item in Settings)
        {
            try { states.Add(connection.Read(item)); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                // An unavailable setting must not prevent reading other settings.
                states.Add(new State(item.Key, null, false, false));
            }
        }
        return states;
    }

    internal sealed record Compatibility(string Key, bool CanStage, string Reason);

    // Pure compatibility check used before importing values from another PC.
    // Unknown overrides are blocked even if the requested value is known,
    // because the original might not be safely restorable on failure.
    internal static IReadOnlyList<Compatibility> AssessCompatibility(
        IReadOnlyDictionary<string, uint> requested, IEnumerable<State> driverStates)
    {
        var installed = driverStates.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var catalog = Settings.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var result = new List<Compatibility>();
        foreach (var (key, value) in requested)
        {
            if (!catalog.TryGetValue(key, out var definition))
            {
                result.Add(new(key, false, "この版で未対応の設定"));
                continue;
            }
            if (!definition.Options.Any(x => x.Value == value))
            {
                result.Add(new(key, false, "未対応の設定値"));
                continue;
            }
            if (!installed.TryGetValue(key, out var actual) || !actual.Available)
            {
                result.Add(new(key, false, "このPCのドライバーでは取得できない"));
                continue;
            }
            if (actual.IsExplicit && (actual.Value is not uint existing ||
                !definition.Options.Any(x => x.Value == existing)))
            {
                result.Add(new(key, false, "不明な既存値があるため安全に復元できない"));
                continue;
            }
            if (actual.Value == value)
            {
                result.Add(new(key, false, "現在の設定と同じ"));
                continue;
            }
            result.Add(new(key, true, "変更候補にできる（実機の書込検証は別途必要）"));
        }
        return result;
    }

    internal static bool VerifyCompatibilityContract()
    {
        var desired = new Dictionary<string, uint> {
            ["power"] = 1, ["vsync"] = 0x60925292,
            ["texture"] = 10, ["unknown"] = 1
        };
        var states = new State[] {
            new("power", 5, false),
            new("vsync", 0x60925292, true),
            new("texture", 0xDEADBEEF, true)
        };
        var outcome = AssessCompatibility(desired, states).ToDictionary(x => x.Key);
        return outcome["power"].CanStage &&
            !outcome["vsync"].CanStage &&
            !outcome["texture"].CanStage &&
            !outcome["unknown"].CanStage &&
            !AssessCompatibility(new Dictionary<string,uint> { ["power"] = 1 },
                [new State("power", null, false, false)])[0].CanStage;
    }

    internal static string Label(Setting item, uint? value)
    {
        if (!value.HasValue) return "未登録／取得不可";
        return item.Options.FirstOrDefault(o => o.Value == value.Value)?.Label ??
               "未対応値 0x" + value.Value.ToString("X8");
    }

    internal static IReadOnlyList<Change> Preview(IEnumerable<(string Key, uint Value)> choices)
    {
        var changes = choices.ToArray();
        if (changes.Length < 1 || changes.Length > Settings.Count || changes.Select(x => x.Key).Distinct().Count() != changes.Length)
            throw new ArgumentException("変更対象が不正です。");
        using var con = new Connection();
        var result = new List<Change>();
        foreach (var (key, value) in changes)
        {
            var setting = Settings.SingleOrDefault(x => x.Key == key) ??
                throw new InvalidDataException("未対応のNVIDIA設定です。");
            if (!setting.Options.Any(x => x.Value == value))
                throw new InvalidDataException("NVIDIA設定値が不正です。");
            var state = con.Read(setting);
            if (!state.Available)
                throw new NotSupportedException(setting.Title +
                    "はこのドライバーで取得できないため、自動変更しません。");
            if (state.Value == value) continue;
            // An unrecognized override cannot be recreated safely after a failed apply.
            // Never alter it until the driver-specific value can be validated.
            if (state.IsExplicit && (state.Value is not uint previous ||
                !setting.Options.Any(x => x.Value == previous)))
                throw new NotSupportedException(setting.Title +
                    "に未知の既存設定値があります。安全な復元ができないため変更しません。");
            result.Add(new Change(key, state.Value, value, state.IsExplicit));
        }
        return result;
    }

    private static void Recheck(Connection con, IEnumerable<Change> changes)
    {
        foreach (var change in changes)
        {
            var setting = Settings.Single(x => x.Key == change.Key);
            var now = con.Read(setting);
            if (!now.Available || now.Value != change.Before || now.IsExplicit != change.BeforeExplicit)
                throw new InvalidOperationException("NVIDIA設定が確認中に変更されました: " + setting.Title);
        }
    }

    internal static string Apply(IReadOnlyList<Change> changes)
    {
        if (changes.Count == 0) return "変更はありません。";
        if (changes.Count > Settings.Count || changes.Select(x => x.Key).Distinct().Count() != changes.Count)
            throw new InvalidDataException("変更内容が不正です。");
        using var con = new Connection();
        Recheck(con, changes);
        var saved = new Backup(DateTimeOffset.Now, changes.ToList());
        Directory.CreateDirectory(BackupFolder);
        string json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        string archive = Path.Combine(BackupFolder, "nvidia-global-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".json");
        File.WriteAllText(archive, json);
        File.WriteAllText(LatestBackup, json);
        bool saveAttempted = false;
        try
        {
            foreach (var item in changes)
                con.Set(Settings.Single(x => x.Key == item.Key), item.After);
            saveAttempted = true;
            con.Save();
        }
        catch (Exception failure)
        {
            // A failing SaveSettings is not proof that *nothing* was written.
            // If the driver save was attempted, restore the captured values.
            if (!saveAttempted) throw;
            try { TryRollback(changes); }
            catch (Exception rollback) {
                throw new AggregateException(
                    "NVIDIAの保存と自動復元に失敗しました。バックアップを確認してください。",
                    failure, rollback);
            }
            throw new InvalidOperationException(
                "NVIDIA設定の保存失敗後、元の値への復元を試みました: " + failure.Message,
                failure);
        }
        try
        {
            using var verify = new Connection();
            foreach (var change in changes)
                if (verify.Read(Settings.Single(x => x.Key == change.Key)).Value != change.After)
                    throw new InvalidOperationException("NVAPI書き込み後の検証が一致しません: " + change.Key);
        }
        catch (Exception failure)
        {
            try { TryRollback(changes); }
            catch (Exception rollback) { throw new AggregateException("NVIDIA設定と復元に失敗しました。バックアップを確認してください。", failure, rollback); }
            throw new InvalidOperationException("NVIDIA設定の検証が失敗したため復元しました: " + failure.Message, failure);
        }
        return changes.Count + "件のNVIDIAグローバル設定を適用し、再取得で確認しました。";
    }

    private static void TryRollback(IReadOnlyList<Change> changes)
    {
        using var con = new Connection();
        foreach (var change in changes)
        {
            var item = Settings.Single(x => x.Key == change.Key);
            if (change.BeforeExplicit && change.Before.HasValue)
                con.Set(item, change.Before.Value);
            else
                con.Delete(item);
        }
        con.Save();
        using var checkedConnection = new Connection();
        foreach (var change in changes)
        {
            var actual = checkedConnection.Read(Settings.Single(x => x.Key == change.Key));
            if (change.BeforeExplicit &&
                (actual.Value != change.Before || !actual.IsExplicit))
                throw new InvalidOperationException(
                    "NVIDIA設定の復元後に読み取り値が一致しません: " + change.Key);
        }
    }

    internal static string Restore()
    {
        if (!CanRestore) throw new FileNotFoundException("NVIDIAバックアップがありません。");
        if (new FileInfo(LatestBackup).Length > 50_000)
            throw new InvalidDataException("NVIDIAバックアップのサイズが不正です。");
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(LatestBackup))
            ?? throw new InvalidDataException("NVIDIAバックアップの形式が不正です。");
        if (backup.Changes.Count < 1 || backup.Changes.Count > Settings.Count ||
            backup.Changes.Select(x => x.Key).Distinct().Count() != backup.Changes.Count)
            throw new InvalidDataException("復元データが不正です。");
        using var con = new Connection();
        foreach (var change in backup.Changes)
        {
            var item = Settings.SingleOrDefault(x => x.Key == change.Key)
                ?? throw new InvalidDataException("未知のNVIDIA設定です。");
            if (con.Read(item).Value != change.After)
                throw new InvalidOperationException("バックアップ後に設定が変更されています: " + item.Title);
            if (change.BeforeExplicit && change.Before.HasValue)
                con.Set(item, change.Before.Value);
            else
                con.Delete(item);
        }
        con.Save();
        using var verify = new Connection();
        foreach (var change in backup.Changes)
        {
            var actual = verify.Read(Settings.Single(x => x.Key == change.Key));
            if (change.BeforeExplicit &&
                (actual.Value != change.Before || !actual.IsExplicit))
                throw new InvalidOperationException(
                    "復元後の読み取り検証に失敗しました: " + change.Key);
        }
        return backup.Changes.Count + "件のNVIDIAグローバル設定を元に戻しました。";
    }

    internal static bool VerifyContract() =>
        StructSize == 12320 && CurrentValueOffset == 8220 &&
        Settings.Count >= 30 &&
        Settings.Select(x => x.Id).Distinct().Count() == Settings.Count &&
        Settings.Select(x => x.Key).Distinct().Count() == Settings.Count &&
        VerifyCompatibilityContract() &&
        Settings.All(x => x.Options.Select(y => y.Value).Distinct().Count() == x.Options.Count);
}

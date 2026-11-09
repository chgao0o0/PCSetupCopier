namespace PCSetupCopier;

// Common capability contract. An item must explicitly opt into write support.
internal enum SettingAccess { ReadOnly, ApplyAndRestore }
internal sealed record SettingCapability(
    string Id, string DisplayName, SettingAccess Access,
    bool Supported, string Reason);

internal sealed record SettingChange(
    string Id, string Name, string CurrentValue, string TargetValue);

internal sealed record SettingPlan(
    IReadOnlyList<SettingChange> Changes,
    IReadOnlyList<string> Blocked)
{
    public bool CanApply => Changes.Count > 0 && Blocked.Count == 0;
}

internal interface ISettingHandler
{
    string Id { get; }
    SettingCapability GetCapability();
    bool TryPrepare(SettingResult source, out string normalized, out string reason);
    string ReadValue();
    string Apply(string normalized);
    bool CanRestore { get; }
    string Restore();
}

// Registered but unsupported items are never silently written.
internal sealed class ReadOnlySettingHandler(string id, string displayName) : ISettingHandler
{
    public string Id => id;
    public SettingCapability GetCapability() =>
        new(id, displayName, SettingAccess.ReadOnly, false,
            "この項目は診断・比較のみ対応しています。安全な適用・復元方法は未実装です。");
    public bool TryPrepare(SettingResult source, out string normalized, out string reason)
    {
        normalized = "";
        reason = GetCapability().Reason;
        return false;
    }
    public string ReadValue() => "未対応";
    public string Apply(string normalized) => throw new NotSupportedException(GetCapability().Reason);
    public bool CanRestore => false;
    public string Restore() => throw new NotSupportedException(GetCapability().Reason);
}

internal sealed class MouseSettingHandler : ISettingHandler
{
    public string Id => "mouse_acceleration";
    public SettingCapability GetCapability()
    {
        bool supported = OperatingSystem.IsWindowsVersionAtLeast(10);
        return new(Id, "マウス加速", SettingAccess.ApplyAndRestore, supported,
            supported ? "Windows 10/11: SPI_GETMOUSE・SPI_SETMOUSEで取得、保存、検証、復元します。"
                      : "Windows 10以降が必要です。");
    }
    public bool TryPrepare(SettingResult source, out string normalized, out string reason)
    {
        normalized = "";
        reason = "";
        if (!GetCapability().Supported) { reason = GetCapability().Reason; return false; }
        if (source.Id != Id || source.Status != "取得済み" ||
            !SettingsApplier.TryParse(source.Value, out var requested))
        {
            reason = "コピー元JSONのマウス設定が未取得、または形式が不正です。";
            return false;
        }
        normalized = Format(requested);
        return true;
    }
    public string ReadValue() => Format(SettingsApplier.Read());
    public string Apply(string normalized)
    {
        if (!SettingsApplier.TryParse(normalized, out var values))
            throw new InvalidDataException("適用するマウス設定が不正です。");
        return SettingsApplier.Apply(values);
    }
    public bool CanRestore => SettingsApplier.CanRestore;
    public string Restore() => SettingsApplier.Restore();
    private static string Format(int[] v) => $"Threshold1={v[0]} / Threshold2={v[1]} / Accel={v[2]}";
}

internal static class SettingEngine
{
    private static readonly IReadOnlyDictionary<string, ISettingHandler> Handlers =
        new ISettingHandler[]
        {
            new ReadOnlySettingHandler("refresh_rate", "リフレッシュレート"),
            new ReadOnlySettingHandler("game_mode", "ゲームモード"),
            new ReadOnlySettingHandler("power", "電源設定"),
            new ReadOnlySettingHandler("graphics", "Windowsグラフィック設定"),
            new ReadOnlySettingHandler("capture", "ゲーム録画"),
            new ReadOnlySettingHandler("fps_limit", "FPS制限"),
            new MouseSettingHandler(),
            new ReadOnlySettingHandler("hags", "HAGS"),
            new ReadOnlySettingHandler("windowed_optimization", "ウィンドウゲームの最適化"),
            new ReadOnlySettingHandler("vrr", "VRR"),
            new ReadOnlySettingHandler("per_app_gpu", "ゲーム別GPU指定")
        }.ToDictionary(x => x.Id, StringComparer.Ordinal);

    internal static bool ValidateSafetyContract()
    {
        // Dry-run registry audit. No Windows settings are changed.
        if (Handlers.Count != 11 || Handlers.Values.Count(
            x => x.GetCapability().Access == SettingAccess.ApplyAndRestore) != 1)
            return false;
        if (Handlers["mouse_acceleration"] is not MouseSettingHandler) return false;
        foreach (var item in Handlers.Values)
        {
            if (item.Id == "mouse_acceleration") continue;
            if (item.GetCapability().Access != SettingAccess.ReadOnly || item.CanRestore)
                return false;
        }
        return !Capability("windows_defender").Supported &&
               !Capability("firewall").Supported &&
               !Capability("uac").Supported;
    }

    public static SettingCapability Capability(string id) =>
        Handlers.TryGetValue(id, out var handler)
            ? handler.GetCapability()
            : new(id, id, SettingAccess.ReadOnly, false, "登録されていない項目です。");

    public static SettingPlan BuildPlan(PcSnapshot? source, IReadOnlyCollection<string> selected)
    {
        var changes = new List<SettingChange>();
        var blocked = new List<string>();
        if (source == null) { blocked.Add("コピー元JSONが選択されていません。"); return new(changes, blocked); }
        if (selected.Count == 0) { blocked.Add("設定項目が選択されていません。"); return new(changes, blocked); }

        foreach (string id in selected.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!Handlers.TryGetValue(id, out var handler))
            {
                blocked.Add($"{id}: 未登録の設定です。");
                continue;
            }
            var capability = handler.GetCapability();
            if (capability.Access != SettingAccess.ApplyAndRestore || !capability.Supported)
            {
                blocked.Add($"{capability.DisplayName}: {capability.Reason}");
                continue;
            }
            var item = source.Settings?.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                blocked.Add($"{capability.DisplayName}: コピー元データがありません。");
                continue;
            }
            if (!handler.TryPrepare(item, out var target, out var reason))
            {
                blocked.Add($"{capability.DisplayName}: {reason}");
                continue;
            }
            try
            {
                var before = handler.ReadValue();
                if (!string.Equals(before, target, StringComparison.Ordinal))
                    changes.Add(new(id, capability.DisplayName, before, target));
            }
            catch (Exception ex)
            {
                blocked.Add($"{capability.DisplayName}: 現在値取得失敗 ({ex.Message})");
            }
        }
        return new(changes, blocked);
    }

    public static string Apply(SettingPlan plan)
    {
        if (!plan.CanApply) throw new InvalidOperationException("適用できる変更がありません。");
        // Currently only one write-capable handler. Future multi-setting transactions must
        // support ordered rollback before allowing multiple changes.
        if (plan.Changes.Count != 1)
            throw new NotSupportedException("複数項目の一括適用は未対応です。");
        var change = plan.Changes[0];
        if (!Handlers.TryGetValue(change.Id, out var handler) ||
            handler.GetCapability().Access != SettingAccess.ApplyAndRestore)
            throw new NotSupportedException("設定の適用は許可されていません。");
        if (handler.ReadValue() != change.CurrentValue)
            throw new InvalidOperationException("確認後に設定が変わりました。再診断してください。");
        return handler.Apply(change.TargetValue);
    }

    public static bool CanRestore(string id) =>
        Handlers.TryGetValue(id, out var handler) && handler.CanRestore;

    public static string Restore(string id)
    {
        if (!Handlers.TryGetValue(id, out var handler) ||
            handler.GetCapability().Access != SettingAccess.ApplyAndRestore || !handler.CanRestore)
            throw new NotSupportedException("復元可能なバックアップがありません。");
        return handler.Restore();
    }
}

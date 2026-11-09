namespace PCSetupCopier;

// Single explicit transaction for the supported gaming preferences.
// No changes occur until the UI has confirmed the complete plan.
internal static class BatchSettingsApplier
{
    internal sealed record Plan(
        bool ChangeMouse, int[] OriginalMouse, int[] DesiredMouse,
        bool ChangePower, Guid OriginalPower, Guid DesiredPower,
        bool ChangeGameMode = false, int? OriginalGameMode = null, int DesiredGameMode = 0)
    {
        internal bool HasChanges => ChangeMouse || ChangePower || ChangeGameMode;
    }

    internal static Plan Prepare(bool includeMouse, int[] mouseTarget, bool includePower, Guid powerTarget,
        bool includeGameMode = false, bool gameModeTarget = false)
    {
        if (!includeMouse && !includePower && !includeGameMode)
            throw new InvalidOperationException("一括適用する設定を選択してください。");
        int[] originalMouse = SettingsApplier.Read();
        Guid originalPower = PowerPlanManager.ReadActive();
        if (mouseTarget.Length != 3 || mouseTarget[0] is < 0 or > 20 ||
            mouseTarget[1] is < 0 or > 20 || mouseTarget[2] is < 0 or > 2)
            throw new InvalidDataException("不正なマウス設定です。");
        if (includePower && !PowerPlanManager.Installed().Any(x => x.Id == powerTarget))
            throw new InvalidOperationException("このPCに存在しない電源プランです。");
        int? originalGameMode = includeGameMode ? GameModeManager.Read() : null;
        int gameModeDesired = gameModeTarget ? 1 : 0;
        return new(includeMouse && !originalMouse.SequenceEqual(mouseTarget),
            originalMouse, [.. mouseTarget], includePower && originalPower != powerTarget,
            originalPower, powerTarget,
            includeGameMode && originalGameMode != gameModeDesired,
            originalGameMode, gameModeDesired);
    }

    internal static string Apply(Plan plan)
    {
        if (!plan.HasChanges) return "選択した設定はすでに指定値になっています。";
        if (!SettingsApplier.Read().SequenceEqual(plan.OriginalMouse) ||
            PowerPlanManager.ReadActive() != plan.OriginalPower)
            throw new InvalidOperationException("確認中に設定が変わりました。再診断してください。");

        if (plan.ChangeGameMode && GameModeManager.Read() != plan.OriginalGameMode)
            throw new InvalidOperationException("ゲームモード関連値が確認後に変更されました。");
        bool powerAttempted = false;
        bool mouseAttempted = false;
        bool gameModeAttempted = false;
        try
        {
            if (plan.ChangeGameMode)
            {
                gameModeAttempted = true;
                GameModeManager.Apply(plan.DesiredGameMode == 1);
            }
            if (plan.ChangePower)
            {
                powerAttempted = true;
                PowerPlanManager.Apply(plan.DesiredPower);
            }
            if (plan.ChangeMouse)
            {
                mouseAttempted = true;
                SettingsApplier.Apply(plan.DesiredMouse);
            }
            if (plan.ChangePower && PowerPlanManager.ReadActive() != plan.DesiredPower)
                throw new InvalidOperationException("電源プランの最終確認に失敗しました。");
            if (plan.ChangeMouse && !SettingsApplier.Read().SequenceEqual(plan.DesiredMouse))
                throw new InvalidOperationException("マウス設定の最終確認に失敗しました。");
            if (plan.ChangeGameMode && GameModeManager.Read() != plan.DesiredGameMode)
                throw new InvalidOperationException("ゲームモード関連値の最終確認に失敗しました。");
            return "選択した設定を一括適用し、再取得で確認しました。変更前の値は保存済みです。";
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            if (mouseAttempted)
            {
                try { SettingsApplier.RestoreExact(plan.OriginalMouse); }
                catch (Exception ex) { rollbackErrors.Add(new InvalidOperationException("マウス設定の復元に失敗", ex)); }
            }
            if (powerAttempted)
            {
                try { PowerPlanManager.RestoreExact(plan.OriginalPower); }
                catch (Exception ex) { rollbackErrors.Add(new InvalidOperationException("電源プランの復元に失敗", ex)); }
            }
            if (gameModeAttempted)
            {
                try { GameModeManager.RestoreExact(plan.OriginalGameMode); }
                catch (Exception ex) { rollbackErrors.Add(new InvalidOperationException("ゲームモード関連値の復元に失敗", ex)); }
            }
            if (rollbackErrors.Count != 0)
                throw new AggregateException("一括適用と自動復元に失敗しました。バックアップから復元してください。",
                    [failure, .. rollbackErrors]);
            throw new InvalidOperationException("一括適用に失敗し、変更を元に戻しました。詳細: " + failure.Message, failure);
        }
    }

    // No Windows writes: used by package preflight.
    internal static bool VerifyContract() =>
        !new Plan(false, [0, 0, 0], [0, 0, 0], false, Guid.Empty, Guid.Empty).HasChanges &&
        new Plan(true, [0, 0, 0], [0, 0, 1], false, Guid.Empty, Guid.Empty).HasChanges &&
        new Plan(false, [0, 0, 0], [0, 0, 0], false, Guid.Empty, Guid.Empty,
            true, null, 1).HasChanges && GameModeManager.VerifyContract();
}

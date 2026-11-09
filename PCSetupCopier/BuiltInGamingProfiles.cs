namespace PCSetupCopier;

// Built-in templates stage choices in the UI; they never touch Windows.
// null means deliberately not including that setting in the batch.
internal static class BuiltInGamingProfiles
{
    internal enum PowerPreference { Keep, PreferPerformance, Balanced }

    internal sealed record Profile(
        string Id, string Title, string Explanation,
        bool? MouseAcceleration, bool? GameMode,
        bool? BackgroundCapture, bool? GameBarCapture,
        PowerPreference Power)
    {
        private static string Display(bool? value) =>
            value is null ? "変更しない" : value.Value ? "オン指定" : "オフ指定";

        internal string Preview =>
            "マウス加速: " + Display(MouseAcceleration) + "\n" +
            "ゲームモード関連値: " + Display(GameMode) + "\n" +
            "バックグラウンド録画関連値: " + Display(BackgroundCapture) + "\n" +
            "Game Barキャプチャ関連値: " + Display(GameBarCapture) + "\n" +
            "電源プラン: " + (Power switch
            {
                PowerPreference.Keep => "変更しない",
                PowerPreference.PreferPerformance => "利用可能なら高パフォーマンス",
                _ => "利用可能ならバランス"
            });
    }

    internal static IReadOnlyList<Profile> All { get; } =
    [
        new("fps", "FPS重視（対戦ゲーム）",
            "入力の一貫性と余計な録画負荷を重視。ゲームごとに最適とは限りません。",
            false, true, false, false, PowerPreference.PreferPerformance),
        new("balanced", "バランス重視",
            "ゲームと日常作業の両立。録画の好みには触れず、電源プランも控えめに調整します。",
            false, true, null, null, PowerPreference.Balanced),
        new("visual", "画質・配信重視",
            "録画を使う方向の設定候補。画質そのものは各ゲーム・GPU設定で調整します。",
            false, true, true, true, PowerPreference.Keep),
        new("minimal", "最小変更（安全）",
            "マウス加速だけをOFFに指定。他の項目は変更しません。",
            false, null, null, null, PowerPreference.Keep)
    ];

    internal static bool VerifyContract() =>
        All.Count == 4 && All.Select(x => x.Id).Distinct().Count() == All.Count &&
        All.All(x => x.MouseAcceleration == false) &&
        All.First(x => x.Id == "balanced").BackgroundCapture == null &&
        All.First(x => x.Id == "minimal").GameMode == null;
}

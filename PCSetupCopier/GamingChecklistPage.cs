using Microsoft.Win32;
using System.Diagnostics;

namespace PCSetupCopier;

// Guidance only. There are no background tweaks, registry writes or automatic recommendations applied.
internal sealed class GamingChecklistPage : UserControl
{
    private readonly DataGridView items = new() {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, RowHeadersVisible = false,
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
    };
    private readonly Label explanation = new() {
        Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(49, 71, 101),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ComboBox profile = new() {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 155
    };
    private sealed record Tip(string Name, string State, string Advice, string Details, string? Url);

    internal GamingChecklistPage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4,
            ColumnCount = 1, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        heading.Controls.Add(new Label { Text = "ゲーム向け設定チェック",
            Width = 240, Height = 36,
            Font = new Font("Yu Gothic UI", 14, FontStyle.Bold) });
        heading.Controls.Add(new Label { Text = "重視する項目", AutoSize = true,
            Margin = new Padding(16, 9, 5, 0) });
        profile.Items.AddRange(["対戦FPS・低遅延", "画質・映像重視", "バランス"]);
        profile.SelectedIndex = 0;
        profile.SelectedIndexChanged += (_, _) => RefreshTips();
        heading.Controls.Add(profile);
        var refresh = new Button { Text = "チェック更新", AutoSize = true };
        refresh.Click += (_, _) => RefreshTips();
        heading.Controls.Add(refresh);
        layout.Controls.Add(heading, 0, 0);

        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "設定",
            FillWeight = 115 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "取得結果",
            FillWeight = 115 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Advice", HeaderText = "確認ポイント",
            FillWeight = 300 });
        items.SelectionChanged += (_, _) => {
            if (items.SelectedRows.Count > 0 &&
                items.SelectedRows[0].Tag is Tip tip)
                explanation.Text = tip.Details;
        };
        layout.Controls.Add(items, 0, 1);

        var detail = new Panel { Dock = DockStyle.Fill,
            Padding = new Padding(15, 8, 15, 8), BackColor = Color.White };
        detail.Controls.Add(explanation);
        layout.Controls.Add(detail, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var open = new Button { Text = "選択した項目のWindows設定を開く",
            AutoSize = true, MinimumSize = new Size(280, 35) };
        open.Click += (_, _) => {
            if (items.SelectedRows.Count == 0 || items.SelectedRows[0].Tag is not Tip tip ||
                tip.Url is null) return;
            try { Process.Start(new ProcessStartInfo(tip.Url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定画面を開けません"); }
        };
        buttons.Controls.Add(open);
        buttons.Controls.Add(new Label {
            Text = "※ これは診断と設定画面への案内です。自動でWindowsを変更しません。",
            AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 10, 0, 0)
        });
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        RefreshTips();
    }

    private static int? Dword(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path, false);
            var value = key?.GetValue(name);
            return value is int number ? number : null;
        }
        catch { return null; }
    }

    private void RefreshTips()
    {
        string mode = profile.SelectedItem as string ?? "";
        bool fps = mode.StartsWith("対戦", StringComparison.Ordinal);
        bool visual = mode.StartsWith("画質", StringComparison.Ordinal);
        var tips = new List<Tip>();
        IReadOnlyList<DisplayRefreshManager.Monitor> screens;
        try { screens = DisplayRefreshManager.Read(); }
        catch { screens = []; }
        string hz = screens.Count == 0 ? "取得不可" :
            string.Join(" / ", screens.Select(x => x.Label + ": " + x.CurrentHz + "Hz"));
        tips.Add(new("モニターのリフレッシュレート", hz,
            fps ? "対応する高Hzを選択し、ゲーム側のFPS設定と合わせて確認" :
                "解像度と対応Hzのバランスを確認",
            "モニターが高Hzに対応していても、ケーブルや解像度、Windowsの現在値が低い場合があります。 " +
            "リフレッシュレートは別タブで選んで変更できます。", "ms-settings:display-advanced"));

        int? game;
        try { game = GameModeManager.Read(); } catch { game = null; }
        tips.Add(new("Windows ゲームモード",
            game is null ? "関連値未設定" : game == 1 ? "オン指定" : "オフ指定",
            "通常はONで検証（ゲームごとの例外あり）",
            "Windows設定画面の表示が優先です。関連レジストリ値が未設定でも設定画面ではONの場合があります。" +
            "Windowsゲームモードはゲームの実行中にリソース配分を調整します。",
            "ms-settings:gaming-gamemode"));

        int? hags = Dword(Registry.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        tips.Add(new("HAGS（GPUスケジューリング）",
            hags is null ? "関連値未設定" : hags == 2 ? "有効を要求" : hags == 1 ? "無効を要求" : "不明な値",
            "GPU・ドライバー・ゲームで比較。低遅延に必ず有利とは限らない",
            "Windows設定で対応機能の有無を確認してください。切替に再起動が必要な環境があります。" +
            "ドライバーの対応状況はレジストリ値だけで判定できません。",
            "ms-settings:display-advancedgraphics-default"));

        tips.Add(new("ウィンドウゲームの最適化",
            "Windows画面で確認",
            "DX10/11のボーダーレス・ウィンドウ表示なら検討",
            "対応ゲームでは表示経路を改善し、遅延を下げる場合があります。" +
            "Auto HDRと連動する場合があります。変更後はゲームを再起動してください。",
            "ms-settings:display-advancedgraphics-default"));

        tips.Add(new("可変リフレッシュレート（VRR）",
            "対応状況の自動判定は未実装",
            fps ? "対応モニターならG-SYNC / FreeSyncとFPS上限を確認" :
                "対応モニターでティアリングと滑らかさのバランスを確認",
            "モニター、接続方法、GPU、ドライバー、OSの各機能が必要です。" +
            "Windowsの設定とGPUメーカーのコントロールパネルの両方を確認してください。",
            "ms-settings:display-advancedgraphics-default"));

        tips.Add(new("ゲーム別GPU優先設定", "別タブで管理",
            "複数GPUがあるPCではゲーム本体exeに高パフォーマンス優先を検討",
            "GPU優先設定は希望値であり、実際に使用するGPUを保証しません。" +
            "ノートPCでは消費電力・発熱の変化も確認してください。",
            "ms-settings:display-advancedgraphics"));

        int? capture;
        try { capture = CapturePreferenceManager.Read(); } catch { capture = null; }
        tips.Add(new("バックグラウンド録画",
            capture is null ? "関連値未設定" : capture == 1 ? "オン指定" : "オフ指定",
            fps ? "録画が不要ならOFFも比較（FPSとフレーム時間で判断）" :
                "クリップ保存を使うならONのまま動作確認",
            "録画の負荷はGPUエンコーダー、録画品質、ディスクなどでも変わります。" +
            "関連値だけでは実効状態を保証できません。",
            "ms-settings:gaming-gamedvr"));

        tips.Add(new("電源プラン・電源モード", "自由設定タブで確認",
            "性能重視時は発熱・消費電力も見ながら比較",
            "Windowsの電源プランと電源モードは別の概念です。" +
            "高パフォーマンス一択とは限らず、ノートPCでは冷却とバッテリーを重視します。",
            "ms-settings:powersleep"));

        tips.Add(new("Auto HDR / HDR", "ディスプレイで確認",
            visual ? "HDR対応ゲーム・画面なら画質比較" : "対戦FPSは視認性を優先してゲームごとに選ぶ",
            "HDR対応モニター・ゲームで有効な場合があります。" +
            "色・明るさ・入力遅延の体感は画面ごとに異なります。",
            "ms-settings:display"));

        tips.Add(new("ゲーム中の通知・集中モード", "Windows画面で確認",
            "試合中の通知やポップアップを抑制",
            "フルスクリーン時の通知抑制や集中モードを確認します。" +
            "セキュリティ通知やシステム更新を無効化する操作はしません。",
            "ms-settings:notifications"));

        items.Rows.Clear();
        foreach (var tip in tips)
        {
            int index = items.Rows.Add(tip.Name, tip.State, tip.Advice);
            items.Rows[index].Tag = tip;
            items.Rows[index].Cells[0].ToolTipText = tip.Details;
        }
        if (items.Rows.Count > 0) items.Rows[0].Selected = true;
    }
}

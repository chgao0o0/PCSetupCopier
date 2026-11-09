using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCSetupCopier;

// Read-only NVIDIA adapter detection and configuration guidance.
// NVIDIA driver profiles must not be modified through undocumented registry keys.
internal sealed class NvidiaSettingsPage : UserControl
{
    private readonly Label gpuInfo = new() {
        Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI", 10), AutoEllipsis = true
    };
    private readonly ComboBox profile = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView advice = new() {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    internal NvidiaSettingsPage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        root.Controls.Add(new Label {
            Text = "NVIDIA GPU・ゲーム別設定ガイド", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 16f, FontStyle.Bold)
        }, 0, 0);
        root.Controls.Add(gpuInfo, 0, 1);
        var selector = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        selector.Controls.Add(new Label { Text = "用途", AutoSize = true, Margin = new Padding(0, 9, 8, 0) });
        profile.Items.AddRange(["対戦FPS・低遅延", "バランス重視", "画質・配信重視"]);
        profile.SelectedIndex = 0;
        profile.SelectedIndexChanged += (_, _) => FillAdvice();
        selector.Controls.Add(profile);
        var check = new Button { Text = "GPUを再診断", AutoSize = true };
        check.Click += (_, _) => RefreshGpu();
        selector.Controls.Add(check);
        root.Controls.Add(selector, 0, 2);

        advice.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "項目", FillWeight = 125 });
        advice.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "推奨する確認内容", FillWeight = 190 });
        advice.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "理由・注意点", FillWeight = 355 });
        root.Controls.Add(advice, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var open = new Button { Text = "NVIDIAコントロールパネルを開く",
            Height = 36, Dock = DockStyle.Fill };
        open.Click += (_, _) => OpenPanel();
        footer.Controls.Add(open, 0, 0);
        footer.Controls.Add(new Label { Text = "NVIDIA Appに設定が移行している場合は、そちらも確認してください。",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(87, 104, 129) }, 1, 0);
        var warning = new Label {
            Text = "NVIDIAドライバーの3D設定は現在値の取得・自動変更をしていません。FPS/バランスプリセットの適用対象はWindows設定だけです。",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(114, 91, 74),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };
        footer.Controls.Add(warning, 0, 1);
        footer.SetColumnSpan(warning, 2);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
        RefreshGpu();
        FillAdvice();
    }

    private void RefreshGpu()
    {
        try
        {
            var adapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < 32; i++)
            {
                var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref device, 0)) break;
                if (device.DeviceString.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    adapters.Add(device.DeviceString);
            }
            gpuInfo.Text = adapters.Count == 0
                ? "NVIDIA製ディスプレイアダプターが見つかりませんでした。GPUドライバー環境で再診断できます。"
                : "検出したNVIDIAアダプター: " + string.Join(" / ", adapters) +
                  "\nドライバーの3Dプロファイル設定値は取得していません。";
        }
        catch (Exception ex) { gpuInfo.Text = "GPU取得エラー: " + ex.Message; }
    }

    private void FillAdvice()
    {
        bool competitive = profile.SelectedIndex == 0;
        bool visual = profile.SelectedIndex == 2;
        advice.Rows.Clear();
        void Add(string name, string candidate, string reason) =>
            advice.Rows.Add(name, candidate, reason);

        Add("NVIDIA Reflex",
            competitive ? "対応ゲームでONを比較" : "ゲーム内で対応状況を確認",
            "低遅延向け。ゲーム側で実装される設定であり、NVIDIAコントロールパネルの一括変更対象ではありません。");
        Add("低遅延モード",
            competitive ? "Reflexがないゲームで検証" : "まず既定値を維持",
            "すべてのAPI・ゲームでUltraが最善とは限りません。Reflexとの関係も確認してください。");
        Add("電源管理モード",
            competitive ? "性能低下するゲームだけ最大性能を比較" : "通常・最適電力から比較",
            "最大性能の常時指定は電力・発熱増加の可能性があります。グローバル固定は避けます。");
        Add("垂直同期・G-SYNC",
            competitive ? "VRR、FPS上限と組み合わせて比較" : "画面の滑らかさを優先して比較",
            "設定の組合せはモニターのVRR対応や表示モードによって異なります。");
        Add("最大フレームレート",
            competitive ? "安定したフレーム時間を重視して決定" : "温度・消費電力とのバランスで決定",
            "モニターのHzとゲームの挙動に合わせる。全ゲーム共通の固定値は指定しません。");
        Add("テクスチャフィルタリング",
            visual ? "品質重視から比較" : "ゲーム側の画質設定を基本に",
            "性能・見た目の差はゲームごとに異なります。");
        Add("DLSS・フレーム生成",
            visual ? "画質と応答性を比較" : "対応ゲーム・GPUで選択",
            "フレーム生成は表示FPSと入力応答の改善が必ずしも一致しません。");
        Add("NVIDIA App オーバーレイ",
            competitive ? "録画・オーバーレイが不要なら負荷比較" : "使う機能は維持",
            "録画や計測が必要なら無理に無効化しないでください。");
    }

    private void OpenPanel()
    {
        try
        {
            // Open only a known installed NVIDIA control-panel launcher; no driver writes.
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "nvcplui.exe");
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            MessageBox.Show(this,
                "NVIDIAコントロールパネルが見つかりませんでした。スタートメニューの「NVIDIA コントロール パネル」またはNVIDIA Appを開いてください。",
                "NVIDIA設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "NVIDIA設定を開けません", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index,
        ref DISPLAY_DEVICE displayDevice, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}

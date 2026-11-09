using System.Runtime.InteropServices;

namespace PCSetupCopier;

// Refresh-rate-only change, no resolution, topology, HDR or GPU changes.
// Temporary mode is rolled back on rejection/timeout. Committing happens only after confirmation.
internal static class DisplayRefreshManager
{
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint CdsTest = 0x00000002;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const int EnumCurrentSettings = -1;

    internal sealed record Choice(int Hz, int Width, int Height, int BitsPerPixel);
    internal sealed record Monitor(string Id, string Label, int CurrentHz, IReadOnlyList<Choice> Choices);

    internal static IReadOnlyList<Monitor> Read()
    {
        var result = new List<Monitor>();
        foreach (Screen screen in Screen.AllScreens)
        {
            if (!TryMode(screen.DeviceName, EnumCurrentSettings, out var current)) continue;
            var options = new SortedDictionary<int, Choice>();
            for (int i = 0; i < 2048; i++)
            {
                if (!TryMode(screen.DeviceName, i, out var candidate)) break;
                if (candidate.dmPelsWidth != current.dmPelsWidth ||
                    candidate.dmPelsHeight != current.dmPelsHeight ||
                    candidate.dmBitsPerPel != current.dmBitsPerPel ||
                    candidate.dmDisplayFrequency is < 30 or > 1000) continue;
                int hz = (int)candidate.dmDisplayFrequency;
                if (!options.ContainsKey(hz))
                    options.Add(hz, new Choice(hz, (int)candidate.dmPelsWidth,
                        (int)candidate.dmPelsHeight, (int)candidate.dmBitsPerPel));
            }
            var label = (screen.Primary ? "メイン" : "サブ") + " / " +
                current.dmPelsWidth + "×" + current.dmPelsHeight;
            result.Add(new Monitor(screen.DeviceName, label, (int)current.dmDisplayFrequency,
                options.Values.ToArray()));
        }
        return result;
    }

    internal static string ApplyWithConfirmation(IWin32Window owner, Monitor selected, int hz)
    {
        if (!selected.Choices.Any(x => x.Hz == hz))
            throw new InvalidOperationException("このモニター・解像度では選択できないHzです。");
        if (!TryMode(selected.Id, EnumCurrentSettings, out var original))
            throw new InvalidOperationException("現在の画面モードを取得できません。");
        if (original.dmDisplayFrequency == hz) return "指定Hzが現在値と同じため変更は不要です。";
        if (original.dmPelsWidth != selected.Choices[0].Width ||
            original.dmPelsHeight != selected.Choices[0].Height ||
            original.dmBitsPerPel != selected.Choices[0].BitsPerPixel)
            throw new InvalidOperationException("診断後に解像度が変更されました。再診断してください。");

        var candidate = original;
        candidate.dmFields = DmDisplayFrequency;
        candidate.dmDisplayFrequency = (uint)hz;
        int test = ChangeDisplaySettingsEx(selected.Id, ref candidate, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != 0) throw new InvalidOperationException("Windowsが指定Hzを拒否しました。コード: " + test);

        int applied = ChangeDisplaySettingsEx(selected.Id, ref candidate, IntPtr.Zero, 0, IntPtr.Zero);
        if (applied != 0)
            throw new InvalidOperationException("一時的なリフレッシュレート変更に失敗: " + applied);

        bool persisted = false;
        try
        {
            if (!TryMode(selected.Id, EnumCurrentSettings, out var now) ||
                now.dmDisplayFrequency != hz)
                throw new InvalidOperationException("画面モード変更後の再取得が一致しません。");

            using var confirmation = new Form {
                Text = "新しいリフレッシュレートを保存しますか？",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(490, 205), MinimumSize = new Size(490, 205),
                MaximumSize = new Size(490, 205),
                TopMost = true, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            };
            var message = new Label {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Yu Gothic UI", 11),
                Text = selected.Label + " を " + hz + "Hz に変更しました。"
            };
            var row = new FlowLayoutPanel {
                Dock = DockStyle.Bottom, Height = 55,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(16, 9, 12, 8)
            };
            var yes = new Button { Text = "この設定を保存", Width = 150, Height = 32,
                DialogResult = DialogResult.OK };
            var no = new Button { Text = "元に戻す", Width = 125, Height = 32,
                DialogResult = DialogResult.Cancel };
            row.Controls.Add(yes);
            row.Controls.Add(no);
            confirmation.Controls.Add(message);
            confirmation.Controls.Add(row);
            confirmation.AcceptButton = yes;
            confirmation.CancelButton = no;
            int remaining = 15;
            using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) => {
                remaining--;
                message.Text = selected.Label + " / " + hz + "Hz\n" +
                    "画面が正常に表示されていますか？ " + remaining + "秒で自動復元します。";
                if (remaining <= 0) confirmation.DialogResult = DialogResult.Cancel;
            };
            message.Text += "\n15秒以内に確定しない場合は元に戻します。";
            timer.Start();
            bool accepted = confirmation.ShowDialog(owner) == DialogResult.OK;
            timer.Stop();
            if (!accepted) return "変更を取り消して元のHzに戻しました。";

            if (!TryMode(selected.Id, EnumCurrentSettings, out now) ||
                now.dmDisplayFrequency != hz)
                throw new InvalidOperationException("確定直前に画面モードが変更されました。");
            int saved = ChangeDisplaySettingsEx(selected.Id, ref candidate,
                IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
            if (saved != 0)
                throw new InvalidOperationException("WindowsへのHz設定保存に失敗しました: " + saved);
            if (!TryMode(selected.Id, EnumCurrentSettings, out now) ||
                now.dmDisplayFrequency != hz)
                throw new InvalidOperationException("Hzの最終確認に失敗しました。");
            persisted = true;
            return selected.Label + "を" + hz + "Hzに変更して保存しました。";
        }
        finally
        {
            if (!persisted)
            {
                var back = original;
                back.dmFields = DmDisplayFrequency;
                int rollback = ChangeDisplaySettingsEx(selected.Id, ref back, IntPtr.Zero, 0, IntPtr.Zero);
                if (rollback != 0)
                    MessageBox.Show("リフレッシュレートを自動復元できませんでした。Windowsの詳細ディスプレイ設定で確認してください。エラー: " + rollback,
                        "画面設定の復元に失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static bool TryMode(string id, int index, out DEVMODE mode)
    {
        mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(id, index, ref mode);
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string name, int index, ref DEVMODE mode);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string device, ref DEVMODE mode, IntPtr hwnd, uint flags, IntPtr parameters);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }
}

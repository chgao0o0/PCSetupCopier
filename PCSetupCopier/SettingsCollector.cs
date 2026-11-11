using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PCSetupCopier;

internal static class SettingsCollector
{
    public static PcSnapshot Collect()
    {
        var screens = ReadScreens();
        var values = new List<SettingResult>
        {
            Refresh(screens),
            GameMode(),
            Power(),
            Result("graphics", "Windowsグラフィック設定", "HAGS / VRR / ゲーム別GPU指定を個別表示", "個別確認", "総称であり単一の設定値はありません。各項目で取得状況を確認してください。"),
            Capture(),
            Result("fps_limit", "FPS制限関連", "Windows共通のFPS上限設定なし", "対象外", "ゲーム側またはNVIDIA側のFPS設定を確認してください。Windows共通の上限値は存在しません。"),
            MouseAcceleration(),
            Hags(),
            Unknown("windowed_optimization", "ウィンドウゲームの最適化", "Windows設定画面の実効状態を安全に取得する公開APIを確認できないため、推測しません。", "検証不可"),
            Unknown("vrr", "可変リフレッシュレート（VRR）", "WindowsのVRRスイッチ・GPUのVRR機能・モニター対応は別項目です。表示中のHzだけではVRRを断定できません。", "検証不可"),
            PerAppGpu()
        };
        return new PcSnapshot("1.3", DateTimeOffset.Now, "M8.4", Environment.MachineName,
            RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount, screens, values);
    }

    private static SettingResult Result(string id, string name, string? value, string state, string description)
        => new(id, "Gaming", name, value, state, description);
    private static SettingResult Ok(string id, string name, string value, string description)
        => Result(id, name, value, "取得済み", description);
    private static SettingResult Partial(string id, string name, string value, string description)
        => Result(id, name, value, "一部取得", description);
    private static SettingResult Unknown(string id, string name, string description, string state = "未取得")
        => Result(id, name, null, state, description);
    private static SettingResult Failure(string id, string name, Exception ex)
        => Unknown(id, name, "取得エラー: " + ex.GetType().Name, "取得失敗");
    private static int? ReadDword(RegistryKey? key, string name) => key?.GetValue(name) is int v ? v : null;
    private static string OnOff(int x) => x switch { 0 => "オフ", 1 => "オン", _ => "値 " + x };

    private static SettingResult Refresh(List<DisplayInfo> displays)
    {
        const string id = "refresh_rate", name = "リフレッシュレート";
        if (displays.Count == 0) return Unknown(id, name, "モニターを検出できませんでした。");
        var value = string.Join(" / ", displays.Select(d => $"{d.Name}: {d.RefreshRateHz ?? "?"} Hz"));
        return Result(id, name, value, displays.All(d => d.RefreshRateHz != null) ? "取得済み" : "一部取得",
            "現在の表示モード。モニターの最大対応Hzとは限りません。");
    }

    private static SettingResult GameMode()
    {
        const string id = "game_mode", name = "ゲームモード";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", false);
            int? a = ReadDword(key, "AutoGameModeEnabled"), b = ReadDword(key, "AllowAutoGameMode");
            if (a == null && b == null) return Unknown(id, name, "関連レジストリ値が未設定です。既定状態は推測しません。");
            string value = string.Join(" / ", new string?[]
            {
                a == null ? null : "AutoGameModeEnabled=" + a,
                b == null ? null : "AllowAutoGameMode=" + b
            }.Where(x => x != null));
            return Partial(id, name, value, "取得した関連値であり、Windows設定画面の実効状態と同一とは断定しません。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
    }

    private static SettingResult Capture()
    {
        const string id = "capture", name = "ゲーム録画・バックグラウンドキャプチャ";
        try
        {
            var entries = new List<string>();
            void Read(string path, params string[] names)
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, false);
                foreach (string n in names)
                {
                    int? x = ReadDword(key, n);
                    if (x != null) entries.Add($"{n}={x} ({OnOff(x.Value)})");
                }
            }
            Read(@"System\GameConfigStore", "GameDVR_Enabled");
            Read(@"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "AppCaptureEnabled", "HistoricalCaptureEnabled", "AudioCaptureEnabled");
            Read(@"Software\Microsoft\GameBar", "ShowStartupPanel");
            return entries.Count == 0 ? Unknown(id, name, "関連設定値が見つかりませんでした。") :
                Partial(id, name, string.Join(" / ", entries), "関連値の一部です。録画機能全体の有効状態を保証しません。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
    }

    private static SettingResult MouseAcceleration()
    {
        const string id = "mouse_acceleration", name = "マウス加速";
        try
        {
            int[] values = new int[3];
            if (SystemParametersInfoGetMouse(3, 0, values, 0))
                return Ok(id, name,
                    $"{(values[2] == 0 ? "加速なし" : "加速あり")} / Threshold1={values[0]} / Threshold2={values[1]} / Accel={values[2]}",
                    "SPI_GETMOUSEで取得したWindows設定。ポインター速度は扱いません。Raw Inputには影響しない場合があります。");
            return Unknown(id, name, "SPI_GETMOUSEから値を取得できませんでした。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
    }

    private static SettingResult Hags()
    {
        const string id = "hags", name = "GPUスケジューリング（HAGS）";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false);
            int? value = ReadDword(key, "HwSchMode");
            if (value == null) return Result(id, name, "レジストリに明示設定なし（OS既定）", "既定値", "未設定は取得失敗ではありません。実効状態はGPU・ドライバー・Windowsバージョンによります。");
            string desired = value switch { 1 => "無効を要求", 2 => "有効を要求", _ => "意味未確認" };
            return Partial(id, name, $"HwSchMode={value} ({desired})",
                "ドライバーへの要求値。実効状態にはGPU・ドライバー・再起動などが関係します。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
    }

    private static SettingResult PerAppGpu()
    {
        const string id = "per_app_gpu", name = "ゲーム別GPU指定";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", false);
            if (key == null) return Result(id, name, "明示的なGPU優先設定なし", "未設定", "登録がないことは取得失敗ではありません。アプリ別の既定GPU選択はWindowsが管理します。");
            var values = key.GetValueNames()
                .Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(n => key.GetValue(n)?.ToString() ?? "").ToArray();
            int high = values.Count(x => x.Contains("GpuPreference=2;", StringComparison.OrdinalIgnoreCase));
            int saving = values.Count(x => x.Contains("GpuPreference=1;", StringComparison.OrdinalIgnoreCase));
            return Partial(id, name, $"登録 {values.Length} 件 / 高パフォーマンス {high} / 省電力 {saving}",
                "登録件数のみ取得し、アプリパスはJSONに出力しません。実効GPUは未確認。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
    }

    private static SettingResult Power()
    {
        const string id = "power", name = "電源設定";
        IntPtr ptr = IntPtr.Zero;
        try
        {
            uint result = PowerGetActiveScheme(IntPtr.Zero, out ptr);
            if (result != 0 || ptr == IntPtr.Zero) return Unknown(id, name, "電源プラン取得失敗: " + result);
            Guid guid = Marshal.PtrToStructure<Guid>(ptr);
            string label = guid.ToString("D").ToUpperInvariant() switch
            {
                "381B4222-F694-41F0-9685-FF5BB260DF2E" => "バランス",
                "8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C" => "高パフォーマンス",
                "A1841308-3541-4FAB-BC81-F71556F20B4A" => "省電力",
                "E9A42B02-D5DF-448D-AA00-03F14749EB61" => "究極のパフォーマンス",
                _ => "カスタム/メーカー設定"
            };
            return Ok(id, name, $"{label} ({guid:D})",
                "有効な電源プラン。Windows電源モードとは別です。別PCへGUIDを無条件にコピーしません。");
        }
        catch (Exception ex) { return Failure(id, name, ex); }
        finally { if (ptr != IntPtr.Zero) LocalFree(ptr); }
    }

    private static List<DisplayInfo> ReadScreens()
    {
        var list = new List<DisplayInfo>();
        foreach (var screen in Screen.AllScreens)
        {
            string? hz = null;
            try
            {
                var dev = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(screen.DeviceName, -1, ref dev) && dev.dmDisplayFrequency > 1)
                    hz = dev.dmDisplayFrequency.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
            list.Add(new DisplayInfo(screen.DeviceName, $"{screen.Bounds.Width}×{screen.Bounds.Height}", screen.Primary, hz));
        }
        return list;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr policy);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int mode, ref DEVMODE deviceMode);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGetMouse(uint action, uint param, [Out] int[] values, uint flags);

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

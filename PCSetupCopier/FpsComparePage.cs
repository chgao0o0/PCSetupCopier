using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace PCSetupCopier;

// Offline CSV comparison only. Never injects into games or changes GPU driver settings.
internal sealed class FpsComparePage : UserControl
{
    internal sealed record Metrics(int Samples, double AverageFps, double OnePercentLow,
        double PointOnePercentLow);
    private Metrics? before, after;
    private readonly Label first = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label second = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly TextBox output = new() {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Yu Gothic UI", 11), BackColor = Color.White
    };

    internal FpsComparePage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill,
            RowCount = 5, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label {
            Text = "FPS・1% Low の変更前後比較", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold)
        }, 0, 0);
        var a = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var loadBefore = new Button { Text = "変更前のCSVを選ぶ", Height = 36, AutoSize = true };
        loadBefore.Click += async (_, _) => await LoadCsvAsync(true);
        a.Controls.Add(loadBefore);
        first.Text = "変更前：未読み込み";
        first.Width = 500;
        a.Controls.Add(first);
        root.Controls.Add(a, 0, 1);
        var b = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var loadAfter = new Button { Text = "変更後のCSVを選ぶ", Height = 36, AutoSize = true };
        loadAfter.Click += async (_, _) => await LoadCsvAsync(false);
        b.Controls.Add(loadAfter);
        second.Text = "変更後：未読み込み";
        second.Width = 500;
        b.Controls.Add(second);
        root.Controls.Add(b, 0, 2);
        root.Controls.Add(new Label {
            Text = "PresentMonなどのフレーム時間CSVを比較します（計測そのものは行いません）。同じゲーム・シーン・設定で測定してください。",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(66, 91, 126)
        }, 0, 3);
        root.Controls.Add(output, 0, 4);
        Controls.Add(root);
        output.Text = "比較結果がここに表示されます。\r\n\r\n" +
            "CSV列例: MsBetweenPresents / MsBetweenDisplayChange / FrameTimeMs。\r\n" +
            "平均FPSは有効フレーム時間の合計から計算します。\r\n" +
            "1% Low / 0.1% Lowは遅いフレームの平均時間から求める近似値です。";
    }

    private async Task LoadCsvAsync(bool baseline)
    {
        using var dialog = new OpenFileDialog {
            Filter = "フレーム時間CSV (*.csv)|*.csv", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            output.Text = "CSVを解析しています…";
            Metrics measure = await Task.Run(() => Analyze(dialog.FileName));
            if (IsDisposed) return;
            if (baseline)
            {
                before = measure;
                first.Text = Format(measure);
            }
            else
            {
                after = measure;
                second.Text = Format(measure);
            }
            ShowResults();
            DiagnosticLog.Record("FPSImport", "Success " + measure.Samples + " frames");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Record("FPSImport", "Failed", ex);
            output.Text = "読み込み失敗: " + ex.Message;
        }
    }

    private static string Format(Metrics m) =>
        "平均 " + m.AverageFps.ToString("F1") + "FPS / 1% Low " +
        m.OnePercentLow.ToString("F1") + "FPS / " + m.Samples + "フレーム";

    private void ShowResults()
    {
        if (before == null || after == null)
        {
            output.Text = "変更前と変更後の両方を読み込むと差分が表示されます。";
            return;
        }
        static string Difference(double from, double to) =>
            (to >= from ? "+" : "") + (to - from).ToString("F1") +
            " FPS (" + ((to / from - 1) * 100).ToString("+0.0;-0.0;0.0") + "%)";
        output.Text =
            "平均FPS\r\n" + before.AverageFps.ToString("F1") + " → " +
            after.AverageFps.ToString("F1") + "  " +
            Difference(before.AverageFps, after.AverageFps) + "\r\n\r\n" +
            "1% Low\r\n" + before.OnePercentLow.ToString("F1") + " → " +
            after.OnePercentLow.ToString("F1") + "  " +
            Difference(before.OnePercentLow, after.OnePercentLow) + "\r\n\r\n" +
            "0.1% Low\r\n" + before.PointOnePercentLow.ToString("F1") + " → " +
            after.PointOnePercentLow.ToString("F1") + "  " +
            Difference(before.PointOnePercentLow, after.PointOnePercentLow) + "\r\n\r\n" +
            "注意: 同じシーン・解像度・負荷で測定していないと比較できません。\r\n" +
            "数値はCSVのフレーム時間から計算した値です。実測FPSの取得機能はありません。";
    }

    private static readonly string[] CandidateColumns = [
        "msbetweenpresents", "msbetweendisplaychange",
        "frametimems", "frametime(ms)", "frametime", "frametime_ms"
    ];

    internal static Metrics Analyze(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 10 or > 60_000_000)
            throw new InvalidDataException("CSVのサイズが不正です（最大60MB）。");
        using var parser = new TextFieldParser(path);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        string[]? headers = parser.ReadFields();
        if (headers == null) throw new InvalidDataException("CSVのヘッダーがありません。");
        var normalized = headers.Select(x =>
            x.Trim().Trim('\uFEFF').Replace(" ", "").ToLowerInvariant()).ToArray();
        int ix = Array.FindIndex(normalized, x => CandidateColumns.Contains(x));
        if (ix < 0)
            throw new InvalidDataException(
                "フレーム時間（ミリ秒）の列が見つかりません。PresentMon形式のCSVを選択してください。");
        var times = new List<double>();
        while (!parser.EndOfData && times.Count < 300_000)
        {
            string[]? cells;
            try { cells = parser.ReadFields(); }
            catch (MalformedLineException) { continue; }
            if (cells == null || ix >= cells.Length) continue;
            if (!double.TryParse(cells[ix], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ms) ||
                double.IsNaN(ms) || double.IsInfinity(ms) || ms is <= 0 or > 1000)
                continue;
            times.Add(ms);
        }
        if (times.Count < 100)
            throw new InvalidDataException("有効なフレーム時間が100件未満です。");
        return Calculate(times);
    }

    internal static Metrics Calculate(IReadOnlyList<double> timings)
    {
        if (timings.Count == 0 || timings.Any(x => x <= 0 || !double.IsFinite(x)))
            throw new ArgumentException("フレーム時間データが不正です。");
        var descending = timings.OrderByDescending(x => x).ToArray();
        static double SlowFps(double[] values, double ratio)
        {
            int n = Math.Max(1, (int)Math.Ceiling(values.Length * ratio));
            return 1000.0 / values.Take(n).Average();
        }
        return new Metrics(timings.Count,
            1000.0 * timings.Count / timings.Sum(),
            SlowFps(descending, 0.01),
            SlowFps(descending, 0.001));
    }

    internal static bool VerifyContract()
    {
        var test = Calculate(Enumerable.Repeat(10.0, 1000).ToArray());
        return Math.Abs(test.AverageFps - 100) < 0.001 &&
               Math.Abs(test.OnePercentLow - 100) < 0.001 &&
               Math.Abs(test.PointOnePercentLow - 100) < 0.001;
    }
}

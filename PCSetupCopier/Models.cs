namespace PCSetupCopier;

public sealed record SettingResult(
    string Id, string Category, string DisplayName,
    string? Value, string Status, string Description);

public sealed record PcSnapshot(
    string SchemaVersion, DateTimeOffset CapturedAt, string ToolVersion,
    string ComputerName, string OsDescription, string OsArchitecture,
    int ProcessorCount, List<DisplayInfo> Displays, List<SettingResult> Settings);

public sealed record DisplayInfo(
    string Name, string Bounds, bool Primary, string? RefreshRateHz);

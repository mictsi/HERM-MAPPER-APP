namespace HERMMapperApp.ViewModels;

public enum ExportDataset
{
    CompletedMappings = 1,
    Applications = 2,
    Services = 3,
    BrmModels = 4,
    DrmModels = 5
}

public enum ExportFileFormat
{
    Csv = 1,
    Json = 2,
    Xlsx = 3
}

public sealed class ExportDataViewModel
{
    public IReadOnlyList<ExportDatasetCardViewModel> Datasets { get; init; } = [];
}

public sealed class ExportDatasetCardViewModel
{
    public ExportDataset Dataset { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string RecordLabel { get; init; } = string.Empty;
    public int RecordCount { get; init; }
    public IReadOnlyList<string> IncludedFields { get; init; } = [];
}

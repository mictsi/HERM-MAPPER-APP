namespace HERMMapperApp.ViewModels;

public sealed class WorkbookImportReviewViewModel
{
    public bool HasReview => Verification is not null;
    public string? PendingImportToken { get; init; }
    public string? UploadedFileName { get; init; }
    public TrmWorkbookVerificationResult? Verification { get; init; }
}

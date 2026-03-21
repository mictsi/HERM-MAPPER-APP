namespace HERMMapperApp.ViewModels;

public sealed class WorkbookImportReviewViewModel
{
    public bool HasReview => Verification is not null;
    public Models.ReferenceModelKind ModelKind { get; init; } = Models.ReferenceModelKind.Trm;
    public string? PendingImportToken { get; init; }
    public string? UploadedFileName { get; init; }
    public TrmWorkbookVerificationResult? Verification { get; init; }
}

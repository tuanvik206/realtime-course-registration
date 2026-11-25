namespace RealtimeCourseRegistration.Contracts.Common;

public sealed record ApiProblem(
    string Type,
    string Title,
    int Status,
    string Detail,
    string ErrorCode,
    string CorrelationId);

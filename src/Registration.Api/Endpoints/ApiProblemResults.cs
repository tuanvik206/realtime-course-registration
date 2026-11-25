using RealtimeCourseRegistration.Contracts.Common;
using Registration.Api.Middleware;

namespace Registration.Api.Endpoints;

internal static class ApiProblemResults
{
    public static IResult Create(HttpContext context, int status, string errorCode, string? detail = null)
    {
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "Yêu cầu không hợp lệ",
            StatusCodes.Status401Unauthorized => "Chưa xác thực",
            StatusCodes.Status403Forbidden => "Không có quyền",
            StatusCodes.Status404NotFound => "Không tìm thấy",
            StatusCodes.Status409Conflict => "Xung đột dữ liệu",
            StatusCodes.Status429TooManyRequests => "Quá nhiều yêu cầu",
            StatusCodes.Status503ServiceUnavailable => "Dịch vụ tạm thời không khả dụng",
            _ => "Đã xảy ra lỗi"
        };
        return Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            type: $"https://httpstatuses.com/{status}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = CorrelationIdMiddleware.Get(context).ToString()
            });
    }

    public static IResult FromGrpc(HttpContext context, Grpc.Core.RpcException exception)
    {
        var (status, errorCode, detail) = exception.StatusCode switch
        {
            Grpc.Core.StatusCode.InvalidArgument => (400, ErrorCodes.ValidationError, "Tham số không hợp lệ."),
            Grpc.Core.StatusCode.NotFound => (404, ErrorCodes.CourseNotFound, "Không tìm thấy dữ liệu."),
            Grpc.Core.StatusCode.AlreadyExists => (409,
                string.IsNullOrWhiteSpace(exception.Status.Detail) ? ErrorCodes.VersionConflict : exception.Status.Detail,
                "Dữ liệu đã tồn tại."),
            Grpc.Core.StatusCode.FailedPrecondition => (409, ErrorCodes.VersionConflict, "Dữ liệu đã thay đổi; hãy tải lại."),
            Grpc.Core.StatusCode.DeadlineExceeded => (503, ErrorCodes.OperationOutcomeUnknown, "Chưa xác định được kết quả. Hãy thử lại cùng Idempotency-Key."),
            Grpc.Core.StatusCode.Unavailable => (503, ErrorCodes.DependencyUnavailable, "Dịch vụ xử lý đăng ký tạm thời không khả dụng."),
            _ => (500, ErrorCodes.InternalError, "Yêu cầu không thể hoàn tất.")
        };
        return Create(context, status, errorCode, detail);
    }
}

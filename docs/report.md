# Báo cáo đồ án — Hệ thống đăng ký học phần thời gian thực

## Tóm tắt

Dự án hiện thực cổng đăng ký học phần với Blazor WebAssembly, REST API, gRPC capacity service, PostgreSQL, RabbitMQ Outbox, SignalR và Audit Worker. Registration.Api xác thực người dùng và lấy student ID từ JWT. CourseCapacity.Service là nơi duy nhất khóa lớp, cập nhật số đăng ký và ghi sự kiện Outbox trong cùng transaction.

## Kiến trúc và bất biến

Sơ đồ, luồng đăng ký và ranh giới dữ liệu nằm tại [architecture.md](architecture.md); schema nằm tại [database-design.md](database-design.md). Bất biến chính là `0 <= registered_count <= capacity`, tối đa một đăng ký Active cho mỗi sinh viên/lớp, idempotency theo actor + operation ID, version tăng theo mutation, Outbox at-least-once và audit dedup theo event ID.

## Phần đã triển khai

- REST đọc lớp, đăng ký/hủy, summary/lịch/lịch sử, JWT và role/ownership.
- gRPC đọc lớp/sinh viên, giữ/trả chỗ và các mutation quản trị lớp.
- PostgreSQL models/migrations/seed, RabbitMQ fan-out, Outbox relay, SignalR Hub, Audit Worker.
- Giao diện sinh viên và admin responsive; các màn admin gọi API thật.
- Dockerfiles, Compose và health endpoints.

## Kết quả kiểm thử

Trước khi gỡ test files theo yêu cầu, Release build pass với 0 warning/0 error và 16 unit tests pass. GitHub Actions run 45560049139 pass với PostgreSQL metadata/concurrency integration, Compose config và image build. Bản hiện tại đã bỏ test source và CI workflow; Release build sau cleanup tiếp tục pass với 0 warning/0 error. Compose runtime, hai trình duyệt, failure injection, audit end-to-end và p95 chưa xác minh vì Docker Engine local không khả dụng. Xem [progress.md](progress.md) để biết trạng thái hiện tại.

## Giới hạn đã biết

- Container stack chưa chạy được trên máy này. PostgreSQL concurrency đã được kiểm tra bằng GitHub Actions trước khi người dùng yêu cầu xóa test files.
- Chưa đo latency hoặc tải; không tuyên bố đạt mục tiêu p95.
- Token giữ trong bộ nhớ trình duyệt, nên refresh trang yêu cầu đăng nhập lại.
- Mật khẩu demo là cấu hình local; cần thay đổi trước khi dùng ngoài demo.
- Hạn mức tín chỉ và phát hiện trùng lịch không thuộc nghiệp vụ đã bật.

## Bằng chứng cần bổ sung khi nghiệm thu

Ảnh chụp ứng dụng đang chạy, health state, hai browser realtime, RabbitMQ queues, audit record, recovery log và `git log` thật. Không thay thế các bằng chứng này bằng ảnh hoặc số liệu dựng sẵn.

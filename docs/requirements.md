# Yêu cầu và phạm vi

## Nguồn

Yêu cầu môn học được tổng hợp trong `MASTER_PLAN_AI_COURSE_REGISTRATION.md`, từ hướng dẫn chung và đề bài hệ thống đăng ký học phần thời gian thực. Các lựa chọn PostgreSQL, Blazor, JWT, quản trị, Outbox, idempotency, CI và mục tiêu hiệu năng là phương án thiết kế của dự án; nếu khác đề bài chính thức thì đề bài chính thức được ưu tiên.

## Ma trận truy vết

| ID | Yêu cầu | Thành phần | Bằng chứng nghiệm thu dự kiến |
|---|---|---|---|
| R01 | C#, .NET 10, ASP.NET Core Minimal API | Registration.Api, service hosts | `net10.0`, build thành công |
| R02 | Web client | Student.Web | Demo UI hoạt động |
| R03 | Danh sách lớp, sức chứa và chi tiết lớp | REST → gRPC → capacity_db | GET `/courses`, GET `/courses/{id}` |
| R04 | Đăng ký và hủy đăng ký | REST → gRPC | POST `/registrations`, DELETE `/registrations/{id}` |
| R05 | Không đăng ký trùng hoặc vượt sức chứa | CourseCapacity.Service, PostgreSQL | Constraint và kiểm thử cạnh tranh |
| R06 | Kiểm tra/giữ/trả chỗ qua gRPC | Shared.Contracts, CourseCapacity.Service | gRPC contract và lời gọi thực tế |
| R07 | Phát sự kiện đăng ký/hủy qua RabbitMQ | Outbox publisher | Confirm, routing key, event ID |
| R08 | Fan-out độc lập tới Notification và Audit | RabbitMQ | Hai queue riêng cùng nhận event |
| R09 | Cập nhật số chỗ theo thời gian thực | Notification.Service, Student.Web | Hai client không cần reload |
| R10 | Lưu lịch sử độc lập | Audit.Worker, audit_db | Dedup theo event ID |
| R11 | Tra cứu các lớp sinh viên đã đăng ký | REST, Student.Web | GET `/students/{studentId}/registrations`, quyền đúng |
| R12 | Lưu sinh viên, lớp, sức chứa, đăng ký, trạng thái | PostgreSQL | Migration, seed và truy vấn dữ liệu thật |
| R13 | Năm ứng dụng .NET chạy độc lập, mỗi app có Dockerfile | 5 deployable projects | Compose, restart từng service |
| R14 | Docker Compose và DNS nội bộ | Compose, Nginx | Không dùng localhost giữa containers |
| R15 | Tách cấu hình khỏi mã nguồn | `.env`, app config | `.env.example`, không commit secret |
| R16 | Logging và phục hồi lỗi | Tất cả services | Correlation ID, kiểm thử outage |
| R17 | Git/GitHub và lịch sử commit thật | Repository | Commit thực; remote/push chỉ khi được phép |
| R18 | README, báo cáo, sơ đồ và demo | `docs/`, root | Hướng dẫn tái lập và checklist bàn giao |

## Phạm vi

### P0 — bắt buộc

Danh sách/chi tiết lớp; đăng ký/hủy; chống đăng ký trùng và quá sức chứa; gRPC; RabbitMQ với fan-out; SignalR; audit; tra cứu đăng ký của sinh viên; dữ liệu PostgreSQL; Docker Compose; cấu hình an toàn; logging/phục hồi; tài liệu và demo.

### P1 — hoàn thiện

JWT với role Student/Admin; dashboard, lịch học, lịch sử; chức năng quản trị lớp/môn; idempotency ledger, transactional Outbox, versioning, retries, health/readiness; test suite, CI và UX responsive.

### P2 — ngoài phạm vi nếu chưa được yêu cầu

Thanh toán, phê duyệt nhiều cấp, quản lý điểm, lịch học tối ưu, giới hạn tín chỉ, OTP/email/SMS, OAuth xã hội, AI, app native, Redis cluster, Kubernetes, Kafka và Elasticsearch.

## Quy tắc nghiệp vụ nền

- Một sinh viên chỉ có tối đa một đăng ký Active cho mỗi lớp; đăng ký lại sau khi hủy tạo enrollment ID mới.
- Chỉ lớp Open còn chỗ mới nhận đăng ký; server kiểm tra lại trong transaction.
- `registered_count` bằng số enrollment Active; `remaining_seats = capacity - registered_count`.
- Giữ chỗ/tạo enrollment và hủy/giảm chỗ phải nguyên tử trong cùng transaction.
- Hủy lặp là no-op, không giảm count hay phát event lần nữa.
- Idempotency key cùng actor và payload trả lại outcome cũ; cùng key nhưng payload khác bị từ chối.
- Mọi client cập nhật giao diện theo snapshot/version từ server, không tin số chỗ hoặc danh tính từ body.

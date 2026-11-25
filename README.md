# Realtime Course Registration

Hệ thống web đăng ký học phần thời gian thực, xây dựng bằng .NET 10, Blazor WebAssembly, gRPC, PostgreSQL, RabbitMQ và SignalR.

## Thành phần

- `Student.Web`: giao diện sinh viên/quản trị và Nginx proxy same-origin.
- `Registration.Api`: REST, JWT, role/ownership, identity database và Outbox publisher.
- `CourseCapacity.Service`: gRPC, transaction giữ chỗ, quản lý lớp và capacity database.
- `Notification.Service`: RabbitMQ consumer và SignalR Hub.
- `Audit.Worker`: consumer lưu audit độc lập và internal read-only endpoint.
- `Shared.Contracts`: protobuf, error codes và integration event envelope.

## Yêu cầu chạy

- Docker Desktop với Linux containers và Docker Compose v2.
- .NET 10 SDK để build ngoài Docker. `global.json` cho phép SDK major mới hơn làm fallback.
- PowerShell 7 hoặc Windows PowerShell.

## Chạy local bằng Docker Compose

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

Truy cập giao diện tại `http://localhost:3000`. RabbitMQ management được publish riêng trên `http://localhost:15672`; username/password nằm trong `.env`. PostgreSQL và AMQP không publish cổng ra host. Không commit hoặc gửi `.env`.

Script tạo secrets ngẫu nhiên và không ghi đè `.env` đã có. Lần đầu chạy, service tự apply migrations và seed dữ liệu demo khi `DEMO_SEED_ENABLED=true`. Username demo là `admin`, `student1`, `student2`, `student3`, `student4`; mật khẩu dùng `DEMO_ADMIN_PASSWORD` hoặc `DEMO_STUDENT_PASSWORD` trong `.env`.

Muốn dừng nhưng giữ dữ liệu:

```powershell
docker compose down
```

Khởi động lại:

```powershell
docker compose up -d
```

Không dùng `docker compose down -v` nếu muốn giữ dữ liệu demo.

## Build

Ngoài Docker, dùng các lệnh sau để restore và build Release:

dotnet restore RealtimeCourseRegistration.slnx

dotnet build RealtimeCourseRegistration.slnx -c Release

## Đường dẫn API

Browser gọi `http://localhost:3000/api/...`; Nginx bỏ prefix `/api` trước khi chuyển sang Registration.Api. SignalR dùng `/hubs/courses`. REST nội bộ của Registration.Api không có `/api`, ví dụ `/courses`, `/registrations` và `/admin/courses`.

REST và gRPC contracts: [docs/api-specification.md](docs/api-specification.md). Kiến trúc: [docs/architecture.md](docs/architecture.md). Lệnh demo: [docs/demo.md](docs/demo.md). Tiến độ và giới hạn môi trường: [docs/progress.md](docs/progress.md).

Quy trình lưu commit Git làm bằng chứng theo tuần: [docs/git-evidence.md](docs/git-evidence.md).

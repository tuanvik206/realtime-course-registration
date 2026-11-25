# Tiến độ triển khai

Ngày cập nhật: 2026-11-25 (Asia/Ho_Chi_Minh)

Nguồn yêu cầu: [`MASTER_PLAN_AI_COURSE_REGISTRATION.md`](../MASTER_PLAN_AI_COURSE_REGISTRATION.md). Tên file nguồn hiện khác `MASTER_PLAN.md` được nhắc trong một số lệnh mẫu; chưa đổi tên.

## Trạng thái tổng quan

Các trạng thái DONE bên dưới nghĩa là phần mã/cấu hình của task đã được hiện thực. Commit `1c938b2` chứa một snapshot tổng hợp của toàn bộ source; đây chưa phải lịch sử commit riêng theo từng tuần. Chạy Compose và nghiệm thu đầu-cuối vẫn cần Docker Engine hoạt động.

| Task | Trạng thái | Commit | Bằng chứng / blocker |
|---|---|---|---|
| T00 — Môi trường và repo | PARTIAL | 1c938b2 | SDK 11.0.100 build được target `net10.0`; Docker daemon không truy cập được. Commit/push ban đầu đã hoàn tất; `.git/config` vẫn không ghi được để lưu remote. |
| T01 — Solution và project | DONE | 1c938b2 | Solution 8 project; isolated Release build pass. Author đã được cung cấp. |
| T02 — Tài liệu, contracts, error model | DONE | 1c938b2 | Docs, protobuf, event/error contracts; unit suite hiện pass. |
| T03 — PostgreSQL, RabbitMQ, env | PARTIAL | 1c938b2 | Compose config pass; chưa chạy container/health/persistence. |
| T04 — EF models, migrations, seed | DONE | 1c938b2 | 3 database context, migrations, seed; thêm migration `SubjectVersion`; kiểm tra không có pending model changes. |
| T05 — Capacity gRPC read | DONE | 1c938b2 | Danh sách/chi tiết lớp, môn, học kỳ và kiểm tra capacity. |
| T06 — ReserveSeat transaction | DONE | 1c938b2 | Row lock, operation ledger, unique active enrollment, Outbox. |
| T07 — ReleaseSeat và tra cứu | DONE | 1c938b2 | Hủy idempotent, ownership, summary, schedule, history. |
| T08 — REST infrastructure/read | DONE | 1c938b2 | ProblemDetails, correlation, course/subject/semester reads, health. |
| T09 — Login, JWT, authorization | DONE | 1c938b2 | Seed user, password hash, JWT, rate limit, Student/Admin policies. |
| T10 — REST đăng ký/hủy | DONE | 1c938b2 | Registration API lấy student ID từ JWT, yêu cầu idempotency key. |
| T11 — RabbitMQ topology | DONE | 1c938b2 | Durable topic exchange, queue fan-out và DLQ. |
| T12 — Transactional Outbox relay | DONE | 1c938b2 | Publisher confirm/routability, deadline/backoff và mark-published gRPC. |
| T13 — Notification và SignalR | DONE | 1c938b2 | Consumer và authenticated Hub; chưa xác minh runtime. |
| T14 — Audit Worker | DONE | 1c938b2 | Dedup event ID, internal key và paginated read API. |
| T15 — Backend end-to-end | BLOCKED | 1c938b2 | Cần PostgreSQL/RabbitMQ/Docker để chạy. |
| T16 — Blazor shell và đăng nhập | DONE | 1c938b2 | Login, in-memory session, route guard và typed API client. |
| T17 — Duyệt lớp, đăng ký, hủy | DONE | 1c938b2 | Tìm/lọc, chi tiết, thao tác idempotent, lịch sử/schedule. |
| T18 — SignalR client/reconnect | DONE | 1c938b2 | WebSocket Hub protocol client, version merge, reconnect/resubscribe, polling fallback; chưa xác minh bằng 2 browser. |
| T19 — Dashboard và lịch sinh viên | DONE | 1c938b2 | Dashboard, học phần, lịch, history, profile. |
| T20 — Dockerfiles và Compose | PARTIAL | 1c938b2 | 5 Dockerfiles và Compose config; chưa build image hoặc `compose up`. |
| T21 — Admin gRPC | DONE | 1c938b2 | CRUD subject/course, capacity/status, version conflict, idempotency, Outbox. |
| T22 — Admin REST | DONE | 1c938b2 | Admin-only routes, statistics thật, pagination và student enrichment. |
| T23 — Audit proxy và Admin UI | DONE | 1c938b2 | Internal Audit Worker proxy và màn admin subjects/courses/students/audit. |
| T24 — Hardening, logging và readiness | PARTIAL | 1c938b2 | Health, timeout, role/ownership, audit key, hub access log off; chưa chạy security/outage suite. |
| T25 — Unit/integration test suite | PARTIAL | 59a39ee | CI pass: 16 unit tests, integration PostgreSQL metadata và concurrency đều pass; các integration khác (HTTP/gRPC/RabbitMQ/SignalR) vẫn chưa có. |
| T26 — Load/concurrency | PARTIAL | 59a39ee | CI xác nhận DB-level 100 request/10 chỗ, retry cùng key và request trùng; HTTP end-to-end, admin-vs-enrollment và p95 chưa đo. |
| T27 — Failure/recovery | BLOCKED | 1c938b2 | Chưa thể dừng/khởi động service khi Docker daemon không khả dụng. |
| T28 — CI, GitHub và setup tái lập | PARTIAL | 59a39ee | GitHub Actions pass gồm PostgreSQL concurrency test, Compose config và image build. [Run CI](https://github.com/tuanvik206/realtime-course-registration/actions/runs/45560049139). `.git/config` không ghi được nên push dùng URL trực tiếp. |
| T29 — Báo cáo, sơ đồ và demo | PARTIAL | 1c938b2 | README, report/demo/test plan và architecture diagram là bản nháp; chưa có ảnh/log chạy thật. |
| T30 — Nghiệm thu và bàn giao | PARTIAL | 1c938b2 | Build/unit pass; source có commit GitHub và CI pass; nghiệm thu runtime còn thiếu do Docker Engine không khả dụng. |

## T00 — Kiểm tra môi trường và repo

**Kết quả:** BLOCKED — việc kiểm tra đã chạy; môi trường chưa đạt điều kiện để nghiệm thu T00 và bắt đầu T01.

### Repo và hệ điều hành

- Working directory đúng tên dự án: `D:\realtime-course-registration`.
- Tại thời điểm kiểm tra, chỉ có `MASTER_PLAN_AI_COURSE_REGISTRATION.md`; chưa có solution, source, `README.md`, `.env`, hay `docs/progress.md` trước lần cập nhật này.
- `git --version` → `2.56.0.windows.4`.
- `git status --short --branch` và `git remote -v` → `fatal: not a git repository`; chưa chạy `git init`.
- Windows x64, OS version `10.0.26300` theo `dotnet --info`.
- Truy vấn WMI (`Get-CimInstance Win32_ComputerSystem/Win32_OperatingSystem`) bị `Access denied`; chưa xác minh được RAM. Dung lượng ổ D cũng không đọc được qua các API có sẵn trong phiên này.

### .NET

- `dotnet --list-sdks` → chỉ có SDK `11.0.100` (x64); host x86 không có SDK.
- Đã có .NET / ASP.NET Core runtime `10.0.15`, nhưng runtime không thay thế SDK để tạo và build solution.
- `dotnet new list` có các template cần cho T01: ASP.NET Core Empty, gRPC, Blazor WebAssembly standalone, Class Library và xUnit.
- Theo trang tải chính thức được kiểm tra ngày 2026-11-25, SDK .NET 10 mới nhất là `10.0.404`, đi cùng runtime `10.0.15`; SDK 10 chưa được cài trên máy. Không cài đặt hoặc nâng cấp trong T00.
- Để không dừng việc triển khai, đã xác minh SDK `11.0.100` có thể tạo và build project `net10.0` trong thư mục tạm: build pass, 0 warning/0 error. `global.json` của repo ghim SDK `10.0.404` với `rollForward: major`, nên máy dùng SDK10 khi có và dùng SDK11 fallback hiện tại.

### Docker và cổng

- Docker CLI `26.1.4`; `docker compose version` → `v2.27.1-desktop.1`.
- `docker version` / `docker info --format '{{.OSType}}'` không kết nối được daemon: named pipe `//./pipe/docker_engine` không tồn tại.
- Windows service `com.docker.service` đang `Stopped` (Manual). Thử khởi động trả lỗi không thể mở service do quyền; trạng thái vẫn `Stopped`. `docker context ls` cũng bị từ chối quyền đọc metadata context.
- Vì daemon không chạy, chưa xác minh được engine Linux, image pull, Compose config với engine, hay tài nguyên Docker.
- Không có listener trên cổng `3000` hoặc `15672` tại thời điểm kiểm tra.

### Phiên bản ứng viên đã tra cứu

Các phiên bản sau được kiểm tra từ trang dự án/registry chính thức; đây mới là ứng viên cho T03/T20, chưa được tải hoặc chạy do Docker daemon không hoạt động:

- .NET SDK `10.0.404`, runtime `10.0.15` — [Microsoft .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- PostgreSQL `17.12` là bản vá được hỗ trợ; official image có tag `postgres:17.12` — [PostgreSQL versioning](https://www.postgresql.org/support/versioning/), [PostgreSQL official image tags](https://hub.docker.com/_/postgres/tags).
- RabbitMQ `4.4.1` là bản phát hành ổn định mới nhất được liệt kê; official management image có tag `rabbitmq:4.4.1-management` — [RabbitMQ releases](https://www.rabbitmq.com/docs/download), [RabbitMQ release information](https://www.rabbitmq.com/release-information), [official image tags](https://hub.docker.com/_/rabbitmq/tags).
- EF Core `10.0.15` tương thích `net10.0` theo NuGet — [NuGet package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.15). Các package khác và Npgsql provider cần kiểm tra phiên bản tương thích khi có solution để restore thật.

### Lệnh đã chạy

`Get-Location`; `dotnet --info`; `dotnet --list-sdks`; `dotnet new list`; `git --version`; `git status --short --branch`; `git remote -v`; `docker version`; `docker info --format '{{.OSType}}'`; `docker compose version`; `docker context ls`; `Get-Service com.docker.service`; `Start-Service com.docker.service` (bị từ chối quyền); kiểm tra listener cổng `3000,15672`; kiểm tra host/disk bằng WMI và .NET DriveInfo.

**Còn thiếu ở T00:** SDK10 chưa cài, Docker Linux engine không khả dụng, RAM/dung lượng đĩa không đọc được. Đã xác nhận có thể phát triển/build target `net10.0` bằng SDK11; Docker chặn kiểm thử cần container. Không vượt qua PowerShell execution policy `Restricted` để cài SDK bằng script. Git author do người dùng cung cấp là `Tuấn <tuanvik206@gmail.com>`. Sau kiểm tra ban đầu, đã tạo và push commit `1c938b2`; `.git/config` vẫn bị khóa ghi nên remote `origin` không lưu trong checkout.

**Commit đề xuất:** `docs: record environment checks and blockers`.

## T01 — Tạo solution và project

**Kết quả:** DONE.

- Tạo `RealtimeCourseRegistration.slnx`, 5 ứng dụng deployable, `Shared.Contracts`, `UnitTests`, `IntegrationTests`.
- Tất cả project target `net10.0`; thêm `global.json` có SDK `10.0.404` và roll-forward fallback, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.dockerignore`.
- Các backend tham chiếu `Shared.Contracts`; IntegrationTests tham chiếu các backend cần kiểm thử; Student.Web không tham chiếu trực tiếp backend.
- Giữ Greeter và sample UI template ở bước scaffold; sẽ thay khi thực hiện task nghiệp vụ/UI tương ứng.

**Lệnh đã chạy và kết quả:**

- `dotnet restore RealtimeCourseRegistration.slnx --ignore-failed-sources -p:NuGetAudit=false` → pass từ NuGet cache cục bộ.
- `dotnet build RealtimeCourseRegistration.slnx --configuration Release --no-restore --verbosity minimal` → pass, 0 warning, 0 error.
- `dotnet test RealtimeCourseRegistration.slnx --configuration Release --no-build --verbosity minimal` → 2 pass, 0 fail. Đây là 2 test mẫu của template, không phải kiểm thử nghiệp vụ.
- Restore mặc định lúc đầu lỗi `NU1301` vì mạng bị chặn tới `api.nuget.org`; chế độ bỏ qua source lỗi thành công vì các package cần cho scaffold đã có cache.

**Commit đề xuất:** `chore: initialize distributed registration solution`.

## T02 — Tài liệu, contracts và error model

**Kết quả:** DONE.

- Thêm requirements traceability, architecture, database design, REST/gRPC/event specification và 3 ADR về ownership, Outbox và same-origin proxy.
- Thêm `capacity.proto` trong `Shared.Contracts`, sinh client/server một lần với `GrpcServices="Both"`; CourseCapacity host dùng service base mới, không còn compile Greeter sample.
- Thêm integration-event envelope camelCase, schema/routing constants, validator, UUID parser, typed protobuf outcomes, REST error codes và ProblemDetails DTO.
- Thêm tests cho protobuf round-trip, event JSON/validation, request ID và outcome mapping.

**Lệnh đã chạy và kết quả:**

- `dotnet restore ... --ignore-failed-sources -p:NuGetAudit=false` trên bản sao build tạm → pass từ cache.
- `dotnet build ... --configuration Release --no-restore` trên bản sao build tạm → pass, 0 warning, 0 error.
- `dotnet test ... --configuration Release --no-build` trên bản sao build tạm → UnitTests: 11 pass; IntegrationTests: 1 pass (vẫn là template placeholder, không tính là nghiệp vụ).
- Bản sao build nằm tại `%TEMP%\rrc-build-t02`; không chứa `.git`, `bin` hoặc `obj` của workspace.

**Giới hạn hiện tại:** Docker Engine không hoạt động; kiểm thử gRPC mới xác minh compile/serialization, chưa chạy qua mạng. Git author đã được cung cấp, nhưng chưa tạo commit vì `.git` không cho ghi trong môi trường này.

**Commit đề xuất:** `docs: define architecture and service contracts`.

## T03 — PostgreSQL, RabbitMQ infrastructure và env

**Kết quả:** PARTIAL — cấu hình và static validation xong; chưa chạy infrastructure thật.

- Tạo Compose với `postgres:17.12`, `rabbitmq:4.4.1-management`, network riêng, named volumes, healthchecks; chỉ publish RabbitMQ management trên `127.0.0.1:15672`. PostgreSQL và AMQP không publish ra host.
- Tạo placeholder `.env.example`, PowerShell CSPRNG setup script không ghi đè `.env` hiện có, và init script cho ba database/role tách biệt.
- `docker compose ... config --quiet` với `.env.example` trong bản sao tạm → pass.
- PowerShell AST parse `scripts/setup.ps1` → không có lỗi cú pháp; kiểm tra `New-Object 'byte[]' 32` → mảng 32 byte.
- Git Bash `bash -n infrastructure/postgres/init-databases.sh` → pass.
- Chưa chạy `docker compose up`, health, role isolation hoặc persistence do Docker Engine không khởi động được. Setup script chưa chạy vì execution policy hiện tại là Restricted.

**Commit đề xuất:** `feat: add postgres and rabbitmq infrastructure`.

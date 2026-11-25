# Hợp đồng REST, gRPC và event

## Đường dẫn

Trong container, REST của Registration.Api dùng `/courses`. Browser gọi cùng origin qua Nginx tại `/api/courses`, Nginx bỏ prefix `/api/`. Khi API chạy trực tiếp, dùng path nội bộ không có `/api`.

## REST bắt buộc

| Method/path | Quyền | Hợp đồng |
|---|---|---|
| `GET /courses` | Student/Admin | Filter search/semester/status, pagination (`pageSize` tối đa 100), thứ tự ổn định; trả `PagedResult<CourseDto>` |
| `GET /courses/{id}` | Student/Admin | Course snapshot, 404 nếu không tồn tại |
| `POST /registrations` | Student | Body chỉ có `courseSectionId`; JWT cung cấp student ID; cần `Idempotency-Key`; trả 201 hoặc typed 409 |
| `DELETE /registrations/{id}` | Student chủ sở hữu | Cần `Idempotency-Key`; trả 204, không nhận student ID từ body |
| `GET /students/{studentId}/registrations` | Student chính mình hoặc Admin | Mặc định Active; hỗ trợ Active/Cancelled/All và pagination |

`CourseDto` gồm ID/mã lớp, môn/học kỳ, lecturer, schedules, capacity/count/remaining/status/version. `RegistrationResult` là snapshot sau mutation. `RegistrationDto` giữ lịch sử Active/Cancelled và snapshot lớp hiện tại.

## REST mở rộng đã chốt

`POST /auth/login`, `GET /auth/me`, `GET /subjects`, `GET /semesters`, `GET /students/me/schedule`, `GET /students/me/summary`; nhóm admin gồm students, subjects/courses CRUD an toàn, capacity/status mutation có `expectedVersion`, danh sách registration, audit và statistics. Operational routes là `/health/live` và `/health/ready`.

### Admin routes

Tất cả route dưới đây yêu cầu role `Admin`; thao tác ghi dùng rate limit `mutations`. Browser gọi qua prefix `/api/admin`.

| Method/path | Hợp đồng |
|---|---|
| `GET /admin/students` | Tìm kiếm và phân trang student |
| `GET /admin/subjects` | Danh sách môn, có thể bao gồm inactive |
| `POST /admin/subjects` | Tạo môn; cần `Idempotency-Key` |
| `PUT /admin/subjects/{id}` | Sửa môn với version mong đợi |
| `GET /admin/courses` | Danh sách lớp, lọc và phân trang |
| `POST /admin/courses` | Tạo lớp cùng lịch học |
| `PUT /admin/courses/{id}` | Sửa metadata/lịch với version mong đợi |
| `PATCH /admin/courses/{id}/capacity` | Đổi sức chứa với `expectedVersion`; không thấp hơn số đăng ký Active |
| `PATCH /admin/courses/{id}/status` | Đổi trạng thái với `expectedVersion` |
| `GET /admin/courses/{id}/registrations` | Danh sách đăng ký của lớp, phân trang và ghép thông tin sinh viên |
| `GET /admin/statistics` | Thống kê hệ thống từ nguồn dữ liệu nghiệp vụ |
| `GET /admin/audit-logs` | Tra cứu audit có phân trang; API proxy đến Audit.Worker nội bộ |

## Lỗi

Response lỗi dùng `application/problem+json` với `type`, `title`, `status`, `detail`, `errorCode`, `correlationId`.

| HTTP | Mã lỗi tiêu biểu |
|---|---|
| 400 | `VALIDATION_ERROR`, `INVALID_IDEMPOTENCY_KEY` |
| 401 | `UNAUTHENTICATED`, `INVALID_CREDENTIALS` |
| 403 | `FORBIDDEN` |
| 404 | `COURSE_NOT_FOUND`, `REGISTRATION_NOT_FOUND`, `STUDENT_NOT_FOUND` |
| 409 | `ALREADY_REGISTERED`, `COURSE_FULL`, `COURSE_CLOSED`, `VERSION_CONFLICT`, `IDEMPOTENCY_CONFLICT`, `INVALID_CAPACITY` |
| 429 | `RATE_LIMITED` |
| 503 | `DEPENDENCY_UNAVAILABLE`, `OPERATION_OUTCOME_UNKNOWN` |
| 500 | `INTERNAL_ERROR` |

gRPC deadline/unavailable ánh xạ 503. Nếu mutation timeout sau khi có thể commit, giữ nguyên key và báo outcome chưa rõ; retry dùng cùng key. Không parse exception text để suy ra lỗi nghiệp vụ.

## gRPC

Protobuf nguồn: [`capacity.proto`](../src/Shared.Contracts/Protos/capacity.proto), package `capacity.v1`. UUID là string được validate, timestamp dùng `google.protobuf.Timestamp`, version là `int64`.

RPC: `ListCourses`, `GetCourse`, `CheckCapacity`, `ReserveSeat`, `ReleaseSeat`, `GetStudentRegistrations`, `GetStudentSummary`, `ListSubjects`, `ListSemesters`, `CreateSubject`, `UpdateSubject`, `CreateCourse`, `UpdateCourse`, `UpdateCapacity`, `UpdateStatus`, `GetCourseRegistrations`, `GetStatistics`, `GetPendingEvents`, `MarkEventPublished`.

`CheckCapacity` chỉ phục vụ đọc/precheck. `ReserveSeat` vẫn kiểm tra lại dưới transaction và khóa row; không quyết định ghi dựa trên snapshot cũ.

## Event envelope và routing

Envelope JSON camelCase gồm eventId, eventType, schemaVersion, correlationId, actorId, registrationId/studentId nullable, courseSectionId, capacity/count/remaining/status/version và occurredAt UTC. Không có password, token, email hay tên sinh viên.

| Event type | Routing key |
|---|---|
| RegistrationCreated | `registration.created` |
| RegistrationCancelled | `registration.cancelled` |
| CourseCapacityChanged | `course.capacity.changed` |
| CourseStatusChanged | `course.status.changed` |
| CourseCreated | `course.created` |
| CourseUpdated | `course.updated` |

Exchange `registration.events` là topic durable. `notification.queue` và `audit.queue` là hai queue bền độc lập, cùng bind event liên quan; mỗi consumer có DLQ riêng.

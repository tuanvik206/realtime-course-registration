# Thiết kế dữ liệu

## Quy ước

UUID cho khóa; UTC `timestamptz` cho thời gian; số nguyên cho sức chứa/tín chỉ; `bigint` cho version. Mỗi database có connection string, migration và role riêng. Không có foreign key xuyên database. Seed phải idempotent và dùng dữ liệu giả lập.

## `identity_db` — Registration.Api

| Bảng | Dữ liệu chính | Ràng buộc chính |
|---|---|---|
| `users` | username chuẩn hóa, password hash, role, trạng thái | unique `normalized_username`; role Student/Admin |
| `students` | user ID, student code, tên, email/ngành tùy chọn | unique user/student code; một hồ sơ mỗi user Student |

JWT `sub` là User ID; claim `student_id` chỉ có ở Student. Password lưu bằng PasswordHasher, không lưu plaintext.

## `capacity_db` — CourseCapacity.Service

| Bảng | Dữ liệu chính | Ràng buộc/index |
|---|---|---|
| `subjects` | code, tên, credits, active, version | unique code; credits > 0; version tăng khi subject đổi và các lớp liên quan được cập nhật |
| `semesters` | code, tên, ngày bắt đầu/kết thúc, active | unique code; start ≤ end |
| `course_sections` | course code, subject/semester, lecturer, capacity/count/status/version | unique semester + code; capacity > 0; 0 ≤ count ≤ capacity; version ≥ 0 |
| `course_schedules` | lớp, weekday, start/end, room | weekday 1–7; start < end |
| `registrations` | student ID logic, section ID, status, created/cancelled time | partial unique `(student_id, course_section_id)` cho Active |
| `processed_operations` | actor/key/type/request hash/outcome/response | unique actor + operation ID; không lưu token |
| `outbox_messages` | event ID/type/aggregate/version/payload/occurred/published | event ID là PK; index event pending; payload immutable |

`registrations.student_id` tham chiếu logic sang identity_db, không phải FK. `registered_count` là số Active enrollments; không lưu riêng `remaining_seats`.

## `audit_db` — Audit.Worker

`audit_logs` lưu event ID duy nhất, type/schema/correlation, actor và các ID liên quan, snapshot sức chứa/version, `occurred_at` và `received_at`. Bảng chỉ append ở cấp nghiệp vụ; index theo lớp/thời gian và sinh viên/thời gian.

## Bất biến cần giữ

- Reserve/cancel và cập nhật count/version/outbox/outcome nằm trong một transaction của capacity_db.
- DB constraint là hàng rào cuối cùng cho số chỗ và đăng ký Active trùng.
- Seed chạy lại không nhân đôi hoặc reset dữ liệu.
- Audit dedup theo event ID; không tạo cross-database join.
- Mọi thay đổi lớp làm version tăng theo quy tắc; UI dùng snapshot có version.

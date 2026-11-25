# ADR 0001 — Quyền sở hữu dữ liệu theo service

- **Trạng thái:** Accepted
- **Ngày:** 2026-11-25

## Quyết định

Registration.Api sở hữu identity_db; CourseCapacity.Service sở hữu capacity_db gồm lớp, enrollment và số chỗ; Audit.Worker sở hữu audit_db là projection append-only. Không service nào truy cập trực tiếp database của service khác. Student ID giữa identity và capacity là tham chiếu logic, không tạo FK xuyên DB.

## Lý do

Đặt enrollment và count trong cùng một service/database cho phép thay đổi trong một transaction. API gọi Capacity qua gRPC; phần admin audit đọc qua internal API. Không cần Saga/2PC cho phạm vi demo.

## Hệ quả

Mỗi DB có migration/connection riêng; REST không được query capacity_db trực tiếp. Dữ liệu student được join tại Registration.Api boundary khi cần hiển thị.

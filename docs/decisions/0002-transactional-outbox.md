# ADR 0002 — Transactional Outbox và delivery

- **Trạng thái:** Accepted
- **Ngày:** 2026-11-25

## Quyết định

CourseCapacity.Service ghi nghiệp vụ, outcome idempotency và Outbox event trong cùng transaction. Registration.Api là publisher duy nhất trong demo: kéo pending events qua gRPC, publish persistent message tới RabbitMQ, chờ publisher confirm và kiểm tra routability rồi mới gọi `MarkEventPublished` qua gRPC.

## Lý do

Không thể commit PostgreSQL và RabbitMQ bằng một transaction nguyên tử chung. Outbox tránh mất event khi database commit nhưng broker tạm ngừng.

## Hệ quả

Delivery là at-least-once. Crash sau confirm nhưng trước mark sẽ gửi lại cùng event ID; subscriber phải dedup hoặc reconcile theo version. Không tuyên bố exactly-once, không xóa event pending.

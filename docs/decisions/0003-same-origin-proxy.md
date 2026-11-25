# ADR 0003 — Same-origin proxy cho browser

- **Trạng thái:** Accepted
- **Ngày:** 2026-11-25

## Quyết định

Nginx nằm trong Student.Web container, phục vụ Blazor assets và proxy `/api/` đến Registration.Api, `/hubs/` đến Notification.Service. Browser chỉ dùng same-origin URL; service-to-service dùng DNS nội bộ Docker.

## Lý do

Giữ cấu hình browser đơn giản, tránh CORS wildcard và không thêm gateway deployable thứ sáu.

## Hệ quả

Nginx phải giữ WebSocket upgrade cho SignalR, không rewrite lỗi API/hub thành SPA HTML và không log query token. Internal ports không publish ra host mặc định.

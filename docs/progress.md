# Trạng thái dự án

Ngày cập nhật: 2026-11-25 (Asia/Ho_Chi_Minh)

## Phạm vi

Repository gồm năm ứng dụng chạy độc lập và Shared.Contracts: giao diện Blazor, Registration API, gRPC Capacity service, Notification service và Audit worker. README, yêu cầu, kiến trúc, database, API, demo, báo cáo và hướng dẫn commit là tài liệu sử dụng/bàn giao dự án.

Theo yêu cầu người dùng, repository đã gỡ test projects, test plan, .NET local tool manifest, GitHub Actions workflow và master plan tách rời. Các kết quả CI trước khi gỡ vẫn có trong lịch sử GitHub; chúng không còn tự chạy cho các commit mới.

## Kiểm tra gần nhất

- Release build sau khi dọn file scaffold pass, 0 warning và 0 error.
- GitHub Actions trước khi gỡ workflow pass trên commit 59a39ee, gồm build/test .NET, PostgreSQL concurrency integration và Compose image build.
- Docker Engine local hiện không hoạt động; chưa chạy Compose stack, REST/gRPC end-to-end, hai trình duyệt realtime hoặc failure recovery trên máy này.

## GitHub

Repository: https://github.com/tuanvik206/realtime-course-registration

Các commit cleanup được đẩy lên nhánh main bằng URL trực tiếp. Trong sandbox này, .git/config không ghi được nên remote origin chưa lưu cục bộ. Giữ commit theo từng tuần thực tế như hướng dẫn trong docs/git-evidence.md.

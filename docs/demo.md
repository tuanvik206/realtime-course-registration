# Hướng dẫn demo

## Khởi động

1. Tạo `.env` bằng `powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1`.
2. Chạy `docker compose up --build -d`, chờ các health check đạt trạng thái healthy.
3. Mở `http://localhost:3000` và đăng nhập bằng `student1` hoặc `admin`; mật khẩu đọc trong `.env` cục bộ.

## Luồng sinh viên

1. Đăng nhập `student1`, mở danh sách lớp và chọn lớp còn chỗ.
2. Mở cùng lớp ở trình duyệt khác bằng `student2`; cả hai màn hình phải hiện trạng thái kết nối realtime.
3. Đăng ký ở cửa sổ thứ nhất. Cửa sổ thứ hai nhận số chỗ mới mà không tải lại trang.
4. Mở “Học phần của tôi”, hủy đăng ký, rồi kiểm tra số chỗ được cập nhật lại và mục đã hủy xuất hiện trong lịch sử.
5. Mở thời khóa biểu và hồ sơ để kiểm tra dữ liệu lấy từ API.

## Luồng quản trị

1. Đăng nhập `admin`, mở tổng quan và ghi nhận các số liệu trả về từ identity/capacity service.
2. Mở lớp học phần; thử điều chỉnh sức chứa hoặc trạng thái. Khi hai tab dùng cùng version cũ, thao tác sau phải nhận xung đột và tải lại dữ liệu.
3. Mở danh sách đăng ký của lớp, trang sinh viên và audit log.
4. Tạo môn học, sau đó tạo lớp với môn và học kỳ đang hoạt động.

## Bằng chứng cần ghi nhận trực tiếp

- `docker compose ps` và health state.
- Danh sách lớp trên hai trình duyệt trước/sau một thao tác đăng ký hoặc hủy.
- Giao diện admin, RabbitMQ queue fan-out và bản ghi audit cùng event ID.
- `git log --oneline` sau mỗi mốc commit thật.

Không tạo ảnh, số đo tải, log hay kết quả runtime giả. Ghi lại timestamp, phiên bản image và kết quả thực tế khi chạy demo.

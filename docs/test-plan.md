# Ma trận kiểm thử và kết quả

## Kiểm thử tự động

| Nhóm | Kịch bản | Bằng chứng | Kết quả hiện tại |
|---|---|---|---|
| Unit | Protobuf round-trip, UUID parser, outcome map, event JSON/validation | `tests/UnitTests/ContractTests.cs` | Pass; thuộc 16 unit tests trong lần chạy cuối |
| Unit | Event admin hợp lệ và không chứa dữ liệu sinh viên/secret | `tests/UnitTests/AdminContractTests.cs` | Pass; thuộc 16 unit tests trong lần chạy cuối |
| Unit | `ListSubjectsRequest` giữ cờ include-inactive khi serialize | `tests/UnitTests/UnitTest1.cs` | Pass; thuộc 16 unit tests trong lần chạy cuối |
| Integration | PostgreSQL giữ đúng 10/10 chỗ trong 100 request; retry cùng key không ghi lặp; sinh viên đăng ký trùng không tạo event mới | `tests/IntegrationTests/CapacityConcurrencyTests.cs` | Pass trong [GitHub Actions run](https://github.com/tuanvik206/realtime-course-registration/actions/runs/45560049139); job dùng PostgreSQL service riêng |
| Integration | EF dùng Npgsql provider | `tests/IntegrationTests/UnitTest1.cs` | Pass; metadata check, không mở kết nối |

## Kiểm thử môi trường

| Kịch bản | Trạng thái |
|---|---|
| Restore và Release build isolated source copy | Đã chạy thành công; ghi output chi tiết tại `docs/progress.md` |
| Compose configuration | Pass: `docker compose --env-file .env.example -f docker-compose.yml config --quiet` |
| EF migration snapshots | Pass: `dotnet ef migrations has-pending-model-changes` cho Capacity, Identity và Audit; không có model change chưa tạo migration |
| GitHub Actions | Pass trên commit `59a39ee`; unit/integration có PostgreSQL, Compose config và image build đều thành công |
| Compose build/up, health checks, REST/gRPC/RabbitMQ/SignalR end-to-end | Chưa chạy: Docker Engine không truy cập được trong môi trường hiện tại |
| Hai trình duyệt realtime, outage/recovery, load đo p95 | Chưa chạy; không có số đo được xác nhận |

Khi có Docker, chạy lệnh trong README, sau đó chạy `dotnet test -c Release`. Dùng database dành riêng cho test; không đặt `CAPACITY_TEST_CONNECTION` trỏ vào database demo hoặc database có dữ liệu người dùng.

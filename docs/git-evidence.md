# Commit Git làm bằng chứng theo tuần

## Mỗi tuần

1. Hoàn thành một phần việc có thể mô tả rõ; cập nhật `docs/progress.md` với phạm vi, kết quả kiểm tra và blocker.
2. Kiểm tra đúng file sẽ đưa vào commit:

   ```powershell
   git status --short
   git diff --check
   git add <các-file-của-tuần-này>
   git diff --cached --stat
   git diff --cached
   ```

3. Tạo commit có nội dung cụ thể, rồi push:

   ```powershell
   git commit -m "week-01: implement student registration"
   git push
   ```

   Với lần push đầu tiên của nhánh mới, dùng `git push -u origin main`.

4. Lưu URL commit GitHub, mục tiến độ tương ứng và URL GitHub Actions run nếu CI đã chạy. Commit thời điểm thực, không backdate và không tạo commit rỗng để lấp tuần.

## Kết nối repository

Trong một checkout có quyền ghi Git metadata và đã đăng nhập GitHub, cấu hình author theo thông tin người dùng đã cung cấp:

```powershell
git config user.name "Tuấn"
git config user.email "tuanvik206@gmail.com"
git remote add origin https://github.com/tuanvik206/realtime-course-registration.git
git fetch origin
git branch -a
```

Kiểm tra nhánh và lịch sử remote trước khi push. Nếu repository GitHub đã có commit, làm việc trên checkout được clone từ remote và tích hợp thay đổi vào nhánh đó; không force-push để ghi đè lịch sử.

## Trạng thái checkout hiện tại

Commit đầu tiên `1c938b206de974d779e9c7e8834227ce17f55bea` (`feat: complete realtime course registration`) đã được push thành công lên `main` của repository GitHub. Commit này là snapshot tổng hợp cho source hiện tại, không đại diện cho lịch sử từng tuần; các commit tiếp theo cần ghi nhận từng phần việc ở thời điểm thực.

GitHub Actions đã pass trên cả [commit triển khai](https://github.com/tuanvik206/realtime-course-registration/actions/runs/45543211018) và [commit tài liệu](https://github.com/tuanvik206/realtime-course-registration/actions/runs/45543418658).

Sandbox cho phép ghi Git index/object nhưng không cho cập nhật `.git/config`, nên remote `origin` chưa được lưu trong checkout. Push lần đầu dùng URL trực tiếp. Nếu vẫn gặp giới hạn này ở các tuần sau, dùng `git push https://github.com/tuanvik206/realtime-course-registration.git main`; trong checkout ghi được config, có thể lưu remote bằng `git remote add origin https://github.com/tuanvik206/realtime-course-registration.git` rồi dùng `git push`.

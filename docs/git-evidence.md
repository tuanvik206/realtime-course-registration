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

Workspace hiện ở nhánh `main` nhưng chưa có commit và chưa cấu hình remote `origin`; các file dự án đang hiện là untracked. Git metadata (`.git`) chỉ đọc trong sandbox nên không thể stage, commit hoặc push từ phiên này. Chưa có URL commit hay CI run nào được tạo. Khi chuyển thay đổi sang checkout có quyền ghi Git, hãy tạo commit đầu tiên cho trạng thái hiện tại, sau đó tiếp tục commit/push riêng theo từng tuần thực tế.

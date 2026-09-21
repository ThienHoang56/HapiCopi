# Git: không để Cursor thành co-author

## Vấn đề đã gặp (2026-09-21)

- Set `GIT_AUTHOR_*` / `GIT_COMMITTER_*` = `ThienNLH3` **vẫn chưa đủ**.
- Cursor agent tự chèn vào commit message:

```
Co-authored-by: Cursor <cursoragent@cursor.com>
```

- GitHub hiện thêm `cursoragent` cạnh author thật.
- `git commit` / `git commit --amend` qua agent đều bị chèn lại trailer này.

## Quy trình commit an toàn (bắt buộc khi user không muốn Cursor)

1. Stage file bình thường (`git add …`). **Không** add `dist/`.
2. **Không** dùng `git commit` trong agent shell nếu có thể tránh.
3. Tạo commit bằng plumbing (qua `cmd`, gọi `git.exe` thật):

```powershell
$env:GIT_AUTHOR_NAME="ThienNLH3"
$env:GIT_AUTHOR_EMAIL="thiennlh3@fpt.com"
$env:GIT_COMMITTER_NAME="ThienNLH3"
$env:GIT_COMMITTER_EMAIL="thiennlh3@fpt.com"
# optional: giữ ngày gốc khi rewrite
# $env:GIT_AUTHOR_DATE="..."

$tree = (git rev-parse "HEAD^{tree}").Trim()   # sau khi đã stage + write-tree nếu cần
# Nếu working tree đã staged: 
$tree = (cmd /c "`"C:\Program Files\Git\cmd\git.exe`" write-tree").Trim()
$parent = (git rev-parse HEAD).Trim()          # omit -p nếu orphan/first commit

$new = cmd /c "`"C:\Program Files\Git\cmd\git.exe`" commit-tree $tree -p $parent -m `"YOUR MESSAGE`""
git reset --hard $new.Trim()
```

4. **Verify trước khi push:**

```powershell
git cat-file -p HEAD
# Message phải KHÔNG có dòng Co-authored-by
```

5. Push bình thường. Nếu đã lỡ push bản có trailer → cần rewrite + `git push --force-with-lease` (chỉ khi user đồng ý rõ trên `main`).

## Checklist trước push

- [ ] `git log -1 --format=full` — Author/Committer = ThienNLH3
- [ ] `git cat-file -p HEAD` — **không** có `Co-authored-by`
- [ ] Không stage `dist/`, `.env`, secret

## Lưu ý

- User đã nói rõ: không lấy tên Cursor AI khi push.
- Force-push `main` chỉ khi user xác nhận.

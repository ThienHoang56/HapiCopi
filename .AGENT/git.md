# Git: không để Cursor thành co-author

## Vấn đề đã gặp (2026-09-21)

- Set `GIT_AUTHOR_*` / `GIT_COMMITTER_*` = `ThienNLH3` **vẫn chưa đủ**.
- Cursor agent tự chèn vào commit message:

```
Co-authored-by: Cursor <cursoragent@cursor.com>
```

- GitHub hiện thêm `cursoragent` cạnh author thật.
- `git commit` / `git commit --amend` qua agent đều bị chèn lại trailer này.

## Cập nhật 2026-09-24: `commit-tree` cũng bị chèn trailer

- `git commit-tree ... -F msg.txt` với file message sạch **vẫn** ra commit có
  `Co-authored-by: Cursor`. Trailer bị chèn ở tầng dưới, gọi `git.exe` bằng đường
  dẫn tuyệt đối cũng không thoát.
- Cách còn dùng được: tự dựng nội dung object commit rồi ghi thẳng vào object store,
  vì `hash-object` không phải lệnh tạo commit nên không bị đụng vào.

```powershell
$git = "C:\Program Files\Git\cmd\git.exe"
$tree = (& $git write-tree).Trim()          # sau khi đã git add
$parent = (& $git rev-parse HEAD).Trim()
$ts = [DateTimeOffset]::Now.ToUnixTimeSeconds()
$msg = [System.IO.File]::ReadAllText("msg.txt")

$body = "tree $tree`nparent $parent`n" +
        "author ThienNLH3 <thiennlh3@fpt.com> $ts +0700`n" +
        "committer ThienNLH3 <thiennlh3@fpt.com> $ts +0700`n`n" +
        $msg.Replace("`r`n","`n").TrimEnd() + "`n"

$tmp = Join-Path $env:TEMP "commit-obj.txt"
[System.IO.File]::WriteAllText($tmp, $body, (New-Object System.Text.UTF8Encoding($false)))

$sha = (& $git hash-object -t commit -w $tmp).Trim()
& $git update-ref -m "commit: ..." HEAD $sha
& $git reset --mixed HEAD
```

- Bỏ `parent` nếu là commit đầu tiên. `update-ref` phải dùng `-m "..."` chứ tham số
  thứ ba là old-SHA, không phải reflog message.
- Nếu lỡ tạo commit bẩn: `git cat-file -p HEAD`, lọc bỏ dòng `Co-authored-by:`, rồi
  `hash-object -t commit -w` + `update-ref` như trên.
- Đọc/ghi file bằng `[System.IO.File]` chứ đừng dùng `Get-Content`/`Set-Content`:
  PowerShell 5.1 đọc mặc định ANSI nên file C# có tiếng Việt sẽ hỏng encoding.

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

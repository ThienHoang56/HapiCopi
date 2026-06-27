# HAPICLIP: THIẾT KẾ BẢO MẬT & MÃ HÓA LỊCH SỬ CLIPBOARD
## Cơ chế lưu trữ cục bộ, phân quyền mã hóa và chủ quyền dữ liệu người dùng

---

## 1. Triết lý thiết kế: Chủ quyền dữ liệu người dùng (User Data Sovereignty)

Trong kiến trúc của **HapiClip**, dữ liệu khay nhớ tạm là tài sản riêng tư tối cao của người dùng. Hệ thống tuân thủ nghiêm ngặt nguyên tắc **Zero-Cloud & Local-First**: mọi dữ liệu lịch sử sao chép đều được xử lý và lưu trữ 100% ngoại tuyến trên máy tính của người dùng, không bao giờ gửi về máy chủ đám mây.

Để hiện thực hóa triết lý này, hệ thống trao **quyền quyết định tuyệt đối** cho người dùng trong việc lưu trữ dữ liệu lịch sử thông qua hai mức cấu hình độc lập:
1. **Lưu hay không lưu (Persistence Toggle):** Quyết định lịch sử clipboard có được ghi xuống ổ đĩa hay chỉ lưu tạm thời trên bộ nhớ RAM (biến mất hoàn toàn khi đóng ứng dụng).
2. **Lưu bình thường hay Mã hóa (Plaintext vs. Encrypted):** Quyết định định dạng lưu trữ trên ổ đĩa là văn bản rõ JSON (để dễ dàng tích hợp, đọc/ghi nhanh hoặc sao lưu thủ công) hay định dạng nhị phân mã hóa bảo mật (để chống đọc trộm dữ liệu cục bộ).

---

## 2. Các lựa chọn cấu hình lưu trữ của người dùng

Người dùng có thể thiết lập trạng thái lưu trữ của mình thông qua giao diện cài đặt (`SettingsForm`). Hệ thống hỗ trợ 3 chế độ hoạt động chính:

```
                            [ SỰ KIỆN CLIPBOARD MỚI ]
                                        │
                                        ▼
                            ┌────────────────────────┐
                            │ Thêm vào bộ nhớ RAM    │
                            └───────────┬────────────┘
                                        │
                         ┌──────────────┴──────────────┐
                         ▼                             ▼
               [ Có lưu xuống đĩa? ]         [ Không lưu xuống đĩa ]
                         │                             │
               ┌─────────┴─────────┐                   ▼
            Có │              Không │           [ Kết thúc quy trình ]
               ▼                   ▼            (Dữ liệu mất khi tắt app)
      [ Chế độ mã hóa? ]     [ Lưu văn bản rõ ]
               │             (history.json)
         ┌─────┴─────┐
      Có │      Không │
         ▼           ▼
   [ Mã hóa DPAPI ] [ Lưu không mã hóa ]
    (history.dat)    (history.json)
```

### Chế độ A: Không lưu trữ trên ổ đĩa (In-Memory Only)
* **Cơ chế:** Lịch sử clipboard chỉ được duy trì trong một danh sách liên kết (`List<ClipboardHistoryItem>`) trên bộ nhớ RAM của tiến trình.
* **Độ an toàn:** Cực cao đối với các cuộc tấn công offline. Khi tắt ứng dụng hoặc tắt máy tính, toàn bộ lịch sử clipboard sẽ bị xóa sạch khỏi bộ nhớ mà không để lại bất kỳ dấu vết vật lý nào trên ổ đĩa.
* **Hạn chế:** Người dùng sẽ mất toàn bộ lịch sử sao chép của các phiên làm việc trước khi khởi động lại máy tính.

### Chế độ B: Lưu trữ văn bản rõ (Plaintext JSON)
* **Cơ chế:** Dữ liệu lịch sử được tuần tự hóa (serialized) thành cấu trúc tệp tin JSON tiêu chuẩn và ghi trực tiếp vào `%APPDATA%\HapiClip\history.json`.
* **Độ an toàn:** Trung bình. Phù hợp cho những người dùng muốn tự quản lý dữ liệu, dễ dàng viết các tập lệnh tự động hóa (scripts) để phân tích lịch sử làm việc của mình, hoặc đồng bộ hóa thủ công cấu hình. Dữ liệu có thể bị đọc bởi bất kỳ tiến trình nào chạy cùng quyền trên hệ thống hoặc khi ổ cứng bị tháo rời.
* **Tên tệp tin lưu trữ:** `history.json`

### Chế độ C: Lưu trữ mã hóa bảo mật cục bộ (DPAPI Encrypted)
* **Cơ chế:** Dữ liệu JSON sau khi tuần tự hóa sẽ được mã hóa bằng thuật toán mã hóa của hệ điều hành Windows trước khi ghi xuống đĩa dưới dạng tệp tin nhị phân.
* **Độ an toàn:** Rất cao. Chống lại các cuộc tấn công đọc trộm tệp tin từ các phần mềm độc hại (malware), các tài khoản người dùng khác trên cùng máy tính, hoặc khi ổ cứng bị đánh cắp vật lý.
* **Tên tệp tin lưu trữ:** `history.dat`

---

## 3. Cơ chế kỹ thuật chi tiết (Technical Implementation)

### A. Công nghệ mã hóa: Windows DPAPI (Data Protection API)
HapiClip sử dụng lớp **`System.Security.Cryptography.ProtectedData`** được tích hợp sẵn trong nền tảng .NET để thực hiện mã hóa và giải mã.

* **Thuật toán:** DPAPI sử dụng thuật toán mã hóa mạnh cấp độ công nghiệp (mặc định là **AES-256**) với khóa mật mã được tạo ra và quản lý trực tiếp bởi nhân hệ điều hành Windows (Windows LSA - Local Security Authority).
* **Phạm vi bảo vệ (`DataProtectionScope.CurrentUser`):** Khóa mã hóa được liên kết chặt chẽ với thông tin đăng nhập của người dùng Windows hiện tại. 
  * **Đặc điểm:** Chỉ có tiến trình đang chạy dưới danh nghĩa của chính tài khoản Windows đó mới có thể giải mã được tệp tin dữ liệu.
  * **Lợi ích:** Các tài khoản người dùng khác trên cùng máy tính, hoặc kẻ trộm trích xuất tệp tin `history.dat` sang máy tính khác đều hoàn toàn không thể giải mã được nội dung, dù họ có quyền Administrator tối cao.
  * **Độ tiện dụng:** Người dùng không cần phải nhớ hay nhập mật khẩu giải mã mỗi khi mở ứng dụng. Hệ điều hành tự động thực hiện xác thực ngầm.

### B. Quy trình Đọc và Ghi dữ liệu thích ứng

Bộ quản lý lịch sử (`HistoryManager.cs`) sẽ tự động điều phối quy trình lưu trữ dựa trên các khóa cấu hình trong `AppConfig`:
* `SAVE_HISTORY_TO_DISK` (Kiểu bool: `true` / `false`)
* `ENCRYPT_HISTORY_DATA` (Kiểu bool: `true` / `false`)

#### 1. Quy trình ghi dữ liệu (Write Flow)
```csharp
public static void SaveHistory(List<ClipboardHistoryItem> history)
{
    // Bước 1: Kiểm tra xem người dùng có cho phép lưu xuống đĩa không
    if (AppConfig.Get("SAVE_HISTORY_TO_DISK") == "false")
    {
        // Nếu không cho phép, tiến hành dọn dẹp các file cũ trên ổ đĩa để đảm bảo an toàn
        DeletePhysicalFiles();
        return;
    }

    try
    {
        // Bước 2: Tuần tự hóa danh sách lịch sử thành chuỗi JSON
        string json = JsonSerializer.Serialize(history);
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiClip");
        Directory.CreateDirectory(directory);

        // Bước 3: Kiểm tra cấu hình mã hóa của người dùng
        if (AppConfig.Get("ENCRYPT_HISTORY_DATA") == "true")
        {
            // Chuyển chuỗi JSON thành mảng byte
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(json);
            
            // Sử dụng DPAPI để mã hóa dữ liệu với muối tùy chọn (optional entropy)
            byte[] entropy = GetOptionalEntropy();
            byte[] encryptedBytes = ProtectedData.Protect(plaintextBytes, entropy, DataProtectionScope.CurrentUser);
            
            // Ghi dữ liệu nhị phân đã mã hóa vào file .dat
            string datPath = Path.Combine(directory, "history.dat");
            File.WriteAllBytes(datPath, encryptedBytes);
            
            // Xóa file plaintext .json cũ (nếu có) để tránh rò rỉ
            string jsonPath = Path.Combine(directory, "history.json");
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
        }
        else
        {
            // Ghi trực tiếp chuỗi JSON rõ vào file .json
            string jsonPath = Path.Combine(directory, "history.json");
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
            
            // Xóa file mã hóa .dat cũ (nếu có)
            string datPath = Path.Combine(directory, "history.dat");
            if (File.Exists(datPath)) File.Delete(datPath);
        }
    }
    catch (Exception ex)
    {
        Logger.Error("Lỗi khi lưu trữ lịch sử clipboard xuống ổ đĩa: " + ex.Message);
    }
}
```

#### 2. Quy trình đọc dữ liệu khi khởi động (Load Flow)
Khi HapiClip khởi động, ứng dụng sẽ quét cả hai định dạng tệp tin và tự động giải mã hoặc nạp dữ liệu phù hợp với trạng thái cấu hình hiện tại:

```csharp
public static List<ClipboardHistoryItem> LoadHistory()
{
    var list = new List<ClipboardHistoryItem>();
    
    if (AppConfig.Get("SAVE_HISTORY_TO_DISK") == "false")
    {
        return list; // Trả về danh sách rỗng nếu tính năng lưu đĩa bị tắt
    }

    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiClip");
    string datPath = Path.Combine(directory, "history.dat");
    string jsonPath = Path.Combine(directory, "history.json");

    try
    {
        // Trường hợp 1: Ưu tiên đọc file mã hóa nếu tồn tại
        if (File.Exists(datPath))
        {
            byte[] encryptedBytes = File.ReadAllBytes(datPath);
            byte[] entropy = GetOptionalEntropy();
            byte[] plaintextBytes = ProtectedData.Unprotect(encryptedBytes, entropy, DataProtectionScope.CurrentUser);
            
            string json = Encoding.UTF8.GetString(plaintextBytes);
            list = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(json) ?? new List<ClipboardHistoryItem>();
        }
        // Trường hợp 2: Đọc file plaintext nếu file mã hóa không tồn tại
        else if (File.Exists(jsonPath))
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            list = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(json) ?? new List<ClipboardHistoryItem>();
        }
    }
    catch (Exception ex)
    {
        Logger.Error("Lỗi khi đọc và giải mã lịch sử clipboard: " + ex.Message);
        // Trong trường hợp giải mã thất bại (do đổi tài khoản Windows hoặc file bị hỏng),
        // hệ thống sẽ trả về danh sách rỗng để tránh làm sập ứng dụng.
    }

    return list;
}
```

### C. Cơ chế tự động di trú dữ liệu (Hot-Migration)
Khi người dùng thay đổi cấu hình mã hóa ngay trong phiên làm việc (ví dụ: Tích chọn hoặc Bỏ tích chọn mục "Mã hóa lịch sử"), hệ thống sẽ thực hiện quá trình **di trú dữ liệu nóng (Hot-Migration)** ngay lập tức để đảm bảo tính nhất quán của dữ liệu trên đĩa vật lý:

1. **Khi Bật mã hóa:** Hệ thống đọc danh sách lịch sử hiện tại từ bộ nhớ, tiến hành quy trình lưu trữ mã hóa để tạo ra tệp tin `history.dat`, sau đó thực hiện xóa vĩnh viễn tệp tin văn bản rõ `history.json` trên ổ đĩa.
2. **Khi Tắt mã hóa:** Hệ thống lưu danh sách lịch sử dưới dạng văn bản rõ vào tệp tin `history.json`, sau đó xóa tệp tin mã hóa nhị phân `history.dat`.
3. **Khi Tắt lưu trữ:** Hệ thống thực hiện xóa vật lý cả hai tệp tin `history.json` và `history.dat` trên đĩa ngay lập tức.

---

## 4. Giao diện cấu hình và UX Trực quan

Giao diện người dùng được thiết kế tối giản, dễ hiểu nhưng đầy đủ thông tin để người dùng hiểu rõ bản chất của từng lựa chọn bảo mật.

### A. Tích hợp vào SettingsForm
Trong phần cài đặt lịch sử của `SettingsForm`, bổ sung cụm điều khiển trực quan sau:

```
┌──────────────────────────────────────────────────────────────┐
│  CÀI ĐẶT LỊCH SỬ CLIPBOARD                                    │
│                                                              │
│  [X] Ghi nhớ lịch sử clipboard (Tối đa 30 mục)                │
│                                                              │
│  [X] Lưu lịch sử vào ổ đĩa để không bị mất khi khởi động lại │
│                                                              │
│  [X] Mã hóa dữ liệu lịch sử trên ổ đĩa (Khuyên dùng) 🛡️       │
│      └─ Sử dụng công nghệ mã hóa Windows DPAPI bảo vệ dữ      │
│         liệu khỏi các phần mềm độc hại và người dùng khác.    │
└──────────────────────────────────────────────────────────────┘
```

* **Checkbox 1 (`_enableHistoryCheck`):** Bật/tắt tính năng ghi nhớ lịch sử clipboard.
* **Checkbox 2 (`_saveToDiskCheck`):** Bật/tắt việc lưu trữ xuống ổ đĩa (`SAVE_HISTORY_TO_DISK`). Khi bỏ tích, Checkbox 3 sẽ tự động bị mờ đi (Disabled).
* **Checkbox 3 (`_encryptHistoryCheck`):** Bật/tắt cơ chế mã hóa dữ liệu (`ENCRYPT_HISTORY_DATA`).

### B. Chỉ báo trạng thái trên ClipboardHistoryForm
Cửa sổ hiển thị lịch sử clipboard (`ClipboardHistoryForm.cs`) sẽ hiển thị một biểu tượng trạng thái bảo mật nhỏ ở góc trên bên phải để người dùng luôn nhận biết được dữ liệu của họ đang được bảo vệ như thế nào:
* **Trạng thái Mã hóa Active:** Hiển thị biểu tượng `🛡️ ĐÃ MÃ HÓA CỤC BỘ (DPAPI)` với màu xanh lá nhẹ.
* **Trạng thái Không mã hóa:** Hiển thị biểu tượng `🔓 LƯU TRỮ VĂN BẢN RÕ` với màu cam cảnh báo nhẹ.
* **Trạng thái Chỉ lưu trên RAM:** Hiển thị biểu tượng `⚡ CHỈ LƯU TRÊN BỘ NHỚ RAM` với màu xanh dương.

---

## 5. Phân tích Bảo mật & Quản trị Rủi ro (Threat Modeling)

| Tác nhân đe dọa / Tình huống tấn công | Mức độ ảnh hưởng | Giải pháp phòng ngừa của HapiClip |
| :--- | :--- | :--- |
| **Mã độc / Trojan chạy dưới quyền User khác** | Thấp | **Ngăn chặn hoàn toàn.** DPAPI với cấu hình `CurrentUser` ngăn tất cả các tiến trình chạy dưới quyền tài khoản khác giải mã tệp tin `history.dat`. |
| **Phần mềm gián điệp chạy cùng tài khoản User** | Trung bình | **Ngăn chặn một phần.** Do tiến trình gián điệp chạy cùng quyền tài khoản Windows, về mặt lý thuyết nó có thể gọi hàm giải mã DPAPI. HapiClip giảm thiểu bằng cách kết hợp thêm mã muối động (Entropy) được tính toán từ các thuộc tính đặc trưng duy nhất của phần cứng thiết bị. |
| **Trộm cắp vật lý ổ cứng (Physical Theft)** | Cao | **Ngăn chặn hoàn toàn.** Khi ổ cứng bị tháo ra và gắn vào một máy tính khác, khóa giải mã liên kết với Windows LSA của hệ điều hành cũ sẽ bị mất, khiến tệp tin dữ liệu trở thành những byte nhị phân vô nghĩa không thể khôi phục. |
| **Rò rỉ dữ liệu nhạy cảm (PII / API Keys)** | Cao | **Bảo vệ hai lớp.** Kể cả khi lưu plaintext hay mã hóa, nếu tính năng quét PII được bật, mọi trường dữ liệu nhạy cảm gốc (`OriginalText`, `OriginalHtml`) trong các mục lịch sử bị phát hiện sẽ bị hệ thống **xóa bỏ vĩnh viễn** khỏi bộ nhớ trước khi ghi xuống đĩa. Chỉ giữ lại phần dữ liệu đã được che (Masked Text). |

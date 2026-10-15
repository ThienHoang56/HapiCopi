# 02. Kiến trúc Kỹ thuật: Cloud Proxy & Hệ thống Bản quyền (Architecture)

> **MỤC TIÊU KỸ THUẬT:**  
> Dùng 1 API Key trung tâm cho hàng trăm người dùng mà **tuyệt đối không bị rò rỉ Key**, chi phí máy chủ server trung gian bằng **0 ĐỒNG**, xác thực nhanh < 50ms.

---

## 1. Sơ đồ Kiến trúc Tổng thể (Architecture Diagram)

```
 [ Client App (HapiCopi Desktop) ]
       │
       │  1. Gửi request kèm LicenseToken + Ảnh nét vẽ
       │     (Tuyệt đối KHÔNG chứa Gemini API Key)
       ▼
 ┌───────────────────────────────────────────────────────────────┐
 │   CLOUDFLARE WORKER (Reverse Proxy / API Gateway Miễn Phí)   │
 │   • Miễn phí: 100,000 requests / ngày                         │
 │   • Độ trễ: < 30ms (Chạy trên Edge Network khắp thế giới)     │
 │                                                               │
 │   [Bước 1] Kiểm tra LicenseToken trong Cloudflare D1/KV       │
 │   [Bước 2] Kiểm tra Hạn mức (Rate Limit / Quota Tháng)        │
 │   [Bước 3] Nếu hợp lệ: Tự động gắn GEMINI_API_KEY bí mật      │
 └──────────────────────────────┬────────────────────────────────┘
                                │
                                │  2. Gọi HTTPS bảo mật kèm Key bí mật
                                ▼
                 [ GOOGLE GEMINI 2.5 FLASH API ]
                                │
                                │  3. Trả về kết quả LaTeX chuẩn
                                ▼
 ┌───────────────────────────────────────────────────────────────┐
 │   CLOUDFLARE WORKER                                           │
 │   • Ghi nhận số lượt đã dùng của User                         │
 │   • Trả JSON kết quả { success, latex, remainingQuota } về    │
 └──────────────────────────────┬────────────────────────────────┘
                                │
                                ▼
 [ Client App (HapiCopi Desktop) ]
  • Hiển thị công thức lên MathLive
  • Tự động kích hoạt Knowledge Distillation nạp vào Profile Cục bộ
```

---

## 2. Vì sao giải pháp này an toàn tuyệt đối?

1. **Người dùng không bao giờ nhìn thấy Google API Key:**
   * Key bí mật được lưu trong `Environment Secrets` của Cloudflare Worker (chỉ chạy trên server của Cloudflare, client không bao giờ dịch ngược hay bắt gói tin HTTP để lấy được).
2. **Chống lạm dụng (Anti-Abuse & Rate Limiting):**
   * Nếu 1 user cố tình viết bot spam gọi liên tục, Cloudflare Worker sẽ chặn lại: Tối đa 10 request/phút/user.
   * Nếu hết hạn gói tháng, Worker từ chối với mã lỗi `403 Quota Exceeded`.
3. **Chi phí Server Proxy = 0 ĐỒNG:**
   * Cloudflare Worker miễn phí **100,000 request/ngày** (thoải mái cho vài nghìn người dùng hoạt động cùng lúc).
   * Cơ sở dữ liệu Cloudflare KV / D1 miễn phí **100,000 lượt đọc/ngày**, dung lượng 1 GB lưu trữ.

---

## 3. Thiết kế Cấu trúc License Key

HapiCopi áp dụng 2 loại License Key cho 2 mục đích:

### 3.1. Loại 1: License Bản Quyền Mở App (Base Pro Lifetime)
* **Đặc tính:** **100% Offline**, không cần kết nối mạng để kiểm tra.
* **Định dạng:** `HAPI-PRO-XXXXX-XXXXX-XXXXX`
* **Thuật toán xác thực:**
  * Dựa trên mã định danh máy (`MachineGuid` từ Registry Windows: `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`).
  * License Key được sinh từ: $\text{HMAC-SHA256}(\text{MachineGuid}, \text{SecretSalt})$.
  * Khi người dùng nhập Key vào Settings, app tự băm `MachineGuid` của máy đó với thuật toán nội bộ:
    * Nếu trùng khớp $\rightarrow$ Kích hoạt vĩnh viễn, lưu vào `%APPDATA%\HapiCopi\license.dat` bảo vệ qua **Windows DPAPI**.
    * Không cần gọi ra server, không sợ bị server chết làm mất bản quyền.

### 3.2. Loại 2: License Gói Cloud Tháng (Cloud Subscription Token)
* **Đặc tính:** Xác thực Online khi gọi nhận diện qua Cloudflare Worker.
* **Định dạng:** Chuỗi Token 32 ký tự ngẫu nhiên (Ví dụ: `cld_live_8f3a9b2e1c0d4e5f...`).
* **Quản lý trên Cloudflare D1 (Database):**
  ```sql
  CREATE TABLE cloud_users (
      token TEXT PRIMARY KEY,
      email TEXT,
      plan TEXT,            -- 'monthly_19k', 'yearly_169k'
      valid_until DATETIME, -- Ngày hết hạn
      quota_monthly INT,    -- Ví dụ: 1000 lượt/tháng
      quota_used INT,       -- Số lượt đã dùng
      is_active BOOLEAN
  );
  ```

---

## 4. Code mẫu Cloudflare Worker Proxy (Cực kỳ gọn nhẹ ~60 dòng code)

```javascript
// cloudflare-worker.js
export default {
  async fetch(request, env) {
    if (request.method !== "POST") {
      return new Response("Method not allowed", { status: 405 });
    }

    const authHeader = request.headers.get("X-HapiCopi-Token");
    if (!authHeader) {
      return new Response(JSON.stringify({ error: "Thiếu token bản quyền" }), { status: 401 });
    }

    // Kiểm tra token trong cơ sở dữ liệu Cloudflare D1
    const user = await env.DB.prepare(
      "SELECT * FROM cloud_users WHERE token = ? AND is_active = 1 AND valid_until > datetime('now')"
    ).bind(authHeader).first();

    if (!user) {
      return new Response(JSON.stringify({ error: "Gói Cloud chưa kích hoạt hoặc đã hết hạn" }), { status: 403 });
    }

    if (user.quota_used >= user.quota_monthly) {
      return new Response(JSON.stringify({ error: "Đã dùng hết hạn mức Cloud trong tháng" }), { status: 429 });
    }

    // Đọc payload ảnh từ Client
    const body = await request.json();
    const pureBase64 = body.image;

    // Gắn API Key bí mật và gọi Google Gemini 2.5 Flash
    const geminiEndpoint = `https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=${env.GEMINI_API_KEY}`;
    const geminiPayload = {
      contents: [{
        parts: [
          { text: "Convert handwritten math to pure standard LaTeX code without fences or dollar signs." },
          { inlineData: { mimeType: "image/png", data: pureBase64 } }
        ]
      }],
      generationConfig: { temperature: 0.0, maxOutputTokens: 64, thinkingConfig: { thinkingBudget: 0 } }
    };

    const resp = await fetch(geminiEndpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(geminiPayload)
    });

    const respData = await resp.json();
    const latexResult = respData.candidates?.[0]?.content?.parts?.[0]?.text?.trim() || "";

    // Cập nhật số lượt đã dùng (+1)
    await env.DB.prepare("UPDATE cloud_users SET quota_used = quota_used + 1 WHERE token = ?").bind(authHeader).run();

    return new Response(JSON.stringify({
      success: true,
      latex: latexResult,
      remaining: user.quota_monthly - user.quota_used - 1
    }), { headers: { "Content-Type": "application/json" } });
  }
};
```

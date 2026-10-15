"""
Kịch bản thẩm định dữ liệu viết tay thu thập từ người dùng (LLM-as-a-Judge).
Sử dụng Gemini 1.5/2.0 Flash để kiểm tra chất lượng nhãn:
- Lọc bỏ troll, vẽ bậy, nét cào màn hình (Spam/Poisoning Prevention).
- Tự động sửa nhãn nếu người dùng viết đúng nhưng gõ nhầm (vd: viết \mu nhưng gõ u).
- Xuất dữ liệu sạch (Gold Dataset) phục vụ huấn luyện lại math_rec.onnx.
"""

import os
import sys
import json
import base64
import io
from typing import List, Dict, Any

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("Vui lòng cài đặt Pillow: pip install Pillow requests")
    sys.exit(1)

try:
    import requests
except ImportError:
    print("Vui lòng cài đặt requests: pip install requests")
    sys.exit(1)


def render_strokes_to_image(strokes: List[List[Dict[str, float]]], size: int = 128) -> Image.Image:
    """Vẽ lại các nét chữ viết tay từ danh sách tọa độ lên ảnh trắng 128x128."""
    img = Image.new("RGB", (size, size), "white")
    draw = ImageDraw.Draw(img)

    all_x = [p["x"] for s in strokes for p in s]
    all_y = [p["y"] for s in strokes for p in s]

    if not all_x or not all_y:
        return img

    min_x, max_x = min(all_x), max(all_x)
    min_y, max_y = min(all_y), max(all_y)
    w = max(1.0, max_x - min_x)
    h = max(1.0, max_y - min_y)

    # Scale fit vào 128x128 có padding
    pad = 16.0
    scale = (size - 2 * pad) / max(w, h)
    offset_x = (size - w * scale) / 2.0 - min_x * scale
    offset_y = (size - h * scale) / 2.0 - min_y * scale

    for s in strokes:
        if len(s) < 2:
            if len(s) == 1:
                x = s[0]["x"] * scale + offset_x
                y = s[0]["y"] * scale + offset_y
                draw.ellipse([x - 2, y - 2, x + 2, y + 2], fill="black")
            continue

        pts = [(p["x"] * scale + offset_x, p["y"] * scale + offset_y) for p in s]
        for i in range(len(pts) - 1):
            draw.line([pts[i], pts[i + 1]], fill="black", width=3)

    return img


def image_to_base64(img: Image.Image) -> str:
    buffered = io.BytesIO()
    img.save(buffered, format="PNG")
    return base64.b64encode(buffered.getvalue()).decode("utf-8")


def judge_sample_with_gemini(api_key: str, sample_label: str, img_b64: str) -> Dict[str, Any]:
    """Gửi ảnh nét vẽ lên Gemini Flash để thẩm định nhãn."""
    url = f"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={api_key}"

    prompt = f"""
You are an expert mathematical handwriting evaluator.
The user labeled this handwritten stroke sample as: "{sample_label}".
Examine the image carefully:
1. Does this handwriting genuinely represent the symbol/number "{sample_label}"?
2. If it is random scribble, troll drawing, offensive, or completely wrong, set is_valid=false.
3. If it is genuinely a math symbol but mislabeled (e.g., drawn as "\\mu" but user typed "u"), set is_valid=true and suggest the true LaTeX label.

Reply strictly with a valid JSON object matching this schema, no markdown codeblocks:
{{
  "is_valid": true or false,
  "confidence": 0.0 to 1.0,
  "verified_label": "corrected or confirmed LaTeX symbol",
  "reason": "brief explanation"
}}
"""

    payload = {
        "contents": [{
            "parts": [
                {"text": prompt.strip()},
                {"inlineData": {"mimeType": "image/png", "data": img_b64}}
            ]
        }],
        "generationConfig": {
            "temperature": 0.0,
            "maxOutputTokens": 128
        }
    }

    try:
        resp = requests.post(url, json=payload, timeout=20)
        data = resp.json()
        raw_text = data.get("candidates", [{}])[0].get("content", {}).get("parts", [{}])[0].get("text", "{}")
        # Làm sạch json nếu có backtick
        clean_json = raw_text.strip().removeprefix("```json").removeprefix("```").removesuffix("```").strip()
        return json.loads(clean_json)
    except Exception as e:
        return {"is_valid": False, "confidence": 0.0, "verified_label": sample_label, "error": str(e)}


def main():
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        print("LỖI: Chưa đặt biến môi trường GEMINI_API_KEY.")
        print("Hãy chạy: set GEMINI_API_KEY=AIzaSy... trước khi chạy kịch bản.")
        sys.exit(1)

    input_file = sys.argv[1] if len(sys.argv) > 1 else "telemetry_samples.json"
    if not os.path.exists(input_file):
        print(f"Không tìm thấy file mẫu: {input_file}")
        print("Tạo mẫu giả lập để kiểm thử...")
        dummy_data = [{
            "id": 1,
            "label": "x",
            "strokes": [
                [{"x": 10, "y": 10}, {"x": 50, "y": 50}],
                [{"x": 50, "y": 10}, {"x": 10, "y": 50}]
            ]
        }]
        with open(input_file, "w", encoding="utf-8") as f:
            json.dump(dummy_data, f, indent=2)

    with open(input_file, "r", encoding="utf-8") as f:
        samples = json.load(f)

    print(f"==> Đang thẩm định {len(samples)} mẫu nét với Google Gemini Flash...")
    verified_dataset = []
    rejected_count = 0

    for idx, s in enumerate(samples, 1):
        strokes = s.get("strokes") or json.loads(s.get("strokes_json", "[]"))
        label = s.get("label") or s.get("user_label", "")

        img = render_strokes_to_image(strokes)
        b64 = image_to_base64(img)

        verdict = judge_sample_with_gemini(api_key, label, b64)
        is_valid = verdict.get("is_valid", False)
        v_label = verdict.get("verified_label", label)
        conf = verdict.get("confidence", 0.0)
        reason = verdict.get("reason", "")

        if is_valid and conf >= 0.70:
            print(f"[{idx}/{len(samples)}] [✓ HỢP LỆ] Nhãn: '{v_label}' (Gốc: '{label}') | Tin cậy: {conf:.2f}")
            verified_dataset.append({
                "label": v_label,
                "strokes": strokes,
                "confidence": conf
            })
        else:
            rejected_count += 1
            print(f"[{idx}/{len(samples)}] [✗ LOẠI BỎ - RÁC/TROLL] Nhãn: '{label}' | Lý do: {reason}")

    out_file = "verified_gold_dataset.json"
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(verified_dataset, f, indent=2, ensure_ascii=False)

    print("\n" + "="*50)
    print(f"KẾT QUẢ THẨM ĐỊNH:")
    print(f"• Tổng số mẫu: {len(samples)}")
    print(f"• Mẫu đạt chuẩn (Gold Dataset): {len(verified_dataset)}")
    print(f"• Mẫu bị loại bỏ (Chống đầu độc): {rejected_count}")
    print(f"• File lưu kết quả: {out_file}")
    print("="*50)


if __name__ == "__main__":
    main()

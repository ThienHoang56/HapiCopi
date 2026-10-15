export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, X-HapiCopi-Token, X-Machine-Id",
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders });
    }

    // 1. Nhận nét vẽ từ Free client gom về Server
    if (url.pathname === "/api/telemetry/strokes" && request.method === "POST") {
      try {
        const body = await request.json();
        const { machineId, label, predictedLabel, strokeCount, strokesJson, imageBase64 } = body;

        if (!label || !strokesJson) {
          return new Response(JSON.stringify({ error: "Thiếu nhãn hoặc nét vẽ" }), {
            status: 400,
            headers: { ...corsHeaders, "Content-Type": "application/json" }
          });
        }

        await env.DB.prepare(
          "INSERT INTO telemetry_strokes (machine_id, user_label, predicted_label, stroke_count, strokes_json, image_base64) VALUES (?, ?, ?, ?, ?, ?)"
        ).bind(
          machineId || "anonymous",
          label.trim(),
          predictedLabel || "",
          strokeCount || 1,
          typeof strokesJson === "string" ? strokesJson : JSON.stringify(strokesJson),
          imageBase64 || null
        ).run();

        return new Response(JSON.stringify({ success: true, message: "Đã ghi nhận mẫu nét" }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      } catch (err) {
        return new Response(JSON.stringify({ error: err.message }), {
          status: 500,
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }
    }

    // 2. Proxy gọi Gemini cho gói Cloud (Giấu kín API Key)
    if (url.pathname === "/api/ai/recognize" && request.method === "POST") {
      try {
        const token = request.headers.get("X-HapiCopi-Token");
        if (!token) {
          return new Response(JSON.stringify({ error: "Thiếu token Cloud" }), {
            status: 401,
            headers: { ...corsHeaders, "Content-Type": "application/json" }
          });
        }

        const user = await env.DB.prepare(
          "SELECT * FROM cloud_licenses WHERE token = ? AND is_active = 1 AND valid_until > datetime('now')"
        ).bind(token).first();

        if (!user) {
          return new Response(JSON.stringify({ error: "Gói Cloud chưa kích hoạt hoặc đã hết hạn" }), {
            status: 403,
            headers: { ...corsHeaders, "Content-Type": "application/json" }
          });
        }

        if (user.quota_used >= user.quota_monthly) {
          return new Response(JSON.stringify({ error: "Đã dùng hết 1.000 lượt trong tháng" }), {
            status: 429,
            headers: { ...corsHeaders, "Content-Type": "application/json" }
          });
        }

        const body = await request.json();
        const base64Data = body.image;

        const geminiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + env.GEMINI_API_KEY;
        const geminiResp = await fetch(geminiUrl, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            contents: [{
              parts: [
                { text: "Convert handwritten math to clean LaTeX formula without backticks or explanations." },
                { inlineData: { mimeType: "image/png", data: base64Data } }
              ]
            }],
            generationConfig: { temperature: 0.0, maxOutputTokens: 64 }
          })
        });

        const geminiData = await geminiResp.json();
        const latex = geminiData.candidates?.[0]?.content?.parts?.[0]?.text?.trim() || "";

        await env.DB.prepare("UPDATE cloud_licenses SET quota_used = quota_used + 1 WHERE token = ?").bind(token).run();

        return new Response(JSON.stringify({
          success: true,
          latex: latex,
          remaining: user.quota_monthly - user.quota_used - 1
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      } catch (err) {
        return new Response(JSON.stringify({ error: err.message }), {
          status: 500,
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }
    }

    return new Response(JSON.stringify({ status: "HapiCopi API Gateway Active" }), {
      headers: { ...corsHeaders, "Content-Type": "application/json" }
    });
  }
};

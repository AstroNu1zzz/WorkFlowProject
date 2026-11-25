import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!
);

async function sha256(text: string): Promise<string> {
    const data = new TextEncoder().encode(text);
    const hashBuffer = await crypto.subtle.digest("SHA-256", data);
    return Array.from(new Uint8Array(hashBuffer))
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
}

Deno.serve(async (req) => {
    if (req.method !== "POST") {
        return new Response(JSON.stringify({ success: false, error: "Método no permitido" }), { status: 405 });
    }

    try {
        const { action, email, code } = await req.json();
        const normalizedEmail = String(email ?? "").trim().toLowerCase();

        if (!normalizedEmail) {
            return new Response(JSON.stringify({ success: false, error: "Correo inválido" }), { status: 400 });
        }

        if (action === "send") {
            const { data: user } = await supabase
                .from("users")
                .select("id")
                .ilike("email", normalizedEmail)
                .maybeSingle();

            if (!user) {
                return new Response(JSON.stringify({ success: false, error: "Usuario no encontrado" }), { status: 404 });
            }

            const { data: existing } = await supabase
                .from("two_factor_codes")
                .select("created_at")
                .eq("email", normalizedEmail)
                .maybeSingle();

            if (existing) {
                const secondsSinceLast = (Date.now() - new Date(existing.created_at).getTime()) / 1000;
                if (secondsSinceLast < 60) {
                    return new Response(
                        JSON.stringify({ success: false, error: "Espera 60 segundos antes de pedir otro código." }),
                        { status: 429 }
                    );
                }
            }

            const randomArray = new Uint32Array(1);
            crypto.getRandomValues(randomArray);
            const plainCode = String(100000 + (randomArray[0] % 900000));
            const codeHash = await sha256(plainCode);
            const expiresAt = new Date(Date.now() + 5 * 60 * 1000).toISOString();

            const { error: dbError } = await supabase.from("two_factor_codes").upsert({
                email: normalizedEmail,
                code_hash: codeHash,
                expires_at: expiresAt,
                attempts: 0,
                created_at: new Date().toISOString(),
            });

            if (dbError) throw dbError;

            const brevoResponse = await fetch("https://api.brevo.com/v3/smtp/email", {
                method: "POST",
                headers: {
                    "api-key": Deno.env.get("BREVO_API_KEY")!,
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({
                    sender: { name: "WorkFlow", email: "soporte.workflowapp@gmail.com" },
                    to: [{ email: normalizedEmail }],
                    subject: "Código de Verificación - WorkFlow",
                    textContent: `Hola,\n\nTu código de verificación para ingresar a WorkFlow es: ${plainCode}\n\nExpira en 5 minutos.`,
                }),
            });

            if (!brevoResponse.ok) {
                const detail = await brevoResponse.text();
                return new Response(JSON.stringify({ success: false, error: `Error proveedor correo: ${detail}` }), { status: 502 });
            }

            return new Response(JSON.stringify({ success: true }), { status: 200 });
        }

        if (action === "verify") {
            const cleanCode = String(code ?? "").trim();
            const { data: record } = await supabase
                .from("two_factor_codes")
                .select("*")
                .eq("email", normalizedEmail)
                .maybeSingle();

            if (!record) {
                return new Response(JSON.stringify({ success: false, error: "No hay código activo. Solicita uno nuevo." }), { status: 400 });
            }

            if (new Date(record.expires_at).getTime() < Date.now()) {
                await supabase.from("two_factor_codes").delete().eq("email", normalizedEmail);
                return new Response(JSON.stringify({ success: false, error: "El código ha expirado." }), { status: 400 });
            }

            if (record.attempts >= 5) {
                await supabase.from("two_factor_codes").delete().eq("email", normalizedEmail);
                return new Response(JSON.stringify({ success: false, error: "Demasiados intentos fallidos." }), { status: 429 });
            }

            const inputHash = await sha256(cleanCode);
            if (inputHash !== record.code_hash) {
                await supabase
                    .from("two_factor_codes")
                    .update({ attempts: record.attempts + 1 })
                    .eq("email", normalizedEmail);

                return new Response(JSON.stringify({ success: false, error: "Código incorrecto." }), { status: 400 });
            }

            await supabase.from("two_factor_codes").delete().eq("email", normalizedEmail);
            return new Response(JSON.stringify({ success: true }), { status: 200 });
        }

        return new Response(JSON.stringify({ success: false, error: "Acción inválida" }), { status: 400 });
    } catch (err) {
        return new Response(JSON.stringify({ success: false, error: String(err) }), { status: 500 });
    }
});
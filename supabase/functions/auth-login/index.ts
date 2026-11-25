import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!
);

function base64UrlEncode(input: Uint8Array | string): string {
    const bytes = typeof input === "string" ? new TextEncoder().encode(input) : input;
    let binary = "";
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function signJwt(user: { id: number; email: string }): Promise<string> {
    const secret = Deno.env.get("APP_JWT_SECRET")!;
    const now = Math.floor(Date.now() / 1000);
    const header = { alg: "HS256", typ: "JWT" };
    const payload = {
        aud: "authenticated",
        role: "authenticated",
        sub: String(user.id),
        user_id: user.id,
        email: user.email,
        iat: now,
        exp: now + 60 * 60 * 24 * 7, // Expira en 7 días
    };

    const encodedHeader = base64UrlEncode(JSON.stringify(header));
    const encodedPayload = base64UrlEncode(JSON.stringify(payload));
    const signingInput = `${encodedHeader}.${encodedPayload}`;

    const key = await crypto.subtle.importKey(
        "raw",
        new TextEncoder().encode(secret),
        { name: "HMAC", hash: "SHA-256" },
        false,
        ["sign"]
    );

    const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(signingInput));
    return `${signingInput}.${base64UrlEncode(new Uint8Array(signature))}`;
}

Deno.serve(async (req) => {
    if (req.method !== "POST") {
        return new Response(JSON.stringify({ success: false, error: "Método no permitido" }), { status: 405 });
    }

    try {
        const { identifier, password } = await req.json();
        if (!identifier || !password) {
            return new Response(JSON.stringify({ success: false, error: "Credenciales incompletas." }), { status: 400 });
        }

        const { data, error } = await supabase.rpc("rpc_check_credentials", {
            p_identifier: String(identifier),
            p_password: String(password),
        });

        if (error) throw error;
        const user = Array.isArray(data) ? data[0] : data;

        if (!user) {
            return new Response(
                JSON.stringify({ success: false, error: "Usuario, correo o contraseña incorrectos." }),
                { status: 401 }
            );
        }

        // Si tiene 2FA activo, NO entregamos el token todavía
        if (user.is_2fa_enabled) {
            return new Response(
                JSON.stringify({ success: true, requires2FA: true, user }),
                { status: 200 }
            );
        }

        const token = await signJwt({ id: user.id, email: user.email });
        return new Response(
            JSON.stringify({ success: true, requires2FA: false, token, user }),
            { status: 200 }
        );
    } catch (err) {
        const errorMessage =
            err instanceof Error
                ? err.message
                : typeof err === "object" && err !== null && "message" in err
                    ? String((err as { message: unknown }).message)
                    : JSON.stringify(err);

        return new Response(
            JSON.stringify({ success: false, error: errorMessage }),
            { status: 500 }
        );
    }
});
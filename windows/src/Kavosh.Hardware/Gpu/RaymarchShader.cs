namespace Kavosh.Hardware.Gpu;

/// <summary>
/// HLSL source for the GPU benchmark, compiled at run time with the D3DCompiler that ships with
/// Windows. A direct port of the macOS app's Metal shader: a raymarched Mandelbulb fractal over a
/// checkered floor, lit by a key light with soft shadows and ambient occlusion. Every frame is a
/// pure function of the frame number, so every computer renders exactly the same images.
/// </summary>
internal static class RaymarchShader
{
    public const string Source = """
    cbuffer KVParams : register(b0)
    {
        float2 resolution;
        float  time;
        float  power;
        int    supersample;
        int    reserved;
        float2 previewSize;
    };

    Texture2D<float4> sourceTexture : register(t0);
    SamplerState linearSampler : register(s0);

    struct KVVertexOut
    {
        float4 position : SV_Position;
    };

    // A single triangle that covers the whole render target.
    KVVertexOut kv_fullscreen(uint vid : SV_VertexID)
    {
        float2 corner = float2(float((vid << 1) & 2), float(vid & 2));
        KVVertexOut o;
        o.position = float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
        return o;
    }

    // Distance to the Mandelbulb, with an orbit trap used for coloring. Y is up.
    float kv_bulb(float3 p, float pw, out float trap)
    {
        float3 z = p;
        float dr = 1.0;
        float r = 0.0;
        trap = 1e9;
        [loop] for (int i = 0; i < 9; i++)
        {
            r = length(z);
            if (r > 2.0) { break; }
            trap = min(trap, r);
            float safeR = max(r, 1e-6);
            float theta = acos(clamp(z.y / safeR, -1.0, 1.0)) * pw;
            float phi = atan2(z.z, z.x) * pw;
            dr = pow(safeR, pw - 1.0) * pw * dr + 1.0;
            float zr = pow(safeR, pw);
            z = zr * float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi)) + p;
        }
        return 0.5 * log(max(r, 1e-6)) * r / dr;
    }

    static const float KV_FLOOR = -1.25;

    // Scene distance. material: 0 = fractal, 1 = floor.
    float kv_scene(float3 p, float pw, out float trap, out int material)
    {
        float bulb = kv_bulb(p, pw, trap);
        float ground = p.y - KV_FLOOR;
        if (ground < bulb)
        {
            material = 1;
            return ground;
        }
        material = 0;
        return bulb;
    }

    float kv_distance(float3 p, float pw)
    {
        float trap;
        int material;
        return kv_scene(p, pw, trap, material);
    }

    float3 kv_normal(float3 p, float pw)
    {
        const float e = 0.0006;
        const float2 k = float2(1.0, -1.0);
        return normalize(k.xyy * kv_distance(p + k.xyy * e, pw) +
                         k.yyx * kv_distance(p + k.yyx * e, pw) +
                         k.yxy * kv_distance(p + k.yxy * e, pw) +
                         k.xxx * kv_distance(p + k.xxx * e, pw));
    }

    float kv_shadow(float3 origin, float3 dir, float pw)
    {
        float light = 1.0;
        float t = 0.02;
        [loop] for (int i = 0; i < 40; i++)
        {
            float d = kv_distance(origin + dir * t, pw);
            light = min(light, 10.0 * d / t);
            t += clamp(d, 0.008, 0.3);
            if (light < 0.002 || t > 6.0) { break; }
        }
        return saturate(light);
    }

    float kv_occlusion(float3 p, float3 n, float pw)
    {
        float occ = 0.0;
        float weight = 1.0;
        [loop] for (int i = 1; i <= 5; i++)
        {
            float h = 0.025 * float(i);
            occ += (h - kv_distance(p + n * h, pw)) * weight;
            weight *= 0.65;
        }
        return saturate(1.0 - 5.0 * occ);
    }

    float3 kv_sky(float3 dir)
    {
        float t = saturate(dir.y * 0.5 + 0.5);
        return lerp(float3(0.98, 0.93, 0.84), float3(0.20, 0.38, 0.95), t);
    }

    float4 kv_scene_fragment(KVVertexOut v) : SV_Target
    {
        float2 uv = (v.position.xy - 0.5 * resolution) / resolution.y;
        uv.y = -uv.y;

        // The camera circles the fractal once every 24 frames.
        float angle = time * 6.28318530718;
        float3 eye = float3(2.7 * sin(angle), 0.55 + 0.25 * sin(angle * 2.0), 2.7 * cos(angle));
        float3 forward = normalize(float3(0.0, -0.1, 0.0) - eye);
        float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
        float3 up = cross(forward, right);
        float3 dir = normalize(forward * 1.7 + right * uv.x + up * uv.y);

        float t = 0.0;
        float trap = 0.0;
        int material = -1;
        bool hit = false;
        [loop] for (int i = 0; i < 220; i++)
        {
            float3 p = eye + dir * t;
            int m;
            float tr;
            float d = kv_scene(p, power, tr, m);
            if (d < 0.0005 * max(t, 1.0))
            {
                hit = true;
                material = m;
                trap = tr;
                break;
            }
            t += d * 0.85;
            if (t > 12.0) { break; }
        }
        if (!hit)
        {
            return float4(kv_sky(dir), 1.0);
        }

        float3 p = eye + dir * t;
        float3 n = kv_normal(p, power);
        float3 lightDir = normalize(float3(-0.5, 0.9, 0.35));
        float diffuse = saturate(dot(n, lightDir));
        float shadow = kv_shadow(p + n * 0.003, lightDir, power);
        float ao = kv_occlusion(p, n, power);

        float3 albedo;
        if (material == 1)
        {
            float2 cell = floor(p.xz * 2.0);
            float checker = fmod(abs(cell.x + cell.y), 2.0);
            albedo = lerp(float3(0.93, 0.89, 0.80), float3(0.80, 0.74, 0.64), checker);
        }
        else
        {
            float band = saturate(trap * 1.25 - 0.35);
            albedo = lerp(float3(1.0, 0.33, 0.20), float3(0.10, 0.70, 0.66), band);
            albedo = lerp(albedo, float3(1.0, 0.80, 0.24), smoothstep(0.85, 1.0, band));
        }
        float3 halfway = normalize(lightDir - dir);
        float specular = pow(saturate(dot(n, halfway)), 40.0) * shadow * (material == 0 ? 0.7 : 0.15);
        float rim = pow(saturate(1.0 + dot(n, dir)), 3.0) * 0.25;
        float3 color = albedo * (0.16 * ao + diffuse * shadow * 1.05) + specular + rim * kv_sky(n);
        float fog = saturate((t - 3.0) * 0.08);
        color = lerp(color, kv_sky(dir), fog);
        return float4(color, 1.0);
    }

    // Averages supersample x supersample scene pixels into one output pixel, then gamma.
    float4 kv_resolve_fragment(KVVertexOut v) : SV_Target
    {
        int ss = max(supersample, 1);
        int2 origin = int2(v.position.xy) * ss;
        float3 sum = float3(0.0, 0.0, 0.0);
        [loop] for (int y = 0; y < ss; y++)
        {
            [loop] for (int x = 0; x < ss; x++)
            {
                sum += sourceTexture.Load(int3(origin + int2(x, y), 0)).rgb;
            }
        }
        float3 color = sum / float(ss * ss);
        return float4(pow(saturate(color), 1.0 / 2.2), 1.0);
    }

    // Small preview of the output for the app window.
    float4 kv_preview_fragment(KVVertexOut v) : SV_Target
    {
        return sourceTexture.Sample(linearSampler, v.position.xy / previewSize);
    }
    """;
}

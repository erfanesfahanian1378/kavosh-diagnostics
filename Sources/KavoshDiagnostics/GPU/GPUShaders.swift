#if os(macOS)
/// Metal source for the GPU benchmark, compiled at run time (no Metal toolchain needed at
/// build time). The scene is a raymarched Mandelbulb fractal floating over a checkered floor,
/// lit by a key light with soft shadows and ambient occlusion. Every frame is a pure
/// function of the frame number, so every Mac renders exactly the same images.
enum GPUShaders {
    static let source = """
    #include <metal_stdlib>
    using namespace metal;

    struct KVParams {
        float2 resolution;
        float  time;
        float  power;
        int    supersample;
        int    reserved;
    };

    struct KVVertexOut {
        float4 position [[position]];
    };

    // A single triangle that covers the whole render target.
    vertex KVVertexOut kv_fullscreen(uint vid [[vertex_id]]) {
        float2 corner = float2(float((vid << 1) & 2), float(vid & 2));
        KVVertexOut out;
        out.position = float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
        return out;
    }

    // Distance to the Mandelbulb, with an orbit trap used for coloring. Y is up.
    static float kv_bulb(float3 p, float power, thread float &trap) {
        float3 z = p;
        float dr = 1.0;
        float r = 0.0;
        trap = 1e9;
        for (int i = 0; i < 9; i++) {
            r = length(z);
            if (r > 2.0) { break; }
            trap = min(trap, r);
            float safeR = max(r, 1e-6);
            float theta = acos(clamp(z.y / safeR, -1.0, 1.0)) * power;
            float phi = atan2(z.z, z.x) * power;
            dr = pow(safeR, power - 1.0) * power * dr + 1.0;
            float zr = pow(safeR, power);
            z = zr * float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi)) + p;
        }
        return 0.5 * log(max(r, 1e-6)) * r / dr;
    }

    constant float KV_FLOOR = -1.25;

    // Scene distance. material: 0 = fractal, 1 = floor.
    static float kv_scene(float3 p, float power, thread float &trap, thread int &material) {
        float bulb = kv_bulb(p, power, trap);
        float ground = p.y - KV_FLOOR;
        if (ground < bulb) {
            material = 1;
            return ground;
        }
        material = 0;
        return bulb;
    }

    static float kv_distance(float3 p, float power) {
        float trap;
        int material;
        return kv_scene(p, power, trap, material);
    }

    static float3 kv_normal(float3 p, float power) {
        const float e = 0.0006;
        const float2 k = float2(1.0, -1.0);
        return normalize(k.xyy * kv_distance(p + k.xyy * e, power) +
                         k.yyx * kv_distance(p + k.yyx * e, power) +
                         k.yxy * kv_distance(p + k.yxy * e, power) +
                         k.xxx * kv_distance(p + k.xxx * e, power));
    }

    static float kv_shadow(float3 origin, float3 dir, float power) {
        float light = 1.0;
        float t = 0.02;
        for (int i = 0; i < 40; i++) {
            float d = kv_distance(origin + dir * t, power);
            light = min(light, 10.0 * d / t);
            t += clamp(d, 0.008, 0.3);
            if (light < 0.002 || t > 6.0) { break; }
        }
        return saturate(light);
    }

    static float kv_occlusion(float3 p, float3 n, float power) {
        float occ = 0.0;
        float weight = 1.0;
        for (int i = 1; i <= 5; i++) {
            float h = 0.025 * float(i);
            occ += (h - kv_distance(p + n * h, power)) * weight;
            weight *= 0.65;
        }
        return saturate(1.0 - 5.0 * occ);
    }

    static float3 kv_sky(float3 dir) {
        float t = saturate(dir.y * 0.5 + 0.5);
        return mix(float3(0.98, 0.93, 0.84), float3(0.20, 0.38, 0.95), t);
    }

    fragment float4 kv_scene_fragment(KVVertexOut v [[stage_in]], constant KVParams &params [[buffer(0)]]) {
        float2 uv = (v.position.xy - 0.5 * params.resolution) / params.resolution.y;
        uv.y = -uv.y;

        // The camera circles the fractal once every 24 frames.
        float angle = params.time * 6.28318530718;
        float3 eye = float3(2.7 * sin(angle), 0.55 + 0.25 * sin(angle * 2.0), 2.7 * cos(angle));
        float3 forward = normalize(float3(0.0, -0.1, 0.0) - eye);
        float3 right = normalize(cross(float3(0.0, 1.0, 0.0), forward));
        float3 up = cross(forward, right);
        float3 dir = normalize(forward * 1.7 + right * uv.x + up * uv.y);

        float t = 0.0;
        float trap = 0.0;
        int material = -1;
        bool hit = false;
        for (int i = 0; i < 220; i++) {
            float3 p = eye + dir * t;
            int m;
            float tr;
            float d = kv_scene(p, params.power, tr, m);
            if (d < 0.0005 * max(t, 1.0)) {
                hit = true;
                material = m;
                trap = tr;
                break;
            }
            t += d * 0.85;
            if (t > 12.0) { break; }
        }
        if (!hit) {
            return float4(kv_sky(dir), 1.0);
        }

        float3 p = eye + dir * t;
        float3 n = kv_normal(p, params.power);
        float3 lightDir = normalize(float3(-0.5, 0.9, 0.35));
        float diffuse = saturate(dot(n, lightDir));
        float shadow = kv_shadow(p + n * 0.003, lightDir, params.power);
        float ao = kv_occlusion(p, n, params.power);

        float3 albedo;
        if (material == 1) {
            float2 cell = floor(p.xz * 2.0);
            float checker = fmod(abs(cell.x + cell.y), 2.0);
            albedo = mix(float3(0.93, 0.89, 0.80), float3(0.80, 0.74, 0.64), checker);
        } else {
            float band = saturate(trap * 1.25 - 0.35);
            albedo = mix(float3(1.0, 0.33, 0.20), float3(0.10, 0.70, 0.66), band);
            albedo = mix(albedo, float3(1.0, 0.80, 0.24), smoothstep(0.85, 1.0, band));
        }
        float3 halfway = normalize(lightDir - dir);
        float specular = pow(saturate(dot(n, halfway)), 40.0) * shadow * (material == 0 ? 0.7 : 0.15);
        float rim = pow(saturate(1.0 + dot(n, dir)), 3.0) * 0.25;
        float3 color = albedo * (0.16 * ao + diffuse * shadow * 1.05) + specular + rim * kv_sky(n);
        float fog = saturate((t - 3.0) * 0.08);
        color = mix(color, kv_sky(dir), fog);
        return float4(color, 1.0);
    }

    // Averages supersample x supersample scene pixels into one output pixel, then gamma.
    fragment float4 kv_resolve_fragment(KVVertexOut v [[stage_in]],
                                        constant KVParams &params [[buffer(0)]],
                                        texture2d<float> scene [[texture(0)]]) {
        int ss = max(params.supersample, 1);
        uint2 origin = uint2(v.position.xy) * uint(ss);
        float3 sum = float3(0.0);
        for (int y = 0; y < ss; y++) {
            for (int x = 0; x < ss; x++) {
                sum += scene.read(origin + uint2(x, y)).rgb;
            }
        }
        float3 color = sum / float(ss * ss);
        return float4(pow(saturate(color), float3(1.0 / 2.2)), 1.0);
    }

    // Small preview of the output for the app window.
    fragment float4 kv_preview_fragment(KVVertexOut v [[stage_in]],
                                        constant float2 &size [[buffer(0)]],
                                        texture2d<float> source [[texture(0)]]) {
        constexpr sampler linearSampler(coord::normalized, filter::linear, address::clamp_to_edge);
        return source.sample(linearSampler, v.position.xy / size);
    }
    """
}
#endif

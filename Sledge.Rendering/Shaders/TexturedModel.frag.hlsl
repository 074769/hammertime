struct FragmentIn
{
    float4 fPosition : SV_Position;
    float4 fNormal : NORMAL0;
    float3 fTexture : TEXCOORD0;
    nointerpolation uint fFlags : TEXCOORD1;
};

Texture2DArray Texture;
SamplerState Sampler;

// GoldSrc studio texture flags (upper bits of the skin table entry, see MdlModel.GetLayerSet)
static const uint F_Flatshade = 0x01;
static const uint F_Fullbright = 0x04;
static const uint F_Alpha = 0x10;
static const uint F_Additive = 0x20;
static const uint F_Masked = 0x40;
static const uint F_GoldSrc = 0x80; // set for every GoldSrc texture, so other model types keep the old unlit look

// Editor-side stand-in for the map lighting
static const float3 LightDirection = { 0.3030458f, 0.2020305f, 0.9306048f }; // normalize({ 0.3, 0.2, 0.92 }), z is up
static const float AmbientLight = 0.55f;   // brightness of a face pointing away from the light
static const float FlatshadeLight = 0.8f;  // constant brightness of flatshade textures

float4 main(FragmentIn input) : SV_Target0
{
    uint f = input.fFlags;

    float4 tex = Texture.Sample(Sampler, input.fTexture);

    if ((f & F_GoldSrc) == 0)
    {
        // Not a GoldSrc texture: original behaviour
        if (tex.w < 0.5) discard;
        tex.w = 1;
        return tex;
    }

    float light;
    if (f & F_Fullbright) light = 1.0f;
    else if (f & F_Flatshade) light = FlatshadeLight;
    else
    {
        float incidence = saturate(dot(normalize(input.fNormal.xyz), LightDirection) * 0.5f + 0.5f);
        light = lerp(AmbientLight, 1.0f, incidence);
    }

    if (f & F_Alpha)
    {
        // Blended pass: alpha comes from the texture (palette index alpha)
        return float4(tex.xyz * light, tex.w);
    }

    if ((f & F_Masked) || !(f & F_Additive))
    {
        if (tex.w < 0.5) discard;
    }
    // Additive pass is blended One/One, so only the colour matters
    return float4(tex.xyz * light, 1);
}

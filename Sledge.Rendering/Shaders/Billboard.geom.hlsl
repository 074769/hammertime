struct GeometryIn
{
    float4 gPosition : SV_Position;
    float4 gNormal : NORMAL0;
    float4 gColour : COLOR0;
    float2 gTexture : TEXCOORD0;
    float4 gTint : COLOR1;
};

struct FragmentIn
{
    float4 fPosition : SV_Position;
    float4 fNormal : NORMAL0;
    float4 fColour : COLOR0;
    float2 fTexture : TEXCOORD0;
    float4 fTint : COLOR1;
};

cbuffer Projection
{
    matrix Selective;
    matrix Model;
    matrix View;
    matrix Projection;
}
cbuffer UVBuffer
{
    float FrameCount;
    float CurrentFrame;
    // SpriteOrientation.cs: 0 = ParallelUpright, 1 = FacingUpright, 2 = Parallel,
    // 3 = Oriented, 4 = ParallelOriented. Taken from the .spr header (or Parallel
    // if there wasn't one to read).
    float Orientation;
    float _uvReserved0;
    // Entity "angles" in radians, as (pitch, yaw, roll). Only meaningful for
    // Oriented and ParallelOriented.
    float3 Angles;
    float _uvReserved1;
};

static const float ORIENT_PARALLEL_UPRIGHT  = 0;
static const float ORIENT_FACING_UPRIGHT    = 1;
static const float ORIENT_PARALLEL          = 2;
static const float ORIENT_ORIENTED          = 3;
static const float ORIENT_PARALLEL_ORIENTED = 4;

static const float3 WorldUp = float3(0, 0, 1);

// Rotates v around a unit axis by angleRad radians (Rodrigues' rotation formula).
float3 RotateAroundAxis(float3 v, float3 axis, float angleRad)
{
    float s = sin(angleRad);
    float c = cos(angleRad);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1 - c);
}

[maxvertexcount(4)]
void main(point GeometryIn input[1], inout TriangleStream<FragmentIn> output)
{
    matrix tModel = transpose(Model);
    matrix tView = transpose(View);
    matrix tProjection = transpose(Projection);

    float w = input[0].gNormal.x / 2;
    float h = input[0].gNormal.y / 2;

    // tModel is the camera's own camera-to-world transform, so these are exactly the
    // camera's world-space right/up/forward axes and its world-space position.
    float3 camRight   = normalize(mul(float4(1, 0, 0, 0), tModel).xyz);
    float3 camUp      = normalize(mul(float4(0, 1, 0, 0), tModel).xyz);
    float3 camForward = normalize(mul(float4(0, 0, 1, 0), tModel).xyz);
    float3 camPos     = mul(float4(0, 0, 0, 1), tModel).xyz;
    float3 spritePos  = input[0].gPosition.xyz;

    float3 right;
    float3 up;

    if (Orientation == ORIENT_ORIENTED)
    {
        // Oriented sprites are a FIXED plane in world space - unlike every other orientation
        // mode here, they must never billboard or otherwise react to the camera at all.
        //
        // Confirmed against an actual compiled/in-game screenshot (yaw 0/90/180/270, pitch and
        // roll both 0) that yaw spins the flat plane in place rather than turning its facing
        // direction: yaw 0/90/180/270 showed the printed arrow pointing right/down/left/up on
        // screen, i.e. a clockwise spin as yaw increases.
        //
        // Pitch turned out NOT to behave like that - lumping it into the same spin as yaw made
        // it indistinguishable from yaw, which testing showed is wrong. So pitch instead tilts
        // the plane's fixed facing direction itself, same as it would for a normal GoldSrc
        // entity, starting from a fixed reference plane (facing world +X, "up" = world Z, i.e.
        // what angles = 0,0,0 means normally) and tilting it around a fixed horizontal axis.
        // Yaw and roll both then spin whatever that tilted plane is, around its own (tilted)
        // facing direction.
        //
        // Only yaw's contribution here is empirically confirmed. Pitch's tilt axis/sign and
        // roll's sign (and whether roll really belongs with yaw here rather than with pitch)
        // are a best guess pending a compiled test with nonzero pitch and/or roll - adjust
        // freely once you can see what those actually do in-game.
        const float3 baseForward = float3(1, 0, 0);
        const float3 baseRight   = float3(0, -1, 0);

        float3 forward = RotateAroundAxis(baseForward, baseRight, Angles.x);
        float3 r = cross(forward, WorldUp);
        if (dot(r, r) < 0.0001) r = baseRight; // forward ~= world up, fall back to the fixed reference
        r = normalize(r);
        float3 u = normalize(cross(r, forward));

        float spin = Angles.z - Angles.y;
        right = RotateAroundAxis(r, forward, spin);
        up = RotateAroundAxis(u, forward, spin);
    }
    else if (Orientation == ORIENT_PARALLEL_UPRIGHT || Orientation == ORIENT_FACING_UPRIGHT)
    {
        // Locked to the Z axis - only yaws to face the camera (ParallelUpright) or the
        // viewer's actual position (FacingUpright), never pitches or rolls.
        float3 toViewer = (Orientation == ORIENT_FACING_UPRIGHT)
            ? normalize(camPos - spritePos)
            : camForward;

        float3 r = cross(WorldUp, toViewer);
        if (dot(r, r) < 0.0001) r = camRight; // looking straight up/down, fall back
        right = normalize(r);
        up = WorldUp;
    }
    else if (Orientation == ORIENT_PARALLEL_ORIENTED)
    {
        // Fully camera-facing like Parallel, but rolled by the entity's own roll angle
        // around the view axis so it can be rotated in Hammer.
        right = RotateAroundAxis(camRight, camForward, Angles.z);
        up = RotateAroundAxis(camUp, camForward, Angles.z);
    }
    else // ORIENT_PARALLEL (default) - always fully face the camera, roll follows the camera.
    {
        right = camRight;
        up = camUp;
    }

    float4 upV = float4(up * h, 0);
    float4 rightV = float4(right * w, 0);

    float4 verts[4];
    verts[0] = -rightV + upV;
    verts[1] = +rightV + upV;
    verts[2] = -rightV - upV;
    verts[3] = +rightV - upV;

    float column = CurrentFrame % FrameCount;
    float frameSize = 1.0 / FrameCount;
    
    float2 texCoords[4];
    texCoords[0] = float2(column * frameSize, 0);
    texCoords[1] = float2((column * frameSize) + frameSize, 0);
    texCoords[2] = float2(column * frameSize, 1);
    texCoords[3] = float2((column * frameSize) + frameSize, 1);

    FragmentIn gOut;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 modelPos = input[0].gPosition + verts[i];
        float4 cameraPos = mul(modelPos, tView);
        float4 viewportPos = mul(cameraPos, tProjection);

        gOut.fPosition = viewportPos;
        gOut.fNormal = float4(0,0,0, 1);
        gOut.fColour = input[0].gColour;
        gOut.fTexture = texCoords[i];
        gOut.fTint = input[0].gTint;

        output.Append(gOut);
    }
}

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

    // The orthographic center-handle "X" is drawn through BillboardOpaquePipeline, which binds
    // no UVBuffer at all, so every field in it reads as 0. A real sprite always has
    // FrameCount >= 1. Without this check that all-zero buffer was read as Orientation 0 =
    // ParallelUpright (which collapses to a flat line in the top view, and never gets the
    // camera's 1/zoom scale, so it was also the wrong size) and FrameCount 0 (NaN UVs).
    bool hasUV = FrameCount >= 1;
    float frameCount = hasUV ? FrameCount : 1;
    float currentFrame = hasUV ? CurrentFrame : 0;

    float3 right;
    float3 up;

    if (!hasUV)
    {
        // Original screen-aligned billboard: offsets are taken from the camera's own axes
        // WITHOUT normalizing, so the orthographic view's zoom scale is preserved and the
        // quad keeps a constant size in screen pixels at any zoom.
        right = mul(float4(1, 0, 0, 0), tModel).xyz;
        up    = mul(float4(0, 1, 0, 0), tModel).xyz;
    }
    else if (Orientation == ORIENT_ORIENTED)
    {
        // Oriented sprites are a FIXED plane in world space - unlike every other orientation
        // mode here, they must never billboard or otherwise react to the camera at all.
        // Starting from a fixed reference plane (facing world +X, "up" = world Z, i.e. what
        // angles = 0,0,0 means for a normal GoldSrc entity), each of pitch/yaw/roll rotates it
        // around its own fixed axis (Angles.x = pitch, Angles.y = yaw, Angles.z = roll), signs
        // confirmed against actual compiled/in-game screenshots per-axis:
        //  - pitch tilts the plane up/down around the fixed horizontal "right" axis.
        //  - roll swings the plane left/right around the fixed vertical axis.
        //  - yaw spins the plane in place around its own (already tilted/swung) facing direction.
        //
        // IMPORTANT: the three basis vectors (forward/right/up) are carried through each
        // rotation step together and rotated directly - never re-derived from a cross product
        // partway through. Re-deriving right/up from cross(forward, WorldUp) after tilting was
        // the earlier bug: at pitch = 90 the tilted forward becomes parallel to WorldUp, so that
        // cross product goes to zero and roll (which swings around WorldUp) silently stopped
        // doing anything. Rotating right/up directly has no such singularity - even when
        // forward points straight up/down, right/up are still horizontal and still rotate.
        float3 f = float3(1, 0, 0);
        float3 r = float3(0, -1, 0);
        float3 u = WorldUp;

        // Pitch tilts the plane up/down around the fixed horizontal "right" axis.
        float3 f1 = RotateAroundAxis(f, r, -Angles.x);
        float3 u1 = RotateAroundAxis(u, r, -Angles.x);
        // r is unchanged - you can't rotate a vector out of the axis it's rotating around.

        // Yaw swings the tilted plane left/right around the fixed world-up axis - this
        // is the rotation that actually changes which way the plane faces. Bug fix:
        // this stage was previously driven by Angles.z (roll) instead of Angles.y (yaw),
        // and the stage below used Angles.y instead of Angles.z - the two were swapped.
        // Verified against oriented_sprite_debbuging.map's hand-placed reference arrows
        // (13 known-good angle/direction pairs covering yaw 0/90/180/270 and roll
        // 0/90/180/270, plus pitch sanity checks) - only this swap reproduces all 13.
        float3 f2 = RotateAroundAxis(f1, WorldUp, Angles.y);
        float3 r2 = RotateAroundAxis(r,  WorldUp, Angles.y);
        float3 u2 = RotateAroundAxis(u1, WorldUp, Angles.y);

        // Roll spins the resulting plane in place around its own (pitched+yawed) facing
        // direction - this only rotates the picture in place, it never changes which
        // way the plane faces.
        right = RotateAroundAxis(r2, f2, Angles.z);
        up = RotateAroundAxis(u2, f2, Angles.z);
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

    float column = currentFrame % frameCount;
    float frameSize = 1.0 / frameCount;
    
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

namespace Sledge.Rendering.Pipelines
{
    public enum PipelineType
    {
        Skybox,
        Wireframe,
        WireframeSelected,

        TexturedOpaque,
        BillboardOpaque,
        
        TexturedAlpha,
        TexturedAdditive,
        BillboardAlpha,

        WireframeModel,
        TexturedModel,
        TexturedModelAdditive,
        TexturedModelAlpha,

        ShadowOverlay,

        Overlay,

        BeamAdditive,
    }
}
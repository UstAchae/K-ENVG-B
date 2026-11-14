// The sights' glass as seen: a red dot's or a holographic sight's window (the game's reticle plane) and a scope's lens,
// drawn over the frame's own depth (no offset), so wherever the sight's frame or anything else stands in front of it,
// it is left out: exactly the part of the glass the tubes see through. Long-wave infrared does not pass glass, so the
// thermal picture is left out there.
Shader "Justachae/ENVG/GlassMask"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            float4 frag() : SV_Target
            {
                return 1;
            }
            ENDCG
        }
    }
}

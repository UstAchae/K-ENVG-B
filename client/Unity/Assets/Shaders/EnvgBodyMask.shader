// Where people are in the frame: their body and gear drawn again over the frame's own depth (equal depth passes), as
// a mask for body heat in the long-wave picture.
Shader "Justachae/ENVG/BodyMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
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

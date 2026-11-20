// Offline check only (not in the bundle): the camera's depth texture, linear
Shader "Hidden/EnvgDepthPeek"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 frag(v2f_img i) : SV_Target
            {
                return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _EnvgDepthBuffer;
            float4 frag(v2f_img i) : SV_Target
            {
                return LinearEyeDepth(tex2D(_EnvgDepthBuffer, i.uv).r);
            }
            ENDCG
        }
    }
}

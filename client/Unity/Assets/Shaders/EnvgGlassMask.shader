// The sights' glass as seen through: a red dot's or a holographic sight's windows and a scope's lenses, drawn over
// the frame's own depth (no offset), so wherever the sight's frame or anything else stands in front of the glass it is
// left out. And only where the line of sight goes on through the sight to the scene beyond (more than 20 cm behind
// the glass): through the rear window at a slant one sees the hood's inside, which the light from the scene never
// crosses; only the part of the view that comes in through the objective end is the sight's picture. Long-wave
// infrared does not pass glass, so the thermal picture is left out exactly there.
Shader "Justachae/ENVG/GlassMask"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            BlendOp Max
            Blend One One
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
                float eye : TEXCOORD1;   // the glass's own distance along the view (m)
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.screen = ComputeScreenPos(o.pos);
                o.eye = -UnityObjectToViewPos(vertex.xyz).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                // what lies behind the glass: the sight's own inside (a few cm) or the scene beyond
                float through = smoothstep(0.15, 0.25, scene - i.eye);
                return through;
            }
            ENDCG
        }
    }
}

// The sights' glass that keeps the thermal picture out. Long-wave infrared does not pass glass, so where the thermal
// lens looks through a sight's window it sees the glass, not the scene beyond.
//
// Pass 0 (without the device's thermal lens node): the glass as the eye sees it, drawn over the frame's own depth, and
// only where the view goes on through the sight to the scene (more than 20 cm behind the glass).
// Passes 1 and 2: as the thermal lens sees it. The ENVG-B's thermal lens sits above and to the left of the right tube:
// it looks at a sight from its own place, mostly over it. The sight is drawn again from there (the camera's own
// turn and field, moved to the lens): pass 1 its housing, as the lens's distance to it; pass 2 its glass, where the
// housing does not stand in front of it and the ray goes on through the sight (no housing within 20 cm behind the
// glass). The thermal picture is laid out as the tubes see (the device's lens boresighted at a distance), so this
// mask, seen from the lens, is where the lens's own rays meet the glass.
Shader "Justachae/ENVG/GlassMask"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" }

        // 0: from the eye, over the frame's depth
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

        // 1: from the thermal lens, the sight's housing: its distance from the lens (nearest)
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float eye : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.eye = -UnityObjectToViewPos(vertex.xyz).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.eye, 0, 0, 1);
            }
            ENDCG
        }

        // 2: from the thermal lens, the sight's glass, in front of its housing and with no housing just behind it
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

            sampler2D _EnvgHousingDepth;   // the housing's distance from the lens (pass 1; far where there is none)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
                float eye : TEXCOORD1;
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
                float housing = tex2Dproj(_EnvgHousingDepth, UNITY_PROJ_COORD(i.screen)).r;
                // the ray meets the glass and goes on out of the sight (no housing within 20 cm behind it)
                return smoothstep(0.15, 0.25, housing - i.eye);
            }
            ENDCG
        }
    }
}

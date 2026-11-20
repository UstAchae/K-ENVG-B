// The gun's motion vectors, drawn back. The gun is left out of the main camera's own drawing (so the scene behind it
// is in the frame's data for the thermal picture) and drawn back into the frame afterwards; the camera's own motion
// pass no longer sees it, so its motion (where each of its pixels was last frame, on the screen) is drawn here: its
// place now and last frame (each part's own matrices, kept by the plugin), through the camera's unjittered view and
// projection now and last frame. Screen uv, y up, now less then, as the game's own.
Shader "Justachae/ENVG/GunMotion"
{
    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4x4 _EnvgNowVP, _EnvgPrevVP, _EnvgPrevM;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 now : TEXCOORD0;
                float4 then : TEXCOORD1;
            };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.now = mul(_EnvgNowVP, mul(unity_ObjectToWorld, vertex));
                o.then = mul(_EnvgPrevVP, mul(_EnvgPrevM, vertex));
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 now = i.now.xy / i.now.w * 0.5 + 0.5;
                float2 then = i.then.xy / i.then.w * 0.5 + 0.5;
                return float4(now - then, 0, 1);
            }
            ENDCG
        }
    }
}

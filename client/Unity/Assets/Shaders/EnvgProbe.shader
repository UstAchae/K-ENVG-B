// TEMPORARY (a look from the thermal lens, for checking; to be removed): the gun in hand as the ENVG-B's thermal lens
// sees it. Pass 0 its body, grey by how it faces the lens, its distance in alpha; pass 1 the sights' glass in cyan
// over it; pass 2 (a blit) a copper line wherever the distance jumps (the gun's outline as the lens sees it).
Shader "Justachae/ENVG/Probe"
{
    SubShader
    {
        // 0: the gun's body
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float eye : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float3 normal : NORMAL)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.n = mul((float3x3)UNITY_MATRIX_MV, normal);
                o.eye = -UnityObjectToViewPos(vertex.xyz).z;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float facing = abs(normalize(i.n).z);
                float g = 0.25 + 0.55 * facing;
                return float4(g, g, g, i.eye);
            }
            ENDCG
        }

        // 1: the sights' glass, cyan, see-through
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return float4(0.2, 0.9, 1.0, 0.45); }
            ENDCG
        }

        // 2: the outline: where the distance jumps
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float d = c.a;
                float jump = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 o = (k == 0 ? float2(1, 0) : k == 1 ? float2(-1, 0) : k == 2 ? float2(0, 1) : float2(0, -1)) * _MainTex_TexelSize.xy;
                    float dn = tex2D(_MainTex, i.uv + o).a;
                    jump = max(jump, abs(dn - d) / max(min(d, dn), 0.05));
                }
                float outline = smoothstep(0.03, 0.08, jump);
                return float4(lerp(c.rgb, float3(1.0, 0.62, 0.3), outline), 1);
            }
            ENDCG
        }
    }
}

Shader "RingSkinC/SpriteGammaComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _SymbolRect ("Replaced symbol UV bounds", Vector) = (0,0,0,0)
        [PerRendererData] _SymbolFillUV ("Unmarked zone UV", Vector) = (0,0,0,0)
        [PerRendererData] _SymbolCircle ("Inner arc exclusion", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            float4 _SymbolRect;
            float4 _SymbolFillUV;
            float4 _SymbolCircle;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // Symbols are baked into the zone sprites. Restore the flat zone
                // underneath only the retired glyph; keep the rest of the art intact.
                if (_SymbolRect.z > _SymbolRect.x && _SymbolRect.w > _SymbolRect.y &&
                    i.uv.x >= _SymbolRect.x && i.uv.x <= _SymbolRect.z &&
                    i.uv.y >= _SymbolRect.y && i.uv.y <= _SymbolRect.w)
                {
                    float2 insideArc = (i.uv - _SymbolCircle.xy) / _SymbolCircle.zw;
                    if (dot(insideArc, insideArc) < 1)
                        c = tex2D(_MainTex, _SymbolFillUV.xy);
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = LinearToGammaSpace(c.rgb);
                #endif
                c *= i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}

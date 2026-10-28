Shader "TaikoSelf/RecordedSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WhiteMask ("White alpha artwork", Float) = 0
        _UseArc ("Angular clip", Float) = 0
        _ArcStart ("Clockwise start", Float) = 0
        _ArcSweep ("Sweep", Float) = 90
        _Progress ("Progress", Range(0,1)) = 1
        _AngleOffset ("Sprite rotation clockwise", Float) = 0
        _BackgroundScale ("Extend background edges", Float) = 1
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
            float _WhiteMask, _UseArc, _ArcStart, _ArcSweep, _Progress, _AngleOffset, _BackgroundScale;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; float2 local:TEXCOORD1; };
            Output vert(Input v)
            {
                Output o; o.position=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
                o.color=v.color*_Color; o.local=v.vertex.xy; return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                if(_UseArc>.5)
                {
                    float angle=atan2(-i.local.y,i.local.x)*57.2957795+_AngleOffset;
                    float delta=fmod(angle-_ArcStart+720,360);
                    if(delta>359.5) delta=0;
                    clip(_Progress*_ArcSweep-delta);
                }
                float2 uv=(i.uv-.5)*_BackgroundScale+.5;
                fixed4 c=tex2D(_MainTex,uv);
                // Fade extended top/bottom edges into their corner colour instead
                // of stretching the baked drum or extruding the landscape stripes.
                float outsideY=max(-uv.y,uv.y-1);
                fixed4 edge=tex2D(_MainTex,float2(.02,saturate(uv.y)));
                c=lerp(c,edge,smoothstep(0,.08,max(0,outsideY)));
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb=LinearToGammaSpace(c.rgb);
                #endif
                c.rgb=lerp(c.rgb,fixed3(1,1,1),_WhiteMask);
                c*=i.color; c.rgb*=c.a; return c;
            }
            ENDCG
        }
    }
}

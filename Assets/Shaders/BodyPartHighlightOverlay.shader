Shader "SpineRecovery/BodyPartHighlightOverlay"
{
    Properties
    {
        _Color ("Highlight Color", Color) = (0, 1, 0, 0.95)
        _Pulse ("Pulse", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha One
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -2, -2

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Pulse;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = _Color;
                color.rgb *= _Pulse;
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}

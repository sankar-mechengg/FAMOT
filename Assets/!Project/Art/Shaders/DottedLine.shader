Shader "Unlit/DottedLine" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range (.002, 0.03)) = .005
        _Dashed ("Dashed", Range(0,1)) = 0.0 // 0: solid, 1: dashed
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
        
        Pass {
            // Drawing the object normally
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 pos : POSITION;
            };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            sampler2D _MainTex;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _Dashed;
            
            fixed4 frag (v2f i) : SV_Target {
                // Simple dashed line logic: use UVs and modulate to create a dashed effect
                if (_Dashed > 0.5) {
                    float pattern = fmod(i.pos.x + i.pos.y, 20); // Modify this for different dash effects
                    if (pattern < 10) return _OutlineColor;
                } 
                return tex2D(_MainTex, i.pos.xy);
            }
            ENDCG
        }
    }
}

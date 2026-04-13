Shader "Custom/WireframeShader" {
    Properties {
        _WireColor ("Wire Color", Color) = (1, 1, 1, 1)  // Wireframe color
        _WireThickness ("Wire Thickness", Range(0.01, 5)) = 1.0 // Thickness of the wireframe
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float4 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0; // Stores barycentric coordinates
            };

            float _WireThickness;
            float4 _WireColor;

            // Custom barycentric generation for wireframe rendering
            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                // Generate barycentric coordinates for wireframe edges
                o.bary = float3(1, 0, 0); // Default value (could use different logic for better triangulation)
                return o;
            }

            // Function to draw wireframe
            fixed4 frag (v2f i) : SV_Target {
                // Find the minimum barycentric coordinate to highlight the wireframe edges
                float edgeFactor = min(min(i.bary.x, i.bary.y), i.bary.z);
                
                // Make the edges thicker or thinner based on the wire thickness value
                float line = smoothstep(0.0, _WireThickness * 0.01, edgeFactor);

                // Render the wireframe
                return lerp(_WireColor, float4(0, 0, 0, 0), line); // Wireframe with color, background transparent
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

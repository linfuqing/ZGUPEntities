// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "ZG/FilterEffect" {
	/*Properties {
		//_SourceColor ("Source Color", Color) = (1,1,1,1)
		//_DestinationColor("Destination Color", Color) = (1,1,1,1)
		//_Glossiness ("Smoothness", Range(0,1)) = 0.5
		//_Metallic ("Metallic", Range(0,1)) = 0.0
	}*/

	/*SubShader
	{
		Tags{ "RenderType" = "Filter" }

		LOD 200
		Pass
		{
			Lighting Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal:NORMAL;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				fixed4 color : COLOR;
			};

			uniform float4 _Color;

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				float3 viewDir = normalize(ObjSpaceViewDir(v.vertex));
				float val = 1.0f - max(dot(v.normal, viewDir), 0.0f);

				o.color = _Color * val;

				return o;
			}

			fixed4 frag(v2f i) : COLOR
			{
				return i.color;
			}
			ENDCG
		}
	}*/

	SubShader
	{
		Tags { "RenderType" = "Filter" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard noshadow nofog noambient

		// Use shader model 3.0 target, to get nicer looking lighting
		//#pragma target 3.0

		uniform half g_FilterGlossiness;
		uniform half g_FilterMetallic;

		uniform fixed4 _FilterColor;

		struct Input {
			float3 worldNormal;
			float3 viewDir;
		};

		void surf(Input IN, inout SurfaceOutputStandard o) 
		{
			fixed value = 1.5f - max(dot(IN.worldNormal, IN.viewDir), 0.0f);
			half4 color = fixed4(_FilterColor.rgb * value, _FilterColor.a);
			// Albedo comes from a texture tinted by color
			o.Albedo = 0;
			o.Emission = color.rgb;
			// Metallic and smoothness come from slider variables
			o.Metallic = g_FilterMetallic;
			o.Smoothness = g_FilterGlossiness;
			o.Alpha = color.a;
		}
		ENDCG
	}

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard vertex:vert noshadow nofog noambient

		// Use shader model 3.0 target, to get nicer looking lighting
		//#pragma target 3.0

		uniform half g_FilterGlossiness;
		uniform half g_FilterMetallic;

		uniform fixed4 g_FilterSourceColor;
		uniform fixed4 g_FilterDestinationColor;

		uniform int g_FilterParamsLength = 0;
		uniform float4 g_FilterParams[32];

		struct Input {
			float dist;
		};

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void vert(inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input, o);

			float3 pos = mul(unity_ObjectToWorld, v.vertex).xyz;

			float4 param;
			float dist = 1.0f;
			for (int i = 0; i < g_FilterParamsLength; ++i)
			{
				param = g_FilterParams[i];
				dist = min(dist, distance(param.xyz, pos) / param.w);
			}

			o.dist = dist;
		}

		void surf (Input IN, inout SurfaceOutputStandard o) {
			fixed alpha = IN.dist;
			half4 color = g_FilterSourceColor * (1.0f - alpha) + g_FilterDestinationColor * alpha;
			// Albedo comes from a texture tinted by color
			o.Albedo = 0;
			o.Emission = color.rgb;
			// Metallic and smoothness come from slider variables
			o.Metallic = g_FilterMetallic;
			o.Smoothness = g_FilterGlossiness;
			o.Alpha = color.a;
		}
		ENDCG
	}

	FallBack "Diffuse"
}

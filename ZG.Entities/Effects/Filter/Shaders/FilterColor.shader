Shader "ZG/FilterColor"
{
	Properties
	{
		[HDR]_Color("Color", Color) = (0, 0, 1, 1)
	}

	SubShader
	{
		Tags{ "Queue" = "Transparent" "RenderType" = "Filter" }
		LOD 100

		Pass
		{ 
			Tags{"LightMode" = "Filter"}

			//BlendOp Max
			Blend SrcAlpha One
			//ZWrite Off
			Lighting Off
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			};

			struct v2f
			{
				half4 color : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			half4 _Color;
			half4 _FilterColor;

			uniform half g_FilterWeight;

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				UNITY_TRANSFER_FOG(o,o.vertex);

				float3 viewDir = normalize(ObjSpaceViewDir(v.vertex));
				float val = 1 - max(dot(v.normal, viewDir), 0.0f);

				half4 color = lerp(_Color, _FilterColor, g_FilterWeight);

				o.color = half4(color.rgb * val, color.a);

				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				// sample the texture
				fixed4 col = i.color;
				// apply fog
				UNITY_APPLY_FOG(i.fogCoord, col);

				//col.a = 1.0f;// i.color.a;
				return col;
			}
			ENDCG
		}
	}
}

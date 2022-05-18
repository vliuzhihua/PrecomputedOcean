Shader "Custom/PrecomputedOceanShader"
{
    Properties
    {
		FoamTex("FoamTexture", 2D) = "white" {}
	}
		SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
			#pragma multi_compile_fog

			#include "UnityCG.cginc"
			#include "UnityImageBasedLighting.cginc"

			UNITY_DECLARE_TEX2DARRAY(DisplaceArray);
			float4 DisplaceArray_TexelSize;
			UNITY_DECLARE_TEX2DARRAY(NormalArray);
			UNITY_DECLARE_TEX2DARRAY(MixArray);
			sampler2D FoamTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float4 normal : TEXCOORD1;
                float4 worldPos: TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
			int RepeatTime;
			int LoopTime;
			int UseBake;

            v2f vert (appdata v)
            {
                v2f o;
				
				float t = frac(_Time.y / RepeatTime);
				float idx0 = floor(t * LoopTime);
				float idx1 = (idx0 + 1) % LoopTime;
				float lerpFactor = frac(t * LoopTime);
				
				float2 uvOffset = DisplaceArray_TexelSize.xy * 0.5;

				float4 disp0  = UNITY_SAMPLE_TEX2DARRAY_LOD(DisplaceArray, float3(v.uv.xy + uvOffset, idx0), 0);
				float4 disp1 = UNITY_SAMPLE_TEX2DARRAY_LOD(DisplaceArray, float3(v.uv.xy + uvOffset, idx1), 0);
				float4 displace = lerp(disp0, disp1, lerpFactor);
				float4 vertex = v.vertex + displace * 1.0f;
				vertex.a = 1.0;

				float4 normal0  = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalArray, float3(v.uv.xy + uvOffset, idx0), 0);
				float4 normal1 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalArray, float3(v.uv.xy + uvOffset, idx1), 0);
				float4 normal = lerp(normal0, normal1, lerpFactor);

				if (UseBake == 0)
				{
					vertex = v.vertex;
					normal = v.normal;
				}

				float4 vtx = vertex;
				vtx.w = 1.0;
                o.vertex = UnityObjectToClipPos(vtx);
				o.uv = v.uv;// TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = normal;
				o.worldPos = mul(unity_ObjectToWorld, vtx);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
				fixed4 col = 0.0f;// tex2D(_MainTex, i.uv);
				float3 normal = i.normal;
                // apply fog
				float t = frac(_Time.y / RepeatTime);
				float idx0 = floor(t * LoopTime);
				float idx1 = (idx0 + 1) % LoopTime;
				float lerpFactor = frac(t * LoopTime);

				float2 uvOffset = DisplaceArray_TexelSize.xy * 0.5;
				//uvOffset = 0.0;

				float4 normal0 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalArray, float3(i.uv.xy + uvOffset, idx0), 0);
				float4 normal1 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalArray, float3(i.uv.xy + uvOffset, idx1), 0);
				normal = lerp(normal0.xyz, normal1.xyz, lerpFactor);

				{
					// show ndl
					//col = dot(i.normal, _WorldSpaceLightPos0.xyz);
					float t = frac(_Time.y / RepeatTime);
					float idx0 = floor(t * LoopTime);
					float4 val = UNITY_SAMPLE_TEX2DARRAY_LOD(DisplaceArray, float3(i.uv.xy, idx0), 0);
					//col = val.y > 1.0;
				}

				float4 mix0 = UNITY_SAMPLE_TEX2DARRAY_LOD(MixArray, float3(i.uv.xy + uvOffset, idx0), 0);
				float4 mix1 = UNITY_SAMPLE_TEX2DARRAY_LOD(MixArray, float3(i.uv.xy + uvOffset, idx1), 0);

				float4 mixValue = lerp(mix0, mix1, lerpFactor) * 2.0;
				//col.xyz = saturate(mixValue.xyz);

				{
					//foam
					col.xyz = tex2D(FoamTex, i.uv.xy * 4.0 + _Time.y * 0.1 ).xyz * mixValue.xyz;
				}

				Unity_GlossyEnvironmentData envData;
				envData.roughness = 0.1;
				float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
				float3 refl = reflect(-view, normal);
				refl.y = max(refl.y, 0.0);
				envData.reflUVW = refl;
				float3 probe0 = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, envData);

				col.xyz += probe0 * 0.5;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}

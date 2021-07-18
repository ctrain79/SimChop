Shader "Unlit/StyleWater"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_CubeMap ("CubeMap", CUBE) = ""{}
	}
	SubShader
	{
		Blend SrcAlpha OneMinusSrcAlpha
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }

		CGPROGRAM
		#pragma editor_sync_compilation
		#pragma surface surf Lambert vertex:vert alpha:fade
		#pragma target 3.5


		#include "UnityCG.cginc"
		#include "BinarySearch.cginc"

		struct Input {
			float2 uv_MainTex;
			float3 worldPos;
			float dist;
			nointerpolation float4 customColor;
			float4 screenPos;
			float3 worldRefl;
		};

		uniform sampler3D interleaved_tex;
		uniform sampler3D coord_tex;
		uniform sampler3D interleaved_shifted_half_unit_tex;
		uniform sampler3D coord_shifted_half_unit_tex;
		uniform int num_inside_vol;
		samplerCUBE _CubeMap;
		uniform float4x4 unit_map;
		uniform float4x4 pos_octant_map; // note: this shifts unit cube to first octant in 3d-space
		uniform float3 vol_dimensions; // for the volume where particle positions are mapped
		uniform float3 tex_dimensions; // for the textures
		uniform float precision;
		uniform float editor_radius;
		uniform int scan_num;
		uniform float editor_alpha;
		uniform float editor_emission;

		void vert (inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
			uint digits = 3*precision;

			uint first_digits =
				(digits > 10) ?
				10 :
				digits;

			float3 worldPremap =
				mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
			float3 unitPos =
				mul(unit_map, float4(worldPremap, 1)).xyz; // map to unit cube
			float3 w_pos =
				mul(pos_octant_map, float4(unitPos, 1)).xyz; // translate unit cube

			float3 closest = vol_dimensions;
			uint closest_i = 0;
			float d = editor_radius + 1;

			// get first and last particle Morton codes
			fixed4 first =
				tex3Dlod(
					interleaved_tex,
					pos_index(0, tex_dimensions)
				);
			fixed4 last =
				tex3Dlod(
					interleaved_tex,
					pos_index(num_inside_vol-1, tex_dimensions)
				);

			float scale = pow(2, floor(precision)) * (1 + frac(precision));

			uint2 interleaved =
				getInterleaved(
					w_pos,
					float3(0, 0, 0),
					first_digits,
					digits,
					precision,
					scale
				);
			// cull vertices beyond volume where particles are located
			// if (
			// 	compare(interleaved, first) < 0 ||
			// 	compare(interleaved, last) > 0
			// ) {
			// 	v.vertex.x = 0.0/0.0;
			// }

			// check shifted neighbouring octants
			uint locIndex =
				binarySearch(
					interleaved,
					interleaved_tex,
					tex_dimensions,
					num_inside_vol
				);
			float4 result =
				lookup(
					locIndex,
					w_pos,
					d,
					closest,
					float3(0, 0, 0),
					vol_dimensions,
					scan_num,
					coord_tex,
					tex_dimensions,
					num_inside_vol
				);
			closest = result.xyz;
			d = result.w;

			for (float x = 0; x < 2; x += 1) {
			for (float y = 0; y < 2; y += 1) {
			for (float z = 0; z < 2; z += 1) {
				interleaved =
					getInterleaved(
						w_pos,
						float3(x, y, z),
						first_digits,
						digits,
						precision,
						scale
					);
				locIndex =
					binarySearch(
						interleaved,
						interleaved_shifted_half_unit_tex,
						tex_dimensions,
						num_inside_vol
					);
				result =
					lookup(
						locIndex,
						w_pos,
						d,
						closest,
						float3(x, y, z),
						vol_dimensions,
						scan_num,
						coord_shifted_half_unit_tex,
						tex_dimensions,
						num_inside_vol
					);
				closest = result.xyz;
				d = result.w;
			}}}

			if (d >= editor_radius + 1){
				v.vertex.x = 0.0/0.0; // usual trick of setting w to NaN is ignored by Unity with vertex/surface combo
			}
			else{
				v.vertex.z -= smoothstep(4,3.6,d)*2.;
				o.dist = d;
			}

			o.worldPos = worldPremap;
			o.customColor =
				float4(
					w_pos,
					(editor_radius - d)/editor_radius
				);
		}
		sampler2D _MainTex;

		void surf(Input IN, inout SurfaceOutput o)
		{
			float d = IN.dist;
			//d *= sin(IN.worldPos.y)+2;
			o.Alpha = smoothstep(4,3.9,d);
			o.Albedo =float3(0,.6,1.)*smoothstep(4,3,d);
			o.Albedo = max(float3(1,1,1)*smoothstep(3,4,d),o.Albedo)*.5;
			//o.Albedo *= texCUBE (_CubeMap, IN.worldRefl).rgb;
		//	o.Alpha = IN.customColor.a;
			o.Emission = texCUBE (_CubeMap, IN.worldRefl).rgb;
			//o.Alpha = 0.5;
		}

		ENDCG
	}
}

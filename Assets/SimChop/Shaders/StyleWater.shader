Shader "Unlit/StyleWater"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_CubeMap ("CubeMap", CUBE) = ""{}
	}
	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha


		CGPROGRAM
		#pragma editor_sync_compilation
		#pragma surface surf Lambert vertex:vert alpha:fade
		#pragma target 3.5

		#include "UnityCG.cginc"
		#include "BinarySearch.cginc"

		struct Input {
			//float2 uv_MainTex; // if needed, create Properties { _MainTex ("Texture", 2D) = "white" {} }
			float3 worldPos;
			float4 customColor;
			float4 screenPos;
			float dist;
			float emit;
			float3 worldRefl;
			float3 refl;
		};

		uniform sampler3D interleaved_tex;
		uniform sampler3D coord_tex;
		uniform sampler3D interleaved_shifted_half_unit_tex;
		uniform sampler3D coord_shifted_half_unit_tex;
		samplerCUBE _CubeMap;
		uniform float4x4 pos_octant_map; // note: this shifts unit cube to first octant in 3d-space
		uniform float4x4 unit_map;

		// controls
		uniform float editor_alpha;
		uniform float editor_emission;
		uniform float editor_radius;
		uniform float editor_rolloff;
		uniform float editor_vertex_delta;
		uniform float span;
		uniform float precision;
		uniform int scan_num;

		uniform float3 vol_dimensions; // for the volume where particle positions are mapped
		uniform float3 tex_dimensions; // for the textures
		uniform int num_inside_vol;

		float wave(float3 p, float w, float x, float y, float z)
		{
			return w*(sin(_Time.y + x*p.x + z*p.z) + sin(_Time.y + y*p.y - z*p.z));
		}

		float wave3(float3 p)
		{
			return
				wave(p, 0.3, 64, 64, 64) +
				wave(p, 0.3, 16, 16, 16) +
				wave(p, 0.3, 32, 32, 32);
		}

		void vert (inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
			uint digits = 3*floor(precision);


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
			o.refl = worldPremap-closest;
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

			float scale = pow(2, floor(precision)) * (1 - 0.5*frac(precision));

			uint2 interleaved =
				getInterleaved(
					w_pos,
					float3(0, 0, 0),
					first_digits,
					digits,
					precision,
					scale
				);

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
				// usual trick of setting w to NaN is ignored by Unity with vertex/surface combo; so do x instead
				v.vertex.x = 0.0/0.0;
			} else {
				//v.vertex.z -= smoothstep(editor_radius, editor_radius*editor_rolloff, d)*5;
				o.dist = d;
			}

			// get rid of emission ripple and vertex waving if you just want regular round particles
		/*	float ripple = wave3(w_pos);
			v.vertex.x += 0.5*editor_vertex_delta*ripple;
			v.vertex.y += 0.5*editor_vertex_delta*ripple;
			v.vertex.z += span*0.2*ripple;

			o.worldPos = worldPremap;
			float lightness =
				saturate(
					sin((1+sin(3*_Time.x))*0.2*worldPremap.x) +
					sin((1+sin(3*_Time.x))*0.2*worldPremap.y) +
					sin((1+sin(3*_Time.x))*0.2*worldPremap.z)
				);
			o.customColor =
				float4(
					0.1*lightness,
					0.4*lightness,
					lightness,
					(editor_radius - d)/editor_radius
				);
			o.emit = ripple;
			*/
		}
		sampler2D _MainTex;

		void surf(Input IN, inout SurfaceOutput o)
		{
			o.Alpha = smoothstep(editor_radius, editor_radius*editor_rolloff, IN.dist) * editor_alpha;
			o.Albedo =float3(0,.6,1.)*smoothstep(editor_radius,editor_radius*editor_rolloff,IN.dist);
			o.Albedo = max(float3(1,1,1)*smoothstep(editor_radius*editor_rolloff*.5,editor_radius,IN.dist),o.Albedo)*.1;
			o.Emission = texCUBE (_CubeMap, IN.worldRefl).rgb*editor_emission;

		}

		ENDCG
	}
}

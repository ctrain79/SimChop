Shader "SimChop/InterleaveSystem"
{
	SubShader
	{
		Blend SrcAlpha OneMinusSrcAlpha
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		
		CGPROGRAM
		#pragma editor_sync_compilation
		#pragma surface surf Lambert vertex:vert alpha:fade

		#include "UnityCG.cginc"
		#include "BinarySearch.cginc"
		
		struct Input {
			//float2 uv_MainTex; // if needed, create Properties { _MainTex ("Texture", 2D) = "white" {} }
			float3 worldPos;
			nointerpolation float4 customColor;
			float4 screenPos;
		};
		
		uniform sampler3D interleaved_tex;
		uniform sampler3D coord_tex;
		uniform sampler3D interleaved_shifted_half_unit_tex;
		uniform sampler3D coord_shifted_half_unit_tex;
		uniform int num_inside_vol;

		uniform float4x4 unit_map;
		uniform float4x4 pos_octant_map; // note: this shifts unit cube to first octant in 3d-space
		uniform float3 vol_dimensions; // for the volume where particle positions are mapped
		uniform float3 inv_dim;
		uniform float3 tex_dimensions; // for the textures
		uniform uint editor_precision;
		uniform float editor_radius;
		uniform float editor_alpha;
		uniform float editor_emission;
		uniform float half_unit;
		
		void vert (inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
			uint digits = 3*editor_precision;
			
			uint first_digits = 
				(digits > 10) ?
				10 :
				digits;
			
			float3 scale = vol_dimensions;
			float3 worldPremap = 
				mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
			float3 unitPos = 
				mul(unit_map, float4(worldPremap, 1)).xyz; // map to unit cube
			float3 w_pos = 
				mul(pos_octant_map, float4(unitPos, 1)).xyz; // translate unit cube0
			
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
			
			
			uint2 interleaved = 
				getInterleaved(
					w_pos, 
					first_digits, 
					digits, 
					editor_precision
				);
			// cull vertices beyond volume where particles are located
			if (
				compare(interleaved, first) < 0 || 
				compare(interleaved, last) > 0
			) {
				v.vertex.x = 0.0/0.0;
			}
			
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
					scale, 
					coord_tex, 
					tex_dimensions, 
					num_inside_vol
				);
			closest = result.xyz;
			d = result.w;
			
			for (float x = -half_unit; x < 1.5*half_unit; x += 2*half_unit) {
			for (float y = -half_unit; y < 1.5*half_unit; y += 2*half_unit) {
			for (float z = -half_unit; z < 1.5*half_unit; z += 2*half_unit) { 
				interleaved = 
					getInterleaved(
						w_pos + float3(x, y, z), 
						first_digits, 
						digits, 
						editor_precision
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
						scale, 
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
			
			o.worldPos = worldPremap;
			o.customColor = 
				float4(
					w_pos, 
					(editor_radius - d)/editor_radius
				);
		}
		
		void surf(Input IN, inout SurfaceOutput o)
		{
			if (IN.customColor.a < 0) discard;
			o.Albedo = IN.customColor.rgb;
			o.Alpha = IN.customColor.a * editor_alpha;
			o.Emission = 
				fixed3(
					editor_emission,
					editor_emission,
					editor_emission
				);
		}
		
		ENDCG
	}
}

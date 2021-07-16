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


		struct Input {
			float2 uv_MainTex;
			float3 worldPos;
			float dist;
			nointerpolation float4 customColor;
			float4 screenPos;
			float3 worldRefl;
		};
		samplerCUBE _CubeMap;
		uniform sampler3D _posTex1;
		uniform sampler3D _coordTex1;
		uniform sampler3D _posTex2;
		uniform sampler3D _coordTex2;
		uniform int particleAmount;

		uniform float4x4 unit_map;
		uniform float4x4 pos_octant_map; // note: this shifts unit cube to first octant in 3d-space
		uniform float3 dimensions; // for the volume where particle positions are mapped
		uniform float3 inv_dim;
		uniform float3 tex_dimensions; // for the textures
		uniform uint precision;
		uniform float radius;
		uniform float editor_alpha;
		uniform float half_unit;

		float4 pos_index(float index) {
			return float4(
				(uint(index) % tex_dimensions.x) / tex_dimensions.x,
				(uint(index) / tex_dimensions.x) / tex_dimensions.y, // Make sure I put 0.5 back once dimensions fit more than 2048 particles
				0,
				0
			);
		}

		float4 posCoord_index(float index) {
			if (index < 0) return float4(0, 0, 0, 0);
			else if (index >= particleAmount)
				index = particleAmount - 1;
			return float4(
				(uint(index) % tex_dimensions.x) / tex_dimensions.x,
				(uint(index) / tex_dimensions.x) / tex_dimensions.y,
				0,
				0
			);
		}

		int compare(uint2 bits, fixed4 col){
			uint big_col = (uint(col.r) << 16) + uint(col.g);
			uint small_col = (uint(col.b) << 16) + uint(col.a);
			if (bits.y < big_col){
				return -1;
			}
			else if (bits.y > big_col){
				return 1;
			}
			if (bits.x < small_col){
				return -1;
			}
			else if (bits.x > small_col){
				return 1;
			}
			return 0;
		}

		uint binarySearch(uint2 bits, sampler3D tex){
			if (particleAmount < 2) return 0;
			uint iterations = ceil(log2(particleAmount));
			uint stop = 0;
			uint low = 0;
			uint high = particleAmount-1;
			uint m = 0;
			fixed4 col = fixed4(0, 0, 0, 1);
			for (uint f = 0; f < iterations; f++){
				if (stop < 1){
					m = (low + high-1)/2.0;
					col = tex3Dlod(tex, pos_index(m)); // some r, g, b, a, with g and a each with 16-bits, r and b with 14
					if (compare(bits, col) > 0){
						low = m+1;
					}
					else if (compare(bits, col) < 0){
						high = m-1;
					}
					else {
						stop = 1;
					}
				}
			}
			if (m >= particleAmount) return particleAmount - 1;
			return m;
		}

		uint2 getInterleaved(float3 w_pos, uint first_digits, uint digits)
		{
			uint2 interleaved =
				{ 0, 0 };
			uint iX = uint(w_pos.x*pow(2, precision));
			uint iY = uint(w_pos.y*pow(2, precision));
			uint iZ = uint(w_pos.z*pow(2, precision));

			for(uint k = 0; k < first_digits; k++){
				interleaved.x |= (0x1 & iZ) << (3*k);
				interleaved.x |= (0x1 & iY) << (3*k+1);
				interleaved.x |= (0x1 & iX) << (3*k+2);

				iX = iX >> 1;
				iY = iY >> 1;
				iZ = iZ >> 1;
			}

			// TO DO: there are not a multiple of 3 bits in a uint, so deal with the offset
			if (digits > 10) {
				for(uint k = 0; k < digits - 10; k++){
					interleaved.y |= (0x1 & iZ) << (3*k  );
					interleaved.y |= (0x1 & iY) << (3*k+1);
					interleaved.y |= (0x1 & iX) << (3*k+2);

					iX = iX >> 1;
					iY = iY >> 1;
					iZ = iZ >> 1;
				}
			}

			return interleaved;
		}

		float4 lookup(uint i, float3 w_pos, float d, float3 closest, float3 shift, float3 scale, sampler3D tex) {
			for(int j = -5; j < 5; j++){

				fixed4 p = tex3Dlod(tex, posCoord_index(i+j));
				float distance = length((w_pos.xyz - float3(p.rgb))*scale); // undo scale warping of unit cube

				if(distance < d) {
					closest = p.rgb;
					//closest_i = uint(locIndex+j);
					d = distance;
				}
			}
			return float4(closest, d);
		}

		void vert (inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
			uint digits = 3*precision;

			uint first_digits =
				(digits > 10) ?
				10 :
				digits;

			float3 scale = dimensions;//*pow(0.5, precision);
			float3 worldPremap = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
			float3 unitPos = mul(unit_map, float4(worldPremap, 1)).xyz; // map to unit cube
			float3 w_pos = mul(pos_octant_map, float4(unitPos, 1)).xyz; // translate unit cube
			o.uv_MainTex.xy = v.texcoord.xy;

			float3 closest = dimensions;
			uint closest_i = 0;
			float d = radius + 1;

			// get first and last particle Morton codes
			fixed4 first =  tex3Dlod(_posTex1, pos_index(0));
			fixed4 last =  tex3Dlod(_posTex1, pos_index(particleAmount-1));


			uint2 interleaved = getInterleaved(w_pos, first_digits, digits);
			// cull vertices beyond volume where particles are located
			if (
				compare(interleaved, first) < 0 ||
				compare(interleaved, last) > 0
			) {
				v.vertex.x = 0.0/0.0;
			}

			// check shifted neighbouring octants
			uint locIndex = binarySearch(interleaved, _posTex1);
			float4 result = lookup(locIndex, w_pos, d, closest, float3(0, 0, 0), scale, _coordTex1);
			closest = result.xyz;
			d = result.w;

			for (float x = -half_unit; x < 1.5*half_unit; x += 2*half_unit) {
			for (float y = -half_unit; y < 1.5*half_unit; y += 2*half_unit) {
			for (float z = -half_unit; z < 1.5*half_unit; z += 2*half_unit) {
				interleaved = getInterleaved(w_pos + float3(x, y, z), first_digits, digits);
				locIndex = binarySearch(interleaved, _posTex2);
				result = lookup(locIndex, w_pos, d, closest, float3(x, y, z), scale, _coordTex2);
				closest = result.xyz;
				d = result.w;
			}}}

			if (d >= radius + 1){
				v.vertex.x = 0.0/0.0; // usual trick of setting w to NaN is ignored by Unity with vertex/surface combo
			}
			else{
				o.dist = d;
			}

			o.worldPos = worldPremap;
			o.customColor = float4(w_pos, (radius - d)/radius);
		}

		sampler2D _MainTex;

		void surf(Input IN, inout SurfaceOutput o)
		{
			float d = IN.dist;
		//	d *= sin(IN.worldPos.y+_Time.y)+2;
			o.Alpha = smoothstep(4,3.9,d);
			o.Albedo =float3(0,.6,1.)*smoothstep(4,3,d);
			o.Albedo = max(float3(1,1,1)*smoothstep(3,4,d),o.Albedo);
			//o.Albedo *= texCUBE (_CubeMap, IN.worldRefl).rgb;
		//	o.Alpha = IN.customColor.a;
			o.Emission = texCUBE (_CubeMap, IN.worldRefl).rgb;
			//o.Alpha = 0.5;
		}

		ENDCG
	}
}

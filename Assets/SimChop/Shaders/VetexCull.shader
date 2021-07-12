Shader "Unlit/VetexCull"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		Blend SrcAlpha OneMinusSrcAlpha
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
			#pragma editor_sync_compilation

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				float3 ro : TEXCOORD1;
				nointerpolation float3 hitPos : TEXCOORD2;
				float3 position : TEXCOORD3;
			};
			
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
			uniform float r;
			uniform float alpha;
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
	 		
			uint2 getInterleaved(float3 worldPos, uint first_digits, uint digits)
			{
				uint2 interleaved = 
					{ 0, 0 };
				uint iX = uint(worldPos.x*pow(2, precision));
				uint iY = uint(worldPos.y*pow(2, precision));
				uint iZ = uint(worldPos.z*pow(2, precision));

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
			
			float4 lookup(uint i, float3 worldPos, float d, float3 closest, float3 shift, float3 scale, sampler3D tex) {
				for(int j = -5; j < 5; j++){
						
					fixed4 p = tex3Dlod(tex, posCoord_index(i+j));
					float distance = length((worldPos.xyz - float3(p.rgb))*scale); // undo scale warping of unit cube

					if(distance < d) {
						closest = p.rgb;
						//closest_i = uint(locIndex+j);
						d = distance;
					}
				}
				return float4(closest, d);
			}
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				o.vertex = v.vertex;
				o.vertex = UnityObjectToClipPos(v.vertex);
				
				uint digits = 3*precision;
				
				uint first_digits = 
					(digits > 10) ?
					10 :
					digits;
				
				float3 scale = dimensions;//*pow(0.5, precision);
				float3 worldPremap = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
				float3 unitPos = mul(unit_map, float4(worldPremap, 1)).xyz; // map to unit cube
				float3 worldPos = mul(pos_octant_map, float4(unitPos, 1)).xyz; // translate unit cube   ///// (map to 2^precision cube)
				o.position = worldPos;
				o.uv = v.uv; // pass to frag shader if you are checking one of the Texture3Ds
				
				float3 closest = dimensions;
				uint closest_i = 0;
				float d = r + 1;
				
				// get first and last particle Morton codes
				fixed4 first =  tex3Dlod(_posTex1, pos_index(0));
				fixed4 last =  tex3Dlod(_posTex1, pos_index(particleAmount-1));
				
				
				uint2 interleaved = getInterleaved(worldPos, first_digits, digits);
				// cull vertices beyond volume where particles are located
				if (
					compare(interleaved, first) < 0 || 
					compare(interleaved, last) > 0
				) {
					o.vertex.w = 0.0/0.0;
				}
				
				// check shifted neighbouring octants
				uint locIndex = binarySearch(interleaved, _posTex1);
				float4 result = lookup(locIndex, worldPos, d, closest, float3(0, 0, 0), scale, _coordTex1);
				closest = result.xyz;
				d = result.w;
				
				for (float x = -half_unit; x < 1.5*half_unit; x += 2*half_unit) {
				for (float y = -half_unit; y < 1.5*half_unit; y += 2*half_unit) {
				for (float z = -half_unit; z < 1.5*half_unit; z += 2*half_unit) { 
					interleaved = getInterleaved(worldPos + float3(x, y, z), first_digits, digits);
					locIndex = binarySearch(interleaved, _posTex2);
					result = lookup(locIndex, worldPos, d, closest, float3(x, y, z), scale, _coordTex2);
					closest = result.xyz;
					d = result.w;
				}}}
				
				if (d >= r + 1){
					o.vertex.w = 0.0/0.0;
				}
				
				o.ro = float3((r - d)/r, d, 0);
				
				o.position = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
				
				o.hitPos = closest;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				float3 unitPos = mul(unit_map, float4(i.position, 1)).xyz;
				float3 worldPos = mul(pos_octant_map, float4(unitPos, 1)).xyz;
				//  unitPos += float3(0.5*dimensions.x, 0.5*dimensions.y, 0);
				
				
				// float3 scale = dimensions*pow(0.5, precision);
				
				// uint interleaved[2];
				// interleaved[0] = 0;
				// interleaved[1] = 0;
				// uint iX = uint(worldPos.x);
				// uint iY = uint(worldPos.y);
				// uint iZ = uint(worldPos.z);

				// uint digits = 3*precision;

				// uint first_digits = 
				// 	(digits > 10) ?
				// 	10 :
				// 	digits;
				// for(uint k = 0; k < first_digits; k++){
				// 	interleaved[0] |= ((0x1 & iZ) << (3*k));
				// 	interleaved[0] |= ((0x1 & iY) << (3*k+1));
				// 	interleaved[0] |= ((0x1 & iX) << (3*k+2));

				// 	iX = iX >> 1;
				// 	iY = iY >> 1;
				// 	iZ = iZ >> 1;
				// }
				
				// if (digits > 10) {
				// 	for(uint k = 0; k < digits - 10; k++){
				// 		interleaved[1] |= (0x1 & iZ) << (3*k  );
				// 		interleaved[1] |= (0x1 & iY) << (3*k+1);
				// 		interleaved[1] |= (0x1 & iX) << (3*k+2);

				// 		iX = iX >> 1;
				// 		iY = iY >> 1;
				// 		iZ = iZ >> 1;
				// 	}
				// }
				
				// uint locIndex = binarySearch(interleaved[1], interleaved[0]);
				
				// float d = 8;
				// for(int j = -15; j < 15; j++){
					
				// 	fixed4 p = tex3Dlod(_coordTex, posCoord_index(locIndex+j));
				// 	float distance = length((worldPos.xyz - float3(p.rgb))*scale); // undo scale warping of unit cube

				// 	/*float3 difference = worldPos.xyz - positionsArray[i].xyz;
				// 	float distance = sqrt(
				// 							  pow(difference.x, 2.0) +
				// 							  pow(difference.y, 2.0) +
				// 							  pow(difference.z, 2.0));
				// 							  */

				// 	if(distance < d) {
				// 		d = distance;
				// 	}
				// }
				// fixed4 c = fixed4(1, 0, 0, (8 - d) * 0.125);
				// return c;
				
				// --------------- debugging -----------------
				
				// this gets a colour (edit binarySearch to return col) from the particle positions array when precision = 14
				// fixed4 locIndex = binarySearch(interleaved[1], interleaved[0]);
				// return fixed4(
				// 	/*c.r/256.0 +*/ locIndex.g/4096.0, // uses 12 bits, all in c.g
				// 	locIndex.b/16384.0 + locIndex.a/65536.0, // uses 30 bits, 14 for c.b, and 16 for c.a
				// 	0, 0.1
				// );
				
				// checking bits
				// return fixed4(
				// 	locIndex & 0x1,
				// 	(locIndex >> 1) & 0x1,
				// 	(locIndex >> 2) & 0x1,
				// 	1	
				// );
				
				// checked volume mapping
				// float3 scale = pow(0.5, precision);
				// return fixed4(worldPos*scale, 1);
				if (i.ro.y > r) discard;
				//float a = 1 - smoothstep(0, r/dimensions.x, length(worldPos - i.hitPos));
				return fixed4(worldPos*1.5, i.ro.x*alpha);
				//return fixed4(1, 0, 0, alpha);
				
				// float unit = pow(0.5, precision);
				// fixed3 w = fixed3(1, 1, 1);
				// fixed3 r = fixed3(1, 0, 0);
				// fixed3 g = fixed3(0, 1, 0);
				// fixed3 b = fixed3(0, 0, 1);
				// fixed3 c = fixed3(0, 0, 0);
				// if (unitPos.x > 0.5)
				// 	c += r;
				// else c += 0.33*w;
				// if (unitPos.y > 0.5)
				// 	c += g;
				// else c += 0.33*w;
				// if (unitPos.z > 0.5)
				// 	c += b;
				// else c += 0.33*w;
				// return 
				// 	fixed4(
				// 		c,
				// 		i.ro.x*alpha
				// 	);
				
				// fixed4 c = fixed4(1, 0, 0, i.ro.x); 
				// return c;
				
				// checked unit cube map
				//return fixed4(worldPos.xyz, 0.1);

				// this displays red based on relative index within array; correct shows z-order space filling curve
				//return fixed4(float(locIndex)/particleAmount, 0, 0, 0.1);
				
				// checked interleaving
				//return fixed4(float(interleaved[0])/pow(2,34) + float(interleaved[1])/pow(2,12), 0, 0, 1);

				// checked binary representation of positions
				// return fixed4(
				// 	(interleaved[1] >> 11) & 0x1,
				// 	(interleaved[1] >> 10) & 0x1,
				// 	(interleaved[1] >> 9) & 0x1,
				// 	1
				// );
				
				// checked texture coordinates---note that level surface planes have normals flipped, so we have to flip the x axis direction
				// return fixed4(1-i.uv.x, i.uv.y, 0, 1);
				
				// checked the interleaving for precision = 14
				// fixed4 c = tex3D(_posTex, float3(1-i.uv.x, i.uv.y, 0));
				// return fixed4(
				// 	/*c.r/256.0 +*/ c.g/4096.0, // uses 12 bits, all in c.g
				// 	c.b/16384.0 + c.a/65536.0, // uses 30 bits, 14 for c.b, and 16 for c.a
				// 	0,
				// 	1
				// );
				
				//return fixed4(i.position.x/particleAmount, 0, 0, i.ro.x);
				//return fixed4(i.position*pow(0.5, precision), i.ro.x);
				
				// if (i.ro.y > r) discard;
				// return fixed4(1, 0, 0, i.ro.x*alpha);
			}
			ENDCG
		}
	}
}

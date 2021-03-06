
// functions for reading data structures passed in by Texture3Ds to sampler3Ds

float4 pos_index(
	float index, 
	float3 dim
) {
	return float4(
		(uint(index) % dim.x + 0.5) / dim.x,
		(floor(uint(index) / dim.x) + 0.5) / dim.y,
		0,
		0
	);
}

float4 posCoord_index(
	float index, 
	float3 dim, 
	int num_inside_vol
) {
	if (index < 0) return float4(0, 0, 0, 0);
	else if (index >= num_inside_vol)
		index = num_inside_vol - 1;
	return float4(
		(uint(index) % dim.x + 0.5) / dim.x,
		(floor(uint(index) / dim.x) + 0.5) / dim.y,
		0,
		0
	);
}

int compare(
	uint2 bits, 
	uint4 col
) {
	uint big_col = (col.r << 16) + col.g;
	uint small_col = (col.b << 16) + col.a;
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

uint binarySearch(
	uint2 bits, 
	sampler3D tex, 
	float3 dim, 
	int num_inside_vol
) {
	if (num_inside_vol < 2) return 0;
	uint iterations = ceil(log2(num_inside_vol));
	uint stop = 0;
	uint low = 0;
	uint high = num_inside_vol-1;
	uint m = 0;
	uint4 col = uint4(0, 0, 0, 1);
	for (uint f = 0; f < iterations; f++){
		if (stop < 1) {
			m = (low + high)/2.0;
			col = tex3Dlod(tex, pos_index(m, dim)); // some r, g, b, a, with g and a each with 16-bits, r and b with 14
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
	if (m >= num_inside_vol) return num_inside_vol - 1;
	return m;
}

uint2 getInterleaved(
	float3 w_pos,
	float3 shift,
	uint first_digits, 
	uint digits,
	float scale
) {
	uint2 interleaved = 
		{ 0, 0 };
		
	uint iX = floor(w_pos.x*scale) + shift.x;
	uint iY = floor(w_pos.y*scale) + shift.y;
	uint iZ = floor(w_pos.z*scale) + shift.z;
	
	for(uint k = 0; k < first_digits; k++){
		interleaved.x |= (0x1 & iZ) << (3*k);
		interleaved.x |= (0x1 & iY) << (3*k+1);
		interleaved.x |= (0x1 & iX) << (3*k+2);

		iX = iX >> 1;
		iY = iY >> 1;
		iZ = iZ >> 1;
	}
	
	// there are not a multiple of 3 bits in a uint, so we deal with the extra bits here
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

float4 lookup(
	uint i, 
	float3 w_pos, 
	float d, 
	float3 closest, 
	float3 shift, 
	float3 scale, 
	int scan_num,
	sampler3D tex, 
	float3 dim, 
	int num_inside_vol
) {
	for(int j = -1 - scan_num; j < scan_num + 1; j++){
			
		float4 p = tex3Dlod(tex, posCoord_index(i+j, dim, num_inside_vol));
		float distance = length((w_pos.xyz - float3(p.rgb))*scale);

		if(distance < d) {
			closest = p.rgb;
			d = distance;
		}
	}
	return float4(closest, d);
}

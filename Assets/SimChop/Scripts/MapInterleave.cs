using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

public struct MapInterleave : IJobFor
{
	[ReadOnly] public float camW;
	[ReadOnly] public float camH;
	[ReadOnly] public float near;
	[ReadOnly] public float far;
	[ReadOnly] public float radius;
	[ReadOnly] public int mortonBitNum;
	[ReadOnly] public float scale;
	
	public NativeArray<int> numInFrustum; // singleton for returning value
	
	[ReadOnly] public NativeArray<Matrix4x4> camMap;
	[ReadOnly] public NativeArray<Vector3> offset;
	[ReadOnly] public NativeArray<Vector3> pos;
	[ReadOnly] public NativeArray<Vector3> transformed;
	public NativeArray<Vector3> frustumArray;
	public NativeArray<ulong> interleaved;
	
	public void Execute(int i)
	{
		Vector4 p =
			camMap[0] *
			new Vector4(
				pos[i].x,
				pos[i].y,
				pos[i].z,
				1
			);
		
		if (
			// cull the frustum, but include particles near the edges
			p.x <= camW * p.z + 2*radius && 
			p.y <= camH * p.z + 2*radius && 
			p.z <= far && 
			p.x >= -camW * p.z - 2*radius && 
			p.y >= -camH * p.z - 2*radius && 
			p.z >= -radius
		) {
			ulong result = 0x00000000;
			uint x = (uint)(Mathf.Floor((transformed[i].x + offset[0].x)*scale));
			uint y = (uint)(Mathf.Floor((transformed[i].y + offset[0].y)*scale));
			uint z = (uint)(Mathf.Floor((transformed[i].z + offset[0].z)*scale));
			// Interleave larger (extra bit from Mathf.Ceil) to cover the frustum volume to allow scaling cells for particle radius controls.
			for(int k = 0; k < mortonBitNum; k++){
				uint mask = (0x1);
				result |= (ulong)(mask & x) << (k*3+2);
				result |= (ulong)(mask & y) << (k*3+1);
				result |= (ulong)(mask & z) << (k*3);
				x = x >> 1;
				y = y >> 1;
				z = z >> 1;
			}
			frustumArray[numInFrustum[0]] = transformed[i];
			interleaved[numInFrustum[0]] = result;
			numInFrustum[0]++;
		}
	}
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

public struct MapToCam : IJobFor
{
	[ReadOnly] public float camW;
	[ReadOnly] public float camH;
	[ReadOnly] public float near;
	[ReadOnly] public float far;
	[ReadOnly] public float editor_radius;
	[ReadOnly] public int full_precision;
	[ReadOnly] public float scale;
	
	public NativeArray<int> num_inside_vol;
	
	
	[ReadOnly] public NativeArray<Matrix4x4> camMap;
	[ReadOnly] public NativeArray<Vector3> offset;
	[ReadOnly] public NativeArray<Vector3> pos;
	[ReadOnly] public NativeArray<Vector3> transformed;
	public NativeArray<Vector3> resArray;
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
			p.x <= camW * p.z + 2*editor_radius && 
			p.y <= camH * p.z + 2*editor_radius && 
			p.z <= far + 2*editor_radius && 
			p.x >= -camW * p.z - 2*editor_radius && 
			p.y >= -camH * p.z - 2*editor_radius && 
			p.z >= near - 2*editor_radius
		) {
			ulong result = 0x00000000;
			uint s1 = (uint)(Mathf.Floor((transformed[i].x + offset[0].x)*scale));
			uint s2 = (uint)(Mathf.Floor((transformed[i].y + offset[0].y)*scale));
			uint s3 = (uint)(Mathf.Floor((transformed[i].z + offset[0].z)*scale));
			for(int k = 0; k < full_precision; k++){
				uint mask = (0x1);
				result |= (ulong)(mask & s3) << (k*3);
				result |= (ulong)(mask & s2) << (k*3+1);
				result |= (ulong)(mask & s1) << (k*3+2);
				s1 = s1 >> 1;
				s2 = s2 >> 1;
				s3 = s3 >> 1;
			}
			resArray[num_inside_vol[0]] = transformed[i];
			interleaved[num_inside_vol[0]] = result;
			num_inside_vol[0]++;
		}
	} 
	
}

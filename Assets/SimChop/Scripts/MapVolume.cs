using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

public struct MapVolume : IJobFor
{
	[ReadOnly]
	public Matrix4x4 map_unit;
	
	[ReadOnly]
	public Matrix4x4 map_shift; // to first octant
	
	public NativeArray<Vector3> pos;
	public NativeArray<Vector3> mapped;
	
	public void Execute(int i)
	{
		mapped[i] = 
			map_unit * 
				(new Vector4(
					pos[i].x,
					pos[i].y,
					pos[i].z,
					1
				));
			
		mapped[i] = 
			map_shift * 
				(new Vector4(
					mapped[i].x,
					mapped[i].y,
					mapped[i].z,
					1
				));
	} 
	
}

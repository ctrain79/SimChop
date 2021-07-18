using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

public struct MapVolume : IJobParallelFor
{
	[ReadOnly]
	public Matrix4x4 mapUnit;
	
	[ReadOnly]
	public Matrix4x4 mapShift; // to first octant
	
	public NativeArray<Vector3> pos;
	public NativeArray<Vector3> mapped;
	
	public void Execute(int i)
	{
		mapped[i] = 
			mapUnit * 
				(new Vector4(
					pos[i].x,
					pos[i].y,
					pos[i].z,
					1
				));
			
		mapped[i] = 
			mapShift * 
				(new Vector4(
					mapped[i].x,
					mapped[i].y,
					mapped[i].z,
					1
				));
	} 
	
}

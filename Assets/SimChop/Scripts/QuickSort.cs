using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Jobs;

public struct QuickSort : IJob
{
	public NativeArray<ulong> bits;
	public NativeArray<Vector3> pos;
	public NativeArray<int> left;
	public NativeArray<int> right;
	public int lo;
	public int hi;
	
	private static ulong xymask;
	private static ulong zmask;
	private static int precision;
	public static int Precision {
		get { return precision; }
		set { 
			precision = value;
			xymask = 0x0;
			zmask = 0x0;
			for (int i = 0; i < precision; i++) {
				xymask |= 3u << (i*3 + 1);
				zmask |= 1u << (i*3);
			}
		}
	}
	
	private void swap(
		int i,
		int j 
	) {
		ulong bit = bits[i];
		bits[i] = bits[j];
		bits[j] = bit;
		Vector3 p = pos[i];
		pos[i] = pos[j];
		pos[j] = p;
	}
	
	static private int compare(ulong a, ulong b, float a_z, float b_z) {
		
		if ((a & xymask) < (b & xymask))
			return -1;
		else if ((a & xymask) > (b & xymask))
			return 1;
		
		if ((a & ~(zmask)) < (b & ~(zmask)))
			return -1;
		else if ((a & ~(zmask)) > (b & ~(zmask)))
			return 1;
		
		// additional comparison for culling
		// if (a_z < b_z)
		// 	return -1;
		// if (a_z > b_z)
		// 	return 1;
		
		return 0;
	}
	
	// Quicksort partition into left and right
	public void Execute()
	{
		ulong pivot = bits[lo];
		float pivot_z = pos[lo].z;
		//Debug.Log("pivot = " + pivot);
		
		left[lo] = lo;
		int i = lo + 1;
		right[lo] = hi;
		
		while (i <= right[lo]) {
			if (compare(bits[i], pivot, pos[i].z, pivot_z) < 0)
			{
				swap(i, left[lo]);
				i++; left[lo]++;
			}
			else if (compare(bits[i], pivot, pos[i].z, pivot_z) > 0)
			{
				swap(i, right[lo]);
				right[lo]--;
			}
			else
			{
				i++;
			}
		}
		
		// Debug.Log(
		// 	"left = " + left +
		// 	", right = " + right
		// );
		// for(int j = lo; j <= hi; j++){
		// 	Debug.Log("bits[" + j + "]: " + bits[j]);
		// 	Debug.Log(
		// 		"bits in binary: (" + 
		// 		System.Convert.ToString((uint)(bits[j] >> 32) & 0xFFFFFFFF, 2) +
		// 		System.Convert.ToString((uint)(bits[j]) & 0xFFFFFFFF, 2) +
		// 		")"
		// 	);
		// }
	}
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshGenerator : MonoBehaviour
{
	Mesh mesh;
	List<Vector3> vertices;
	List<int> triangles;
	CameraData data;
	
	void OnEnable()
	{
		Simulation.SetFrustumEvent += SetData;
		vertices = new List<Vector3>();
		triangles = new List<int>();
	}
	
	public void SetData(CameraData cameraData)
	{
		data = cameraData;
	}
	
	// This code is a modification of the demonstration in
	// Brackey's: MESH GENERATION in Unity - Basics
	// https://www.youtube.com/watch?v=eJEpeUH1EMg
	void Start() // should not need to create an event to control execution, since CameraData is setup from Simulation OnEnable executed before all Starts
	{
		mesh = new Mesh();
		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		GetComponent<MeshFilter>().mesh = mesh;
		CreatePlanes();
		SetUpMesh();
	}
	
	// 2021 July 18, Russell Campbell
	// These are my plane frustum calculations.
	void CreatePlanes()
	{	
		Camera cam = Camera.main;
		Matrix4x4 camMap =
			Matrix4x4.Rotate(cam.transform.rotation).inverse *
			Matrix4x4.Translate(-1*cam.transform.position);
		// for frustum dimensions
		float camH = Mathf.Tan(cam.fieldOfView*Mathf.PI/360);
		float camW = Mathf.Tan(Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect)*Mathf.PI/360);
		
		// note: triangles created in clockwise listed vertex order will have normals face in negative z-axis direction
		// note: all coordinates are relative to the position of the object owner of this script 
		
		// distance between vertices
		float dx = data.VertexDelta;
		float dy = data.VertexDelta;
		int v_count = 0;
		int i_count = 0;
		for (int z = data.NumOfLevels-1; z >= 0; z--)
		{
			float depth = 0.5f*data.Radius + data.Near + z*data.Span;
			float w = 2.3f * camW * depth;
			float h = 2.3f * camH * depth;
			float half_w = w / 2.0f;
			float half_h = h / 2.0f;
			int numX = Mathf.CeilToInt(w / dx);
			int numY = Mathf.CeilToInt(h / dy);
			// construct a plane
			int prev_v_count = v_count;
			for (int y = 0; y < numY; y++)
			{
				for (int x = 0; x < numX; x++)
				{
					vertices.Add( 
						new Vector3(
							dx * x - half_w,
							dy * y - half_h,
							depth
						)
					);
					v_count++;
				}
			}
			// construct triangles for one plane at a time (easier to manage)
			for (int y = 0; y < numY-1; y++)
			{
				for (int x = 0; x < numX-1; x++)
				{
					// two triangles for each row and column; built as "forward slash" top triangle first, then bottom triangle
					triangles.Add( prev_v_count + x     +  y*numX);
					triangles.Add( prev_v_count + x     + (y+1)*numX);
					triangles.Add( prev_v_count + x + 1 +  y*numX);
					triangles.Add( prev_v_count + x     + (y+1)*numX);
					triangles.Add( prev_v_count + x + 1 + (y+1)*numX);
					triangles.Add( prev_v_count + x + 1 +  y*numX);
					i_count += 6;
				}
			}
		}
	}
	
	// Brackey's
	void SetUpMesh()
	{
		mesh.Clear();
		
		mesh.vertices = vertices.ToArray();
		mesh.triangles = triangles.ToArray();
		
		mesh.RecalculateNormals();
	}
	
}

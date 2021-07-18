using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshGenerator : MonoBehaviour
{
	Mesh mesh;
	Vector3[] vertices;
	int[] triangles;
	CameraData data;
	
	// This probably causes excessive use of stack memory... not sure of a way around this for now.
	private const int MAX_VERTICES = 30000000;
	
	void OnEnable()
	{
		Simulation.SetFrustumEvent += SetData;
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
		Vector3[] v = new Vector3[MAX_VERTICES];
		int[] tris = new int[6*MAX_VERTICES];
		
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
		for (int z = 0; z < data.NumOfLevels; z++)
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
					v[v_count++] = 
						new Vector3(
							dx * x - half_w,
							dy * y - half_h,
							depth
						);
				}
			}
			// construct triangles for one plane at a time (easier to manage)
			for (int y = 0; y < numY-1; y++)
			{
				for (int x = 0; x < numX-1; x++)
				{
					// two triangles for each row and column; built as "forward slash" top triangle first, then bottom triangle
					tris[i_count++] = prev_v_count + x     +  y*numX;
					tris[i_count++] = prev_v_count + x     + (y+1)*numX;
					tris[i_count++] = prev_v_count + x + 1 +  y*numX;
					tris[i_count++] = prev_v_count + x     + (y+1)*numX;
					tris[i_count++] = prev_v_count + x + 1 + (y+1)*numX;
					tris[i_count++] = prev_v_count + x + 1 +  y*numX;
				}
			}
		}
		
		vertices = new Vector3[v_count];
		for (int i = 0; i < v_count; i++)
			vertices[i] = v[i];
		triangles = new int[i_count];
		for (int i = 0; i < i_count; i++)
			triangles[i] = tris[i];
		
	}
	
	// Brackey's
	void SetUpMesh()
	{
		mesh.Clear();
		
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		
		mesh.RecalculateNormals();
	}
	
}

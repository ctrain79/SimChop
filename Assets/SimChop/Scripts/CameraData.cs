using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct CameraData
{
	public CameraData(
		Vector3 dim, 
		float near, 
		float far,
		int numOfLevels,
		float vertexDelta,
		float span,
		float radius // of particles
	)
		: this()
	{
		this.dim = dim;
		this.near = near;
		this.far = far;
		this.numOfLevels = numOfLevels;
		this.vertexDelta = vertexDelta;
		this.span = span;
		this.radius = radius;
	}
	
	private float near;
	public float Near {
		get { return near; }
		set { near = value; }
	}
	
	private float far;
	public float Far {
		get { return far; }
		set { far = value; }
	}
	
	private Vector3 dim;
	public Vector3 Dim {
		get { return dim; }
		set { Dim = value; }
	}
	
	private int numOfLevels;
	public int NumOfLevels {
		get { return numOfLevels; }
		set { numOfLevels = value; }
	}
	
	private float vertexDelta;
	public float VertexDelta {
		get { return vertexDelta; }
		set { vertexDelta = value; }
	}
	
	private float span;
	public float Span {
		get { return span; }
		set { span = value; }
	}
	
	private float radius;
	public float Radius {
		get { return radius; }
		set { radius = value; }
	}
}

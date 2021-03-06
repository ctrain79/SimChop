using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;

// TO DO: put interleaving in Jobs

public class Simulation : MonoBehaviour
{
	// When Unity fully releases DOTS we will probably be able to have 10,000 particles.
	private const int MAX_PARTICLES = 8192;
	
	// TO DO: group together into [Serializable] struct so fields are collapsible
	[Header("Hot Swappable")]
	
	[SerializeField, Range(0, MAX_PARTICLES)]
	public int spawnCount = default;
	private int lastSpawnCount;
	[SerializeField, Range(0, 1)]
	public float alpha = default;
	[SerializeField, Range(0, 1)]
	public float rolloff = default;
	[SerializeField, Range(0, 3)]
	public float emission = default;
	private const float MAX_RADIUS = 10;
	[SerializeField, Range(2, MAX_RADIUS)]
	public float radius = 4;
	private float lastRadius = 0;
	private float cellSidelength;
	//[SerializeField, Range(1, 70)]
	private int scanNumber = default;
	
	// TO DO: group together into [Serializable] struct so fields are collapsible
	[Header("Not Hot Swappable")]
	
	[SerializeField]
	public float near = default; // camera near-plane distance
	[SerializeField]
	public float far = default; // camera far-plane distance
	[SerializeField]
	public GameObject source = default;
	[SerializeField]
	public Vector3 spread = default;
	public int numOfLevels = 30;
	public bool levelsVisibleInHierarchy = true;
	public float vertexDelta = default; // level surface geometry controls
	private float sqrtVertDelta;
	public GameObject particle;
	public bool particlesVisibleInHierarchy = true;
	[SerializeField]
	public bool displayParticleSceneViewGizmos = default; 
	
	// set to public if you want to keep track of it in the editor (but you obviously cannot edit the number)
	public int numInsideFrustum;
	
	float precision; // number of bits for each coordinate value to make Morton codes for interleaving
	
	// rectangular volume covering frustum
	float width;
	float height;
	float depth;
	
	// camera dimensions
	float camH; // height
	float camW; // width
	float scale; // dimension of the cube sidelength that interleaving will cover (larger than) rectangular volume 
	
	Camera cam;
	
	void setPrecisionAndDimensions() {
		lastRadius = radius;
		sqrtVertDelta = Mathf.Sqrt(vertexDelta);
		
		float colliderR = particle.GetComponent<SphereCollider>().radius;
		if (radius < colliderR) {
			cellSidelength = colliderR + sqrtVertDelta;
		}
		else {
			cellSidelength = radius + sqrtVertDelta;
		}
		// adjust far plane to deal with back cell wall not being usable for lookups because of double-interleaving
		
		// Debug.Log(
		// 	"width = " + 
		// 	width + 
		// 	" height = " + 
		// 	height + 
		// 	" depth = " + 
		// 	depth
		// );
		
		// TO DO: double check one-time calculations
		if (!playing) {
			cam = Camera.main;
			camH = Mathf.Tan(cam.fieldOfView*Mathf.PI/360);
			camW = Mathf.Tan(Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect)*Mathf.PI/360);
			width = 2.05f * camW * far;
			height = 2.05f * camH * far;
			depth = far - near;
			span = (depth-radius-2*cellSidelength) / numOfLevels;
			far += 2*cellSidelength;
		}
		
		Debug.Log("span = " + span);
		CameraData camData = new CameraData(
			new Vector3(width, height, depth),
			near,
			far,
			numOfLevels,
			vertexDelta,
			span,
			MAX_RADIUS
		);
		SetFrustumEvent(camData);
		
		float min = Mathf.Min(width, height, depth);
		precision = Mathf.Log(min/cellSidelength, 2) - 1; // radius of particles ~ same as sidelength
		//Debug.Log("precision = " + precision);
		
		// cell size is a bit bigger, but no more than twice the length of cellSidelength
		float largerSidelength = min*Mathf.Pow(0.5f, Mathf.Floor(precision));
		//Debug.Log("largerSidelength = " + largerSidelength);
		
		// but we need to smoothly increase from normal size to double size
		float ratio = largerSidelength/(2*cellSidelength);
		//Debug.Log("ratio = " + ratio);
		scale = Mathf.Pow(2, Mathf.Floor(precision))*ratio;
		
		// just to check
		//float vol = Mathf.Pow(largerSidelength + 2*colliderR*ratio, 3);
		//Debug.Log("vol = " + vol);
		
		float approxScanNum = 0.1767766954f*Mathf.Pow((largerSidelength + 2*colliderR*ratio)/(colliderR*ratio), 3);
		// double-checked Sphere Packing and used Thomas C. Hales' Theorem: Pi/Sqrt(18) density
		scanNumber = Mathf.CeilToInt(approxScanNum);
		//Debug.Log("approxScanNum = " + approxScanNum);
		
		// Later, the unit cube gets scaled up by the scale amount.
		// The number of digits in the Morton code always has one more
		// digit to cover double the volume needed.
	}
	
	ParticleData particleData;
	Stack<GameObject> simObjs;
	BoxCollider volumeCollider;
	
	public static event Action<CameraData> SetFrustumEvent;
	
	public static event Action<ParticleData> EnableEvent;
	public static event Action InitializationEvent;
	public static event Action<int> NumberOfParticlesChangedEvent;
	
	
	float span;
	
	Matrix4x4 unitScale; // performs transformation to unit cube
	Vector3 shift; // amount of translation for second interleaving data structure
	
	// jobs version
	NativeArray<Vector3> frustumArray;
	NativeArray<ulong> interleaved;
	
	NativeArray<Vector3> positionsArray;
	NativeArray<Vector3> cameraPositionsArray;
	NativeArray<Vector3> transformedPositionsArray;
	NativeArray<int> left; // indices for Quicksort
	NativeArray<int> right;
	
	// compute shader version
	// Vector3[] positionsArray;
	// Vector3[] transformedPositionsArray;
	
	// Note we cannot *change* the texture dimensions because of avoiding garbage collection, so we use constants to cache textures instead. 
	private const int X = 2048;
	private const int Y = 2; // change this if MAX_PARTICLES is changed
	private const int Z = 1;
	Texture3D interleavedTex;
	Texture3D rawPositionTex;
	private float[] interleavedColors; // for filling data into Texture3Ds
	private float[] rawPositionColors;
	
	int stride = 4*3; /* num of bytes per data element in compute buffer */
	static readonly int orderPosId = Shader.PropertyToID("_Positions"),
	orderMatrixToUnitId = Shader.PropertyToID("_Unit"),
	orderMatrixUnitShiftId = Shader.PropertyToID("_Shift"),
	orderNumOfPartId = Shader.PropertyToID("_N");
	
	// could switch back to compute shader instead of jobs when number of particles is larger
	[SerializeField]
	ComputeShader computeOrder = default;
	ComputeBuffer transformedPositionsBuffer;
	
	// do not draw gizmos when not in play mode
	bool playing = false;
	
	// Start is called before the first frame update
	void Start()
	{
		//Debug.Log("NewSimulation Start");
		playing = true;
		
		// setup textures for data strucures
		interleavedTex = new Texture3D(X, Y, Z, TextureFormat.RGBAFloat, false);
		rawPositionTex = new Texture3D(X, Y, Z, TextureFormat.RGBAFloat, false);
		interleavedTex.wrapMode = TextureWrapMode.Clamp;
		interleavedTex.filterMode = FilterMode.Point;
		rawPositionTex.wrapMode = TextureWrapMode.Clamp;
		rawPositionTex.filterMode = FilterMode.Point;
		interleavedColors = new float[4*MAX_PARTICLES];
		rawPositionColors = new float[4*MAX_PARTICLES];
	}
	
	// Project Settings: order called after NewSimulation (because of setting up events)
	void OnEnable()
	{
		setPrecisionAndDimensions();
		
		// initialize pool of particles
		simObjs = new Stack<GameObject>();
		volumeCollider = gameObject.GetComponent<BoxCollider>();
		volumeCollider.center = 
			cam.transform.position + 
			cam.transform.forward * (depth/2 + near);
		volumeCollider.size = new Vector3(width/2, height/2, depth/2);
		
		particleData = new ParticleData(
			MAX_PARTICLES,
			spawnCount,
			simObjs,
			particle,
			particlesVisibleInHierarchy,
			cam.transform.position,
			new Vector3(width, height, depth),
			near,
			source,
			volumeCollider,
			spread
		);
		EnableEvent(particleData);
		
		InitializationEvent();
		
		//transformedPositionsBuffer = new ComputeBuffer(particleData.MAX_PARTICLES, stride);
		
		// setup particle data for jobs
		positionsArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		cameraPositionsArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		transformedPositionsArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		left = new NativeArray<int>(MAX_PARTICLES, Allocator.Persistent);
		right = new NativeArray<int>(MAX_PARTICLES, Allocator.Persistent);
		// compute shader version
		// positionsArray = new Vector3[MAX_PARTICLES];
		// transformedPositionsArray = new Vector3[MAX_PARTICLES];
		
		frustumArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		interleaved = new NativeArray<ulong>(MAX_PARTICLES, Allocator.Persistent);
		
		Setup();
	}
	
	void OnDestroy(){
		positionsArray.Dispose();
		cameraPositionsArray.Dispose();
		transformedPositionsArray.Dispose();
		left.Dispose();
		right.Dispose();
		interleaved.Dispose();
		frustumArray.Dispose();
	}
	
	void Setup()
	{
		//Debug.Log("Setup");
		setPrecisionAndDimensions();
		NumberOfParticlesChangedEvent(spawnCount);
		
		int j = 0;
		foreach (GameObject obj in simObjs){
			if (j < spawnCount)
				positionsArray[j++] = obj.transform.position;
		}
	}
	
	void OnDisable()
	{
		TakeDown();
	}
	
	void TakeDown() {
	}
	
	void updatePositions(){
			
		if (
			(spawnCount >= 0 && spawnCount != lastSpawnCount) ||
			!Mathf.Approximately(lastRadius, radius)
		) {
			lastSpawnCount = spawnCount;
			TakeDown();
			Setup();
		}
		
		if (spawnCount >= 0) {
			
			int j = 0;
			foreach (GameObject obj in simObjs){
				if (j < spawnCount)
					positionsArray[j++] = obj.transform.position;
			}
			
			// control the bounds of particles we are tracking
			unitScale = SimHelper.createMatrixScale(width, height, depth);
			
			// controls mapping 3D space to the unit cube
			Matrix4x4 posOctantMap = SimHelper.createMatrixMapToPosOctant();
			Matrix4x4 unitMap = SimHelper.createMatrixMapToUnitCube(unitScale, near);
			
			MapVolume mapJob = new MapVolume()
			{
				mapUnit = unitMap,
				mapShift = posOctantMap,
				pos = positionsArray,
				mapped = transformedPositionsArray
			};
			mapJob.Run(spawnCount);
			
			Shader.SetGlobalMatrix("pos_octant_map", posOctantMap);
			Shader.SetGlobalMatrix("unit_map", unitMap);
			Shader.SetGlobalFloat("editor_alpha", alpha);
			Shader.SetGlobalFloat("editor_emission", emission);
			Shader.SetGlobalFloat("editor_radius", radius);
			Shader.SetGlobalFloat("editor_rolloff", rolloff);
			Shader.SetGlobalFloat("editor_vertex_delta", vertexDelta);
			Shader.SetGlobalFloat("scale", scale);
			Shader.SetGlobalFloat("span", span);
			Shader.SetGlobalFloat("precision", precision);
			Shader.SetGlobalInt("scan_num", scanNumber);
			
			// computeOrder.SetMatrix(orderMatrixUnitShiftId, posOctantMap);
			// computeOrder.SetMatrix(orderMatrixToUnitId, unitMap);
			
			// int groups = Mathf.CeilToInt(spawnCount/1024.0f);
			// computeOrder.SetInt(orderNumOfPartId, spawnCount);
			
			// transformedPositionsBuffer.SetData(positionsArray, 0, 0, spawnCount);
			// computeOrder.SetBuffer(0, orderPosId, transformedPositionsBuffer);
			// computeOrder.Dispatch(0, groups, 1, 1);
			
			// transformedPositionsBuffer.GetData(transformedPositionsArray, 0, 0, spawnCount);
			
			buildInterleaveShaderData(
				"interleaved_tex",
				"coord_tex",
				Vector3.zero
			);
			buildInterleaveShaderData(
				"interleaved_shifted_half_unit_tex",
				"coord_shifted_half_unit_tex",
				Vector3.one*Mathf.Pow(0.5f, Mathf.Floor(precision)+2) // half a cell unit
			);
			
		}
	}
	
	
	
	void interleave(
		Vector3 offset
	) {
		numInsideFrustum = 0;
		NativeArray<Matrix4x4> camMap = new NativeArray<Matrix4x4>(1, Allocator.TempJob);
		camMap[0] = 
			Matrix4x4.Rotate(cam.transform.rotation).inverse * 
			Matrix4x4.Translate(-1*cam.transform.position);
		NativeArray<Vector3> jobOffset = new NativeArray<Vector3>(1, Allocator.TempJob);
		jobOffset[0] = offset;
		NativeArray<int> jobNumInVol = new NativeArray<int>(1, Allocator.TempJob);
		
		MapInterleave mapJob = new MapInterleave()
		{
			camW = camW,
			camH = camH,
			near = near,
			far = far,
			radius = radius,
			mortonBitNum = Mathf.FloorToInt(precision)+1,
			scale = scale,
			numInFrustum = jobNumInVol,
			camMap = camMap,
			offset = jobOffset,
			pos = positionsArray,
			transformed = transformedPositionsArray,
			frustumArray = frustumArray,
			interleaved = interleaved
		};
		mapJob.Run(spawnCount);
		
		numInsideFrustum = jobNumInVol[0];
		
		camMap.Dispose();
		jobOffset.Dispose();
		jobNumInVol.Dispose();
	}
	
	private void buildInterleaveShaderData(
		string interleavedTexUniform,
		string coordTexUniform,
		Vector3 offset
	) {
		interleave(offset);
		
		JobHandle sort = new JobHandle();
		quicksort(
			0, 
			numInsideFrustum-1, 
			0,
			ref sort
		);
		sort.Complete();
		
		// Set up 3d texture; note that dimensions of textures need to be a perfect power of 2.
		// The largest a Texture3D dimension can be is 2048; perhaps in the future with awesome computers, we will need Z dimension :-)
		Vector3 tex_dimensions = new Vector3(X,Y,Z);
		
		Shader.SetGlobalVector("tex_dimensions", tex_dimensions);
		Shader.SetGlobalVector("vol_dimensions", new Vector3(width, height, depth));
		Shader.SetGlobalInt("num_inside_vol", numInsideFrustum); // maybe want different culling depending on use
		
		setupInterleavingTextures(
			interleavedTexUniform,
			coordTexUniform
		);
		
	}
	
	private void quicksort(
		int lo,
		int hi,
		int level,
		ref JobHandle parent
	)
	{
		if (hi <= lo) return;
		
		left[lo] = lo;
		right[lo] = hi;
		QuickSort sort = new QuickSort()
		{
			bits = interleaved, 
			pos = frustumArray,
			left = left,
			right = right,
			lo = lo,
			hi = hi
		};
		
		JobHandle scheduled = sort.Schedule(parent);
		scheduled.Complete();
		
		int left_i = sort.left[lo];
		int right_i = sort.right[lo];
		
		quicksort(lo, left_i-1, level+1, ref scheduled);
		quicksort(right_i+1, hi, level+1, ref scheduled);
	}
	
	private void setupInterleavingTextures(
		string interleavedTexUniform,
		string coordTexUniform
	) {
		
		getTex();
		getTexVec();
		
		Shader.SetGlobalTexture(interleavedTexUniform, interleavedTex);
		Shader.SetGlobalTexture(coordTexUniform, rawPositionTex);
	}
	
	private void getTex()
	{
		interleavedTex.SetPixelData(interleavedColors, 0, 0);
		interleavedTex.Apply();
		for (int i = 0; i < numInsideFrustum*4; i+=4)
		{
			interleavedColors[i]   = (float)(0x3FFF & (interleaved[i/4] >> 46));
			interleavedColors[i+1] = (float)(0xFFFF & (interleaved[i/4] >> 30));
			interleavedColors[i+2] = (float)(0x3FFF & (interleaved[i/4] >> 16));
			interleavedColors[i+3] = (float)(0xFFFF & (interleaved[i/4]));
		}
		interleavedTex.SetPixelData(interleavedColors, 0, 0);
		interleavedTex.Apply();
	}

	private void getTexVec()
	{
		rawPositionTex.SetPixelData(rawPositionColors, 0, 0);
		rawPositionTex.Apply();
		for (int i = 0; i < numInsideFrustum*4; i+=4)
		{
			rawPositionColors[i]   = frustumArray[i/4].x;
			rawPositionColors[i+1] = frustumArray[i/4].y;
			rawPositionColors[i+2] = frustumArray[i/4].z;
			rawPositionColors[i+3] = 0;
		}
		rawPositionTex.SetPixelData(rawPositionColors, 0, 0);
		rawPositionTex.Apply();
	}
	
	void OnDrawGizmos(){
		if (playing && displayParticleSceneViewGizmos) {
			Gizmos.color = new Color(1, 1, 1, 1);
			Gizmos.DrawWireCube(
				new Vector3(
					cam.transform.position.x,
					cam.transform.position.y,
					cam.transform.position.z + depth/2 + near
				),
				new Vector3(
					width,
					height,
					depth
				)
			);
			for(int i = 0; i < positionsArray.Length; i++) {
				Gizmos.color = new Color(1, 1, 1, 1);
				Gizmos.DrawWireCube(positionsArray[i], Vector3.one);
			}
		}
	}
	
	void Update()
	{
		updatePositions();
	}
}

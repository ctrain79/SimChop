using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;

public class Simulation : MonoBehaviour
{
	private const int MAX_PARTICLES = 4096;
	
	[Header("Hot Swappable")]
	
	[SerializeField, Range(0, MAX_PARTICLES-1)]
	public int spawnCount = default;
	private int lastSpawnCount;
	[SerializeField, Range(0, 1)]
	public float editor_alpha = default;
	[SerializeField, Range(0, 3)]
	public float editor_emission = default;
	[SerializeField, Range(0.5f, 10)]
	public float editor_radius = 4;
	[SerializeField, Range(1, 20)]
	public int scan_num = default;
	[SerializeField]
	public float near = default; // camera near-plane distance
	[SerializeField]
	public float far = default; // camera far-plane distance
	
	[Header("Not Hot Swappable")]
	[SerializeField]
	public GameObject source = default;
	[SerializeField]
	public Vector3 spread = default;
	[SerializeField]
	GameObject level = default;
	public bool visibleInHierarchy = true; 
	public GameObject particle;
	
	// set to public if you want to keep track of it in the editor (but you obviously cannot edit the number)
	int num_inside_vol;
	
	int precision;
	float width;
	float height;
	float depth;
	float camH;
	float camW;
	
	void setPrecisionAndDimensions() {
		camH = Mathf.Tan(Camera.main.fieldOfView*Mathf.PI/360);
		camW = Mathf.Tan(Camera.VerticalToHorizontalFieldOfView(Camera.main.fieldOfView, Camera.main.aspect)*Mathf.PI/360);
		
		// float k = Mathf.Ceil(Mathf.Log(2 * camW * far, 2));
		// width = Mathf.Pow(2, k); // so the volume covers the furthest part of camera view
		//width = 2 * camW * far;
		
		// k = Mathf.Ceil(Mathf.Log(2 * camH * far, 2));
		// height = Mathf.Pow(2, k); // so the volume covers the furthest part of camera view
		// height = 2 * camH * far;
		depth = far - near;
		
		precision = 4;
		float sidelength = Mathf.Pow(2, precision)*1.6f*(editor_radius+1);
		width = height = sidelength;
		//precision = (int)Mathf.Floor(Mathf.Log(sidelength/(editor_radius+1), 2)) - 1;
		Debug.Log("precision = " + precision);
		Debug.Log("sidelength = " + sidelength);
		Debug.Log("cell size = " + sidelength*Mathf.Pow(0.5f, precision));
		Debug.Log("editor radius = " + editor_radius);
	}
	
	ParticleData particleData;
	Stack<GameObject> simObjs;
	BoxCollider volumeCollider; // TO DO: have collider trigger adding/removing particles in addition to editor slider
	
	public static event Action<ParticleData> EnableEvent;
	public static event Action InitializationEvent;
	public static event Action<int> NumberOfParticlesChangedEvent;
	
	
	float span;
	const int N = 30;
	GameObject[] levels;
	
	Matrix4x4 unitScale; // performs transformation to unit cube
	Vector3 shift; // amount of translation for second interleaving data structure
	
	// jobs version
	List<Vector3> restrictedPos = new List<Vector3>();
	List<int> restrictedObj = new List<int>();
	NativeArray<Vector3> resArray;
	NativeArray<ulong> interleaved;
	
	NativeArray<Vector3> positionsArray;
	NativeArray<Vector3> transformedPositionsArray;
	NativeArray<int> left;
	NativeArray<int> right;
	
	// compute shader version
	// Vector3[] positionsArray;
	// Vector3[] transformedPositionsArray;
	
	
	private const int X = 2048;
	private const int Y = 2;
	private const int Z = 1;
	Texture3D interleavedTex;
	Texture3D rawPositionTex;
	private float[] interleavedColors;
	private float[] rawPositionColors;
	
	int stride = 4*3; /* num of bytes per data element in compute buffer */
	static readonly int orderPosId = Shader.PropertyToID("_Positions"),
	orderMatrixToUnitId = Shader.PropertyToID("_Unit"),
	orderMatrixUnitShiftId = Shader.PropertyToID("_Shift"),
	orderNumOfPartId = Shader.PropertyToID("_N");
	
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
		span = (depth-editor_radius) / N;
		levels = new GameObject[N];
		for (int i = 0; i < N; i++) {
			levels[i] = Instantiate(
				level,
				new Vector3(0, 0, 0),
				Quaternion.identity,
				Camera.main.transform
			);
			levels[i].transform.localScale =
				new Vector3(
					width,
					height,
					1
				);
			levels[i].transform.localRotation = Quaternion.identity;
			levels[i].transform.localPosition = new Vector3(0, 0, near + span*i + editor_radius*0.5f);
			
			HideFlags inHierarchy = 
				visibleInHierarchy ? 
				HideFlags.None : 
				HideFlags.HideInHierarchy;
			levels[i].hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | inHierarchy;
			
		}
		
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
			Camera.main.transform.position + 
			Camera.main.transform.forward * (depth/2 + near);
		volumeCollider.size = new Vector3(width/2, height/2, depth/2);
		
		particleData = new ParticleData(
			MAX_PARTICLES,
			spawnCount,
			simObjs,
			particle,
			Camera.main.transform.position,
			new Vector3(width, height, depth),
			near,
			source,
			volumeCollider,
			spread
		);
		EnableEvent(particleData);
		
		InitializationEvent();
		
		transformedPositionsBuffer = new ComputeBuffer(particleData.MAX_PARTICLES, stride);
		
		// setup particle data for jobs
		positionsArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		transformedPositionsArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		left = new NativeArray<int>(MAX_PARTICLES, Allocator.Persistent);
		right = new NativeArray<int>(MAX_PARTICLES, Allocator.Persistent);
		// compute shader version
		// positionsArray = new Vector3[MAX_PARTICLES];
		// transformedPositionsArray = new Vector3[MAX_PARTICLES];
		
		resArray = new NativeArray<Vector3>(MAX_PARTICLES, Allocator.Persistent);
		interleaved = new NativeArray<ulong>(MAX_PARTICLES, Allocator.Persistent);
		
		Setup();
	}
	
	void OnDestroy(){
		positionsArray.Dispose();
		transformedPositionsArray.Dispose();
		left.Dispose();
		right.Dispose();
		interleaved.Dispose();
		resArray.Dispose();
	}
	
	void Setup()
	{
		//Debug.Log("Setup");
		setPrecisionAndDimensions();
		//Debug.Log("precision = " + precision);
		//shift = Mathf.Pow(0.5f, precision + 1)*(new Vector3(width, height, depth));
		NumberOfParticlesChangedEvent(spawnCount);
		//Debug.Log("interleaved length" + interleaved.Length);
		
		//Array.Clear(positionsArray, 0, MAX_PARTICLES);
		
		
		int j = 0;
		foreach (GameObject obj in simObjs){
			if (j < spawnCount)
				positionsArray[j++] = obj.transform.position;
		}
		// for(int i = 0; i < spawnCount; i++){
		// 	Debug.Log("start " + i + " posArray: (" + positionsArray[i].x + ", " + positionsArray[i].y + ", " + positionsArray[i].z + ")");
		// }
	}
	
	void OnDisable()
	{
		transformedPositionsBuffer.Release();
		transformedPositionsBuffer = null;
		TakeDown();
	}
	
	void TakeDown() {
	}
	
	void updatePositions(){
			
		if (spawnCount > 0 && spawnCount != lastSpawnCount) {
			lastSpawnCount = spawnCount;
			Debug.Log("spawnCount = " + spawnCount + " positionsArray length = " + positionsArray.Length);
			TakeDown();
			Setup();
		}
		
		if (spawnCount > 0) {
			
			//editor_radius = Mathf.Pow(0.5f, precision+1)*depth;
			
			// TO DO: get rid of redundant code
			int j = 0;
			foreach (GameObject obj in simObjs){
				if (j < spawnCount)
					positionsArray[j++] = obj.transform.position;
			}
			//control the bounds of what we are tracking
			unitScale = SimulationHelper.createMatrixScale(width, height, depth);
			// controls mapping 3D space to the unit cube
			Matrix4x4 posOctantMap = SimulationHelper.createMatrixMapToPosOctant();
			Matrix4x4 unitMap = SimulationHelper.createMatrixMapToUnitCube(unitScale, near);
			
			MapVolume mapJob = new MapVolume()
			{
				map_unit = unitMap,
				map_shift = posOctantMap,
				pos = positionsArray,
				mapped = transformedPositionsArray
			};
			mapJob.Run(spawnCount);
			
			//Debug.Log("time: " + Time.realtimeSinceStartup);
			
				// computeOrder.SetMatrix(orderMatrixToUnitId, unitMap);
				// computeOrder.SetMatrix(orderMatrixUnitShiftId, posOctantMap);
			Shader.SetGlobalMatrix("unit_map", unitMap);
			Shader.SetGlobalMatrix("pos_octant_map", posOctantMap);
			
			//Debug.Log("section = " + section);
			//Shader.SetGlobalInt("section", section);
			Shader.SetGlobalFloat("editor_alpha", editor_alpha);
			Shader.SetGlobalFloat("editor_emission", editor_emission);
			Shader.SetGlobalFloat("precision", precision);
			Shader.SetGlobalFloat("editor_radius", editor_radius);
			Shader.SetGlobalInt("scan_num", scan_num);
				// int groups = Mathf.CeilToInt(spawnCount/1024.0f);
				// computeOrder.SetInt(orderNumOfPartId, spawnCount);
				
				// transformedPositionsBuffer.SetData(positionsArray, 0, 0, spawnCount);
				// computeOrder.SetBuffer(0, orderPosId, transformedPositionsBuffer);
				// computeOrder.Dispatch(0, groups, 1, 1);
				
				// transformedPositionsBuffer.GetData(transformedPositionsArray, 0, 0, spawnCount);
			// for(int i = 0; i < transformedPositionsArray.Length; i++){
			// 	Debug.Log("positionsArray[" + i + "]: " + positionsArray[i]);
			// 	Debug.Log("transformedPosition[" + i + "]: " + transformedPositionsArray[i]);
			// }
			// Debug.Log("done");
			
			//naInterleaved = new NativeArray<ulong>(interleaved,Allocator.Persistent);
			//naPositionsArray= new NativeArray<Vector4>(positionsArray,Allocator.Persistent);
			
			buildInterleaveShaderData(
				"interleaved_tex",
				"coord_tex",
				Vector3.zero
			);
			buildInterleaveShaderData(
				"interleaved_shifted_half_unit_tex",
				"coord_shifted_half_unit_tex",
				Vector3.one*Mathf.Pow(0.5f, precision+1)
			);
			
		}
	}

	void interleave(
		Vector3 offset
	) {
		num_inside_vol = 0;
		ulong result = 0x00000000;
		float unit = Mathf.Pow(0.5f, precision);
		float two = Mathf.Pow(2, precision);
		for(int i = 0; i < spawnCount; i++) {
			if (
				transformedPositionsArray[i].x <= 1-unit && 
				transformedPositionsArray[i].y <= 1-unit && 
				transformedPositionsArray[i].z <= 1-unit && 
				transformedPositionsArray[i].x >= unit && 
				transformedPositionsArray[i].y >= unit && 
				transformedPositionsArray[i].z >= unit
			) {
				result = 0;
				uint s1 = (uint)(Mathf.Floor((transformedPositionsArray[i].x + offset.x)*two));
				uint s2 = (uint)(Mathf.Floor((transformedPositionsArray[i].y + offset.y)*two));
				uint s3 = (uint)(Mathf.Floor((transformedPositionsArray[i].z + offset.z)*two));
				for(int k = 0; k < precision; k++){
					uint mask = (0x1);
					result |= (ulong)(mask & s3) << (k*3);
					result |= (ulong)(mask & s2) << (k*3+1);
					result |= (ulong)(mask & s1) << (k*3+2);
					s1 = s1 >> 1;
					s2 = s2 >> 1;
					s3 = s3 >> 1;
				}
				resArray[num_inside_vol] = transformedPositionsArray[i];
				interleaved[num_inside_vol] = result;
				num_inside_vol++;
			}
		}
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
			num_inside_vol-1, 
			0,
			ref sort
		);
		sort.Complete();
		
		//set up 3d texture
		// int powTwo = (int)Mathf.Pow(2, Mathf.Ceil(Mathf.Log(num_inside_vol, 2))); // texture needs to be a perfect power of 2
		// Debug.Log("powTwo = " + powTwo);
		// int x = 
		// 	(num_inside_vol > 2048) ?
		// 	2048 :
		// 	Mathf.Max(powTwo, 1);
		// int y =
		// 	(x == 2048) ?
		// 	(int)Mathf.Ceil(num_inside_vol / 2048.0f) :
		// 	1;
		//Debug.Log("X: " + X);
		Vector3 tex_dimensions = new Vector3(X,Y,Z); // TO DO: extend dimensions
		
		Shader.SetGlobalVector("tex_dimensions", tex_dimensions);
		Shader.SetGlobalVector("vol_dimensions", new Vector3(width, height, depth));
		Shader.SetGlobalInt("precision", precision);
		Shader.SetGlobalInt("num_inside_vol", num_inside_vol);
		
		setupInterleavingTextures(
			interleavedTexUniform,
			coordTexUniform,
			offset*Mathf.Pow(0.5f, precision)
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
			pos = resArray,
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
		string coordTexUniform,
		Vector3 offset
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
		for (int i = 0; i < num_inside_vol*4; i+=4)
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
		for (int i = 0; i < num_inside_vol*4; i+=4)
		{
			rawPositionColors[i]   = resArray[i/4].x;
			rawPositionColors[i+1] = resArray[i/4].y;
			rawPositionColors[i+2] = resArray[i/4].z;
			rawPositionColors[i+3] = 0;
		}
		rawPositionTex.SetPixelData(rawPositionColors, 0, 0);
		rawPositionTex.Apply();
	}
	
	void OnDrawGizmos(){
		if (playing) {
			GameObject cam = GameObject.FindWithTag("MainCamera");
			
			// Vector3 unit = Mathf.Pow(0.5f, precision)*(new Vector3(width, height, depth));
			// Gizmos.color = new Color(1, 1, 1, 1);
			// for (int i = 0; i*unit.x < width; i++) {
			// 	for (int j = 0; j*unit.y < height; j++) {
			// 		for (int k = 0; k*unit.z < depth; k++) {
			// 			Gizmos.DrawWireCube(
			// 				cam.transform.position +
			// 				new Vector3(
			// 					i*unit.x + unit.x/2 - width/2, 
			// 					j*unit.y + unit.y/2 - height/2, 
			// 					k*unit.z + unit.z/2 + near
			// 				),
			// 				unit
			// 			);
			// 		}
			// 	}
			// }
			
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
				// Gizmos.color = new Color(1, 0, 1, 1);
				// Gizmos.DrawWireCube(transformedPositionsArray[i], Vector3.one*0.1f);
			}
		}
	}
	
	// Update is called once per frame
	void Update()
	{
		updatePositions();
	}

	public void LateUpdate()
	{	
		// TO DO: keep track of how many are in the collection instead of freeing memory with TrimExcess
		restrictedPos.Clear();
		restrictedPos.TrimExcess();
		restrictedObj.Clear();
		restrictedObj.TrimExcess();
		//Resources.UnloadUnusedAssets();
		//  m_JobHandle.Complete();

			// copy our results to managed arrays so we can assign them
		//  interleaveJob.interleavedPositions.CopyTo(interleaved);

			//naInterleaved.Dispose();
		//  naPositionsArray.Dispose();
	}
}

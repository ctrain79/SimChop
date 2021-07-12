using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;

public class NewSimulation : MonoBehaviour
{
	private const int MAX_PARTICLES = 4096;
	[SerializeField, Range(0, MAX_PARTICLES-1)]
	public int spawnCount = default;
	private int lastSpawnCount;
	[SerializeField, Range(0, 1)]
	public float alpha = default;
	[SerializeField, Range(0.5f, 10)]
	public float radius = default;
	[SerializeField]
	public GameObject source = default;
	[SerializeField]
	public Vector3 spread = default;
	[SerializeField]
	public int precision = default;
	public int num_inside_vol;
	
	// [SerializeField, Range(0, 0.2f)]
	private float half_unit;
	
	public static float width;
	public static float height;
	public static float depth;
	public float near = default; // camera near-plane distance
	
	// default, but changed in the Setup function
	// private int precision;
	// public int Precision {
	// 	get { return precision; }
	// 	set { precision = value; }
	// }
	
	void setPrecisionAndDimensions() {
		// pass in the radius
		//float min = Mathf.Min(width, height, depth);
		//precision = (int)Mathf.Floor(Mathf.Log(3*min/(radius+1), 2)) - 1;
		float sidelength = Mathf.Pow(2, precision)*1.5f*(radius+1); 
		width = height = sidelength;
		depth = sidelength;
	}
	
	ParticleData particleData;
	Stack<GameObject> simObjs;
	BoxCollider volumeCollider;
	
	public static event Action<ParticleData> EnableEvent;
	public static event Action InitializationEvent;
	public static event Action<int> NumberOfParticlesChangedEvent;

	[SerializeField]
	GameObject level = default;
	public GameObject particle;
	
	float span;
	const int N = 70;
	GameObject[] levels;

	Matrix4x4 unitScale; // performs transformation to unit cube
	Vector3 shift; // amount of translation for second interleaving data structure


	List<Vector3> restrictedPos = new List<Vector3>();
	List<int> restrictedObj = new List<int>();
	NativeArray<Vector3> resArray;
	NativeArray<ulong> interleaved;
	
	// jobs version
	NativeArray<Vector3> positionsArray;
	NativeArray<Vector3> transformedPositionsArray;
	NativeArray<int> left;
	NativeArray<int> right;
	
	// compute shader version
	// Vector3[] positionsArray;
	// Vector3[] transformedPositionsArray;
	
	// jobs for interleaving
	NativeArray<ulong> naInterleaved;
	NativeArray<Vector4> naPositionsArray;

	//interleaveBinJob interleaveJob;
	//  cullParticles cullJob;
	JobHandle m_JobHandle;
	JobHandle m_CullJobHandle;

	private const int X = 2048;
	private const int Y = 2;
	private const int Z = 1;
	Texture3D positionTex;
	Texture3D rawPositionTex;
	private float[] positionColors;
	private float[] rawPositionColors;
	
	int stride = 4*3; /* num of bytes per data element in compute buffer */
	static readonly int orderPosId = Shader.PropertyToID("_Positions"),
	orderMatrixUnitId = Shader.PropertyToID("_Unit"),
	orderMatrixTwoId = Shader.PropertyToID("_Two"),
	orderNumOfPartId = Shader.PropertyToID("_N");
//  NativeList<Vector4> culled;
	[SerializeField]
	ComputeShader computeOrder = default;
	ComputeBuffer transformedPositionsBuffer;
	
	// do not draw gizmos when not in play mode
	bool playing = false;

/*  struct cullParticles: IJobParallelFor{
		public NativeArray<Vector4> positions;
		public NativeList<Vector4> culledPositions;
		public void Execute(int i){
			if(position.x <= 1 && positions.y <= 1 && positions.z <= 1 && position.x >= 0 && positions.y >= 0 && positions.z >= 0){
				culledPositions.Add(positions[i]);
			}
		}
	}*/
	
	/*
	struct interleaveBinJob: IJobParallelFor{
		public NativeArray<Vector4> positions;
		public NativeArray<ulong> interleavedPositions;
		public void Execute(int i){
			uint s1 = SimulationHelper.fToI(positions[i].x, precision);
			uint s2 = SimulationHelper.fToI(positions[i].y, precision);
			uint s3 = SimulationHelper.fToI(positions[i].z, precision);
			// Debug.Log("s1 bin" + System.Convert.ToString(s1, 2));
			// Debug.Log("s2 bin" + System.Convert.ToString(s2, 2));
			// Debug.Log("s3 bin" + System.Convert.ToString(s3, 2));
			int digits = (int)Mathf.Ceil(Mathf.Log(Mathf.Pow(10, precision),2));
			ulong result = 0x00000000;
			if(
				positions[i].x <= 1 && 
				positions[i].y <= 1 && 
				positions[i].z <= 1 && 
				positions[i].x >= 0 && 
				positions[i].y >= 0 && 
				positions[i].z >= 0
			) {
				for(int k = 0; k < digits; k++){
					uint mask = (0x00000001);
					result |= (ulong)(mask & s3) << (k*3);
					result |= (ulong)(mask & s2) << (k*3+1);
					result |= (ulong)(mask & s1) << (k*3+2);
					s1 = s1 >> 1;
					s2 = s2 >> 1;
					s3 = s3 >> 1;
				}
			}
			else{
				result = 0xffffffffffffffff;
			}


			interleavedPositions[i] = result;
			//Debug.Log("job done: " + interleavedPositions[i]);
		}
	}
	*/
	
	// Start is called before the first frame update
	void Start()
	{
		//Debug.Log("NewSimulation Start");
		playing = true;
		//radius = Mathf.Pow(0.5f, precision+1)*depth;
		span = (depth-radius) / N;
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
			levels[i].transform.localPosition = new Vector3(0, 0, near + span*i + radius*0.5f);
		}
		
		// setup textures for data strucures
		positionTex = new Texture3D(X, Y, Z, TextureFormat.RGBAFloat, false);
		rawPositionTex = new Texture3D(X, Y, Z, TextureFormat.RGBAFloat, false);
		positionTex.wrapMode = TextureWrapMode.Clamp;
		positionTex.filterMode = FilterMode.Point;
		rawPositionTex.wrapMode = TextureWrapMode.Clamp;
		rawPositionTex.filterMode = FilterMode.Point;
		positionColors = new float[4*MAX_PARTICLES];
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
			//Debug.Log("spawnCount = " + spawnCount + " positionsArray length = " + positionsArray.Length);
			TakeDown();
			Setup();
		}
		
		if (spawnCount > 0) {
			
			// move planes
			half_unit = Mathf.Pow(0.5f, precision+1);
			//radius = Mathf.Pow(0.5f, precision+1)*depth;
			
			// TO DO: get rid of redundant code
			int j = 0;
			foreach (GameObject obj in simObjs){
				if (j < spawnCount)
					positionsArray[j++] = obj.transform.position;
			}
			//control the bounds of what we are tracking
			unitScale = SimulationHelper.createMatrixScale(width, height, depth);
			// controls mapping 3D space to the unit cube
			Matrix4x4 twoMap = SimulationHelper.createMatrixMapToTwoCube(precision);
			Matrix4x4 unitMap = SimulationHelper.createMatrixMapToUnitCube(unitScale, near);
			
			MapVolume mapJob = new MapVolume()
			{
				map_unit = unitMap,
				map_shift = twoMap,
				pos = positionsArray,
				mapped = transformedPositionsArray
			};
			mapJob.Run(spawnCount);
			
			//Debug.Log("Done.");
			
				// computeOrder.SetMatrix(orderMatrixUnitId, unitMap);
				// computeOrder.SetMatrix(orderMatrixTwoId, twoMap);
			Shader.SetGlobalMatrix("unit_transform", unitMap);
			Shader.SetGlobalMatrix("two_transform", twoMap);
			
			//Debug.Log("section = " + section);
			//Shader.SetGlobalInt("section", section);
			Shader.SetGlobalFloat("alpha", alpha);
			Shader.SetGlobalFloat("r", radius);
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
			
			int count = 1;
			String posName = "_posTex";
			String coordName = "_coordTex";
			buildInterleaveShaderData(
				posName + count.ToString(),
				coordName + count.ToString(),
				Vector3.zero
			);
			count++;
			buildInterleaveShaderData(
				posName + count.ToString(),
				coordName + count.ToString(),
				Vector3.one*half_unit
			);
			// for (int x = -1; x < 2; x += 2) {
			// for (int y = -1; y < 2; y += 2) {
			// for (int z = -1; z < 2; z += 2) {
			// 	buildInterleaveShaderData(
			// 		posName + count.ToString(),
			// 		coordName + count.ToString(),
			// 		new Vector3(x*0.5f, y*0.5f, z*0.5f)
			// 	);
			// 	count++;
			// }}}
		}
	}

	void interleave(
		Vector3 offset
	) {
		num_inside_vol = 0;
		ulong result = 0x00000000;
		float unit = Mathf.Pow(0.5f, precision);
		float two = Mathf.Pow(2, precision);
		// Debug.Log("Number of binary digits: " + digits);
		for(int i = 0; i < spawnCount; i++){
			// Debug.Log(
			// 	"transformed position " +
			// 	i.ToString() + 
			// 	" at " +
			// 	transformedPositionsArray[i].ToString()
			// );
			if (
				transformedPositionsArray[i].x <= 1-unit && 
				transformedPositionsArray[i].y <= 1-unit && 
				transformedPositionsArray[i].z <= 1-unit && 
				transformedPositionsArray[i].x >= unit && 
				transformedPositionsArray[i].y >= unit && 
				transformedPositionsArray[i].z >= unit
			) {
				// Debug.Log(
				// 	"transformed position " +
				// 	i.ToString() + 
				// 	" at " +
				// 	transformedPositionsArray[i].ToString() +
				// 	" is in unit cube"
				// );
				result = 0;
				// uint s1 = SimulationHelper.fToI(transformedPositionsArray[i].x, precision);
				// uint s2 = SimulationHelper.fToI(transformedPositionsArray[i].y, precision);
				// uint s3 = SimulationHelper.fToI(transformedPositionsArray[i].z, precision);
				uint s1 = (uint)(Mathf.Floor(transformedPositionsArray[i].x*two + offset.x));
				uint s2 = (uint)(Mathf.Floor(transformedPositionsArray[i].y*two + offset.y));
				uint s3 = (uint)(Mathf.Floor(transformedPositionsArray[i].z*two + offset.z));
				// Debug.Log("resArray[" + i + "]: " + transformedPositionsArray[i]);
				// Debug.Log(
				// 	"s1 = " + 
				// 	System.Convert.ToString(s1, 2) +
				// 	", s2 = " + 
				// 	System.Convert.ToString(s2, 2) +
				// 	", s3 = " + 
				// 	System.Convert.ToString(s3, 2)
				// );
				for(int k = 0; k < precision; k++){
					uint mask = (0x1);
					result |= (ulong)(mask & s3) << (k*3);
					result |= (ulong)(mask & s2) << (k*3+1);
					result |= (ulong)(mask & s1) << (k*3+2);
					s1 = s1 >> 1;
					s2 = s2 >> 1;
					s3 = s3 >> 1;
				}
				// Debug.Log(
				// 	i + ": (" + 
				// 	System.Convert.ToString((uint)(result >> 32) & 0xFFFFFFFF, 2) +
				// 	System.Convert.ToString((uint)(result) & 0xFFFFFFFF, 2) +
				// 	")"
				// );
				resArray[num_inside_vol] = transformedPositionsArray[i];
				//Debug.Log(transformedPositionsArray[i].ToString());
				//result = result | (1u << 3*precision);
				interleaved[num_inside_vol] = result;
				num_inside_vol++;
			}
		}
	}
	
	private void buildInterleaveShaderData(
		string pos_uniform,
		string coord_uniform,
		Vector3 offset
	) {
		interleave(offset);
		
		//ulong[] sortedBinary = 
			// MergeSort.mergeSort(
			// 	interleaved, 
			// 	ref resArray,
			// 	0
			// );
		//Debug.Log("number of particles inside the volume is " + num_inside_vol);
		JobHandle sort = new JobHandle();
		quicksort(
			0, 
			num_inside_vol-1, 
			0,
			ref sort
		);
		
		// for(int i = 0; i < num_inside_vol; i++){
		// 	Debug.Log("resArray[" + i + "]: " + resArray[i]);
		// 	Debug.Log(
		// 		"resArray in binary: (" + 
		// 		System.Convert.ToString((uint)(interleaved[i] >> 32) & 0xFFFFFFFF, 2) +
		// 		System.Convert.ToString((uint)(interleaved[i]) & 0xFFFFFFFF, 2) +
		// 		")"
		// 	);
		// }
		// Debug.Log("Done.");
		
		// ulong[] check = check_interleave(resArray);
		// for(int i = 0; i < check.Length; i++){
		// 	Debug.Log("check_interleave[" + i + "]: " + check[i]);
		// 	Debug.Log(
		// 		"check   in binary(" + 
		// 		System.Convert.ToString((uint)(check[i] >> 32) & 0xFFFFFFFF, 2) +
		// 		System.Convert.ToString((uint)(check[i]) & 0xFFFFFFFF, 2) +
		// 		")"
		// 	);
		// }
		
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
		Shader.SetGlobalVector("dimensions", new Vector3(width, height, depth));
		Shader.SetGlobalInt("precision", precision);
		Shader.SetGlobalFloat("half_unit", half_unit);
		Shader.SetGlobalInt("particleAmount", num_inside_vol);
		
		setupInterleavingTextures(
			pos_uniform,
			coord_uniform,
			offset*Mathf.Pow(0.5f, precision)
		);

		// ---- check texture data
		// NativeArray<float> positionColors = positionTex.GetPixelData<float>(0);
		// Debug.Log(
		// 	"binary interleaved positionColors = " + string.Join(", ",
		// 	new List<float>(positionColors).ConvertAll(i => i.ToString()))
		// );
		// positionColors.Dispose();
		// NativeArray<float> rawPositionColors = rawPositionTex.GetPixelData<float>(0);
		// Debug.Log(
		// 	"positions rawPositionColors = " + string.Join(", ",
		// 	new List<float>(rawPositionColors).ConvertAll(i => i.ToString()))
		// );
		// rawPositionColors.Dispose();
		
		/*  interleaveJob = new interleaveBinJob(){
			positions = naPositionsArray,
			interleavedPositions = naInterleaved,
			precision = 3
		};*/

		/*cullJob = new cullParticles(){
			positions = naPositionsArray,
			culledPositions = culled
		};*/
		//  m_CullJobHandle = cullParticles.Schedule(spawnCount,64);
		//m_JobHandle = interleaveJob.Schedule(spawnCount, 64);


		//Shader.SetGlobalVectorArray("positionsArray", positionsArray);
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
		
		// Debug.Log(
		// 	"LEVEL = " + level +
		// 	", lo = " + lo +
		// 	", left = " + sort.left +
		// 	", right = " + sort.right + 
		// 	", hi = " + hi
		// );
		
		quicksort(lo, sort.left[lo]-1, level+1, ref scheduled);
		quicksort(sort.right[lo]+1, hi, level+1, ref scheduled);
	}
	
	private void setupInterleavingTextures(
		string pos_uniform,
		string coord_uniform,
		Vector3 offset
	) {
		
		getTex();
		getTexVec();
		
		Shader.SetGlobalTexture(pos_uniform, positionTex);
		Shader.SetGlobalTexture(coord_uniform, rawPositionTex);
	}

	// TO DO: setup positionColors declared with a max size and reuse array instead of initializing a new one each frame
	private void getTex()
	{
		positionTex.SetPixelData(positionColors, 0, 0);
		positionTex.Apply();
		for (int i = 0; i < num_inside_vol*4; i+=4)
		{
			// Debug.Log("interleaved length" + interleaved.Length);
			// Debug.Log("positionColors: interleaved[" + i/4 + "] = " + interleaved[i/4]);
			positionColors[i]   = (float)(0x3FFF & (interleaved[i/4] >> 46));
			positionColors[i+1] = (float)(0xFFFF & (interleaved[i/4] >> 30));
			positionColors[i+2] = (float)(0x3FFF & (interleaved[i/4] >> 16));
			positionColors[i+3] = (float)(0xFFFF & (interleaved[i/4]));
		}
		//Debug.Log("positionColors[3] = " + positionColors[3].ToString());
		//Debug.Log("positions length " + positionColors.Length);
		positionTex.SetPixelData(positionColors, 0, 0);
		positionTex.Apply();
	}

	private void getTexVec()
	{
		rawPositionTex.SetPixelData(rawPositionColors, 0, 0);
		rawPositionTex.Apply();
		for (int i = 0; i < num_inside_vol*4; i+=4)
		{
			//Debug.Log("positionColors: interleaved[" + i + "] = " + pos[i/4].ToString());
			rawPositionColors[i]   = resArray[i/4].x;
			rawPositionColors[i+1] = resArray[i/4].y;
			rawPositionColors[i+2] = resArray[i/4].z;
			rawPositionColors[i+3] = 0;
		}
		//Debug.Log("rawPositionColors[0] = " + rawPositionColors[0].ToString());
		//Debug.Log("raw positions " + rawPositionColors.Length);
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

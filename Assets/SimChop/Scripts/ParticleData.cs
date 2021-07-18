using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public struct ParticleData
{
	public ParticleData(
		int max_particles,
		int n,
		Stack<GameObject> active,
		GameObject particle,
		bool particlesVisibleInHierarchy,
		Vector3 camera_pos, 
		Vector3 volume,
		float near,
		GameObject source,
		BoxCollider volumeCollider,
		Vector3 spread
	) : 
		this()
	{
		this.max_particles = max_particles;
		this.n = n;
		this.active = active;
		this.particle = particle;
		this.particlesVisibleInHierarchy = particlesVisibleInHierarchy;
		this.camera_pos = camera_pos;
		this.volume = volume;
		this.near = near;
		this.source = source;
		this.spread = spread;
	}
	
	private readonly int max_particles;
	public int MAX_PARTICLES { 
		get { return max_particles; }
	}
	
	private int n;
	public int N {
		get { return n; }
		set {
			if (-1 < value && value <= MAX_PARTICLES) {
				n = value;
			}
			else
			{
				Debug.Log(
					"ERROR: cannot set number of particles outside of spawn count range [0, " +
					MAX_PARTICLES +
					"]."
				);
				n = 0;
			}
		}
	}
	
	private Stack<GameObject> active;
	public Stack<GameObject> Active {
		get { return active; }
		set { active = value; }
	}
	
	private GameObject particle;
	public GameObject Particle {
		get {
			return particle; 
		}
		set { particle = value; }
	}
	
	private bool particlesVisibleInHierarchy;
	public bool ParticlesVisibleInHierarchy {
		get {
			return particlesVisibleInHierarchy;
		}
		set { particlesVisibleInHierarchy = value; }
	}
	
	private Vector3 camera_pos;
	public Vector3 Camera_pos {
		get { return camera_pos; }
		set { camera_pos = value; }
	}
	
	private Vector3 volume;
	public Vector3 Volume {
		get { return volume; }
		set { volume = value; }
	}
	
	private float near;
	public float Near {
		get { return near; }
		set { near = value; }
	}
	
	private GameObject source;
	public GameObject Source {
		get { return source; }
		set { source = value; }
	}
	
	private BoxCollider volumeCollider;
	public BoxCollider VolumeCollider {
		get { return volumeCollider; }
		set { volumeCollider = value; }
	}
	
	private Vector3 spread;
	public Vector3 Spread {
		get { return spread; }
		set { spread = value; }
	}
}

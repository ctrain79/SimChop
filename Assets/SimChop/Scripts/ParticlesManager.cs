using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// PROJECT SETTINGS: set the execution order for Particles Manager BEFORE Simulation
public class ParticlesManager : MonoBehaviour
{
	ParticleData data;
	GameObject[] pool;
	bool updating = false;
	
	// Project Settings: order called before Simulation (because Simulation reference is static)
    void OnEnable()
    {
        Simulation.EnableEvent += SetData;
        Simulation.InitializationEvent += InitializePool;
        Simulation.NumberOfParticlesChangedEvent += ActivateParticles;
    }
    
	public void SetData(
		ParticleData particleData
	) {
		data = particleData;
		//Debug.Log("Particle manager setData: p = " + data.Particle.ToString());
		pool = new GameObject[data.MAX_PARTICLES]; 
		Vector3 pos = data.Camera_pos;
	}
	
	public void InitializePool()
	{
		//Debug.Log("data.N = " + data.N);
		//Debug.Log("initialize pool: p = " + data.Particle.ToString());
		for (int i = 0; i < data.MAX_PARTICLES; i++) {
			Vector3 pos = data.Camera_pos;
			if (i < data.N) {
				pool[i] =
					Instantiate(
						data.Particle,
						data.Source.transform.position + 
						new Vector3(
							Random.Range(-data.Spread.x/2, data.Spread.x/2),
							Random.Range(5, data.Spread.y),
							Random.Range(-data.Spread.z/2, data.Spread.z/2)
						),
						Quaternion.identity,
						data.Source.transform
					);
				data.Active.Push(pool[i]);
			} else {
				pool[i] =
					Instantiate(
						data.Particle, 
						new Vector3(
							Random.Range(-10000, 10000),
							10000,
							Random.Range(-10000, 10000)
						), 
						Quaternion.identity,
						data.Source.transform
					);
				pool[i].GetComponent<Rigidbody>().velocity = Vector3.zero;
				pool[i].GetComponent<Rigidbody>().useGravity = false;
				pool[i].SetActive(false);
			}
		}
	}
	
	public void ActivateParticles(int n)
	{
		if (!updating) {
			updating = true;
			if (data.N < n && n <= data.MAX_PARTICLES) {
				for(int i = data.N; i < n; i++) {
					pool[i].SetActive(true);
					pool[i].transform.localPosition =  
						new Vector3(
							Random.Range(-data.Spread.x/2, data.Spread.x/2),
							Random.Range(5, data.Spread.y),
							Random.Range(-data.Spread.z/2, data.Spread.z/2)
						);
					Rigidbody rb = pool[i].GetComponent<Rigidbody>();
					rb.useGravity = true;
					data.Active.Push(pool[i]);
				}
				data.N = n;
			}
			else if (-1 < n && n < data.N) {
				for (int i = data.N-1; i >= n; i--) {
					data.Active.Pop();
					pool[i].GetComponent<Rigidbody>().useGravity = false;
					pool[i].GetComponent<Rigidbody>().velocity = Vector3.zero;
					pool[i].SetActive(false);
					pool[i].transform.localPosition = 
						new Vector3(
							Random.Range(-10000, 10000),
							10000,
							Random.Range(-10000, 10000)
						);
				}
				data.Active.TrimExcess();
				data.N = n;
			}
			else if (data.N == n) {
				//Debug.Log("Nothing changed");
			}
			else
			{
				Debug.Log(
					"ERROR: particle manager cannot enable the number of particles " + n + " outside of range [0, " + 
					data.MAX_PARTICLES + 
					"], with the current number of particles " +
					data.N
				);
			}
			updating = false;
		}
	}
	
}

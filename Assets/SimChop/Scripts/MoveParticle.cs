using UnityEngine;

public class MoveParticle : MonoBehaviour
{
	float time;
	Rigidbody rb;
	
	void Start() {
		time = 0;
		rb = GetComponent<Rigidbody>();
	}
	
	void Update()
	{
		// Maybe people want to control movement of particles with a vector field, but this is a fairly easy alternative.
		
		time += Time.deltaTime;
		if (time > 1)
		{
			Vector3 pos = transform.position;
			Vector3 dir = 
				new Vector3(
					-Mathf.Sin(0.1f*pos.x) + Mathf.Sin(0.1f*pos.z),
					0,
					Mathf.Sin(0.1f*pos.x) - Mathf.Sin(0.1f*pos.z)
				);
			rb.AddForce(dir.normalized*Time.deltaTime*500);
			time = 0;
		}
	}
}

using UnityEngine;

public class Sink : MonoBehaviour
{
	[SerializeField]
	GameObject source = default;
	
	void OnTriggerEnter(Collider c)
	{
		c.gameObject.transform.position = 
			source.transform.position + 
			Vector3.up*Random.Range(0, 100f) + 
			Vector3.forward*Random.Range(-20, 20);
	}
}
